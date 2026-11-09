using System.Text.Json;

namespace MobileKeyManager;

// What is remembered between runs. NEVER the database password, a key or a password.
public class AppSettings
{
    public string Host { get; set; } = "103.234.116.64";
    public string Port { get; set; } = "1521";
    public string Service { get; set; } = "CLASSICDEV";
    public string User { get; set; } = "CLASSICNEW";
    public string Api1Address { get; set; } = "";   // printed on the phone setup card, e.g. http://103.234.116.64:90/

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobileKeyManager");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* settings are a convenience only */ }
    }

    // who did what to which company and when - no key, no password, no connection details
    public static void Audit(string action, long ctrlId, string company, string login)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "audit.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{Environment.UserName}@{Environment.MachineName}\t{action}\tcontrol={ctrlId}\t{company}\t{login}{Environment.NewLine}");
        }
        catch { }
    }

    // master data setup changes
    public static void AuditConfig(string action, long cfgId, string scope, string key)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "audit.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{Environment.UserName}@{Environment.MachineName}\t{action}\tconfig={cfgId}\t{scope}\t{key}{Environment.NewLine}");
        }
        catch { }
    }

    public static string AuditPath => Path.Combine(Dir, "audit.log");
}

// The key that encrypts the client database passwords stored in MOBILE_CONTROL (the same key is set on API 1 as Gateway__DbPasswordKey).
// Taken from the environment variable MOBILEKEYMANAGER_DBPWD_KEY if set; otherwise from this Windows user's protected store (DPAPI):
// the file is useless on another PC or for another Windows user.
public static class KeyStore
{
    const string Env = "MOBILEKEYMANAGER_DBPWD_KEY";
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobileKeyManager", "dbpwd.key");

    public static string? Get()
    {
        var e = Environment.GetEnvironmentVariable(Env);
        if (!string.IsNullOrWhiteSpace(e)) return e.Trim();
        try
        {
            if (!File.Exists(FilePath)) return null;
            return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    public static string Source => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Env)) ? "environment variable " + Env : File.Exists(FilePath) ? "this Windows user's protected store" : "not set";

    public static void Set(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(key.Trim()), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    }
}

// puts a secret on the clipboard and removes it again after a minute, if nothing else has replaced it
public static class ClipboardGuard
{
    public static void Copy(string text)
    {
        Clipboard.SetText(text);
        var ctx = SynchronizationContext.Current;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            ctx?.Post(_ => { try { if (Clipboard.ContainsText() && Clipboard.GetText() == text) Clipboard.Clear(); } catch { } }, null);
        });
    }
}
