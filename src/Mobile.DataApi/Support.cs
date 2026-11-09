using Mobile.Common;
using Oracle.ManagedDataAccess.Client;

namespace Mobile.DataApi;

// What one request knows: the control row behind the token, and its merged MOBILE_MASTER_CONFIG items.
public class Ctx
{
    public ControlRow Row { get; }
    public List<MasterItem> Items { get; }
    public Ctx(ControlRow row, List<MasterItem> items) { Row = row; Items = items; }

    public MasterItem? Item(string key) => Items.FirstOrDefault(i => i.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase));
    public string Table(MasterItem m) => MasterQuery.Schema(m, Row) + "." + m.Table;

    // MVC: " AND LOCA = :loca AND UNIT = :unit [AND DOC_YEAR = :docyear]" (VB: nothing). Values come from the control row, never from the phone.
    public string ScopeSql(MasterItem m)
    {
        if (!m.UnitFilter) return "";
        return " AND LOCA = :loca AND UNIT = :unit" + (m.DocYearFilter ? " AND DOC_YEAR = :docyear" : "");
    }

    public void BindScope(OracleCommand c, MasterItem m)
    {
        if (!m.UnitFilter) return;
        c.Parameters.Add("loca", OracleDbType.Varchar2).Value = Row.Loca;
        c.Parameters.Add("unit", OracleDbType.Varchar2).Value = Row.Unit;
        if (m.DocYearFilter) c.Parameters.Add("docyear", OracleDbType.Varchar2).Value = Row.EffectiveDocYear;
    }

    // extra columns for an INSERT into a scoped (MVC) table
    public void AddScopeColumns(OracleCommand c, MasterItem m, List<string> cols, List<string> vals)
    {
        if (!m.UnitFilter) return;
        cols.Add("LOCA"); vals.Add(":loca"); cols.Add("UNIT"); vals.Add(":unit");
        c.Parameters.Add("loca", OracleDbType.Varchar2).Value = Row.Loca;
        c.Parameters.Add("unit", OracleDbType.Varchar2).Value = Row.Unit;
        if (m.DocYearFilter) { cols.Add("DOC_YEAR"); vals.Add(":docyear"); c.Parameters.Add("docyear", OracleDbType.Varchar2).Value = Row.EffectiveDocYear; }
    }
}

public static class Api
{
    public static IResult Fail(int status, string message) => Results.Json(new { success = false, message }, statusCode: status);

    public static OracleCommand Cmd(OracleConnection con, OracleTransaction? tx, string sql, params (string Name, string? Value)[] ps)
    {
        var c = con.CreateCommand();
        c.Transaction = tx;
        c.BindByName = true;
        c.CommandText = sql;
        foreach (var (n, v) in ps) c.Parameters.Add(n, OracleDbType.Varchar2).Value = (object?)v ?? DBNull.Value;
        return c;
    }

    public static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static async Task<IResult> Guard(ILogger log, string what, Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "{What} failed", what); return Fail(500, "The request could not be completed."); }
    }

    // The phone signs the ERP user in itself (user-login master); face / consent / attendance calls name that user in header x-app-user.
    // The user must exist in the 'user-login' table (and pass its role filter) - checked here so the audit columns cannot be made up.
    public static async Task<(IResult? Error, string By)> AppUser(HttpContext http, Ctx ctx, Func<ControlRow, OracleConnection> open, CancellationToken ct)
    {
        var user = http.Request.Headers["x-app-user"].ToString().Trim();
        if (user.Length is 0 or > 30) return (Fail(400, "Send the signed-in user in header x-app-user."), "");
        var login = ctx.Item("user-login");
        if (login == null || login.Columns.Count == 0) return (Fail(503, "user-login is not configured for this client."), "");
        using var con = open(ctx.Row);
        await con.OpenAsync(ct);
        var sql = $"SELECT COUNT(*) FROM {ctx.Table(login)} WHERE UPPER(TRIM({login.Columns[0]})) = UPPER(:u)";
        using var c = con.CreateCommand();
        c.BindByName = true;
        c.Parameters.Add("u", OracleDbType.Varchar2).Value = user;
        if (login.FilterColumn != null && login.FilterValues.Count > 0)
        {
            var names = new List<string>();
            for (var i = 0; i < login.FilterValues.Count; i++) { names.Add("UPPER(:f" + i + ")"); c.Parameters.Add("f" + i, OracleDbType.Varchar2).Value = login.FilterValues[i]; }
            sql += $" AND UPPER(TRIM({login.FilterColumn})) IN ({string.Join(", ", names)})";
        }
        c.CommandText = sql;
        if (Convert.ToInt32(await c.ExecuteScalarAsync(ct)) == 0) return (Fail(403, "This user is not allowed to use the mobile app."), "");
        return (null, user);
    }
}
