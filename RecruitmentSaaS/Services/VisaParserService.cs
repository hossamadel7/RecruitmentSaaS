using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RecruitmentSaaS.Services
{
    public class VisaExtractedData
    {
        public string? PassportNumber { get; set; }
        public string? VisaNumber { get; set; }
        public DateOnly? VisaExpiry { get; set; }
        public string? FullName { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(PassportNumber);
    }

    public interface IVisaParserService
    {
        VisaExtractedData ParsePdf(Stream pdfStream);
        VisaExtractedData ParseText(string text);
    }

    public class VisaParserService : IVisaParserService
    {
        // ── Extract text from PDF ────────────────────────────────────────────
        public VisaExtractedData ParsePdf(Stream pdfStream)
        {
            var sb = new System.Text.StringBuilder();
            using var pdf = PdfDocument.Open(pdfStream);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);

            return ParseText(sb.ToString());
        }

        // ── Parse extracted text ─────────────────────────────────────────────
        public VisaExtractedData ParseText(string text)
        {
            var result = new VisaExtractedData();

            // Passport Number
            // Passport Number
            // Pattern 1: "Passport No. : Normal / GAWAZ1" → grab after last /
            var passportMatch = Regex.Match(text,
                @"Passport\s+No\.?\s*:\s*\w+\s*/\s*([A-Z][A-Z0-9]{2,9})\b",
                RegexOptions.IgnoreCase);
            if (passportMatch.Success)
                result.PassportNumber = passportMatch.Groups[1].Value.Trim().ToUpper();

            // Pattern 2: "Passport No. : GAWAZ1" → direct (no Normal /)
            if (string.IsNullOrEmpty(result.PassportNumber))
            {
                var direct = Regex.Match(text,
                    @"Passport\s+No\.?\s*:\s*([A-Z][A-Z0-9]{2,9})\b",
                    RegexOptions.IgnoreCase);
                if (direct.Success)
                    result.PassportNumber = direct.Groups[1].Value.Trim().ToUpper();
            }

            // Visa / Entry Permit Number
            // Pattern: "ENTRY PERMIT NO : 201/2026/11400251919" or "إذن دخول رقم : 201/2026/11400251919"
            var visaMatch = Regex.Match(text,
                @"(?:ENTRY\s+PERMIT\s+NO|PERMIT\s+ENTRY\s+NO|إذن\s+دخول\s+رقم)\s*:?\s*([\d/]+)",
                RegexOptions.IgnoreCase);
            if (visaMatch.Success)
                result.VisaNumber = visaMatch.Groups[1].Value.Trim();

            // Expiry Date
            // Patterns: "Valid Until : 20-04-2026" or "تاريخ صلاحية الدخول : 2026-04-20"
            var expiryMatch = Regex.Match(text,
                @"(?:Valid\s+Until|Until\s+Valid|تاريخ\s+صلاحية\s+الدخول)\s*:?\s*(\d{2}[-/]\d{2}[-/]\d{4}|\d{4}[-/]\d{2}[-/]\d{2})",
                RegexOptions.IgnoreCase);
            if (expiryMatch.Success)
            {
                var dateStr = expiryMatch.Groups[1].Value.Trim();
                result.VisaExpiry = ParseDate(dateStr);
            }

            // Full Name
            // Pattern: "Full Name : Mr. KHALIL BAHAGAT KHALIL BAHAGAT TORKY"
            var nameMatch = Regex.Match(text,
                @"Full\s+Name\s*:\s*(?:Mr\.|Mrs\.|Ms\.)?\s*([A-Z][A-Z\s]+?)(?:\n|\r|$|\s{2,})",
                RegexOptions.IgnoreCase);
            if (nameMatch.Success)
                result.FullName = CleanName(nameMatch.Groups[1].Value);

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static DateOnly? ParseDate(string dateStr)
        {
            // Try dd-MM-yyyy or dd/MM/yyyy
            if (Regex.IsMatch(dateStr, @"^\d{2}[-/]\d{2}[-/]\d{4}$"))
            {
                var parts = dateStr.Split('-', '/');
                if (int.TryParse(parts[0], out int d) &&
                    int.TryParse(parts[1], out int m) &&
                    int.TryParse(parts[2], out int y))
                    return new DateOnly(y, m, d);
            }
            // Try yyyy-MM-dd or yyyy/MM/dd
            if (Regex.IsMatch(dateStr, @"^\d{4}[-/]\d{2}[-/]\d{2}$"))
            {
                var parts = dateStr.Split('-', '/');
                if (int.TryParse(parts[0], out int y) &&
                    int.TryParse(parts[1], out int m) &&
                    int.TryParse(parts[2], out int d))
                    return new DateOnly(y, m, d);
            }
            return null;
        }

        private static string CleanName(string name)
        {
            // Remove duplicate words (e.g., "KHALIL BAHAGAT KHALIL BAHAGAT TORKY" → "KHALIL BAHAGAT TORKY")
            var words = name.Trim().ToUpper().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var unique = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                // Check if this word starts a repeated sequence
                bool isRepeat = false;
                for (int j = 0; j < i; j++)
                {
                    if (words[j] == words[i])
                    {
                        isRepeat = true;
                        break;
                    }
                }
                if (!isRepeat) unique.Add(words[i]);
            }
            return string.Join(" ", unique);
        }
    }
}