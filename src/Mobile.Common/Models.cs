namespace Mobile.Common;

public enum AppMode { VB, MVC }

// one row of CLASSICNEW.MOBILE_CONTROL
public class ControlRow
{
    public long CtrlId { get; set; }
    public string? Loca { get; set; }
    public string? Unit { get; set; }
    public string? MainUnit { get; set; }
    public string UserId { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? ApiKeyHash { get; set; }
    public string IpAddress { get; set; } = "";
    public int HostedPort { get; set; }
    public string ClientName { get; set; } = "";
    public int DatabasePort { get; set; } = 1521;
    public string DatabaseName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string? PhoneId { get; set; }
    // the client's database login: DbUser and the encrypted password are stored in MOBILE_CONTROL (DbHost blank = IpAddress);
    // DbPassword (clear) only ever exists in memory: API 1 puts it in the sealed config, API 2 reads it from there
    public string? DbUser { get; set; }
    public string? DbPasswordEnc { get; set; }
    public string? DbHost { get; set; }
    public string? DbPassword { get; set; }
    public string? ConfigTable { get; set; }
    public string? DocYear { get; set; }
    // DOC_YEAR of the control row; when blank, the 4-digit year at the end of the schema name (PAY_BTFL2027 -> 2027)
    public string? EffectiveDocYear => !string.IsNullOrWhiteSpace(DocYear) ? DocYear.Trim()
        : System.Text.RegularExpressions.Regex.Match(SchemaName ?? "", @"\d{4}$") is { Success: true } m ? m.Value : null;
    public int TokenMinutes { get; set; } = 720;
    public string? ApiClients { get; set; }
    public bool Active { get; set; }
    public DateTime? ActiveDate { get; set; }
    public DateTime? Validity { get; set; }

    // UNIT blank -> old VB app (no unit filter); UNIT filled -> MVC app (filtered by LOCA + UNIT)
    public AppMode Mode => string.IsNullOrWhiteSpace(Unit) ? AppMode.VB : AppMode.MVC;

    public bool IsCurrent(DateTime today) =>
        Active && (ActiveDate == null || today.Date >= ActiveDate.Value.Date) && (Validity == null || today.Date <= Validity.Value.Date);
}

public enum OpType { Control, Read, UploadInsert, UpdateColumn, DeviceCheck }

// one row of CLASSICNEW.MOBILE_MASTER_CONFIG (already merged: client override beats default)
public class MasterItem
{
    public long CfgId { get; set; }
    public AppMode Mode { get; set; }
    public int SeqNo { get; set; }
    public string ItemKey { get; set; } = "";
    public string Description { get; set; } = "";
    public OpType Op { get; set; }
    public bool ClassicNewSchema { get; set; }          // SCHEMA_SOURCE = CLASSICNEW
    public string Table { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public Dictionary<string, string> Aliases { get; set; } = new();   // output name -> source column
    public string? FilterColumn { get; set; }
    public List<string> FilterValues { get; set; } = new();
    public bool UnitFilter { get; set; }                // MVC: LOCA = :loca AND UNIT = :unit
    public bool DocYearFilter { get; set; }             // MVC: also DOC_YEAR = :docyear (only tables that have the column)
    public bool RequireListing { get; set; }
    public bool AllowClients { get; set; } = true;
    public string? UpdateKeyColumn { get; set; }
    public string? UpdateColumn { get; set; }
    public int UpdateMaxLength { get; set; } = 4000;
    public string? Remarks { get; set; }
    // OPTIONS column: "RequireConsent=Y;MaxVectorLength=4000" - settings of the item that used to sit in masters.config.json
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Opt(string name, string fallback) => Options.TryGetValue(name, out var v) && v.Length > 0 ? v : fallback;
    public bool OptBool(string name, bool fallback) => Options.TryGetValue(name, out var v) ? v.Equals("Y", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
    public int OptInt(string name, int fallback) => Options.TryGetValue(name, out var v) && int.TryParse(v, out var n) ? n : fallback;

    public static Dictionary<string, string> ParseOptions(string? s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Length > 0) d[kv[0].Trim()] = kv[1].Trim();
        }
        return d;
    }
}
