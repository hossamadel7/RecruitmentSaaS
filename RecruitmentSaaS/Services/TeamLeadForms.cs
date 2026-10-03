using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Every active TeleSales Manager (Role 7) gets their own team registration form
    /// at /register/{slug}. Rows are created on demand the first time anyone looks.
    /// </summary>
    public static class TeamLeadForms
    {
        private static readonly Regex SlugPattern = new("^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$", RegexOptions.Compiled);

        public static bool IsValidSlug(string slug) => SlugPattern.IsMatch(slug);

        /// <summary>Creates a form row for any active manager that doesn't have one yet.</summary>
        public static async Task EnsureForManagersAsync(RecruitmentCrmContext context, IEnumerable<Guid>? managerIds = null)
        {
            var managersQuery = context.Users.Where(u => u.Role == 7 && u.IsActive);
            if (managerIds != null)
            {
                var ids = managerIds.ToList();
                managersQuery = managersQuery.Where(u => ids.Contains(u.Id));
            }

            var missing = await managersQuery
                .Where(u => !context.TeamLeadForms.Any(f => f.ManagerId == u.Id))
                .Select(u => u.Id)
                .ToListAsync();

            if (missing.Count == 0) return;

            var taken = (await context.TeamLeadForms.Select(f => f.Slug).ToListAsync()).ToHashSet();
            foreach (var managerId in missing)
            {
                string slug;
                do slug = "t-" + RandomCode(6); while (!taken.Add(slug));

                context.TeamLeadForms.Add(new TeamLeadForm
                {
                    Id = Guid.NewGuid(),
                    ManagerId = managerId,
                    Slug = slug,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
        }

        private static string RandomCode(int length)
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789"; // no look-alikes (0/o, 1/l/i)
            return string.Create(length, alphabet, (span, a) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = a[RandomNumberGenerator.GetInt32(a.Length)];
            });
        }
    }
}
