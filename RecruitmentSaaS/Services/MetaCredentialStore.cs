using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Resolves the Meta access token to use for Graph API calls. Prefers the
    /// Business Integration System User token obtained via Embedded Signup (stored in
    /// the database, since it can change over time and isn't something we can reliably
    /// rewrite into appsettings/environment variables at runtime); falls back to the
    /// manually-configured WhatsApp:AccessToken for accounts set up before Embedded
    /// Signup existed. Never exposes the token itself outside this service.
    /// </summary>
    public interface IMetaCredentialStore
    {
        Task<string?> GetCurrentAccessTokenAsync(CancellationToken ct = default);

        Task SaveAccessTokenAsync(string accessToken, CancellationToken ct = default);
    }

    public class MetaCredentialStore : IMetaCredentialStore
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IConfiguration _config;

        public MetaCredentialStore(RecruitmentCrmContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        public async Task<string?> GetCurrentAccessTokenAsync(CancellationToken ct = default)
        {
            var stored = await _context.MetaSystemCredentials
                .AsNoTracking()
                .OrderByDescending(c => c.ObtainedAt)
                .Select(c => c.AccessToken)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(stored))
                return stored;

            var configured = _config["WhatsApp:AccessToken"];
            return string.IsNullOrWhiteSpace(configured) ? null : configured;
        }

        public async Task SaveAccessTokenAsync(string accessToken, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            // Single evolving row — Embedded Signup's system-user token isn't per-WABA,
            // so each successful connect just refreshes the one credential we keep.
            var existing = await _context.MetaSystemCredentials.OrderByDescending(c => c.ObtainedAt).FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                existing.AccessToken = accessToken;
                existing.ObtainedAt = now;
                existing.UpdatedAt = now;
            }
            else
            {
                _context.MetaSystemCredentials.Add(new MetaSystemCredential
                {
                    Id = Guid.NewGuid(),
                    AccessToken = accessToken,
                    ObtainedAt = now,
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync(ct);
        }
    }
}
