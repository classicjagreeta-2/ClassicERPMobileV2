using Oracle.ManagedDataAccess.Client;

namespace Mobile.Common;

// Reads CLASSICNEW.MOBILE_CONTROL and CLASSICNEW.MOBILE_MASTER_CONFIG.
public class ControlRepository
{
    readonly string _cs;
    readonly string _configTable;
    // configTable: where the master setup lives. Central (default) = CLASSICNEW.MOBILE_MASTER_CONFIG; a client's own database = MOBILE_MASTER_CONFIG
    public ControlRepository(string connectionString, string configTable = "CLASSICNEW.MOBILE_MASTER_CONFIG")
    {
        _cs = connectionString;
        if (!System.Text.RegularExpressions.Regex.IsMatch(configTable, @"^[A-Za-z][A-Za-z0-9_$#]{0,29}(\.[A-Za-z][A-Za-z0-9_$#]{0,29})?$"))
            throw new ArgumentException("Illegal master setup table name.");
        _configTable = configTable;
    }

    const string ControlCols = "CTRL_ID, LOCA, UNIT, MAIN_UNIT, USER_ID, PASSWORD_HASH, API_KEY_HASH, IP_ADDRESS, HOSTED_PORT, CLIENT_NAME_DISPLAY, " +
        "DATABASE_PORT, DATABASE_NAME, SCHEMA_NAME, DOC_YEAR, PHONE_ID, TOKEN_MINUTES, API_CLIENTS, STATUS, MOBILE_ACTIVE_DATE, MOBILE_VALIDITY, DB_USER, DB_PASSWORD_ENC, DB_HOST";

    static string? S(OracleDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : r.GetString(i).Trim(); }
    static int I(OracleDataReader r, string n, int d) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? d : Convert.ToInt32(r.GetValue(i)); }
    static DateTime? D(OracleDataReader r, string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : r.GetDateTime(i); }

    static ControlRow Map(OracleDataReader r) => new()
    {
        CtrlId = Convert.ToInt64(r.GetValue(r.GetOrdinal("CTRL_ID"))),
        Loca = S(r, "LOCA"), Unit = S(r, "UNIT") is { Length: > 0 } u ? u : null, MainUnit = S(r, "MAIN_UNIT"),
        UserId = S(r, "USER_ID") ?? "", PasswordHash = S(r, "PASSWORD_HASH"), ApiKeyHash = S(r, "API_KEY_HASH"),
        IpAddress = S(r, "IP_ADDRESS") ?? "", HostedPort = I(r, "HOSTED_PORT", 0), ClientName = S(r, "CLIENT_NAME_DISPLAY") ?? "",
        DatabasePort = I(r, "DATABASE_PORT", 1521), DatabaseName = S(r, "DATABASE_NAME") ?? "", SchemaName = S(r, "SCHEMA_NAME") ?? "",
        DocYear = S(r, "DOC_YEAR"),
        PhoneId = S(r, "PHONE_ID") is { Length: > 0 } p ? p : null, TokenMinutes = I(r, "TOKEN_MINUTES", 720), ApiClients = S(r, "API_CLIENTS"),
        Active = S(r, "STATUS") == "Y", ActiveDate = D(r, "MOBILE_ACTIVE_DATE"), Validity = D(r, "MOBILE_VALIDITY"),
        DbUser = S(r, "DB_USER"), DbPasswordEnc = S(r, "DB_PASSWORD_ENC"), DbHost = S(r, "DB_HOST"),
    };

    // the single active row of this login (the unique index guarantees at most one); null = unknown login / none active
    public async Task<ControlRow?> FindActiveByUser(string userId, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = $"SELECT {ControlCols} FROM CLASSICNEW.MOBILE_CONTROL WHERE UPPER(USER_ID) = UPPER(:u) AND STATUS = 'Y'";
        cmd.Parameters.Add("u", OracleDbType.Varchar2).Value = userId;
        using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map((OracleDataReader)r) : null;
    }

    // ---- administration (used by tools/MobileKeyManager) ----
    public async Task<List<ControlRow>> ListAll(CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {ControlCols} FROM CLASSICNEW.MOBILE_CONTROL ORDER BY CLIENT_NAME_DISPLAY, USER_ID, CTRL_ID";
        using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ControlRow>();
        while (await r.ReadAsync(ct)) list.Add(Map((OracleDataReader)r));
        return list;
    }

    async Task<bool> UpdateOne(string set, long id, string? value, CancellationToken ct)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = $"UPDATE CLASSICNEW.MOBILE_CONTROL SET {set} WHERE CTRL_ID = :id";
        if (value != null) cmd.Parameters.Add("v", OracleDbType.Varchar2).Value = value;
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = id;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;   // autocommit: the single statement is committed
    }

    string Cfg(string sql) => sql.Replace("@@CFG@@", _configTable);
    static object V(object? o) => o ?? DBNull.Value;
    static string? Nul(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static void BindMaster(OracleCommand cmd, ControlRow c)
    {
        void P(string n, OracleDbType t, object? v) => cmd.Parameters.Add(n, t).Value = V(v);
        P("b_loca", OracleDbType.Varchar2, Nul(c.Loca)); P("b_unit", OracleDbType.Varchar2, Nul(c.Unit)); P("b_main", OracleDbType.Varchar2, Nul(c.MainUnit));
        P("b_login", OracleDbType.Varchar2, c.UserId.Trim()); P("b_ip", OracleDbType.Varchar2, c.IpAddress.Trim()); P("b_hp", OracleDbType.Int32, c.HostedPort);
        P("b_name", OracleDbType.Varchar2, c.ClientName.Trim()); P("b_dbp", OracleDbType.Int32, c.DatabasePort); P("b_db", OracleDbType.Varchar2, c.DatabaseName.Trim());
        P("b_schema", OracleDbType.Varchar2, c.SchemaName.Trim().ToUpperInvariant()); P("b_doc", OracleDbType.Varchar2, Nul(c.DocYear));
        P("b_tm", OracleDbType.Int32, c.TokenMinutes); P("b_ac", OracleDbType.Varchar2, Nul(c.ApiClients)); P("b_st", OracleDbType.Varchar2, c.Active ? "Y" : "N");
        P("b_ad", OracleDbType.Date, c.ActiveDate?.Date); P("b_mv", OracleDbType.Date, c.Validity?.Date);
        P("b_phone", OracleDbType.Varchar2, Nul(c.PhoneId));   // the registered device id (blank = none; the first phone that signs in is then registered)
        P("b_dbuser", OracleDbType.Varchar2, Nul(c.DbUser)); P("b_dbhost", OracleDbType.Varchar2, Nul(c.DbHost));
    }

    // true when the Oracle error is ORA-00001 (another ACTIVE row already uses this login)
    public static bool IsDuplicateLogin(Exception ex) => ex is OracleException { Number: 1 };

    // new company row with its (already hashed) password and API key; returns CTRL_ID
    public async Task<long> Insert(ControlRow c, string passwordHash, string apiKeyHash, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "INSERT INTO CLASSICNEW.MOBILE_CONTROL (CTRL_ID, LOCA, UNIT, MAIN_UNIT, USER_ID, IP_ADDRESS, HOSTED_PORT, CLIENT_NAME_DISPLAY, DATABASE_PORT, DATABASE_NAME, " +
            "SCHEMA_NAME, DOC_YEAR, TOKEN_MINUTES, API_CLIENTS, STATUS, MOBILE_ACTIVE_DATE, MOBILE_VALIDITY, PHONE_ID, DB_USER, DB_HOST, DB_PASSWORD_ENC, PASSWORD_HASH, API_KEY_HASH) " +
            "VALUES (SEQ_MOBILE_CONTROL.NEXTVAL, :b_loca, :b_unit, :b_main, :b_login, :b_ip, :b_hp, :b_name, :b_dbp, :b_db, :b_schema, :b_doc, :b_tm, :b_ac, :b_st, :b_ad, :b_mv, :b_phone, :b_dbuser, :b_dbhost, :b_dbpw, :b_ph, :b_kh) " +
            "RETURNING CTRL_ID INTO :b_rid";
        BindMaster(cmd, c);
        cmd.Parameters.Add("b_dbpw", OracleDbType.Varchar2).Value = V(Nul(c.DbPasswordEnc));   // already encrypted by the caller
        cmd.Parameters.Add("b_ph", OracleDbType.Varchar2).Value = passwordHash;
        cmd.Parameters.Add("b_kh", OracleDbType.Varchar2).Value = apiKeyHash;
        var rid = cmd.Parameters.Add("b_rid", OracleDbType.Int64, System.Data.ParameterDirection.Output);
        await cmd.ExecuteNonQueryAsync(ct);
        return Convert.ToInt64(rid.Value.ToString());
    }

    // the master data of an existing company, including its registered device id; password and API key are not touched
    public async Task<bool> UpdateMaster(ControlRow c, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "UPDATE CLASSICNEW.MOBILE_CONTROL SET LOCA = :b_loca, UNIT = :b_unit, MAIN_UNIT = :b_main, USER_ID = :b_login, IP_ADDRESS = :b_ip, HOSTED_PORT = :b_hp, " +
            "CLIENT_NAME_DISPLAY = :b_name, DATABASE_PORT = :b_dbp, DATABASE_NAME = :b_db, SCHEMA_NAME = :b_schema, DOC_YEAR = :b_doc, TOKEN_MINUTES = :b_tm, " +
            "API_CLIENTS = :b_ac, STATUS = :b_st, MOBILE_ACTIVE_DATE = :b_ad, MOBILE_VALIDITY = :b_mv, PHONE_ID = :b_phone, DB_USER = :b_dbuser, DB_HOST = :b_dbhost WHERE CTRL_ID = :b_id";
        BindMaster(cmd, c);
        cmd.Parameters.Add("b_id", OracleDbType.Int64).Value = c.CtrlId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    // ---- master data setup: CLASSICNEW.MOBILE_MASTER_CONFIG ----
    const string ConfigCols = "CFG_ID, CTRL_ID, APP_MODE, SEQ_NO, ITEM_KEY, DESCRIPTION, OP_TYPE, SCHEMA_SOURCE, TABLE_NAME, COLUMN_LIST, ALIASES, FILTER_COLUMN, FILTER_VALUES, " +
        "UNIT_FILTER, DOC_YEAR_FILTER, REQUIRE_LISTING, ALLOW_CLIENTS, UPDATE_KEY_COL, UPDATE_COL, UPDATE_MAXLEN, ACTIVE, REMARKS, OPTIONS";

    static ConfigRow MapConfig(OracleDataReader r) => new()
    {
        CfgId = Convert.ToInt64(r.GetValue(r.GetOrdinal("CFG_ID"))),
        CtrlId = r.IsDBNull(r.GetOrdinal("CTRL_ID")) ? null : Convert.ToInt64(r.GetValue(r.GetOrdinal("CTRL_ID"))),
        AppMode = S(r, "APP_MODE") ?? "VB", SeqNo = I(r, "SEQ_NO", 0), ItemKey = S(r, "ITEM_KEY") ?? "", Description = S(r, "DESCRIPTION"),
        OpType = S(r, "OP_TYPE") ?? "READ", SchemaSource = S(r, "SCHEMA_SOURCE") ?? "CLIENT", TableName = S(r, "TABLE_NAME") ?? "",
        ColumnList = S(r, "COLUMN_LIST"), Aliases = S(r, "ALIASES"), FilterColumn = S(r, "FILTER_COLUMN"), FilterValues = S(r, "FILTER_VALUES"),
        UnitFilter = S(r, "UNIT_FILTER") == "Y", DocYearFilter = S(r, "DOC_YEAR_FILTER") == "Y", RequireListing = S(r, "REQUIRE_LISTING") == "Y",
        AllowClients = S(r, "ALLOW_CLIENTS") != "N", UpdateKeyCol = S(r, "UPDATE_KEY_COL"), UpdateCol = S(r, "UPDATE_COL"),
        UpdateMaxLen = r.IsDBNull(r.GetOrdinal("UPDATE_MAXLEN")) ? null : I(r, "UPDATE_MAXLEN", 4000),
        Active = S(r, "ACTIVE") != "N", Remarks = S(r, "REMARKS"), Options = S(r, "OPTIONS")
    };

    public async Task<List<ConfigRow>> ListConfig(CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.CommandText = Cfg($"SELECT {ConfigCols} FROM @@CFG@@ ORDER BY APP_MODE, NVL(CTRL_ID, 0), SEQ_NO, ITEM_KEY");
        using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ConfigRow>();
        while (await r.ReadAsync(ct)) list.Add(MapConfig((OracleDataReader)r));
        return list;
    }

    static void BindConfig(OracleCommand cmd, ConfigRow c)
    {
        void P(string n, OracleDbType t, object? v) => cmd.Parameters.Add(n, t).Value = V(v);
        string YN(bool b) => b ? "Y" : "N";
        P("c_mode", OracleDbType.Varchar2, c.AppMode); P("c_seq", OracleDbType.Int32, c.SeqNo); P("c_key", OracleDbType.Varchar2, c.ItemKey.Trim());
        P("c_desc", OracleDbType.Varchar2, Nul(c.Description)); P("c_op", OracleDbType.Varchar2, c.OpType); P("c_src", OracleDbType.Varchar2, c.SchemaSource);
        P("c_tbl", OracleDbType.Varchar2, c.TableName.Trim().ToUpperInvariant()); P("c_cols", OracleDbType.Varchar2, Nul(c.ColumnList)); P("c_alias", OracleDbType.Varchar2, Nul(c.Aliases));
        P("c_fcol", OracleDbType.Varchar2, Nul(c.FilterColumn)?.ToUpperInvariant()); P("c_fval", OracleDbType.Varchar2, Nul(c.FilterValues));
        P("c_unit", OracleDbType.Varchar2, YN(c.UnitFilter)); P("c_doc", OracleDbType.Varchar2, YN(c.DocYearFilter)); P("c_list", OracleDbType.Varchar2, YN(c.RequireListing));
        P("c_allow", OracleDbType.Varchar2, YN(c.AllowClients)); P("c_ukey", OracleDbType.Varchar2, Nul(c.UpdateKeyCol)?.ToUpperInvariant()); P("c_ucol", OracleDbType.Varchar2, Nul(c.UpdateCol)?.ToUpperInvariant());
        P("c_umax", OracleDbType.Int32, Nul(c.UpdateCol) == null ? null : c.UpdateMaxLen ?? 4000); P("c_active", OracleDbType.Varchar2, YN(c.Active));
        P("c_rem", OracleDbType.Varchar2, Nul(c.Remarks)); P("c_opts", OracleDbType.Varchar2, Nul(c.Options));
    }

    public async Task<long> InsertConfig(ConfigRow c, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = Cfg("INSERT INTO @@CFG@@ (CFG_ID, CTRL_ID, APP_MODE, SEQ_NO, ITEM_KEY, DESCRIPTION, OP_TYPE, SCHEMA_SOURCE, TABLE_NAME, COLUMN_LIST, ALIASES, " +
            "FILTER_COLUMN, FILTER_VALUES, UNIT_FILTER, DOC_YEAR_FILTER, REQUIRE_LISTING, ALLOW_CLIENTS, UPDATE_KEY_COL, UPDATE_COL, UPDATE_MAXLEN, ACTIVE, REMARKS, OPTIONS) " +
            "VALUES (SEQ_MOBILE_MASTER_CONFIG.NEXTVAL, :c_ctrl, :c_mode, :c_seq, :c_key, :c_desc, :c_op, :c_src, :c_tbl, :c_cols, :c_alias, :c_fcol, :c_fval, :c_unit, :c_doc, :c_list, :c_allow, " +
            ":c_ukey, :c_ucol, :c_umax, :c_active, :c_rem, :c_opts) RETURNING CFG_ID INTO :c_id");
        cmd.Parameters.Add("c_ctrl", OracleDbType.Int64).Value = V(c.CtrlId);
        BindConfig(cmd, c);
        var id = cmd.Parameters.Add("c_id", OracleDbType.Int64, System.Data.ParameterDirection.Output);
        await cmd.ExecuteNonQueryAsync(ct);
        return Convert.ToInt64(id.Value.ToString());
    }

    // the scope (default / company) of a row does not change; everything else can
    public async Task<bool> UpdateConfig(ConfigRow c, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = Cfg("UPDATE @@CFG@@ SET APP_MODE = :c_mode, SEQ_NO = :c_seq, ITEM_KEY = :c_key, DESCRIPTION = :c_desc, OP_TYPE = :c_op, SCHEMA_SOURCE = :c_src, " +
            "TABLE_NAME = :c_tbl, COLUMN_LIST = :c_cols, ALIASES = :c_alias, FILTER_COLUMN = :c_fcol, FILTER_VALUES = :c_fval, UNIT_FILTER = :c_unit, DOC_YEAR_FILTER = :c_doc, " +
            "REQUIRE_LISTING = :c_list, ALLOW_CLIENTS = :c_allow, UPDATE_KEY_COL = :c_ukey, UPDATE_COL = :c_ucol, UPDATE_MAXLEN = :c_umax, ACTIVE = :c_active, REMARKS = :c_rem, OPTIONS = :c_opts " +
            "WHERE CFG_ID = :c_id");
        BindConfig(cmd, c);
        cmd.Parameters.Add("c_id", OracleDbType.Int64).Value = c.CfgId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> SetConfigActive(long cfgId, bool active, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = Cfg("UPDATE @@CFG@@ SET ACTIVE = :a WHERE CFG_ID = :id");
        cmd.Parameters.Add("a", OracleDbType.Varchar2).Value = active ? "Y" : "N";
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = cfgId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    // only a company's OWN row can be deleted (the company then uses the default again); default rows are switched off instead
    public async Task<bool> DeleteCompanyConfig(long cfgId, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = Cfg("DELETE FROM @@CFG@@ WHERE CFG_ID = :id AND CTRL_ID IS NOT NULL");
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = cfgId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    // the old key / password stop working at once
    public Task<bool> SetApiKeyHash(long id, string hash, CancellationToken ct = default) => UpdateOne("API_KEY_HASH = :v", id, hash, ct);
    public Task<bool> SetPasswordHash(long id, string hash, CancellationToken ct = default) => UpdateOne("PASSWORD_HASH = :v", id, hash, ct);
    // the client's database password, already encrypted (SecretBox) by the caller; null clears it
    public Task<bool> SetDbPasswordEnc(long id, string? encrypted, CancellationToken ct = default) =>
        encrypted == null ? UpdateOne("DB_PASSWORD_ENC = NULL", id, null, ct) : UpdateOne("DB_PASSWORD_ENC = :v", id, encrypted, ct);
    public Task<bool> ClearDevice(long id, CancellationToken ct = default) => UpdateOne("PHONE_ID = NULL", id, null, ct);

    public async Task<ControlRow?> GetById(long id, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = $"SELECT {ControlCols} FROM CLASSICNEW.MOBILE_CONTROL WHERE CTRL_ID = :id";
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = id;
        using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map((OracleDataReader)r) : null;
    }

    // binds the device on first use; false = a different device is already bound
    public async Task<bool> BindDevice(ControlRow c, string deviceId, CancellationToken ct = default)
    {
        if (c.PhoneId != null) return string.Equals(c.PhoneId, deviceId, StringComparison.OrdinalIgnoreCase);
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = "UPDATE CLASSICNEW.MOBILE_CONTROL SET PHONE_ID = :p WHERE CTRL_ID = :id AND (PHONE_ID IS NULL OR TRIM(PHONE_ID) IS NULL)";
        cmd.Parameters.Add("p", OracleDbType.Varchar2).Value = deviceId;
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = c.CtrlId;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    static List<string> Csv(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Default rows (CTRL_ID NULL) of the client's mode, replaced item by item by the client's own rows; ordered by SEQ_NO.
    // A row whose identifiers are illegal is skipped (and reported through onBad) instead of failing the whole call.
    public async Task<List<MasterItem>> LoadItems(ControlRow c, bool includeInactive = false, Action<string>? onBad = null, CancellationToken ct = default)
    {
        using var con = new OracleConnection(_cs);
        await con.OpenAsync(ct);
        using var cmd = con.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = Cfg("SELECT CFG_ID, CTRL_ID, SEQ_NO, ITEM_KEY, DESCRIPTION, OP_TYPE, SCHEMA_SOURCE, TABLE_NAME, COLUMN_LIST, ALIASES, FILTER_COLUMN, FILTER_VALUES, " +
            "UNIT_FILTER, DOC_YEAR_FILTER, REQUIRE_LISTING, ALLOW_CLIENTS, UPDATE_KEY_COL, UPDATE_COL, UPDATE_MAXLEN, ACTIVE, REMARKS, OPTIONS " +
            "FROM @@CFG@@ WHERE APP_MODE = :m AND (CTRL_ID IS NULL OR CTRL_ID = :id)");
        cmd.Parameters.Add("m", OracleDbType.Varchar2).Value = c.Mode.ToString();
        cmd.Parameters.Add("id", OracleDbType.Int64).Value = c.CtrlId;
        var byKey = new Dictionary<string, (bool Own, MasterItem Item, bool Active)>(StringComparer.OrdinalIgnoreCase);
        using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            var rd = (OracleDataReader)r;
            while (await rd.ReadAsync(ct))
            {
                var own = !rd.IsDBNull(rd.GetOrdinal("CTRL_ID"));
                var key = S(rd, "ITEM_KEY") ?? "";
                var m = new MasterItem
                {
                    CfgId = Convert.ToInt64(rd.GetValue(0)), Mode = c.Mode, SeqNo = I(rd, "SEQ_NO", 0), ItemKey = key, Description = S(rd, "DESCRIPTION") ?? "",
                    Op = (S(rd, "OP_TYPE") ?? "") switch { "CONTROL" => OpType.Control, "READ" => OpType.Read, "UPLOAD_INSERT" => OpType.UploadInsert, "DEVICE_CHECK" => OpType.DeviceCheck, _ => OpType.UpdateColumn },
                    ClassicNewSchema = S(rd, "SCHEMA_SOURCE") == "CLASSICNEW", Table = S(rd, "TABLE_NAME") ?? "",
                    Columns = Csv(S(rd, "COLUMN_LIST")),
                    Aliases = Csv(S(rd, "ALIASES")).Select(a => a.Split('=', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0].Trim(), a => a[1].Trim()),
                    FilterColumn = S(rd, "FILTER_COLUMN"), FilterValues = Csv(S(rd, "FILTER_VALUES")),
                    UnitFilter = S(rd, "UNIT_FILTER") == "Y", DocYearFilter = S(rd, "DOC_YEAR_FILTER") == "Y",RequireListing = S(rd, "REQUIRE_LISTING") == "Y", AllowClients = S(rd, "ALLOW_CLIENTS") != "N",
                    UpdateKeyColumn = S(rd, "UPDATE_KEY_COL"), UpdateColumn = S(rd, "UPDATE_COL"), UpdateMaxLength = I(rd, "UPDATE_MAXLEN", 4000), Remarks = S(rd, "REMARKS"), Options = MasterItem.ParseOptions(S(rd, "OPTIONS")),
                };
                var active = S(rd, "ACTIVE") != "N";
                if (byKey.TryGetValue(key, out var prev) && prev.Own && !own) continue;   // the client's own row wins
                byKey[key] = (own, m, active);
            }
        }
        var list = new List<MasterItem>();
        foreach (var (key, v) in byKey.OrderBy(k => k.Value.Item.SeqNo))
        {
            if (!v.Active && !includeInactive) continue;
            try { MasterQuery.Validate(v.Item, c); list.Add(v.Item); }
            catch (Exception ex) { onBad?.Invoke($"{key}: {ex.Message}"); }
        }
        return list;
    }
}


