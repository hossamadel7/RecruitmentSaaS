using System.Text;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Upload checks for candidate documents, contracts and visas. Files are served from our own
    /// domain, so an uploaded .html / .svg would run as the person opening it — only PDF and
    /// JPG/PNG are accepted, and the file's first bytes must match its extension.
    /// </summary>
    public static class SafeUploads
    {
        public static readonly string[] DocumentTypes = { ".pdf", ".jpg", ".jpeg", ".png" };
        public static readonly string[] PdfOnly = { ".pdf" };

        public const string DocumentTypesError = "نوع الملف غير مسموح — مسموح فقط PDF أو صور JPG / PNG";
        public const string PdfOnlyError = "الملف ليس PDF صالح";

        /// <summary>Returns the normalized extension (".pdf", ".jpg", …) or null when the file isn't allowed.</summary>
        public static async Task<string?> CheckAsync(IFormFile file, string[] allowedExtensions)
        {
            var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
            if (!allowedExtensions.Contains(ext)) return null;

            var head = new byte[1024];
            int read;
            using (var stream = file.OpenReadStream())
                read = await stream.ReadAsync(head.AsMemory(0, head.Length));

            var ok = ext switch
            {
                // "%PDF-" may follow a few junk bytes, which PDF readers allow
                ".pdf" => Encoding.ASCII.GetString(head, 0, read).Contains("%PDF-"),
                ".jpg" or ".jpeg" => read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
                ".png" => read >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                                    && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A,
                _ => false
            };
            return ok ? (ext == ".jpeg" ? ".jpg" : ext) : null;
        }

        /// <summary>A file name safe to put in a path: no directories, no "..", no odd characters.</summary>
        public static string SafeFileName(string? name)
        {
            var baseName = Path.GetFileName(name ?? "") ?? "";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(baseName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray())
                .Replace("..", "_").Trim(' ', '.');
            if (cleaned.Length > 80) cleaned = cleaned[..80];
            return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
        }

        // Extensions a browser would run as a page. Served from /uploads they're forced to download.
        private static readonly HashSet<string> ActiveContentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".xhtml", ".shtml", ".svg", ".svgz", ".xml", ".xsl", ".js", ".mjs"
        };

        public static bool IsActiveContent(string path) => ActiveContentExtensions.Contains(Path.GetExtension(path));
    }
}
