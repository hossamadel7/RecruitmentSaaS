using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Services;
using System.Security.Cryptography;
using System.Text;

namespace RecruitmentSaaS.Controllers
{
    /// <summary>
    /// Receives Meta WhatsApp Business Cloud API webhooks for every connected WhatsApp number.
    /// Routing between numbers happens purely off metadata.phone_number_id in the payload —
    /// this controller itself has no idea how many WhatsAppAccounts exist.
    /// </summary>
    [Route("api/webhooks/whatsapp")]
    [ApiController]
    [AllowAnonymous]
    public class WhatsAppWebhookController : ControllerBase
    {
        private readonly IWhatsAppWebhookProcessor _processor;
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppWebhookController> _logger;
        private readonly string _verifyToken;
        private readonly string _appSecret;

        public WhatsAppWebhookController(
            IWhatsAppWebhookProcessor processor,
            IConfiguration config,
            ILogger<WhatsAppWebhookController> logger)
        {
            _processor = processor;
            _config = config;
            _logger = logger;
            _verifyToken = _config["WhatsApp:VerifyToken"] ?? string.Empty;
            _appSecret = _config["WhatsApp:AppSecret"] ?? string.Empty;
        }

        // ── GET: Meta webhook verification handshake ─────────────────────────
        [HttpGet]
        public IActionResult VerifyWebhook()
        {
            var mode = Request.Query["hub.mode"].ToString();
            var challenge = Request.Query["hub.challenge"].ToString();
            var verifyToken = Request.Query["hub.verify_token"].ToString();

            _logger.LogInformation("WhatsApp webhook verify attempt: mode={Mode}", mode);

            if (mode == "subscribe" && !string.IsNullOrEmpty(_verifyToken) && verifyToken == _verifyToken)
            {
                return Content(challenge, "text/plain");
            }

            _logger.LogWarning("WhatsApp webhook verification failed.");
            return Forbid();
        }

        // ── POST: Meta webhook notification (messages + status updates) ──────
        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken ct)
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrWhiteSpace(rawBody))
                return Ok(); // nothing to do, but still 200 — Meta will otherwise keep retrying

            if (!IsValidSignature(rawBody))
            {
                _logger.LogWarning("WhatsApp webhook signature validation failed — rejecting payload.");
                return Unauthorized();
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp webhook: invalid JSON payload.");
                // Meta will retry on non-2xx, and a malformed body will never parse — swallow it.
                return Ok();
            }

            try
            {
                await _processor.ProcessAsync(payload, ct);
            }
            catch (Exception ex)
            {
                // Never lose a message: log and still ack, since Meta redelivery + our own
                // idempotency-by-WhatsAppMessageId means a retry after a real fix will heal this.
                _logger.LogError(ex, "Error processing WhatsApp webhook payload.");
            }

            return Ok();
        }

        private bool IsValidSignature(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(_appSecret))
            {
                _logger.LogWarning("WhatsApp:AppSecret is not configured — skipping webhook signature validation.");
                return true;
            }

            var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();
            if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256="))
                return false;

            var expectedHex = signatureHeader["sha256=".Length..];

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_appSecret));
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
            var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHex),
                Encoding.UTF8.GetBytes(expectedHex));
        }
    }
}
