using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RecruitmentSaaS.Services
{
    public class MetaPhoneNumberDetails
    {
        public string PhoneNumberId { get; set; } = string.Empty;
        public string? DisplayPhoneNumber { get; set; }
        public string? VerifiedName { get; set; }
        public bool? IsOnBizApp { get; set; }
        public string? PlatformType { get; set; }
    }

    public class MetaOperationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Everything needed to complete WhatsApp Embedded Signup server-side: exchanging the
    /// short-lived authorization code for a Business Integration System User access token,
    /// looking up phone number / WABA metadata, and subscribing our app to receive that
    /// WABA's webhooks. Kept separate from WhatsAppCloudApiService (messaging) since this
    /// is about onboarding/auth, not sending messages.
    /// </summary>
    public interface IMetaAuthService
    {
        /// <summary>
        /// Builds the full-page Facebook OAuth dialog URL for Embedded Signup. Used instead of
        /// the FB.login() JS SDK popup, because that popup's real redirect_uri is Meta's own
        /// ephemeral xd_arbiter relay page (a fresh random URL every time) which a server can
        /// never predict — a classic top-level redirect with our own static, self-controlled
        /// redirectUri sidesteps that entirely and is what's used for the exchange below too.
        /// </summary>
        string BuildAuthorizationUrl(string redirectUri, string state);

        /// <param name="redirectUri">
        /// Must be byte-for-byte identical to the redirectUri used in BuildAuthorizationUrl.
        /// </param>
        Task<(bool Success, string? AccessToken, string? ErrorMessage)> ExchangeCodeForTokenAsync(string code, string? redirectUri, CancellationToken ct = default);

        Task<MetaPhoneNumberDetails?> GetPhoneNumberDetailsAsync(string phoneNumberId, string accessToken, CancellationToken ct = default);

        /// <summary>
        /// Server-side fallback for discovering the WABA ID an access token was just granted,
        /// without depending on the frontend's WA_EMBEDDED_SIGNUP postMessage event having
        /// actually arrived (it doesn't always, in practice). Reads granular_scopes from
        /// Meta's debug_token endpoint; the most recently granted WABA is first in the list.
        /// </summary>
        Task<string?> DiscoverWabaIdAsync(string accessToken, CancellationToken ct = default);

        Task<List<MetaPhoneNumberDetails>> GetWabaPhoneNumbersAsync(string wabaId, string accessToken, CancellationToken ct = default);

        Task<MetaOperationResult> SubscribeAppToWabaAsync(string wabaId, string accessToken, CancellationToken ct = default);
    }

    public class MetaAuthService : IMetaAuthService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MetaAuthService> _logger;
        private readonly string _apiVersion;
        private readonly string _appId;
        private readonly string _appSecret;
        private readonly string _configId;

        public MetaAuthService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<MetaAuthService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _apiVersion = config["WhatsApp:ApiVersion"] ?? "v21.0";
            _appId = config["WhatsApp:AppId"] ?? string.Empty;
            _appSecret = config["WhatsApp:AppSecret"] ?? string.Empty;
            _configId = config["WhatsApp:EmbeddedSignupConfigId"] ?? string.Empty;
        }

        public string BuildAuthorizationUrl(string redirectUri, string state)
        {
            // Same extras shape Meta's own App Dashboard shows for this app's config
            // (confirmed against the "Embedded Signup code setup" walkthrough) — an all-null
            // setup object is valid; featureType is what specifically requests Coexistence.
            var extrasJson = "{\"version\":\"v4\",\"sessionInfoVersion\":\"3\",\"setup\":{" +
                "\"business\":{\"id\":null,\"name\":null,\"email\":null,\"phone\":{\"code\":null,\"number\":null}," +
                "\"website\":null,\"address\":{\"streetAddress1\":null,\"streetAddress2\":null,\"city\":null,\"state\":null,\"zipPostal\":null,\"country\":null}," +
                "\"timezone\":null}," +
                "\"phone\":{\"displayName\":null,\"category\":null,\"description\":null}," +
                "\"preVerifiedPhone\":{\"ids\":null},\"solutionID\":null,\"whatsAppBusinessAccount\":{\"ids\":null}}," +
                "\"featureType\":\"whatsapp_business_app_onboarding\"}";

            return $"https://www.facebook.com/{_apiVersion}/dialog/oauth" +
                   $"?client_id={Uri.EscapeDataString(_appId)}" +
                   $"&config_id={Uri.EscapeDataString(_configId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                   $"&response_type=code" +
                   $"&override_default_response_type=true" +
                   $"&display=page" +
                   $"&state={Uri.EscapeDataString(state)}" +
                   $"&extras={Uri.EscapeDataString(extrasJson)}";
        }

        public async Task<(bool Success, string? AccessToken, string? ErrorMessage)> ExchangeCodeForTokenAsync(string code, string? redirectUri, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appSecret))
            {
                _logger.LogError("Meta code exchange attempted without WhatsApp:AppId / WhatsApp:AppSecret configured.");
                return (false, null, "WhatsApp:AppId / WhatsApp:AppSecret is not configured on the server.");
            }

            var url = $"https://graph.facebook.com/{_apiVersion}/oauth/access_token" +
                      $"?client_id={Uri.EscapeDataString(_appId)}" +
                      $"&client_secret={Uri.EscapeDataString(_appSecret)}" +
                      $"&code={Uri.EscapeDataString(code)}";

            if (!string.IsNullOrWhiteSpace(redirectUri))
                url += $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";

            var http = _httpClientFactory.CreateClient();

            try
            {
                var response = await http.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errJ = SafeParse(body);
                    var errMsg = errJ?["error"]?["message"]?.ToString() ?? $"HTTP {(int)response.StatusCode}";
                    _logger.LogError("Meta code exchange failed. status={Status} error={Error}", response.StatusCode, errMsg);
                    return (false, null, errMsg);
                }

                var j = SafeParse(body);
                var token = j?["access_token"]?.ToString();

                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogError("Meta code exchange returned no access_token. body={Body}", Redact(body));
                    return (false, null, "Meta did not return an access token.");
                }

                _logger.LogInformation("Meta code exchange succeeded. tokenLength={Length}", token.Length);
                return (true, token, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Meta code exchange.");
                return (false, null, ex.Message);
            }
        }

        public async Task<MetaPhoneNumberDetails?> GetPhoneNumberDetailsAsync(string phoneNumberId, string accessToken, CancellationToken ct = default)
        {
            var url = $"https://graph.facebook.com/{_apiVersion}/{phoneNumberId}?fields=display_phone_number,verified_name,is_on_biz_app,platform_type";

            var j = await GetJsonAsync(url, accessToken, ct);
            if (j == null) return null;

            return new MetaPhoneNumberDetails
            {
                PhoneNumberId = phoneNumberId,
                DisplayPhoneNumber = j["display_phone_number"]?.ToString(),
                VerifiedName = j["verified_name"]?.ToString(),
                IsOnBizApp = j["is_on_biz_app"]?.ToObject<bool?>(),
                PlatformType = j["platform_type"]?.ToString()
            };
        }

        public async Task<string?> DiscoverWabaIdAsync(string accessToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appSecret))
            {
                _logger.LogError("debug_token attempted without WhatsApp:AppId / WhatsApp:AppSecret configured.");
                return null;
            }

            var appAccessToken = $"{_appId}|{_appSecret}";
            var url = $"https://graph.facebook.com/{_apiVersion}/debug_token" +
                      $"?input_token={Uri.EscapeDataString(accessToken)}" +
                      $"&access_token={Uri.EscapeDataString(appAccessToken)}";

            var http = _httpClientFactory.CreateClient();

            try
            {
                var response = await http.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("debug_token call failed. status={Status}", response.StatusCode);
                    return null;
                }

                var data = SafeParse(body)?["data"];
                var granularScopes = data?["granular_scopes"] as JArray;
                if (granularScopes == null) return null;

                foreach (var scope in granularScopes)
                {
                    if (!string.Equals(scope["scope"]?.ToString(), "whatsapp_business_management", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Most recently onboarded WABA appears first.
                    var targetIds = scope["target_ids"] as JArray;
                    var firstId = targetIds?.FirstOrDefault()?.ToString();
                    if (!string.IsNullOrWhiteSpace(firstId))
                        return firstId;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling debug_token.");
                return null;
            }
        }

        public async Task<List<MetaPhoneNumberDetails>> GetWabaPhoneNumbersAsync(string wabaId, string accessToken, CancellationToken ct = default)
        {
            var url = $"https://graph.facebook.com/{_apiVersion}/{wabaId}/phone_numbers?fields=id,display_phone_number,verified_name,is_on_biz_app,platform_type";

            var j = await GetJsonAsync(url, accessToken, ct);
            var result = new List<MetaPhoneNumberDetails>();
            if (j?["data"] is not JArray data) return result;

            foreach (var item in data)
            {
                var id = item["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                result.Add(new MetaPhoneNumberDetails
                {
                    PhoneNumberId = id,
                    DisplayPhoneNumber = item["display_phone_number"]?.ToString(),
                    VerifiedName = item["verified_name"]?.ToString(),
                    IsOnBizApp = item["is_on_biz_app"]?.ToObject<bool?>(),
                    PlatformType = item["platform_type"]?.ToString()
                });
            }

            return result;
        }

        public async Task<MetaOperationResult> SubscribeAppToWabaAsync(string wabaId, string accessToken, CancellationToken ct = default)
        {
            var url = $"https://graph.facebook.com/{_apiVersion}/{wabaId}/subscribed_apps";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var http = _httpClientFactory.CreateClient();

            try
            {
                var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errJ = SafeParse(body);
                    var errMsg = errJ?["error"]?["message"]?.ToString() ?? $"HTTP {(int)response.StatusCode}";
                    _logger.LogError("WABA subscribe failed. wabaId={WabaId} status={Status} error={Error}", wabaId, response.StatusCode, errMsg);
                    return new MetaOperationResult { Success = false, ErrorMessage = errMsg };
                }

                var j = SafeParse(body);
                var success = j?["success"]?.ToObject<bool?>() ?? true;

                return new MetaOperationResult { Success = success };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception subscribing app to WABA {WabaId}.", wabaId);
                return new MetaOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<JObject?> GetJsonAsync(string url, string accessToken, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var http = _httpClientFactory.CreateClient();

            try
            {
                var response = await http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Meta Graph API GET failed. url={Url} status={Status} body={Body}", url, response.StatusCode, body);
                    return null;
                }

                return SafeParse(body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling Meta Graph API. url={Url}", url);
                return null;
            }
        }

        private static JObject? SafeParse(string body)
        {
            try { return JObject.Parse(body); }
            catch { return null; }
        }

        private static string Redact(string body) => body.Length > 200 ? body[..200] + "…" : body;
    }
}
