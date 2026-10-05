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

        /// <summary>Meta's media id, set by a successful upload.</summary>
        public string? MediaId { get; set; }
    }

    /// <summary>A media file downloaded from Meta (customer photos, voice notes, …).</summary>
    public class WhatsAppMediaFile
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";
    }

    /// <summary>
    /// Thin wrapper around the official Meta WhatsApp Business Cloud API (Graph API).
    /// Never call graph.facebook.com from the frontend — everything goes through here.
    /// </summary>
    public interface IWhatsAppCloudApiService
    {
        Task<WhatsAppSendResult> SendTextMessageAsync(string phoneNumberId, string toWaId, string text, CancellationToken ct = default);

        /// <summary>
        /// Sends a Meta-approved template — the only way a business may write first (outside the
        /// customer's 24-hour window). <paramref name="bodyParams"/> fill {{1}}, {{2}}… in order.
        /// </summary>
        Task<WhatsAppSendResult> SendTemplateMessageAsync(string phoneNumberId, string toWaId, string templateName,
                                                         string languageCode, IReadOnlyList<string> bodyParams, CancellationToken ct = default);

        /// <summary>Uploads a file to Meta for this number; on success <see cref="WhatsAppSendResult.MediaId"/> is set.</summary>
        Task<WhatsAppSendResult> UploadMediaAsync(string phoneNumberId, byte[] data, string mimeType, string fileName, CancellationToken ct = default);

        /// <summary>Sends an already-uploaded media file. <paramref name="type"/> is image | audio | video | document.</summary>
        Task<WhatsAppSendResult> SendMediaMessageAsync(string phoneNumberId, string toWaId, string type, string mediaId,
                                                      string? caption, string? fileName, CancellationToken ct = default);

        /// <summary>Downloads a media file (Meta's media URLs need the access token, so the browser can't fetch them).</summary>
        Task<WhatsAppMediaFile?> DownloadMediaAsync(string mediaId, CancellationToken ct = default);

        Task<string?> ResolveMediaUrlAsync(string mediaId, CancellationToken ct = default);
    }

    public class WhatsAppCloudApiService : IWhatsAppCloudApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMetaCredentialStore _credentialStore;
        private readonly ILogger<WhatsAppCloudApiService> _logger;
        private readonly string _apiVersion;

        public WhatsAppCloudApiService(
            IHttpClientFactory httpClientFactory,
            IMetaCredentialStore credentialStore,
            IConfiguration config,
            ILogger<WhatsAppCloudApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _credentialStore = credentialStore;
            _logger = logger;
            _apiVersion = config["WhatsApp:ApiVersion"] ?? "v21.0";
        }

        // Graph error codes meaning "this token can't act on this number" — worth trying another token:
        // 190 invalid/expired token, 10 / 200 permission denied, 100 object not accessible with this token
        private static readonly HashSet<string> TokenPermissionErrorCodes = new() { "190", "10", "200", "100" };

        // phoneNumberId -> the token that last worked for it, tried first next time so a number
        // covered only by the second token doesn't pay a refused round-trip on every call
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> WorkingTokenByNumber = new();

        public Task<WhatsAppSendResult> SendTextMessageAsync(string phoneNumberId, string toWaId, string text, CancellationToken ct = default)
        {
            var payload = NewMessagePayload(toWaId, "text");
            payload["text"] = new JObject { ["preview_url"] = false, ["body"] = text };
            return PostMessageAsync(phoneNumberId, payload, ct);
        }

        public Task<WhatsAppSendResult> SendTemplateMessageAsync(string phoneNumberId, string toWaId, string templateName,
                                                                string languageCode, IReadOnlyList<string> bodyParams, CancellationToken ct = default)
        {
            var template = new JObject
            {
                ["name"] = templateName,
                ["language"] = new JObject { ["code"] = languageCode }
            };
            if (bodyParams.Count > 0)
            {
                var parameters = new JArray();
                foreach (var value in bodyParams)
                    parameters.Add(new JObject { ["type"] = "text", ["text"] = value });
                template["components"] = new JArray { new JObject { ["type"] = "body", ["parameters"] = parameters } };
            }

            var payload = NewMessagePayload(toWaId, "template");
            payload["template"] = template;
            return PostMessageAsync(phoneNumberId, payload, ct);
        }

        public Task<WhatsAppSendResult> SendMediaMessageAsync(string phoneNumberId, string toWaId, string type, string mediaId,
                                                             string? caption, string? fileName, CancellationToken ct = default)
        {
            var media = new JObject { ["id"] = mediaId };
            // Meta rejects captions on audio; documents also carry their file name
            if (!string.IsNullOrWhiteSpace(caption) && type != "audio") media["caption"] = caption;
            if (type == "document" && !string.IsNullOrWhiteSpace(fileName)) media["filename"] = fileName;

            var payload = NewMessagePayload(toWaId, type);
            payload[type] = media;
            return PostMessageAsync(phoneNumberId, payload, ct);
        }

        public Task<WhatsAppSendResult> UploadMediaAsync(string phoneNumberId, byte[] data, string mimeType, string fileName, CancellationToken ct = default)
        {
            return WithTokenFallbackAsync(phoneNumberId, async token =>
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("whatsapp"), "messaging_product");
                form.Add(new StringContent(mimeType), "type");
                var file = new ByteArrayContent(data);
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                form.Add(file, "file", fileName);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_apiVersion}/{phoneNumberId}/media") { Content = form };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var (ok, j, err) = await SendGraphAsync(request, $"upload media ({mimeType})", ct);
                return ok ? new WhatsAppSendResult { Success = true, MediaId = j?["id"]?.ToString() } : err!;
            }, ct);
        }

        public async Task<WhatsAppMediaFile?> DownloadMediaAsync(string mediaId, CancellationToken ct = default)
        {
            // Media belongs to one number's WABA and has no phone number attached, so try each token
            foreach (var token in await _credentialStore.GetAccessTokensAsync(ct))
            {
                var url = await ResolveMediaUrlWithTokenAsync(token, mediaId, ct);
                if (url == null) continue;

                try
                {
                    var http = _httpClientFactory.CreateClient();
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var response = await http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode) continue;

                    return new WhatsAppMediaFile
                    {
                        Data = await response.Content.ReadAsByteArrayAsync(ct),
                        ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception downloading WhatsApp media {MediaId}", mediaId);
                }
            }
            return null;
        }

        public async Task<string?> ResolveMediaUrlAsync(string mediaId, CancellationToken ct = default)
        {
            foreach (var accessToken in await _credentialStore.GetAccessTokensAsync(ct))
            {
                var url = await ResolveMediaUrlWithTokenAsync(accessToken, mediaId, ct);
                if (url != null) return url;
            }
            return null;
        }

        // ── internals ───────────────────────────────────────────────────────

        private static JObject NewMessagePayload(string toWaId, string type) => new()
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = toWaId,
            ["type"] = type
        };

        private Task<WhatsAppSendResult> PostMessageAsync(string phoneNumberId, JObject payload, CancellationToken ct)
        {
            return WithTokenFallbackAsync(phoneNumberId, async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_apiVersion}/{phoneNumberId}/messages")
                {
                    Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var (ok, j, err) = await SendGraphAsync(request, $"send {payload["type"]}", ct);
                return ok
                    ? new WhatsAppSendResult { Success = true, WhatsAppMessageId = j?["messages"]?.FirstOrDefaultToken()?["id"]?.ToString() }
                    : err!;
            }, ct);
        }

        /// <summary>
        /// Runs a Graph call with each available token (last-known-good first) until one isn't refused
        /// for permission reasons — numbers can be covered by different tokens.
        /// </summary>
        private async Task<WhatsAppSendResult> WithTokenFallbackAsync(string phoneNumberId, Func<string, Task<WhatsAppSendResult>> call, CancellationToken ct)
        {
            var tokens = await _credentialStore.GetAccessTokensAsync(ct);
            if (tokens.Count == 0)
            {
                _logger.LogError("No WhatsApp access token available (neither Embedded Signup nor WhatsApp:AccessToken configured).");
                return new WhatsAppSendResult { Success = false, ErrorMessage = "WhatsApp access token is not configured." };
            }

            if (WorkingTokenByNumber.TryGetValue(phoneNumberId, out var known) && tokens.Contains(known))
                tokens = tokens.OrderByDescending(t => t == known).ToList();

            WhatsAppSendResult result = null!;
            for (var i = 0; i < tokens.Count; i++)
            {
                result = await call(tokens[i]);
                if (result.Success)
                    WorkingTokenByNumber[phoneNumberId] = tokens[i];
                if (result.Success || result.ErrorCode == null || !TokenPermissionErrorCodes.Contains(result.ErrorCode))
                    return result;

                if (i < tokens.Count - 1)
                    _logger.LogWarning("WhatsApp call via phoneNumberId={PhoneNumberId} was refused for token #{Index} (code {Code}) — retrying with the next token.",
                        phoneNumberId, i + 1, result.ErrorCode);
            }
            return result;
        }

        private async Task<(bool ok, JObject? body, WhatsAppSendResult? error)> SendGraphAsync(HttpRequestMessage request, string what, CancellationToken ct)
        {
            try
            {
                var http = _httpClientFactory.CreateClient();
                using var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                var j = SafeParse(body);

                if (response.IsSuccessStatusCode)
                    return (true, j, null);

                var errCode = j?["error"]?["code"]?.ToString();
                var errMsg = j?["error"]?["message"]?.ToString() ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogError("WhatsApp {What} failed. url={Url} status={Status} error={Error}", what, request.RequestUri, response.StatusCode, errMsg);
                return (false, j, new WhatsAppSendResult { Success = false, ErrorCode = errCode, ErrorMessage = errMsg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during WhatsApp {What}", what);
                // Meta's own error texts above are useful to agents; raw exceptions aren't (logged instead)
                return (false, null, new WhatsAppSendResult { Success = false, ErrorMessage = "تعذر الاتصال بواتساب، حاول مرة أخرى" });
            }
        }

        private async Task<string?> ResolveMediaUrlWithTokenAsync(string accessToken, string mediaId, CancellationToken ct)
        {
            var http = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://graph.facebook.com/{_apiVersion}/{mediaId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

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
