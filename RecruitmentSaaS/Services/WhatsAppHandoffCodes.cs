using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Reference codes ("REF-XXXXXX") embedded in a wa.me pre-filled message. When the customer's
    /// message reaches the webhook, WhatsAppWebhookProcessor.TryConnectHandoffAsync matches the code
    /// and links the conversation to the lead and its assigned agent.
    /// </summary>
    public static class WhatsAppHandoffCodes
    {
        public static async Task<string> GenerateUniqueAsync(RecruitmentCrmContext context)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/I/1 ambiguity

            for (var attempt = 0; attempt < 10; attempt++)
            {
                var suffix = RandomNumberGenerator.GetString(alphabet, 6);
                var code = $"REF-{suffix}";

                var exists = await context.WhatsAppHandoffs.AnyAsync(h => h.ReferenceCode == code);
                if (!exists) return code;
            }

            throw new InvalidOperationException("Could not generate a unique handoff reference code.");
        }
    }
}
