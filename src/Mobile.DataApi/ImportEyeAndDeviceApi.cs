using System.Text.Json.Nodes;
using Mobile.Common;
using Oracle.ManagedDataAccess.Client;
using static Mobile.DataApi.Api;

namespace Mobile.DataApi;

// the unique index refused a row that could not be matched to a stored one
public class ImportEyeDuplicateException : Exception
{
    public int Row { get; }
    public ImportEyeDuplicateException(int row, Exception inner) : base("Row " + row + " already exists.", inner) { Row = row; }
}

// POST /mobile/v1/import-eye  - attendance / plucking rows into IMPORT_EYE. Ported from the old ImportEye.cs: same validation
// (ImportEyeParser), one transaction (any failure saves nothing), duplicates answered with the stored ID.
// Table, MaxRows, SkipDuplicates, ForceMcid, Sequence come from the 'import-eye' item (OPTIONS); LOCA/UNIT (MVC) from the control row.
public static class ImportEyeApi
{
    public static void Map(RouteGroupBuilder v1, Func<ControlRow, OracleConnection> open, ILogger log)
    {
        v1.MapMethods("/import-eye", new[] { "GET", "PUT", "PATCH", "DELETE" }, () => Fail(405, "Use POST /mobile/v1/import-eye to send attendance.")).ExcludeFromDescription();

        v1.MapPost("/import-eye", (HttpContext c, JsonObject body) => Guard(log, "import-eye", async () =>
        {
            var ctx = (Ctx)c.Items["ctx"]!; var ct = c.RequestAborted;
            var item = ctx.Item("import-eye");
            if (item == null) return Fail(503, "Attendance upload is not set up for this client.");
            var (err, by) = await AppUser(c, ctx, open, ct); if (err != null) return err;
            var maxRows = Math.Clamp(item.OptInt("MaxRows", 500), 1, 5000);
            var skipDup = item.OptBool("SkipDuplicates", true);
            var mcid = item.Opt("ForceMcid", "");
            var seq = item.Opt("Sequence", "");
            if (seq != "" && !Ident.IsValid(seq)) return Fail(500, "The request could not be completed.");

            var items = body["rows"] is JsonArray a ? a.ToList() : new List<JsonNode?> { body };
            if (items.Count == 0) return Fail(400, "No rows.");
            if (items.Count > maxRows) return Fail(400, $"Up to {maxRows} rows in one call.");
            var rows = new List<Dictionary<string, object?>>(); var errors = new List<object>();
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject o) { errors.Add(new { row = i + 1, errors = new[] { "must be an object." } }); continue; }
                var errs = ImportEyeParser.Parse(o, out var values, mcid);
                if (errs.Count > 0) errors.Add(new { row = i + 1, errors = errs }); else rows.Add(values);
            }
            if (errors.Count > 0) return Results.Json(new { success = false, message = "Nothing was saved. Fix the rows listed.", errors }, statusCode: 400);

            var table = ctx.Table(item);
            using var con = open(ctx.Row); await con.OpenAsync(ct);
            using var tx = con.BeginTransaction();
            var ids = new List<long>(); var dups = new List<int>();
            try
            {
                foreach (var r in rows)
                {
                    if (skipDup && await FindSameKey(con, tx, ctx, item, table, r, ct) is { } old) { ids.Add(old); dups.Add(ids.Count); continue; }
                    var cols = new List<string> { "U_NAME", "U_ENTDT", "O_USER", "T_ID" };
                    var vals = new List<string> { ":u_name", "SYSDATE", ":o_user", ":t_id" };
                    if (seq != "") { cols.Insert(0, "ID"); vals.Insert(0, $"{MasterQuery.Schema(item, ctx.Row)}.{seq}.NEXTVAL"); }
                    using var cmd = con.CreateCommand(); cmd.Transaction = tx; cmd.BindByName = true;
                    cmd.Parameters.Add("u_name", OracleDbType.Varchar2).Value = by.Length > 10 ? by[..10] : by;
                    cmd.Parameters.Add("o_user", OracleDbType.Varchar2).Value = by;
                    cmd.Parameters.Add("t_id", OracleDbType.Varchar2).Value = "MOBILEAPI";
                    var n = 0;
                    foreach (var (col, v) in r)
                    {
                        cols.Add(col); vals.Add(":p" + n);
                        var p = v switch { DateTime => cmd.Parameters.Add("p" + n, OracleDbType.Date), decimal => cmd.Parameters.Add("p" + n, OracleDbType.Decimal), _ => cmd.Parameters.Add("p" + n, OracleDbType.Varchar2) };
                        p.Value = v; n++;
                    }
                    ctx.AddScopeColumns(cmd, item, cols, vals);   // MVC: LOCA / UNIT from the control row
                    cmd.CommandText = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)}) RETURNING ID INTO :rid";
                    var rid = cmd.Parameters.Add("rid", OracleDbType.Int64, System.Data.ParameterDirection.Output);
                    try { await cmd.ExecuteNonQueryAsync(ct); }
                    catch (OracleException ex) when (ex.Number == 1)   // unique index: the same row arrived twice at once; only this statement is undone
                    {
                        if (!skipDup || await FindSameKey(con, tx, ctx, item, table, r, ct) is not { } same) throw new ImportEyeDuplicateException(ids.Count + 1, ex);
                        ids.Add(same); dups.Add(ids.Count); continue;
                    }
                    ids.Add(Convert.ToInt64(rid.Value.ToString()));
                }
                tx.Commit();
            }
            catch (ImportEyeDuplicateException ex)
            {
                return Fail(409, $"Nothing was saved. Row {ex.Row} already exists (same estate, hazira_code, date, employee and app_record_id).");
            }
            var saved = ids.Count - dups.Count;
            log.LogInformation("IMPORT_EYE: {Count} row(s) from {By} for control {Id}, {Dup} already stored", ids.Count, by, ctx.Row.CtrlId, dups.Count);
            return Results.Json(new { success = true, count = ids.Count, saved, alreadyReceived = dups,
                message = dups.Count > 0 ? "Already received: row(s) " + string.Join(", ", dups) + " (not saved again)." : null, ids }, statusCode: saved > 0 ? 201 : 200);
        }));
    }

    // ID of the stored row with the same estate + hazira_code + date + worker + app_record_id (empty = empty), or null
    static async Task<long?> FindSameKey(OracleConnection con, OracleTransaction tx, Ctx ctx, MasterItem item, string table, Dictionary<string, object?> r, CancellationToken ct)
    {
        using var c = con.CreateCommand(); c.Transaction = tx; c.BindByName = true;
        var conds = new List<string>();
        foreach (var col in new[] { "TEAESTATE", "HAZIRA_CODE", "TRANSDATE", "ECODE", "APP_RECORD_ID" })
        {
            r.TryGetValue(col, out var v); var name = col.ToLowerInvariant();
            if (col == "TRANSDATE") { conds.Add($"TRUNC({col}) = :{name}"); c.Parameters.Add(name, OracleDbType.Date).Value = v; }
            else { conds.Add($"NVL({col}, CHR(1)) = :{name}"); c.Parameters.Add(name, OracleDbType.Varchar2).Value = v ?? "\u0001"; }
        }
        c.CommandText = $"SELECT MIN(ID) FROM {table} WHERE {string.Join(" AND ", conds)}{ctx.ScopeSql(item)}";
        ctx.BindScope(c, item);
        var id = await c.ExecuteScalarAsync(ct);
        return id is null or DBNull ? null : Convert.ToInt64(id.ToString());
    }
}

// GET /mobile/v1/auth/device-check?username=<user>[&deviceId=<this phone>]
//   allowed = false when the user has 2+ different device ids, or (deviceId sent) one that is not this phone's. Device ids are never returned.
// Table / columns come from the 'device-check' item (COLUMN_LIST = user column, device column).
public static class DeviceCheckApi
{
    public static void Map(RouteGroupBuilder v1, Func<ControlRow, OracleConnection> open, ILogger log)
    {
        v1.MapGet("/auth/device-check", (HttpContext c, string? username, string? deviceId) => Guard(log, "device check", async () =>
        {
            var ctx = (Ctx)c.Items["ctx"]!; var ct = c.RequestAborted;
            var item = ctx.Item("device-check");
            if (item == null || item.Columns.Count < 2) return Fail(503, "Device check is not set up for this client.");
            username = username?.Trim(); deviceId = deviceId?.Trim();
            if (string.IsNullOrEmpty(username) || username.Length > 60) return Fail(400, "username is required (up to 60 characters).");
            if (deviceId is { Length: > 100 }) return Fail(400, "deviceId is too long.");
            var (uc, dc) = (item.Columns[0], item.Columns[1]);
            using var con = open(ctx.Row); await con.OpenAsync(ct);
            using var cmd = con.CreateCommand(); cmd.BindByName = true;
            cmd.CommandText = $"SELECT DISTINCT TRIM({dc}) FROM {ctx.Table(item)} WHERE UPPER(TRIM({uc})) = UPPER(:u) AND TRIM({dc}) IS NOT NULL";
            cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = username;
            var devices = new List<string>();
            using (var rd = await cmd.ExecuteReaderAsync(ct)) while (await rd.ReadAsync(ct)) devices.Add(rd.GetString(0));
            if (devices.Count > 1)
                return Results.Json(new { success = true, allowed = false, reason = "MULTIPLE_DEVICES", message = "This user is registered on more than one device. Login is not allowed.", deviceCount = devices.Count });
            if (!string.IsNullOrEmpty(deviceId) && devices.Count == 1 && !string.Equals(devices[0], deviceId, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { success = true, allowed = false, reason = "DEVICE_MISMATCH", message = "This user is registered on a different device. Login is not allowed.", deviceCount = 1 });
            return Results.Json(new { success = true, allowed = true, deviceCount = devices.Count });
        }));
    }
}
