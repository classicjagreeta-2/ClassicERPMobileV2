using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Mobile.Common;

// API 1 - the one central address the phone is given. Called the FIRST time (and about once a month, or when the saved config expires):
// it checks the app login against CLASSICNEW.MOBILE_CONTROL and returns "appConfig" - the JSON the app saves as its config file: the API 2
// address and every API 2 end point, the company details, and the access token for API 2. It holds NO database connection (no host, port,
// service, user or password): API 2 connects with the database login in its own settings. From then on the app reads its saved file and
// calls API 2 directly.
// Settings (environment variables preferred):
//   Gateway__ConnectionString   Oracle connection to CLASSICNEW (reads MOBILE_CONTROL)
//   Gateway__ConfigKey          seals the access token; the SAME key is set on every API 2 (>= 32 characters)
//   Gateway__MaxTokenHours      the longest an access token lives (default 12). The app gets a new one from API 1 by itself, so a login that is
//                               blocked or expired in MOBILE_CONTROL loses API 2 access within this time.
//   Gateway__Api2Scheme         http (default) | https;  Gateway__EnableSwagger  true | false
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();   // runs as a Windows service when started by the service manager; an ordinary console program otherwise
var cfg = builder.Configuration.GetSection("Gateway");
var cs = cfg["ConnectionString"] ?? throw new InvalidOperationException("Gateway:ConnectionString is not set.");
var sealer = new ConfigSealer(cfg["ConfigKey"] ?? "");
var maxTokenHours = double.TryParse(cfg["MaxTokenHours"], System.Globalization.CultureInfo.InvariantCulture, out var mh) && mh is >= 0.25 and <= 24 * 31 ? mh : 12;
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

// POST /gateway/v1/provision   headers: x-username, x-password, x-api-key
// No device id here: API 1 is the customer-level sign-in the app calls before anything else. The phone / user device is validated later
// by API 2 against the customer's own database (GET /mobile/v1/auth/device-check).
app.MapPost("/gateway/v1/provision", async (HttpContext c, CancellationToken ct) =>
{
    var h = c.Request.Headers;
    string user = h["x-username"].ToString().Trim(), pass = h["x-password"].ToString(), key = h["x-api-key"].ToString();
    if (user.Length is 0 or > 30 || pass.Length is 0 or > 200 || key.Length is 0 or > 100)
        return Results.Json(new { success = false, message = "Send x-username, x-password and x-api-key as headers." }, statusCode: 400);

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

    // the token lives the SHORTEST of: Gateway__MaxTokenHours, the company's TOKEN_MINUTES (> 0), the end of its valid-until day.
    // Short on purpose: it cannot be cancelled on API 2, so a blocked login is cut off when the app next asks API 1 for a new one.
    var now = DateTimeOffset.UtcNow;
    var expires = now.AddHours(maxTokenHours);
    if (row.Validity != null && new DateTimeOffset(row.Validity.Value.Date.AddDays(1)) < expires) expires = new DateTimeOffset(row.Validity.Value.Date.AddDays(1));
    if (row.TokenMinutes > 0 && now.AddMinutes(row.TokenMinutes) < expires) expires = now.AddMinutes(row.TokenMinutes);
    // refresh at 80% of the token's life, so a working app never meets an expired token
    var refreshAfter = now + (expires - now) * 0.8;

    var token = sealer.Seal(new ClientConfig
    {
        CtrlId = row.CtrlId, Company = row.ClientName, Mode = row.Mode.ToString(), Loca = row.Loca, Unit = row.Unit, MainUnit = row.MainUnit, DocYear = row.EffectiveDocYear,
        Schema = row.SchemaName, ValidFrom = row.ActiveDate, ValidTo = row.Validity, Iat = now.ToUnixTimeSeconds(), Exp = expires.ToUnixTimeSeconds()
    });
    var api2 = $"{scheme}://{row.IpAddress}:{row.HostedPort}";
    var api1 = $"{c.Request.Scheme}://{c.Request.Host}";

    log.LogInformation("Provisioned {User} -> control {Id} ({Mode}) config until {Exp:u}", user, row.CtrlId, row.Mode, expires);
    // appConfig = the file the app saves and reads on every start. API calling details only - no database connection.
    // passwordEnc / apiKeyEnc are left empty: the app fills them with the password and API key the user typed, encrypted with the
    // phone's own key store (API 1 only has their hashes), so it can call API 1 again by itself when refreshAfter is reached.
    var appConfig = new
    {
        version = 3, issuedAt = now,
        userName = user, passwordEnc = "", apiKeyEnc = "",
        controlId = row.CtrlId, clientName = row.ClientName, mode = row.Mode.ToString(), loca = row.Loca, unit = row.Unit, docYear = row.EffectiveDocYear,
        api1 = new { baseUrl = api1, provision = "/gateway/v1/provision" },
        api2 = new
        {
            baseUrl = api2,
            endpoints = new
            {
                plan = "/mobile/v1/plan",
                masters = "/mobile/v1/masters/{key}",
                updateMaster = "/mobile/v1/masters/{key}/{id}",
                upload = "/mobile/v1/upload/{key}",
                faceRecognition = "/mobile/v1/face-recognition",
                faceConsent = "/mobile/v1/face-consent",
                importEye = "/mobile/v1/import-eye",
                deviceCheck = "/mobile/v1/auth/device-check",
                health = "/health"
            }
        },
        auth = new { header = "Authorization", scheme = "Bearer", token, appUserHeader = "x-app-user" },
        validFrom = row.ActiveDate, validTo = row.Validity, refreshAfter, tokenExpires = expires
    };
    return Results.Json(new { success = true, appConfig });
}).RequireRateLimiting("provision");

app.Run();
