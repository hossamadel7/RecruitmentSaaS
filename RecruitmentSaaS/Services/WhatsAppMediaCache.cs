using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Keeps WhatsApp photos / voice notes / files on the server after the first download, so
    /// opening them again doesn't cost two round-trips to Meta (and still works after Meta's
    /// ~30-day media expiry). Stored outside wwwroot — files are only served through the
    /// permission-checked /api/conversations/{id}/messages/{messageId}/media endpoint.
    /// Location: WhatsApp:MediaCachePath, default {ContentRoot}/App_Data/wa-media.
    /// </summary>
    public sealed class WhatsAppMediaCache
    {
        private static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);
        private readonly string _root;

        private WhatsAppMediaCache(string root) => _root = root;

        public static WhatsAppMediaCache For(IWebHostEnvironment env, IConfiguration config)
        {
            var configured = config["WhatsApp:MediaCachePath"];
            return new WhatsAppMediaCache(string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(env.ContentRootPath, "App_Data", "wa-media")
                : configured);
        }

        public async Task<WhatsAppMediaFile?> TryGetAsync(string mediaId, CancellationToken ct)
        {
            if (!SafeId.IsMatch(mediaId)) return null;
            var dataPath = Path.Combine(_root, mediaId);
            var typePath = dataPath + ".type";
            if (!File.Exists(dataPath) || !File.Exists(typePath)) return null;

            try
            {
                return new WhatsAppMediaFile
                {
                    Data = await File.ReadAllBytesAsync(dataPath, ct),
                    ContentType = (await File.ReadAllTextAsync(typePath, ct)).Trim()
                };
            }
            catch (IOException) { return null; } // e.g. being written right now — just fetch from Meta
        }

        /// <summary>Best effort: a cache that can't be written must never break showing or sending media.</summary>
        public async Task SaveAsync(string mediaId, WhatsAppMediaFile file, CancellationToken ct)
        {
            if (!SafeId.IsMatch(mediaId) || file.Data.Length == 0) return;
            try
            {
                Directory.CreateDirectory(_root);
                var dataPath = Path.Combine(_root, mediaId);
                var tmp = dataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tmp, file.Data, ct);
                File.Move(tmp, dataPath, overwrite: true);   // never serve a half-written file
                await File.WriteAllTextAsync(dataPath + ".type", file.ContentType, ct);
            }
            catch (Exception) { /* disk full / permissions — the request still succeeds */ }
        }
    }
}
