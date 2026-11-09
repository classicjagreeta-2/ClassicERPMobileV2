using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mobile.Common;

// Identifiers that come from the database (table / column / schema names) are validated before they reach any SQL text.
public static class Ident
{
    static readonly Regex Name = new(@"^[A-Za-z][A-Za-z0-9_$#]{0,29}$", RegexOptions.Compiled);
    static readonly Regex Secret = new("PASSWORD|PASSWD|PWD|ACCESS_STR|SECRET|TOKEN", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsValid(string? s) => s != null && Name.IsMatch(s);
    public static bool LooksLikeCredential(string s) => Secret.IsMatch(s);
    public static string Need(string? s, string what)
    {
        if (!IsValid(s)) throw new InvalidOperationException($"invalid {what} '{s}'");
        return s!;
    }
}

// PBKDF2 for passwords ("v1.<iterations>.<salt>.<hash>"), plain SHA-256 for API keys (32 random bytes, so no salt needed).
public static class Hashing
{
    const int Iterations = 120_000;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(h)}";
    }

    public static bool VerifyPassword(string password, string? stored)
    {
        // a missing hash still costs one PBKDF2 so timing does not tell whether the user has a password
        var parts = (stored ?? "").Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var it) || it < 1000)
        {
            Rfc2898DeriveBytes.Pbkdf2(password, new byte[16], Iterations, HashAlgorithmName.SHA256, 32);
            return false;
        }
        var h = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), it, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(parts[3]));
    }

    public static string HashApiKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool VerifyApiKey(string? key, string? storedHash)
    {
        var a = Encoding.ASCII.GetBytes(HashApiKey(key ?? ""));
        var b = Encoding.ASCII.GetBytes((storedHash ?? "").ToUpperInvariant().PadRight(a.Length)[..a.Length]);
        return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(storedHash) && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string NewApiKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
