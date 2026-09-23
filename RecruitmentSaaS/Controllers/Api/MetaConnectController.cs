using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api
{
    /// <summary>
    /// Completes WhatsApp Embedded Signup via a classic full-page OAuth redirect rather than the
    /// FB.login() JS SDK popup. That popup's real redirect_uri turned out to be Meta's own
    /// ephemeral xd_arbiter relay page (a fresh random URL every attempt) which a server can never
    /// predict, so exchanging the code from it always failed with a redirect_uri mismatch. A
    /// top-level redirect uses a static, self-controlled redirect_uri (this controller's own
    /// Callback URL) that's identical every time, which is what Meta's exchange endpoint needs.
    /// </summary>
    [Route("api/whatsapp/connect")]
    [ApiController]
    public class MetaConnectController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IMetaAuthService _metaAuth;
        private readonly IMetaCredentialStore _credentialStore;
        private readonly ILogger<MetaConnectController> _logger;

        public MetaConnectController(
            RecruitmentCrmContext context,
            IMetaAuthService metaAuth,
            IMetaCredentialStore credentialStore,
            ILogger<MetaConnectController> logger)
        {
            _context = context;
            _metaAuth = metaAuth;
            _credentialStore = credentialStore;
            _logger = logger;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

        private string CallbackUrl => $"{Request.Scheme}://{Request.Host}/api/whatsapp/connect/callback";

        // ── GET /api/whatsapp/connect/start ───────────────────────────────────
        // What the "Connect WhatsApp" button links to. Redirects the whole browser
        // tab to Meta's OAuth dialog — no JS SDK involved.
        [HttpGet("start")]
        [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
        public IActionResult Start()
        {
            if (!WhatsAppAuthorization.CanManageAccounts(CurrentRole))
                return Forbid();

            // Lightweight CSRF guard: this state value round-trips through Meta unchanged,
            // and Callback checks it matches what we handed out via this signed cookie.
            var state = Guid.NewGuid().ToString("N");
            Response.Cookies.Append("wa_connect_state", state, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10)
            });

            var url = _metaAuth.BuildAuthorizationUrl(CallbackUrl, state);
            return Redirect(url);
        }

        // ── GET /api/whatsapp/connect/callback ────────────────────────────────
        // Meta redirects the browser back here with ?code=... (or ?error=...).
        // The browser still carries our auth cookie across the round trip to
        // facebook.com and back, so [Authorize] here works exactly as normal.
        [HttpGet("callback")]
        [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery(Name = "error")] string? error,
            [FromQuery(Name = "error_description")] string? errorDescription,
            CancellationToken ct)
        {
            if (!WhatsAppAuthorization.CanManageAccounts(CurrentRole))
                return Forbid();

            var expectedState = Request.Cookies["wa_connect_state"];
            Response.Cookies.Delete("wa_connect_state");

            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("WhatsApp connect: Meta returned an error. error={Error} description={Description}", error, errorDescription);
                return RedirectToAdmin(success: false, message: errorDescription ?? error);
            }

            if (string.IsNullOrWhiteSpace(code))
                return RedirectToAdmin(success: false, message: "Meta did not return an authorization code.");

            if (string.IsNullOrEmpty(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
            {
                _logger.LogWarning("WhatsApp connect: state mismatch — possible CSRF or expired session.");
                return RedirectToAdmin(success: false, message: "Connection request expired or was invalid — please try again.");
            }

            // ── Step 1: exchange the authorization code for a system-user access token ──
            var (exchanged, accessToken, exchangeError) = await _metaAuth.ExchangeCodeForTokenAsync(code, CallbackUrl, ct);
            if (!exchanged || string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogWarning("WhatsApp connect: token exchange failed. error={Error}", exchangeError);
                return RedirectToAdmin(success: false, message: exchangeError ?? "Token exchange failed.");
            }

            await _credentialStore.SaveAccessTokenAsync(accessToken, ct);

            // ── Step 2: discover which WABA this token was just granted ──
            var wabaId = await _metaAuth.DiscoverWabaIdAsync(accessToken, ct);
            if (string.IsNullOrWhiteSpace(wabaId))
            {
                return RedirectToAdmin(success: false, message: "Connected to Meta, but couldn't determine which WhatsApp Business Account was granted. Try again.");
            }

            // ── Step 3: resolve the phone number(s) on that WABA ──
            var numbers = await _metaAuth.GetWabaPhoneNumbersAsync(wabaId, accessToken, ct);
            if (numbers.Count == 0)
            {
                return RedirectToAdmin(success: false, message: "Connected to Meta, but no phone number was found on this WhatsApp Business Account yet. Try again shortly.");
            }
            var details = numbers[0];
            var phoneNumberId = details.PhoneNumberId;

            // ── Step 4: subscribe our app to this WABA's webhooks ──
            var subscribeResult = await _metaAuth.SubscribeAppToWabaAsync(wabaId, accessToken, ct);

            // ── Step 5: persist (or update) the WhatsAppAccount ──
            var account = await _context.WhatsAppAccounts.FirstOrDefaultAsync(a => a.PhoneNumberId == phoneNumberId, ct);
            var now = DateTime.UtcNow;
            var isNew = account == null;

            if (account == null)
            {
                account = new WhatsAppAccount
                {
                    Id = Guid.NewGuid(),
                    PhoneNumberId = phoneNumberId,
                    CreatedAt = now
                };
                _context.WhatsAppAccounts.Add(account);
            }

            account.Name = details.VerifiedName ?? details.DisplayPhoneNumber ?? phoneNumberId;
            account.DisplayPhoneNumber = details.DisplayPhoneNumber ?? account.DisplayPhoneNumber ?? string.Empty;
            account.WabaId = wabaId;
            account.VerifiedName = details.VerifiedName;
            account.ConnectionMode = (byte)WhatsAppConnectionMode.Coexistence;
            account.WebhookSubscriptionStatus = subscribeResult.Success
                ? (byte)WebhookSubscriptionStatus.Subscribed
                : (byte)WebhookSubscriptionStatus.Failed;
            account.IsActive = true;
            account.UpdatedAt = now;

            await _context.SaveChangesAsync(ct);

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = isNew ? "WhatsAppAccountConnected" : "WhatsAppAccountReconnected",
                EntityType = "WhatsAppAccount",
                EntityId = account.Id,
                NewValueJson = $"{{\"wabaId\":\"{wabaId}\",\"phoneNumberId\":\"{phoneNumberId}\",\"subscribed\":{subscribeResult.Success.ToString().ToLowerInvariant()}}}",
                CreatedAt = now
            });
            await _context.SaveChangesAsync(ct);

            if (!subscribeResult.Success)
            {
                _logger.LogWarning("WhatsApp connect: account saved but WABA webhook subscription failed. wabaId={WabaId} error={Error}", wabaId, subscribeResult.ErrorMessage);
                return RedirectToAdmin(success: true, message: $"تم توصيل {account.Name}، لكن الاشتراك في الويب هوك فشل — قد تتأخر الرسائل حتى تتم إعادة المحاولة.");
            }

            return RedirectToAdmin(success: true, message: $"تم توصيل {account.Name} بنجاح.");
        }

        private IActionResult RedirectToAdmin(bool success, string message)
        {
            var param = success ? "whatsappConnected" : "whatsappError";
            return Redirect($"/Admin/WhatsAppAccounts?{param}={Uri.EscapeDataString(message)}");
        }
    }
}
