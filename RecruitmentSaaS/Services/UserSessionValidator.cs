using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Re-checks every signed-in user against the database so a deactivated user (or one whose
    /// role changed) is logged out instead of keeping a valid cookie — the cookie renews itself
    /// (sliding expiration), so without this a fired employee could stay logged in indefinitely.
    /// The DB lookup is cached for a minute; <see cref="Forget"/> makes a change apply immediately.
    /// </summary>
    public static class UserSessionValidator
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

        private static string Key(Guid userId) => "user-session:" + userId;

        public static async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            var principal = context.Principal;
            if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                await RejectAsync(context);
                return;
            }

            var services = context.HttpContext.RequestServices;
            var cache = services.GetRequiredService<IMemoryCache>();

            if (!cache.TryGetValue(Key(userId), out (bool IsActive, string Role) current))
            {
                var db = services.GetRequiredService<RecruitmentCrmContext>();
                var user = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.IsActive, u.Role })
                    .FirstOrDefaultAsync();

                current = (user?.IsActive == true, user?.Role.ToString() ?? "");
                cache.Set(Key(userId), current, CacheFor);
            }

            if (!current.IsActive || current.Role != principal!.FindFirstValue(ClaimTypes.Role))
                await RejectAsync(context);
        }

        /// <summary>Call after deactivating a user or changing their role so it applies on their next request.</summary>
        public static void Forget(IMemoryCache cache, Guid userId) => cache.Remove(Key(userId));

        private static async Task RejectAsync(CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
