using ClosedXML.Excel;
using Mobile.Common;
using Oracle.ManagedDataAccess.Client;

// Loads the "Mobile Control Table" sheet of the Excel into CLASSICNEW.MOBILE_CONTROL.
//   dotnet run --project tools/SeedMobileControl -- "<xlsx>"            dry run: shows what would be written, touches nothing
//   dotnet run --project tools/SeedMobileControl -- "<xlsx>" --apply    writes (connection string from env SEED_CONNECTION)
// Passwords and API keys are hashed here; the clear values of the ones GENERATED (Excel cell blank) are printed once and never stored.
// An existing row (same USER_ID + DATABASE + SCHEMA) is updated but its password / API-key hashes are NOT replaced.
var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? throw new ArgumentException("Give the xlsx path.");
var apply = args.Contains("--apply");
var cs = Environment.GetEnvironmentVariable("SEED_CONNECTION");
if (apply && string.IsNullOrEmpty(cs)) throw new InvalidOperationException("Set SEED_CONNECTION (Oracle connection to CLASSICNEW) for --apply.");

using var wb = new XLWorkbook(path);
var ws = wb.Worksheet("Mobile Control Table");
string? T(IXLRow r, string col) { var s = r.Cell(col).GetString().Trim(); return s.Length == 0 ? null : s; }
DateTime? Dt(IXLRow r, string col)
{
    var c = r.Cell(col);
    if (c.IsEmpty()) return null;
    return c.DataType == XLDataType.DateTime ? c.GetDateTime() : c.TryGetValue<double>(out var d) ? DateTime.FromOADate(d) : null;
}

var rows = new List<(string User, string? Pass, string? Key, bool GenPass, bool GenKey, IXLRow R)>();
foreach (var r in ws.RowsUsed().Where(r => r.RowNumber() >= 3))
{
    var user = T(r, "E"); if (user == null) continue;
    var pass = T(r, "F"); var key = T(r, "M");
    rows.Add((user, pass ?? Hashing.NewApiKey()[..12], key ?? Hashing.NewApiKey(), pass == null, key == null, r));
}

OracleConnection? con = apply ? new OracleConnection(cs) : null;
con?.Open();
foreach (var (user, pass, key, genPass, genKey, r) in rows)
{
    var db = T(r, "K"); var schema = T(r, "L");
    // no Status in the Excel = not released: test rows stay N
    var status = T(r, "Q")?.ToUpperInvariant() == "Y" ? "Y" : "N";
    var unit = T(r, "D");
    Console.WriteLine($"row {r.RowNumber()}: {user,-10} {T(r, "I") ?? "(no name)",-12} {T(r, "G")}:{T(r, "H")} db={db} schema={schema} unit={(unit ?? "-")} mode={(unit == null ? "VB" : "MVC")} status={status}");
    if (con == null) continue;

    using var find = con.CreateCommand();
    find.BindByName = true;
    find.CommandText = "SELECT CTRL_ID FROM CLASSICNEW.MOBILE_CONTROL WHERE UPPER(USER_ID)=UPPER(:u) AND UPPER(NVL(DATABASE_NAME,'-'))=UPPER(NVL(:d,'-')) AND UPPER(NVL(SCHEMA_NAME,'-'))=UPPER(NVL(:s,'-'))";
    find.Parameters.Add("u", OracleDbType.Varchar2).Value = user;
    find.Parameters.Add("d", OracleDbType.Varchar2).Value = (object?)db ?? DBNull.Value;
    find.Parameters.Add("s", OracleDbType.Varchar2).Value = (object?)schema ?? DBNull.Value;
    var existing = find.ExecuteScalar();

    using var cmd = con.CreateCommand();
    cmd.BindByName = true;
    object V(object? o) => o ?? DBNull.Value;
    var common = "LOCA=:loca, UNIT=:unitv, IP_ADDRESS=:ip, HOSTED_PORT=:hp, CLIENT_NAME_DISPLAY=:cname, DATABASE_PORT=:dbp, DATABASE_NAME=:db, SCHEMA_NAME=:schm, " +
                 "PHONE_ID=NVL(PHONE_ID,:phone), TOKEN_MINUTES=:tm, API_CLIENTS=:ac, STATUS=:st, MOBILE_ACTIVE_DATE=:ad, MOBILE_VALIDITY=:mv";
    if (existing != null)
    {
        cmd.CommandText = $"UPDATE CLASSICNEW.MOBILE_CONTROL SET {common} WHERE CTRL_ID=:id";
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = Convert.ToInt64(existing);
    }
    else
    {
        cmd.CommandText = "INSERT INTO CLASSICNEW.MOBILE_CONTROL (CTRL_ID, USER_ID, PASSWORD_HASH, API_KEY_HASH, LOCA, UNIT, IP_ADDRESS, HOSTED_PORT, CLIENT_NAME_DISPLAY, DATABASE_PORT, DATABASE_NAME, SCHEMA_NAME, PHONE_ID, TOKEN_MINUTES, API_CLIENTS, STATUS, MOBILE_ACTIVE_DATE, MOBILE_VALIDITY) " +
                          "VALUES (SEQ_MOBILE_CONTROL.NEXTVAL, :uname, :ph, :kh, :loca, :unitv, :ip, :hp, :cname, :dbp, :db, :schm, :phone, :tm, :ac, :st, :ad, :mv)";
        cmd.Parameters.Add("uname", OracleDbType.Varchar2).Value = user;
        cmd.Parameters.Add("ph", OracleDbType.Varchar2).Value = Hashing.HashPassword(pass!);
        cmd.Parameters.Add("kh", OracleDbType.Varchar2).Value = Hashing.HashApiKey(key!);
    }
    cmd.Parameters.Add("loca", OracleDbType.Varchar2).Value = V(T(r, "C"));
    cmd.Parameters.Add("unitv", OracleDbType.Varchar2).Value = V(unit);
    cmd.Parameters.Add("ip", OracleDbType.Varchar2).Value = V(T(r, "G"));
    cmd.Parameters.Add("hp", OracleDbType.Int32).Value = V(int.TryParse(T(r, "H"), out var hp) ? hp : null);
    cmd.Parameters.Add("cname", OracleDbType.Varchar2).Value = V(T(r, "I"));
    cmd.Parameters.Add("dbp", OracleDbType.Int32).Value = V(int.TryParse(T(r, "J"), out var dbp) ? dbp : 1521);
    cmd.Parameters.Add("db", OracleDbType.Varchar2).Value = V(db);
    cmd.Parameters.Add("schm", OracleDbType.Varchar2).Value = V(schema);
    cmd.Parameters.Add("phone", OracleDbType.Varchar2).Value = V(T(r, "N"));
    cmd.Parameters.Add("tm", OracleDbType.Int32).Value = V(int.TryParse(T(r, "O"), out var tm) ? tm : 720);
    cmd.Parameters.Add("ac", OracleDbType.Varchar2).Value = V(T(r, "P"));
    cmd.Parameters.Add("st", OracleDbType.Varchar2).Value = status;
    cmd.Parameters.Add("ad", OracleDbType.Date).Value = V(Dt(r, "R"));
    cmd.Parameters.Add("mv", OracleDbType.Date).Value = V(Dt(r, "S"));
    cmd.ExecuteNonQuery();
    if (existing == null && (genPass || genKey))
        Console.WriteLine($"   NEW SECRETS for {user} (shown once, not stored): " + (genPass ? $"password={pass}  " : "") + (genKey ? $"api-key={key}" : ""));
}
Console.WriteLine(apply ? "Done." : "Dry run only - nothing written. Add --apply to write.");
con?.Dispose();
