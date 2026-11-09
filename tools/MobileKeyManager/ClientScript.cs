using System.Reflection;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace MobileKeyManager;

// Runs the embedded script sql\03_client_mobile_master_config.sql (creates MOBILE_MASTER_CONFIG + its sequence in the connected user's schema and
// seeds the default masters). The script is idempotent: running it on a database that already has the table changes nothing.
public static class ClientScript
{
    public static async Task RunAsync(string connectionString, CancellationToken ct = default)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("client_master_config.sql")
                      ?? throw new InvalidOperationException("The setup script is missing from this build.");
        var text = await new StreamReader(s).ReadToEndAsync(ct);
        using var con = new OracleConnection(connectionString);
        await con.OpenAsync(ct);
        // statements are separated by a line holding only "/"
        foreach (var chunk0 in Regex.Split(text, @"^\s*/\s*$", RegexOptions.Multiline))
        {
            var chunk = chunk0.Trim();
            if (Regex.Replace(chunk, @"^--.*$", "", RegexOptions.Multiline).Trim().Length == 0) continue;   // comments only
            using var cmd = con.CreateCommand();
            cmd.CommandText = chunk;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
