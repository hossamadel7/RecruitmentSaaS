using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace RecruitmentSaaS.Services
{
    public class ContractParseResult
    {
        public string? PassportNumber { get; set; }  // matched candidate passport
        public string? EmployerName { get; set; }
        public string? TransactionNumber { get; set; }

        // Both extracted passports — controller tries both
        public string? Passport1 { get; set; }  // near "Passport No"
        public string? Passport2 { get; set; }  // near "Passport Number"
    }

    public interface IContractParserService
    {
        ContractParseResult ParsePdf(Stream stream);
        ContractParseResult ParseText(string text);
    }

    public class ContractParserService : IContractParserService
    {
        private static readonly Regex _txnRegex = new(
            @"\b(MB\d{9}[A-Z]{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _passportRegex = new(
            @"\b([A-Z]{1,2}\d{7,9})\b",
            RegexOptions.Compiled);

        private static readonly Regex _employerRegex = new(
            @"Establishment\s+Name\s+([A-Z][A-Z\s\.]+(?:L\.L\.C|LLC|CORP|CO\.|LTD)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public ContractParseResult ParsePdf(Stream stream)
        {
            stream.Position = 0;
            using var pdf = PdfDocument.Open(stream);

            var result = new ContractParseResult();

            foreach (var page in pdf.GetPages())
            {
                var words = page.GetWords().ToList();
                var pageText = string.Join(" ", words.Select(w => w.Text));

                // Transaction number
                if (string.IsNullOrEmpty(result.TransactionNumber))
                {
                    var txn = _txnRegex.Match(pageText);
                    if (txn.Success)
                        result.TransactionNumber = txn.Groups[1].Value.ToUpper();
                }

                // Employer name
                if (string.IsNullOrEmpty(result.EmployerName))
                {
                    var emp = _employerRegex.Match(pageText);
                    if (emp.Success)
                        result.EmployerName = emp.Groups[1].Value.Trim();
                }

                // Find all passport-like words with Y positions
                var passportWords = words
                    .Where(w => Regex.IsMatch(w.Text, @"^[A-Z]{1,2}\d{7,9}$"))
                    .Select(w => new { Passport = w.Text.ToUpper(), Top = w.BoundingBox.Top })
                    .ToList();

                if (passportWords.Count == 0) continue;

                // Find "Passport No" label Y position  → Passport1 (employer)
                for (int i = 1; i < words.Count; i++)
                {
                    if (words[i].Text.Equals("No", StringComparison.OrdinalIgnoreCase) &&
                        words[i - 1].Text.Equals("Passport", StringComparison.OrdinalIgnoreCase))
                    {
                        var labelTop = words[i].BoundingBox.Top;
                        var nearest = passportWords
                            .OrderBy(p => Math.Abs(p.Top - labelTop))
                            .FirstOrDefault();
                        if (nearest != null)
                            result.Passport1 = nearest.Passport;
                        break;
                    }
                }

                // Find "Passport Number" label Y position → Passport2 (employee)
                for (int i = 1; i < words.Count; i++)
                {
                    if (words[i].Text.Equals("Number", StringComparison.OrdinalIgnoreCase) &&
                        words[i - 1].Text.Equals("Passport", StringComparison.OrdinalIgnoreCase))
                    {
                        var labelTop = words[i].BoundingBox.Top;
                        var nearest = passportWords
                            .OrderBy(p => Math.Abs(p.Top - labelTop))
                            .FirstOrDefault();
                        if (nearest != null)
                            result.Passport2 = nearest.Passport;
                        break;
                    }
                }

                // If only one label found, collect both unique passports
                if (result.Passport1 == null && result.Passport2 == null)
                {
                    var unique = passportWords
                        .Select(p => p.Passport)
                        .Where(p => !p.StartsWith("MB") && !p.StartsWith("ST"))
                        .Distinct()
                        .ToList();
                    if (unique.Count >= 1) result.Passport1 = unique[0];
                    if (unique.Count >= 2) result.Passport2 = unique[1];
                }
            }

            return result;
        }

        public ContractParseResult ParseText(string text)
        {
            var result = new ContractParseResult();

            var txn = _txnRegex.Match(text);
            if (txn.Success)
                result.TransactionNumber = txn.Groups[1].Value.ToUpper();

            var emp = _employerRegex.Match(text);
            if (emp.Success)
                result.EmployerName = emp.Groups[1].Value.Trim();

            // Extract all unique passports
            var passports = _passportRegex.Matches(text)
                .Select(m => m.Groups[1].Value.ToUpper())
                .Where(p => !p.StartsWith("MB") && !p.StartsWith("ST"))
                .Distinct()
                .ToList();

            if (passports.Count >= 1) result.Passport1 = passports[0];
            if (passports.Count >= 2) result.Passport2 = passports[1];

            return result;
        }
    }
}
