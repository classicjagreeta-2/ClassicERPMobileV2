using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mobile.Common;
using Mobile.DataApi;
using Oracle.ManagedDataAccess.Client;

// API 2 - runs on each client server (the IP:Hosted_port the phone was given). It has NO central database. Every call carries the
// access token API 1 issued (Authorization: Bearer <token>): it says which company / mode / LOCA-UNIT / schema the phone serves, but holds
// no database connection. API 2 connects to the client's database with the login in ITS OWN settings below, reads the master setup table
// (MOBILE_MASTER_CONFIG) and fetches the data it describes.
// Settings (environment variables preferred):
//   DataApi__ConfigKey      the SAME key as Gateway__ConfigKey (>= 32 characters)
//   DataApi__DbService      Oracle service name of the client database (required)
//   DataApi__DbUser         database user (required);  DataApi__DbPassword  its password (required)
//   DataApi__DbHost         Oracle host as seen from THIS server (default localhost);  DataApi__DbPort  (default 1521)
//   DataApi__EnableSwagger  true | false
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();   // runs as a Windows service when started by the service manager; an ordinary console program otherwise
var cfg = builder.Configuration.GetSection("DataApi");
var sealer = new ConfigSealer(cfg["ConfigKey"] ?? "");
string Need(string k) => string.IsNullOrWhiteSpace(cfg[k]) ? throw new InvalidOperationException($"DataApi:{k} is not set.") : cfg[k]!.Trim();
var dbHost = string.IsNullOrWhiteSpace(cfg["DbHost"]) ? "localhost" : cfg["DbHost"]!.Trim();
var dbService = Need("DbService");
foreach (var (v, w) in new[] { (dbService, "DbService"), (dbHost, "DbHost") })
    if (v.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))) throw new InvalidOperationException($"DataApi:{w} has illegal characters.");
var dbPort = int.TryParse(cfg["DbPort"], out var p0) && p0 is > 0 and < 65536 ? p0 : 1521;
var connString = new OracleConnectionStringBuilder
{ UserID = Need("DbUser"), Password = Need("DbPassword"), DataSource = $"//{dbHost}:{dbPort}/{dbService}", ConnectionTimeout = 20 }.ConnectionString;
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = 8 * 1024 * 1024; });
// Swagger UI at /swagger; off unless DataApi__EnableSwagger=true (keep it off on a public server). "Authorize" takes the config from API 1.
var swagger = string.Equals(cfg["EnableSwagger"], "true", StringComparison.OrdinalIgnoreCase);
if (swagger)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o =>
    {
        o.OperationFilter<AppUserHeader>();
        o.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        { Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http, Scheme = "bearer", Description = "The \"config\" value returned by API 1 (POST /gateway/v1/provision)" });
        o.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {{ new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }});
    });
}
var app = builder.Build();
if (swagger) { app.UseSwagger(); app.UseSwaggerUI(); }
var log = app.Logger;

// every company served here uses this server's own database login (DataApi__Db* settings); the token only picks the schema / unit
OracleConnection Open(ControlRow _) => new(connString);

// a company's settings (from its sealed config) and its master list (read from ITS database) are cached for a minute
var cache = new ConcurrentDictionary<string, (DateTime At, ControlRow Row, List<MasterItem> Items)>();
async Task<(ControlRow Row, List<MasterItem> Items)> Load(ClientConfig config, string blob, CancellationToken ct)
{
    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(blob)));
    if (cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromSeconds(60)) return (hit.Row, hit.Items);
    var row = config.ToControlRow();
    var table = row.ConfigTable!;
    var items = await new ControlRepository(connString, table)
        .LoadItems(row, onBad: b => log.LogError("{Table} row skipped for control {Id}: {Why}", table, config.CtrlId, b), ct: ct);
    if (cache.Count > 500) foreach (var old in cache.Where(k => DateTime.UtcNow - k.Value.At > TimeSpan.FromMinutes(5)).Select(k => k.Key).ToList()) cache.TryRemove(old, out _);
    cache[key] = (DateTime.UtcNow, row, items);
    return (row, items);
}

static void Bind(OracleCommand cmd, Dictionary<string, object?> binds)
{
    foreach (var (k, v) in binds) cmd.Parameters.Add(k, OracleDbType.Varchar2).Value = v ?? DBNull.Value;
}

var v1 = app.MapGroup("/mobile/v1");
// every call needs the sealed config API 1 issued: it must open with our key and not be expired
v1.AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    var auth = http.Request.Headers.Authorization.ToString();
    var blob = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : null;
    var (config, problem) = sealer.Open(blob);
    if (config == null)
        // reason: "token-expired" = get a new token from API 1 silently and retry; "token-invalid" = sign in to API 1 again
        return Results.Json(new { success = false, reason = problem == "expired" ? "token-expired" : "token-invalid", message = problem == "expired"
            ? "The saved configuration has expired. Get a new one from API 1."
            : "Not signed in. Send the configuration you got from API 1 as Authorization: Bearer <config>." }, statusCode: 401);
    ControlRow row; List<MasterItem> items;
    try { (row, items) = await Load(config, blob!, http.RequestAborted); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        log.LogError(ex, "Could not read the master setup of control {Id} from its database", config.CtrlId);
        return Results.Json(new { success = false, message = "This company's database could not be reached, or its MOBILE_MASTER_CONFIG table is missing." }, statusCode: 503);
    }
    http.Items["row"] = row; http.Items["items"] = items;
    http.Items["ctx"] = new Ctx(row, items);
    return await next(ctx);
});

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "Mobile.DataApi" }));

static (ControlRow Row, List<MasterItem> Items) Cx(HttpContext c) => ((ControlRow)c.Items["row"]!, (List<MasterItem>)c.Items["items"]!);

// GET /mobile/v1/plan - the masters this company's database says the phone should read, in order, with the call for each
v1.MapGet("/plan", (HttpContext c) =>
{
    var (row, items) = Cx(c);
    return Results.Json(new { success = true, clientName = row.ClientName, mode = row.Mode.ToString(), validTo = row.Validity, syncPlan = PlanBuilder.Build(items) });
});

// GET /mobile/v1/masters/{key}
v1.MapGet("/masters/{key}", async (HttpContext c, string key, CancellationToken ct) =>
{
    var (row, items) = Cx(c);
    var item = items.FirstOrDefault(i => i.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Op == OpType.Read);
    if (item == null) return Results.Json(new { success = false, message = "Unknown master." }, statusCode: 404);
    try
    {
        using var con = Open(row);
        await con.OpenAsync(ct);
        if (MasterQuery.NeedsListingCheck(item, row))   // VB app with a MAIN_UNIT: only tables listed in MASTER_TABLE_LOCATION; no MAIN_UNIT = read the schema directly
        {
            var chk = MasterQuery.BuildListingCheck(item, row);
            using var lc = con.CreateCommand(); lc.BindByName = true; lc.CommandText = chk.Sql; Bind(lc, chk.Binds);
            if (Convert.ToInt32(await lc.ExecuteScalarAsync(ct)) == 0) return Results.Json(new { success = false, message = "Master not available for this unit." }, statusCode: 404);
        }
        var q = MasterQuery.BuildSelect(item, row);
        using var cmd = con.CreateCommand(); cmd.BindByName = true; cmd.CommandText = q.Sql; Bind(cmd, q.Binds);
        using var rd = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await rd.ReadAsync(ct))
        {
            var o = new Dictionary<string, object?>(rd.FieldCount);
            for (var i = 0; i < rd.FieldCount; i++)
            {
                var v = rd.IsDBNull(i) ? null : rd.GetValue(i);
                o[rd.GetName(i)] = v is byte[] b ? Convert.ToBase64String(b) : v is string s ? s.Trim() : v;
            }
            rows.Add(o);
        }
        log.LogInformation("Master {Key} for control {Id}: {N} rows", key, row.CtrlId, rows.Count);
        return Results.Json(new { success = true, key = item.ItemKey, count = rows.Count, rows });
    }
    catch (Exception ex) { log.LogError(ex, "Master {Key} failed for control {Id}", key, row.CtrlId); return Results.Json(new { success = false, message = "The master could not be read." }, statusCode: 500); }
});

// PUT /mobile/v1/masters/{key}/{id}   body { "value": "..." }  sets the ONE column configured as UPDATE_COL (employee face id)
v1.MapPut("/masters/{key}/{id}", async (HttpContext c, string key, string id, JsonObject body, CancellationToken ct) =>
{
    var (row, items) = Cx(c);
    var item = items.FirstOrDefault(i => i.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase));
    if (item?.UpdateColumn == null || item.UpdateKeyColumn == null) return Results.Json(new { success = false, message = "This master cannot be updated." }, statusCode: 404);
    var value = body["value"]?.GetValue<string>();
    if (value != null && value.Length > item.UpdateMaxLength) return Results.Json(new { success = false, message = "Value too long." }, statusCode: 400);
    using var con = Open(row); await con.OpenAsync(ct);
    using var cmd = con.CreateCommand(); cmd.BindByName = true;
    cmd.CommandText = $"UPDATE {MasterQuery.Schema(item, row)}.{item.Table} SET {item.UpdateColumn} = :val WHERE {item.UpdateKeyColumn} = :id" + (item.UnitFilter ? " AND LOCA = :loca AND UNIT = :unit" : "") + (item.UnitFilter && item.DocYearFilter ? " AND DOC_YEAR = :docyear" : "");
    cmd.Parameters.Add("val", OracleDbType.Varchar2).Value = string.IsNullOrEmpty(value) ? DBNull.Value : value;
    cmd.Parameters.Add("id", OracleDbType.Varchar2).Value = id;
    if (item.UnitFilter) { cmd.Parameters.Add("loca", OracleDbType.Varchar2).Value = row.Loca; cmd.Parameters.Add("unit", OracleDbType.Varchar2).Value = row.Unit; }
    if (item.UnitFilter && item.DocYearFilter) cmd.Parameters.Add("docyear", OracleDbType.Varchar2).Value = row.EffectiveDocYear;
    var n =await cmd.ExecuteNonQueryAsync(ct);
    return n == 1 ? Results.Json(new { success = true }) : Results.Json(new { success = false, message = "No such record." }, statusCode: 404);
});

// POST /mobile/v1/upload/{key}   body: JSON array of rows (or { "rows": [...] }). Only the columns in COLUMN_LIST are accepted;
// MVC rows get LOCA/UNIT from the control row (never from the phone). A row that already exists (ORA-00001) is counted as skipped.
v1.MapPost("/upload/{key}", async (HttpContext c, string key, JsonNode body, CancellationToken ct) =>
{
    var (row, items) = Cx(c);
    // face-consent, face-recognition and import-eye have their own calls with business rules; they never go through this generic insert
    var item = items.FirstOrDefault(i => i.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Op == OpType.UploadInsert
                                         && i.ItemKey is not ("face-consent" or "face-recognition" or "import-eye"));
    if (item == null) return Results.Json(new { success = false, message = "Unknown upload." }, statusCode: 404);
    var rows = (body as JsonArray ?? body["rows"] as JsonArray)?.OfType<JsonObject>().ToList();
    if (rows == null || rows.Count == 0 || rows.Count > 500) return Results.Json(new { success = false, message = "Send 1 to 500 rows." }, statusCode: 400);
    var allowed = new HashSet<string>(item.Columns, StringComparer.OrdinalIgnoreCase);
    int inserted = 0, skipped = 0; var errors = new List<string>();
    using var con = Open(row); await con.OpenAsync(ct);
    for (var n = 0; n < rows.Count; n++)
    {
        var cols = new List<string>(); var cmd = con.CreateCommand(); cmd.BindByName = true;
        foreach (var (name, val) in rows[n])
        {
            if (!allowed.Contains(name)) { errors.Add($"row {n + 1}: column {name} not allowed"); cols.Clear(); break; }
            cols.Add(name.ToUpperInvariant());
            var p = cmd.Parameters.Add("p" + (cols.Count - 1), OracleDbType.Varchar2);
            if (val == null) p.Value = DBNull.Value;
            else if (val.GetValueKind() is JsonValueKind.Number) { p.OracleDbType = OracleDbType.Decimal; p.Value = val.GetValue<decimal>(); }
            else
            {
                var s = val.ToString();
                if (s.Length >= 19 && s[4] == '-' && s[10] == 'T' && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) { p.OracleDbType = OracleDbType.TimeStamp; p.Value = dt; }
                else p.Value = s;
            }
        }
        if (cols.Count == 0) { cmd.Dispose(); continue; }
        if (item.UnitFilter) { cols.Add("LOCA"); cols.Add("UNIT"); cmd.Parameters.Add("loca", OracleDbType.Varchar2).Value = row.Loca; cmd.Parameters.Add("unit", OracleDbType.Varchar2).Value = row.Unit; }
        if (item.UnitFilter && item.DocYearFilter) { cols.Add("DOC_YEAR"); cmd.Parameters.Add("docyear", OracleDbType.Varchar2).Value = row.EffectiveDocYear; }
        cmd.CommandText = $"INSERT INTO {MasterQuery.Schema(item, row)}.{item.Table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", cols.Select((cn, i) => cn is "LOCA" or "UNIT" or "DOC_YEAR" ? ":" + cn.Replace("_", "").ToLowerInvariant() : ":p" + i))})";
        try { await cmd.ExecuteNonQueryAsync(ct); inserted++; }
        catch (OracleException ex) when (ex.Number == 1) { skipped++; }
        catch (OracleException ex) { errors.Add($"row {n + 1}: ORA-{ex.Number:00000}"); log.LogWarning("Upload {Key} row {N}: ORA-{Code}", key, n + 1, ex.Number); }
        finally { cmd.Dispose(); }
    }
    log.LogInformation("Upload {Key} for control {Id}: {I} inserted, {S} skipped, {E} errors", key, row.CtrlId, inserted, skipped, errors.Count);
    return Results.Json(new { success = errors.Count == 0, inserted, skipped, errors });
});

FaceApi.Map(v1, Open, log);
ImportEyeApi.Map(v1, Open, log);
DeviceCheckApi.Map(v1, Open, log);

app.Run();

