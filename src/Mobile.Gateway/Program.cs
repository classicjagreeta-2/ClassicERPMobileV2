using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Mobile.Common;

// API 1 - the one central address the phone is given. Called the FIRST time (and about once a month, or when the saved config expires):
// it checks the app login against CLASSICNEW.MOBILE_CONTROL and returns a SEALED CONFIG for the phone to save. The config holds everything API 2
// needs to serve that company - including the client's database login - but it is encrypted and signed with a key the phone never has, so the
// phone only carries it. From then on the phone calls the company's API 2 directly with the saved config.
// Settings (environment variables preferred):
//   Gateway__ConnectionString   Oracle connection to CLASSICNEW (reads MOBILE_CONTROL)
//   Gateway__ConfigKey          seals the config; the SAME key is set on every API 2 (>= 32 characters)
//   Gateway__DbPasswordKey      decrypts the client database passwords stored in MOBILE_CONTROL; shared with MobileKeyManager only (>= 32 characters)
//   Gateway__Api2Scheme         http (default) | https;  Gateway__EnableSwagger  true | false
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();   // runs as a Windows service when started by the service manager; an ordinary console program otherwise
var cfg = builder.Configuration.GetSection("Gateway");
var cs = cfg["ConnectionString"] ?? throw new InvalidOperationException("Gateway:ConnectionString is not set.");
var sealer = new ConfigSealer(cfg["ConfigKey"] ?? "");
var dbPwKey = cfg["DbPasswordKey"] ?? "";
if (dbPwKey.Length < 32) throw new InvalidOperationException("Gateway:DbPasswordKey must be at least 32 characters.");
var scheme = cfg["Api2Scheme"] is "https" ? "https" : "http";
var repo = new ControlRepository(cs);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("provision", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
// Swagger UI at /swagger; off unless Gateway__EnableSwagger=true (keep it off on the public server)
var swagger = string.Equals(cfg["EnableSwagger"], "true", StringComparison.OrdinalIgnoreCase);
if (swagger) { builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(o => o.OperationFilter<Mobile.Gateway.ProvisionHeaders>()); }

var app = builder.Build();
if (swagger) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseRateLimiter();
var log = app.Logger;

app.MapGet("/health", () => Results.Json(new { status = "ok", service = "Mobile.Gateway" }));

// POST /gateway/v1/provision   headers: x-username, x-password, x-api-key, x-device-id
app.MapPost("/gateway/v1/provision", async (HttpContext c, CancellationToken ct) =>
{
    var h = c.Request.Headers;
    string user = h["x-username"].ToString().Trim(), pass = h["x-password"].ToString(), key = h["x-api-key"].ToString(), device = h["x-device-id"].ToString().Trim();
    if (user.Length is 0 or > 30 || pass.Length is 0 or > 200 || key.Length is 0 or > 100 || device.Length is 0 or > 60)
        return Results.Json(new { success = false, message = "Send x-username, x-password, x-api-key and x-device-id as headers." }, statusCode: 400);

    const string bad = "Login, password or API key is incorrect, or the mobile access is not active.";
    var row = await repo.FindActiveByUser(user, ct);
    // password and key are always both verified, so timing does not tell which part was wrong
    var okPass = Hashing.VerifyPassword(pass, row?.PasswordHash);
    var okKey = Hashing.VerifyApiKey(key, row?.ApiKeyHash);
    if (row == null || !okPass || !okKey || !row.IsCurrent(DateTime.Today))
    {
        log.LogWarning("Provision refused for {User} from {Ip}", user, c.Connection.RemoteIpAddress);
        return Results.Json(new { success = false, message = bad }, statusCode: 401);
    }
    if (!await repo.BindDevice(row, device, ct))
    {
        log.LogWarning("Provision refused for {User}: different device", user);
        return Results.Json(new { success = false, message = "This login is registered to another device." }, statusCode: 403);
    }

    // the client's database login must be saved for the company (MobileKeyManager -> Edit company)
    if (string.IsNullOrWhiteSpace(row.DbUser) || string.IsNullOrEmpty(row.DbPasswordEnc))
    {
        log.LogError("Control {Id} ({User}) has no database login saved", row.CtrlId, user);
        return Results.Json(new { success = false, message = "This company's database login is not set up yet. Ask the administrator." }, statusCode: 503);
    }
    var dbPassword = SecretBox.Decrypt(dbPwKey, row.DbPasswordEnc);
    if (dbPassword == null)
    {
        log.LogError("Control {Id}: the saved database password cannot be decrypted (wrong Gateway:DbPasswordKey?)", row.CtrlId);
        return Results.Json(new { success = false, message = "The company's database login could not be read. Ask the administrator." }, statusCode: 500);
    }

    // the config is valid until the end of the company's valid-until day (30 days when none is set); TOKEN_MINUTES > 0 can only shorten that
    var now = DateTimeOffset.UtcNow;
    var expires = row.Validity != null ? new DateTimeOffset(row.Validity.Value.Date.AddDays(1)) : now.AddDays(30);
    if (row.TokenMinutes > 0 && now.AddMinutes(row.TokenMinutes) < expires) expires = now.AddMinutes(row.TokenMinutes);

    var config = new ClientConfig
    {
        CtrlId = row.CtrlId, Company = row.ClientName, Mode = row.Mode.ToString(), Loca = row.Loca, Unit = row.Unit, MainUnit = row.MainUnit, DocYear = row.EffectiveDocYear,
        DbHost = string.IsNullOrWhiteSpace(row.DbHost) ? row.IpAddress : row.DbHost, DbPort = row.DatabasePort, DbService = row.DatabaseName, Schema = row.SchemaName,
        DbUser = row.DbUser!, DbPassword = dbPassword, ConfigTable = "MOBILE_MASTER_CONFIG",
        ValidFrom = row.ActiveDate, ValidTo = row.Validity, Iat = now.ToUnixTimeSeconds(), Exp = expires.ToUnixTimeSeconds()
    };

    log.LogInformation("Provisioned {User} -> control {Id} ({Mode}) config until {Exp:u}", user, row.CtrlId, row.Mode, expires);
    // The reply shows the phone only what it needs: where API 2 is, and the sealed config to save. The database name, schema,
    // user and password are inside the sealed config, which only API 2 can open.
    return Results.Json(new
    {
        success = true, version = 2, issuedAt = now, controlId = row.CtrlId,
        clientName = row.ClientName, mode = row.Mode.ToString(), loca = row.Loca, unit = row.Unit,
        api2BaseUrl = $"{scheme}://{row.IpAddress}:{row.HostedPort}/",
        validFrom = row.ActiveDate, validTo = row.Validity,
        refreshAfter = expires.AddDays(-3), configExpires = expires,
        config = sealer.Seal(config)
    });
}).RequireRateLimiting("provision");

app.Run();
