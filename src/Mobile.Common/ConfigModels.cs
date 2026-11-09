namespace Mobile.Common;

// one row of CLASSICNEW.MOBILE_MASTER_CONFIG exactly as stored (used by tools/MobileKeyManager to maintain the table)
public class ConfigRow
{
    public long CfgId { get; set; }
    public long? CtrlId { get; set; }            // null = default for every company; set = override for that company
    public string AppMode { get; set; } = "VB";   // VB / MVC
    public int SeqNo { get; set; } = 100;
    public string ItemKey { get; set; } = "";
    public string? Description { get; set; }
    public string OpType { get; set; } = "READ";  // CONTROL / READ / UPLOAD_INSERT / UPDATE_COLUMN / DEVICE_CHECK
    public string SchemaSource { get; set; } = "CLIENT";   // CLIENT = the company's schema, CLASSICNEW
    public string TableName { get; set; } = "";
    public string? ColumnList { get; set; }       // CODE,DESCN,PTYPE
    public string? Aliases { get; set; }          // OUTNAME=SOURCECOLUMN,...
    public string? FilterColumn { get; set; }
    public string? FilterValues { get; set; }     // W,S
    public bool UnitFilter { get; set; }          // MVC: LOCA + UNIT
    public bool DocYearFilter { get; set; }       // MVC: + DOC_YEAR
    public bool RequireListing { get; set; }      // VB: must be listed in MASTER_TABLE_LOCATION (when the company has a MAIN_UNIT)
    public bool AllowClients { get; set; } = true;
    public string? UpdateKeyCol { get; set; }
    public string? UpdateCol { get; set; }
    public int? UpdateMaxLen { get; set; }
    public bool Active { get; set; } = true;
    public string? Remarks { get; set; }
    public string? Options { get; set; }          // Key=Value;Key=Value

    public ConfigRow Clone() => (ConfigRow)MemberwiseClone();
    public static readonly string[] OpTypes = { "READ", "UPLOAD_INSERT", "UPDATE_COLUMN", "DEVICE_CHECK", "CONTROL" };
    public static readonly string[] DedicatedKeys = { "face-consent", "face-recognition", "import-eye", "device-check", "user-login", "employee" };
}
