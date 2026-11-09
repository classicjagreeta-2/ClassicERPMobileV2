using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mobile.Common;

static class B64Url
{
    public static string Enc(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Dec(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

// AES-256-GCM helpers. The key is derived from a secret of at least 32 characters; every message gets its own random nonce and is authenticated
// (a changed byte makes it fail to open). "purpose" is bound into the message so a blob made for one use cannot be opened for another.
static class Gcm
{
    public static byte[] Key(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < 32) throw new InvalidOperationException("An encryption key must be at least 32 characters.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public static string Seal(string secret, string purpose, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Key(secret), 16);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.ASCII.GetBytes(purpose));
        return B64Url.Enc(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public static byte[]? Open(string secret, string purpose, string blob)
    {
        try
        {
            var all = B64Url.Dec(blob);
            if (all.Length < 29) return null;
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(Key(secret), 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Encoding.ASCII.GetBytes(purpose));
            return plain;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return null; }
    }
}

// The client's database password, encrypted at rest in MOBILE_CONTROL.DB_PASSWORD_ENC. Key: held by API 1 and MobileKeyManager only.
public static class SecretBox
{
    const string Purpose = "mobile-dbpwd-v1";
    public static string Encrypt(string secretKey, string plain) => "v1." + Gcm.Seal(secretKey, Purpose, Encoding.UTF8.GetBytes(plain));
    // null = not encrypted with this key / damaged
    public static string? Decrypt(string secretKey, string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith("v1.")) return null;
        var p = Gcm.Open(secretKey, Purpose, stored[3..]);
        return p == null ? null : Encoding.UTF8.GetString(p);
    }
}

// Everything API 2 needs to serve one company, including its database login. Sealed (encrypted + signed) by API 1; the phone only carries it.
public class ClientConfig
{
    public int V { get; set; } = 2;
    public long CtrlId { get; set; }
    public string Company { get; set; } = "";
    public string Mode { get; set; } = "VB";          // VB / MVC
    public string? Loca { get; set; }
    public string? Unit { get; set; }
    public string? MainUnit { get; set; }
    public string? DocYear { get; set; }
    public string DbHost { get; set; } = "";
    public int DbPort { get; set; } = 1521;
    public string DbService { get; set; } = "";
    public string Schema { get; set; } = "";
    public string DbUser { get; set; } = "";
    public string DbPassword { get; set; } = "";
    public string ConfigTable { get; set; } = "MOBILE_MASTER_CONFIG";   // the master setup table inside the client's own database
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public long Iat { get; set; }
    public long Exp { get; set; }                      // unix seconds

    public ControlRow ToControlRow() => new()
    {
        CtrlId = CtrlId, ClientName = Company, Loca = Loca, Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit, MainUnit = MainUnit, DocYear = DocYear,
        IpAddress = DbHost, DatabasePort = DbPort, DatabaseName = DbService, SchemaName = Schema, DbHost = DbHost, DbUser = DbUser, DbPassword = DbPassword,
        ConfigTable = ConfigTable, Active = true, ActiveDate = ValidFrom, Validity = ValidTo
    };
}

public class ConfigSealer
{
    const string Purpose = "mobile-config-v2";
    readonly string _secret;
    public ConfigSealer(string secret) { Gcm.Key(secret); _secret = secret; }   // fails early on a short key

    public string Seal(ClientConfig c) => "c2." + Gcm.Seal(_secret, Purpose, JsonSerializer.SerializeToUtf8Bytes(c));

    // (config, null) when valid; (null, reason) when not: "invalid" (not made by API 1 / changed) or "expired"
    public (ClientConfig? Config, string? Problem) Open(string? blob, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(blob) || blob.Length > 8192 || !blob.StartsWith("c2.")) return (null, "invalid");
        var plain = Gcm.Open(_secret, Purpose, blob[3..]);
        if (plain == null) return (null, "invalid");
        ClientConfig? c;
        try { c = JsonSerializer.Deserialize<ClientConfig>(plain); } catch (JsonException) { return (null, "invalid"); }
        if (c == null || c.V != 2) return (null, "invalid");
        if (c.Exp < (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()) return (null, "expired");
        return (c, null);
    }
}
