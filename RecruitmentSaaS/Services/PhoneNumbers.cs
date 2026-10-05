namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// One phone format for every lead source (website form, reception, Facebook, Google Sheets,
    /// WhatsApp) so the same person can't become two leads as "01016026436" and "+201016026436".
    /// Egyptian mobiles are stored in local form (01xxxxxxxxx); other numbers as digits only.
    /// </summary>
    public static class PhoneNumbers
    {
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            // Arabic-Indic (٠-٩) and Persian (۰-۹) digits → ASCII; drop spaces, dashes, "+", brackets…
            var digits = new string(raw.Select(c =>
                    c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠')) :
                    c >= '۰' && c <= '۹' ? (char)('0' + (c - '۰')) : c)
                .Where(char.IsAsciiDigit)
                .ToArray());

            if (digits.StartsWith("00")) digits = digits[2..];               // 00201… → 201…

            if (digits.Length == 12 && digits.StartsWith("201"))             // 201xxxxxxxxx → 01xxxxxxxxx
                return "0" + digits[2..];

            if (digits.Length == 10 && digits.StartsWith("1"))               // 1xxxxxxxxx (zero dropped) → 01xxxxxxxxx
                return "0" + digits;

            return digits;
        }

        /// <summary>
        /// Every way an already-stored lead could have this number — leads saved before
        /// normalization may still be in international form. Use for duplicate checks.
        /// </summary>
        public static string[] StoredVariants(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return Array.Empty<string>();
            if (normalized.Length == 11 && normalized.StartsWith("01"))
            {
                var intl = "20" + normalized[1..];
                return new[] { normalized, intl, "+" + intl, "00" + intl };
            }
            return new[] { normalized, "+" + normalized, "00" + normalized };
        }

        /// <summary>The number as WhatsApp wants it (international digits): 01xxxxxxxxx → 201xxxxxxxxx.</summary>
        public static string ToWhatsAppId(string normalized) =>
            normalized.Length == 11 && normalized.StartsWith("01") ? "20" + normalized[1..] : normalized;

        /// <summary>A usable phone number has at least 8 digits after normalizing.</summary>
        public static bool IsValid(string normalized) => normalized.Length is >= 8 and <= 15;
    }
}
