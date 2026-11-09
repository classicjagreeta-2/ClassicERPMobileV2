using System.Text.RegularExpressions;
using Mobile.Common;
using Oracle.ManagedDataAccess.Client;
using static Mobile.DataApi.Api;

namespace Mobile.DataApi;

public record FaceEnrollBody(string? Empcd, string? ModelId, string? Vector, string? DeviceId);
public record FaceUpdateBody(string? ModelId, string? Vector, string? DeviceId, string? Status);
public record ConsentBody(string? Empcd, string? State, string? WordingVersion, string? DeviceId);

public enum FaceResult { Ok, NoEmployee, NoConsent, AlreadyActive, NotFound, Erased }

// Face recognition + consent. Ported from ClassicERPMobileAPI/FaceData.cs: same rules (employee exists, one ACTIVE face per employee,
// consent GIVEN before enrolment, WITHDRAWN erases the face, ERASED cannot change), same checks, same JSON.
// Differences: table names / schema / LOCA+UNIT scope come from MOBILE_MASTER_CONFIG (items face-recognition, face-consent, employee);
// RequireConsent / EraseOnWithdrawn / MaxVectorLength come from that item's OPTIONS; the signed-in user is header x-app-user.
public static class FaceApi
{
    const string Ts = "'YYYY-MM-DD\"T\"HH24:MI:SS'";
    static readonly Regex Emp = new("^[A-Za-z0-9._-]{1,10}$", RegexOptions.Compiled);
    static readonly Regex Guid36 = new("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
    static readonly string[] States = { "GIVEN", "DECLINED", "WITHDRAWN" };

    static string Camel(string col) => col switch
    {
        "FACE_ID" => "faceId", "EMPCD" => "empcd", "MODEL_ID" => "modelId", "VECTOR" => "vector", "STATUS" => "status",
        "ENROLLED_AT" => "enrolledAt", "ENROLLED_BY" => "enrolledBy", "DEVICE_ID" => "deviceId", "UPDATED_AT" => "updatedAt",
        "EVENT_ID" => "eventId", "STATE" => "state", "AT" => "at", "BY_USER" => "byUser", "WORDING_VERSION" => "wordingVersion",
        _ => col.ToLowerInvariant()
    };

    static async Task<List<Dictionary<string, object?>>> Rows(OracleCommand c, CancellationToken ct)
    {
        using var rd = await c.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await rd.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(rd.FieldCount);
            for (var i = 0; i < rd.FieldCount; i++)
                row[Camel(rd.GetName(i))] = rd.IsDBNull(i) ? null : rd.GetValue(i) is string s ? s.Trim() : rd.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    // the three configured items; null when the client has no face feature
    sealed record Items(MasterItem Fr, MasterItem Fc, MasterItem Em, Ctx Ctx)
    {
        public string Faces => Ctx.Table(Fr);
        public string Consents => Ctx.Table(Fc);
        public string Empl => Ctx.Table(Em);
        public bool RequireConsent => Fr.OptBool("RequireConsent", true);
        public bool EraseOnWithdrawn => Fc.OptBool("EraseOnWithdrawn", true);
        public int MaxVector => Math.Clamp(Fr.OptInt("MaxVectorLength", 4000), 1, 4000);
    }

    static Items? Get(Ctx ctx) =>
        ctx.Item("face-recognition") is { } fr && ctx.Item("face-consent") is { } fc && ctx.Item("employee") is { } em ? new Items(fr, fc, em, ctx) : null;

    static async Task<bool> EmployeeExists(OracleConnection con, OracleTransaction? tx, Items it, string empcd, CancellationToken ct)
    {
        using var c = Cmd(con, tx, $"SELECT COUNT(*) FROM {it.Empl} WHERE EMPCD = :e{it.Ctx.ScopeSql(it.Em)}", ("e", empcd));
        it.Ctx.BindScope(c, it.Em);
        return Convert.ToInt64(await c.ExecuteScalarAsync(ct)) > 0;
    }

    // GIVEN / DECLINED / WITHDRAWN of the newest event, or null when there is none
    static async Task<string?> CurrentConsent(OracleConnection con, OracleTransaction? tx, Items it, string empcd, CancellationToken ct)
    {
        using var c = Cmd(con, tx, $"SELECT STATE FROM (SELECT STATE FROM {it.Consents} WHERE EMPCD = :e{it.Ctx.ScopeSql(it.Fc)} ORDER BY AT DESC, EVENT_ID DESC) WHERE ROWNUM = 1", ("e", empcd));
        it.Ctx.BindScope(c, it.Fc);
        var v = await c.ExecuteScalarAsync(ct);
        return v is null or DBNull ? null : v.ToString()!.Trim();
    }

    static async Task<long> ActiveFaces(OracleConnection con, OracleTransaction? tx, Items it, string empcd, CancellationToken ct)
    {
        using var c = Cmd(con, tx, $"SELECT COUNT(*) FROM {it.Faces} WHERE EMPCD = :e AND STATUS = 'ACTIVE'{it.Ctx.ScopeSql(it.Fr)}", ("e", empcd));
        it.Ctx.BindScope(c, it.Fr);
        return Convert.ToInt64(await c.ExecuteScalarAsync(ct));
    }

    static string FaceSelect(Items it, bool vector) =>
        "SELECT TRIM(FACE_ID) FACE_ID, EMPCD, MODEL_ID, " + (vector ? "VECTOR, " : "") +
        $"STATUS, TO_CHAR(ENROLLED_AT, {Ts}) ENROLLED_AT, ENROLLED_BY, DEVICE_ID, TO_CHAR(UPDATED_AT, {Ts}) UPDATED_AT FROM {it.Faces} WHERE 1 = 1{it.Ctx.ScopeSql(it.Fr)}";

    public static void Map(RouteGroupBuilder v1, Func<ControlRow, OracleConnection> open, ILogger log)
    {
        static Ctx C(HttpContext h) => (Ctx)h.Items["ctx"]!;
        static IResult Off() => Fail(503, "Face recognition is not set up for this client.");

        v1.MapGet("/face-recognition", (HttpContext c, string? empcd, string? status, bool? includeVector, int? limit) => Guard(log, "list faces", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            if ((await AppUser(c, ctx, open, c.RequestAborted)).Error is { } d) return d;
            empcd = Trimmed(empcd); status = Trimmed(status)?.ToUpperInvariant();
            if (empcd != null && !Emp.IsMatch(empcd)) return Fail(400, "Bad employee code.");
            if (status != null && status is not ("ACTIVE" or "ERASED")) return Fail(400, "status must be ACTIVE or ERASED.");
            using var con = open(ctx.Row); await con.OpenAsync(c.RequestAborted);
            var ps = new List<(string, string?)>(); var sql = FaceSelect(it, includeVector == true);
            if (empcd != null) { sql += " AND EMPCD = :e"; ps.Add(("e", empcd)); }
            if (status != null) { sql += " AND STATUS = :s"; ps.Add(("s", status)); }
            sql += $" ORDER BY ENROLLED_AT DESC, FACE_ID FETCH FIRST {Math.Clamp(limit ?? 1000, 1, 5000)} ROWS ONLY";
            using var cmd = Cmd(con, null, sql, ps.ToArray()); ctx.BindScope(cmd, it.Fr);
            var rows = await Rows(cmd, c.RequestAborted);
            return Results.Json(new { success = true, count = rows.Count, data = rows });
        }));

        v1.MapGet("/face-recognition/{faceId}", (HttpContext c, string faceId, bool? includeVector) => Guard(log, "get face", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            if ((await AppUser(c, ctx, open, c.RequestAborted)).Error is { } d) return d;
            if (!Guid36.IsMatch(faceId)) return Fail(400, "Bad face id.");
            using var con = open(ctx.Row); await con.OpenAsync(c.RequestAborted);
            using var cmd = Cmd(con, null, FaceSelect(it, includeVector == true) + " AND FACE_ID = :id", ("id", faceId.ToLowerInvariant())); ctx.BindScope(cmd, it.Fr);
            var row = (await Rows(cmd, c.RequestAborted)).FirstOrDefault();
            return row == null ? Fail(404, "Face not found.") : Results.Json(new { success = true, data = row });
        }));

        // new ACTIVE face; fails when the employee is unknown, has no GIVEN consent (if required) or already has an ACTIVE face
        v1.MapPost("/face-recognition", (HttpContext c, FaceEnrollBody body) => Guard(log, "enroll face", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            var (err, by) = await AppUser(c, ctx, open, c.RequestAborted); if (err != null) return err;
            var b = new FaceEnrollBody(Trimmed(body.Empcd), Trimmed(body.ModelId), Trimmed(body.Vector), Trimmed(body.DeviceId));
            if (b.Empcd == null || !Emp.IsMatch(b.Empcd)) return Fail(400, "Bad employee code.");
            if (b.ModelId == null || b.ModelId.Length > 50) return Fail(400, "modelId is required (up to 50 characters).");
            if (b.Vector == null) return Fail(400, "vector is required.");
            if (b.Vector.Length > it.MaxVector) return Fail(400, $"vector is up to {it.MaxVector} characters.");
            if (b.DeviceId != null && b.DeviceId.Length > 20) return Fail(400, "deviceId is up to 20 characters.");

            using var con = open(ctx.Row); await con.OpenAsync(c.RequestAborted); var ct = c.RequestAborted;
            if (!await EmployeeExists(con, null, it, b.Empcd, ct)) return Fail(404, "Employee not found.");
            using var tx = con.BeginTransaction();
            if (it.RequireConsent && await CurrentConsent(con, tx, it, b.Empcd, ct) != "GIVEN") return Fail(409, "The employee has not given consent for face recognition.");
            if (await ActiveFaces(con, tx, it, b.Empcd, ct) > 0) return Fail(409, "The employee already has an ACTIVE face. Update it, or erase it first.");

            var id = Guid.NewGuid().ToString();
            var cols = new List<string> { "FACE_ID", "EMPCD", "MODEL_ID", "VECTOR", "STATUS", "ENROLLED_AT", "ENROLLED_BY", "DEVICE_ID", "UPDATED_AT" };
            var vals = new List<string> { ":id", ":e", ":m", ":v", "'ACTIVE'", "SYSTIMESTAMP", ":u", ":d", "SYSTIMESTAMP" };
            using (var ins = Cmd(con, tx, "", ("id", id), ("e", b.Empcd), ("m", b.ModelId), ("v", b.Vector), ("u", by), ("d", b.DeviceId)))
            {
                ctx.AddScopeColumns(ins, it.Fr, cols, vals);
                ins.CommandText = $"INSERT INTO {it.Faces} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
                await ins.ExecuteNonQueryAsync(ct);
            }
            if (await ActiveFaces(con, tx, it, b.Empcd, ct) > 1) return Fail(409, "The employee already has an ACTIVE face. Update it, or erase it first.");   // two enrolments raced: rolled back
            tx.Commit();
            log.LogInformation("Face enrolled for {Empcd} by {By} (face {FaceId}, vector {Len} characters)", b.Empcd, by, id, b.Vector.Length);
            return Results.Json(new { success = true, faceId = id, empcd = b.Empcd, status = "ACTIVE" }, statusCode: 201);
        }));

        // changes model / vector / device of an ACTIVE face, or erases it (Status = ERASED clears the vector); an ERASED face cannot be changed
        v1.MapPut("/face-recognition/{faceId}", (HttpContext c, string faceId, FaceUpdateBody body) => Guard(log, "update face", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            var (err, by) = await AppUser(c, ctx, open, c.RequestAborted); if (err != null) return err;
            if (!Guid36.IsMatch(faceId)) return Fail(400, "Bad face id.");
            var b = new FaceUpdateBody(Trimmed(body.ModelId), Trimmed(body.Vector), Trimmed(body.DeviceId), Trimmed(body.Status)?.ToUpperInvariant());
            if (b.ModelId == null && b.Vector == null && b.DeviceId == null && b.Status == null) return Fail(400, "Nothing to update.");
            if (b.Status != null && b.Status is not ("ACTIVE" or "ERASED")) return Fail(400, "status can only be set to ERASED (or left ACTIVE).");
            if (b.Status == "ERASED" && (b.ModelId != null || b.Vector != null)) return Fail(400, "An erased face has no vector or model.");
            if (b.ModelId != null && b.ModelId.Length > 50) return Fail(400, "modelId is up to 50 characters.");
            if (b.Vector != null && b.Vector.Length > it.MaxVector) return Fail(400, $"vector is up to {it.MaxVector} characters.");
            if (b.DeviceId != null && b.DeviceId.Length > 20) return Fail(400, "deviceId is up to 20 characters.");

            var ct = c.RequestAborted; var fid = faceId.ToLowerInvariant();
            using var con = open(ctx.Row); await con.OpenAsync(ct);
            using var tx = con.BeginTransaction();
            string empcd, status;
            using (var s = Cmd(con, tx, $"SELECT EMPCD, STATUS FROM {it.Faces} WHERE FACE_ID = :id{ctx.ScopeSql(it.Fr)} FOR UPDATE", ("id", fid)))
            {
                ctx.BindScope(s, it.Fr);
                using var rd = await s.ExecuteReaderAsync(ct);
                if (!await rd.ReadAsync(ct)) return Fail(404, "Face not found.");
                empcd = rd.GetString(0).Trim(); status = rd.GetString(1).Trim();
            }
            if (status == "ERASED") return Fail(409, "The face is ERASED and cannot be changed. Enrol a new face instead.");
            var erase = b.Status == "ERASED";
            var sets = new List<string> { "UPDATED_AT = SYSTIMESTAMP" };
            var ps = new List<(string, string?)> { ("id", fid) };
            if (erase) { sets.Add("STATUS = 'ERASED'"); sets.Add("VECTOR = NULL"); }
            else
            {
                if (it.RequireConsent && (b.Vector != null || b.ModelId != null) && await CurrentConsent(con, tx, it, empcd, ct) != "GIVEN")
                    return Fail(409, "The employee has not given consent for face recognition.");
                if (b.ModelId != null) { sets.Add("MODEL_ID = :m"); ps.Add(("m", b.ModelId)); }
                if (b.Vector != null) { sets.Add("VECTOR = :v"); ps.Add(("v", b.Vector)); }
                if (b.DeviceId != null) { sets.Add("DEVICE_ID = :d"); ps.Add(("d", b.DeviceId)); }
            }
            using (var u = Cmd(con, tx, $"UPDATE {it.Faces} SET {string.Join(", ", sets)} WHERE FACE_ID = :id{ctx.ScopeSql(it.Fr)}", ps.ToArray()))
            { ctx.BindScope(u, it.Fr); await u.ExecuteNonQueryAsync(ct); }
            tx.Commit();
            log.LogInformation("Face {FaceId} of {Empcd} {Action} by {By}", fid, empcd, erase ? "erased" : "updated", by);
            return Results.Json(new { success = true, faceId = fid, erased = erase });
        }));

        v1.MapGet("/face-consent", (HttpContext c, string? empcd, string? state, bool? latest, int? limit) => Guard(log, "list consent", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            if ((await AppUser(c, ctx, open, c.RequestAborted)).Error is { } d) return d;
            empcd = Trimmed(empcd); state = Trimmed(state)?.ToUpperInvariant();
            if (empcd != null && !Emp.IsMatch(empcd)) return Fail(400, "Bad employee code.");
            if (state != null && !States.Contains(state)) return Fail(400, "state must be GIVEN, DECLINED or WITHDRAWN.");
            if (latest == true && empcd == null) return Fail(400, "latest needs empcd.");
            using var con = open(ctx.Row); await con.OpenAsync(c.RequestAborted);
            var ps = new List<(string, string?)>();
            var sql = $"SELECT TRIM(EVENT_ID) EVENT_ID, EMPCD, STATE, TO_CHAR(AT, {Ts}) AT, BY_USER, DEVICE_ID, WORDING_VERSION FROM {it.Consents} WHERE 1 = 1{ctx.ScopeSql(it.Fc)}";
            if (empcd != null) { sql += " AND EMPCD = :e"; ps.Add(("e", empcd)); }
            if (state != null) { sql += " AND STATE = :s"; ps.Add(("s", state)); }
            sql += $" ORDER BY AT DESC, EVENT_ID DESC FETCH FIRST {(latest == true ? 1 : Math.Clamp(limit ?? 1000, 1, 5000))} ROWS ONLY";
            using var cmd = Cmd(con, null, sql, ps.ToArray()); ctx.BindScope(cmd, it.Fc);
            var rows = await Rows(cmd, c.RequestAborted);
            return Results.Json(new { success = true, count = rows.Count, data = rows });
        }));

        // appends one consent event (events are never changed); WITHDRAWN also erases the employee's ACTIVE face when EraseOnWithdrawn is on
        v1.MapPost("/face-consent", (HttpContext c, ConsentBody body) => Guard(log, "record consent", async () =>
        {
            var ctx = C(c); if (Get(ctx) is not { } it) return Off();
            var (err, by) = await AppUser(c, ctx, open, c.RequestAborted); if (err != null) return err;
            var b = new ConsentBody(Trimmed(body.Empcd), Trimmed(body.State)?.ToUpperInvariant(), Trimmed(body.WordingVersion), Trimmed(body.DeviceId));
            if (b.Empcd == null || !Emp.IsMatch(b.Empcd)) return Fail(400, "Bad employee code.");
            if (b.State == null || !States.Contains(b.State)) return Fail(400, "state must be GIVEN, DECLINED or WITHDRAWN.");
            if (b.WordingVersion == null || b.WordingVersion.Length > 20) return Fail(400, "wordingVersion is required (up to 20 characters).");
            if (b.DeviceId != null && b.DeviceId.Length > 20) return Fail(400, "deviceId is up to 20 characters.");

            var ct = c.RequestAborted;
            using var con = open(ctx.Row); await con.OpenAsync(ct);
            if (!await EmployeeExists(con, null, it, b.Empcd, ct)) return Fail(404, "Employee not found.");
            using var tx = con.BeginTransaction();
            var id = Guid.NewGuid().ToString();
            var cols = new List<string> { "EVENT_ID", "EMPCD", "STATE", "AT", "BY_USER", "DEVICE_ID", "WORDING_VERSION" };
            var vals = new List<string> { ":id", ":e", ":s", "SYSTIMESTAMP", ":u", ":d", ":w" };
            using (var ins = Cmd(con, tx, "", ("id", id), ("e", b.Empcd), ("s", b.State), ("u", by), ("d", b.DeviceId), ("w", b.WordingVersion)))
            {
                ctx.AddScopeColumns(ins, it.Fc, cols, vals);
                ins.CommandText = $"INSERT INTO {it.Consents} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";
                await ins.ExecuteNonQueryAsync(ct);
            }
            var erased = 0;
            if (b.State == "WITHDRAWN" && it.EraseOnWithdrawn)
            {
                using var u = Cmd(con, tx, $"UPDATE {it.Faces} SET STATUS = 'ERASED', VECTOR = NULL, UPDATED_AT = SYSTIMESTAMP WHERE EMPCD = :e AND STATUS = 'ACTIVE'{ctx.ScopeSql(it.Fr)}", ("e", b.Empcd));
                ctx.BindScope(u, it.Fr);
                erased = await u.ExecuteNonQueryAsync(ct);
            }
            tx.Commit();
            log.LogInformation("Face consent {State} recorded for {Empcd} by {By}; {Erased} face(s) erased", b.State, b.Empcd, by, erased);
            return Results.Json(new { success = true, eventId = id, empcd = b.Empcd, state = b.State, facesErased = erased }, statusCode: 201);
        }));
    }
}
