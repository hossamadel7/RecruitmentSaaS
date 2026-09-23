using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RecruitmentSaaS.Services
{
    public class WhatsAppSendResult
    {
        public bool Success { get; set; }

        /// <summary>Meta's wamid for the sent message, when successful.</summary>
        public string? WhatsAppMessageId { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Thin wrapper around the official Meta WhatsApp Business Cloud API (Graph API).
    /// Never call graph.facebook.com from the frontend — everything goes through here.
    /// </summary>
    public interface IWhatsAppCloudApiService
    {
        Task<WhatsAppSendResult> SendTextMessageAsync(string phoneNumberId, string toWaId, string text, CancellationToken ct = default);

        Task<string?> ResolveMediaUrlAsync(string mediaId, CancellationToken ct = default);
    }

    public class WhatsAppCloudApiService : IWhatsAppCloudApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WhatsAppCloudApiService> _logger;
        private readonly string _apiVersion;
        private readonly string _accessToken;

        public WhatsAppCloudApiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<WhatsAppCloudApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiVersion = config["WhatsApp:ApiVersion"] ?? "v21.0";
            _accessToken = config["WhatsApp:AccessToken"] ?? string.Empty;
        }

        public async Task<WhatsAppSendResult> SendTextMessageAsync(string phoneNumberId, string toWaId, string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                _logger.LogError("WhatsApp:AccessToken is not configured.");
                return new WhatsAppSendResult { Success = false, ErrorMessage = "WhatsApp access token is not configured." };
            }

            var url = $"https://graph.facebook.com/{_apiVersion}/{phoneNumberId}/messages";

            var payload = new JObject
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = "individual",
                ["to"] = toWaId,
                ["type"] = "text",
                ["text"] = new JObject { ["preview_url"] = false, ["body"] = text }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var http = _httpClientFactory.CreateClient();

            try
            {
                var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errJ = SafeParse(body);
                    var errCode = errJ?["error"]?["code"]?.ToString();
                    var errMsg = errJ?["error"]?["message"]?.ToString() ?? $"HTTP {(int)response.StatusCode}";
                    _logger.LogError("WhatsApp send failed. phoneNumberId={PhoneNumberId} status={Status} error={Error}",
                        phoneNumberId, response.StatusCode, errMsg);
                    return new WhatsAppSendResult { Success = false, ErrorCode = errCode, ErrorMessage = errMsg };
                }

                var j = SafeParse(body);
                var wamid = j?["messages"]?.FirstOrDefaultToken()?["id"]?.ToString();

                return new WhatsAppSendResult { Success = true, WhatsAppMessageId = wamid };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending WhatsApp message via phoneNumberId={PhoneNumberId}", phoneNumberId);
                return new WhatsAppSendResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<string?> ResolveMediaUrlAsync(string mediaId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_accessToken)) return null;

            var http = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.facebook.com/{_apiVersion}/{mediaId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            try
            {
                var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;

                var body = await response.Content.ReadAsStringAsync(ct);
                return SafeParse(body)?["url"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception resolving WhatsApp media url for mediaId={MediaId}", mediaId);
                return null;
            }
        }

        private static JObject? SafeParse(string body)
        {
            try { return JObject.Parse(body); }
            catch { return null; }
        }
    }

    internal static class JTokenExtensions
    {
        public static JToken? FirstOrDefaultToken(this JToken? token)
        {
            if (token is JArray arr && arr.Count > 0) return arr[0];
            return null;
        }
    }
}
