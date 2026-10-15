using System.Security.Cryptography;
using System.Text;

namespace RecruitmentSaaS.Services;

public static class PasswordSecurity
{
    public static string Hash(string password) => "bcrypt:" + BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    public static bool Verify(string stored, string supplied, out bool upgrade)
    {
        upgrade = false;
        if (stored.StartsWith("bcrypt:", StringComparison.Ordinal))
        {
            try { return BCrypt.Net.BCrypt.Verify(supplied, stored[7..]); }
            catch { return false; }
        }
        // Transitional legacy verification; upgrade immediately on successful authentication.
        upgrade = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(stored)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
        return upgrade;
    }
}
