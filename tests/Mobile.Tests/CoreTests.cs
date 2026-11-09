using Mobile.Common;
using Xunit;

namespace Mobile.Tests;

public class CoreTests
{
    static ControlRow Vb() => new() { CtrlId = 1, Loca = "BTL", Unit = null, MainUnit = "ANAN", SchemaName = "PAY_BATE_NOR2027", Active = true };
    static ControlRow Mvc() => new() { CtrlId = 2, Loca = "BTL", Unit = "ANAN", MainUnit = "ANAN", SchemaName = "PAY_BTFL2027", Active = true };
    static MasterItem Item(bool unit = false) => new()
    {
        ItemKey = "division", Op = OpType.Read, Table = "M_DVSN", Columns = { "CODE", "DESCN" }, UnitFilter = unit,
        FilterColumn = "PTYPE", FilterValues = { "W", "S" }
    };

    [Fact] public void Mode_is_decided_by_unit() { Assert.Equal(AppMode.VB, Vb().Mode); Assert.Equal(AppMode.MVC, Mvc().Mode); }

    [Fact] public void Vb_query_has_no_unit_filter()
    {
        var q = MasterQuery.BuildSelect(Item(false), Vb());
        Assert.Equal("SELECT CODE, DESCN FROM PAY_BATE_NOR2027.M_DVSN WHERE UPPER(TRIM(PTYPE)) IN (UPPER(:f0), UPPER(:f1))", q.Sql);
        Assert.DoesNotContain("loca", q.Binds.Keys);
    }

    [Fact] public void Mvc_query_filters_by_loca_and_unit_with_binds()
    {
        var q = MasterQuery.BuildSelect(Item(true), Mvc());
        Assert.Contains("LOCA = :loca AND UNIT = :unit", q.Sql);
        Assert.Equal("BTL", q.Binds["loca"]); Assert.Equal("ANAN", q.Binds["unit"]);
        Assert.DoesNotContain("BTL", q.Sql);
    }

    [Fact] public void Empty_main_unit_skips_listing_check_and_reads_schema_directly()
    {
        var item = Item(); item.RequireListing = true;
        var c = Vb(); c.MainUnit = null;
        Assert.False(MasterQuery.NeedsListingCheck(item, c));
        Assert.StartsWith("SELECT CODE, DESCN FROM PAY_BATE_NOR2027.M_DVSN", MasterQuery.BuildSelect(item, c).Sql);
        c.MainUnit = "ANAN";
        Assert.True(MasterQuery.NeedsListingCheck(item, c));
    }

    [Fact] public void Mvc_doc_year_filter_uses_control_doc_year_or_schema_suffix()
    {
        var m = Item(true); m.DocYearFilter = true;
        var c = Mvc();                                   // DOC_YEAR blank -> 2027 from PAY_BTFL2027
        var q = MasterQuery.BuildSelect(m, c);
        Assert.Contains("LOCA = :loca AND UNIT = :unit AND DOC_YEAR = :docyear", q.Sql);
        Assert.Equal("2027", q.Binds["docyear"]);
        c.DocYear = "2026"; Assert.Equal("2026", MasterQuery.BuildSelect(m, c).Binds["docyear"]);
        var noDoc = Item(true); Assert.DoesNotContain("DOC_YEAR", MasterQuery.BuildSelect(noDoc, Mvc()).Sql);
    }

    [Fact] public void Vb_never_filters_by_doc_year()
    {
        var m = Item(false); m.DocYearFilter = true;     // DocYear only applies together with the unit filter
        Assert.DoesNotContain("DOC_YEAR", MasterQuery.BuildSelect(m, Vb()).Sql);
    }

    [Fact] public void Options_are_parsed_from_the_options_column()
    {
        var m = Item(); m.Options = MasterItem.ParseOptions("RequireConsent=Y; MaxVectorLength=2000;SkipDuplicates=N;ForceMcid=M001");
        Assert.True(m.OptBool("RequireConsent", false)); Assert.False(m.OptBool("SkipDuplicates", true));
        Assert.Equal(2000, m.OptInt("MaxVectorLength", 4000)); Assert.Equal("M001", m.Opt("ForceMcid", ""));
        Assert.Equal(500, m.OptInt("MaxRows", 500)); Assert.True(m.OptBool("Missing", true));   // defaults when the option is absent
    }

    static List<string> Imp(string json, out Dictionary<string, object?> v, string? mcid = null) =>
        ImportEyeParser.Parse((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!, out v, mcid);

    [Fact] public void ImportEye_valid_row_numbers_default_to_zero_and_mcid_is_forced()
    {
        var errs = Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"15154","app_record_id":"abc","hazira_code":"N1","mcid":"X9"}""", out var v, "M001");
        Assert.Empty(errs);
        Assert.Equal("M001", v["MCID"]); Assert.Equal(0m, v["NETTWT1"]); Assert.Equal(new DateTime(2026, 9, 29), v["TRANSDATE"]);
    }

    [Fact] public void ImportEye_bad_rows_are_rejected_with_reasons()
    {
        Assert.Contains(Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"1"}""", out _), e => e.StartsWith("app_record_id"));
        Assert.Contains(Imp("""{"rectype":"3","transdate":"29/09/2026","ecode":"1","app_record_id":"a"}""", out _), e => e.StartsWith("transdate"));
        Assert.Contains(Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"1","app_record_id":"a","hazira_code":"ZZ"}""", out _), e => e.StartsWith("hazira_code"));
        Assert.Contains(Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"1","app_record_id":"a","time1":"25:00"}""", out _), e => e.StartsWith("time1"));
        Assert.Contains(Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"1","app_record_id":"a","evil":"x"}""", out _), e => e.StartsWith("evil"));
        Assert.Contains(Imp("""{"rectype":"3","transdate":"2026-09-29","ecode":"1","app_record_id":"a","h":"1.5"}""", out _), e => e.StartsWith("h:"));
    }

    static ControlRow Good() => new()
    {
        ClientName = "Acme Tea", UserId = "ACME1", IpAddress = "103.234.116.64", HostedPort = 90, DatabasePort = 1521, DatabaseName = "CLASSICDEV",
        SchemaName = "PAY_ACME2027", TokenMinutes = 0, DbUser = "ACMEDB", Active = true, ActiveDate = DateTime.Today, Validity = DateTime.Today.AddDays(30)
    };

    [Fact] public void Valid_company_row_passes()
    {
        var (e, w) = ControlValidator.Validate(Good());
        Assert.Empty(e); Assert.Empty(w);
    }

    [Fact] public void Company_row_errors_and_warnings()
    {
        var c = Good(); c.UserId = "bad login!"; c.SchemaName = "X; DROP"; c.HostedPort = 0; c.Unit = "ANANDA"; c.Loca = null;
        var (e, w) = ControlValidator.Validate(c);
        Assert.Contains(e, x => x.StartsWith("Login")); Assert.Contains(e, x => x.StartsWith("Schema")); Assert.Contains(e, x => x.StartsWith("Hosted port"));
        Assert.Contains(e, x => x.StartsWith("LOCA is required"));
        c = Good(); c.Unit = "ANANDA"; c.Loca = "ATE"; c.Active = false; c.Validity = DateTime.Today.AddDays(-1); c.ActiveDate = DateTime.Today.AddDays(-5);
        (e, w) = ControlValidator.Validate(c);
        Assert.Empty(e); Assert.Equal(3, w.Count);   // 4-char unit limit, validity in the past, inactive
        c = Good(); c.ActiveDate = DateTime.Today; c.Validity = DateTime.Today.AddDays(-1);
        Assert.Contains(ControlValidator.Validate(c).Errors, x => x.StartsWith("Valid-until"));
        c = Good(); c.PhoneId = new string('D', 61);
        Assert.Contains(ControlValidator.Validate(c).Errors, x => x.StartsWith("Device ID"));
        c = Good(); c.PhoneId = "E10F 575B DB09 7DEA"; Assert.Empty(ControlValidator.Validate(c).Errors);
    }

    static ConfigRow GoodCfg() => new()
    {
        AppMode = "VB", SeqNo = 150, ItemKey = "my-master", Description = "My master", OpType = "READ", SchemaSource = "CLIENT", TableName = "M_MYTABLE",
        ColumnList = "CODE,DESCN,PTYPE", FilterColumn = "PTYPE", FilterValues = "W,S", Active = true
    };

    [Fact] public void Good_master_config_row_passes_and_builds_the_expected_query()
    {
        var (e, w) = ConfigValidator.Validate(GoodCfg());
        Assert.Empty(e); Assert.Empty(w);
        var q = MasterQuery.BuildSelect(ConfigValidator.ToItem(GoodCfg()), Vb());
        Assert.StartsWith("SELECT CODE, DESCN, PTYPE FROM PAY_BATE_NOR2027.M_MYTABLE WHERE UPPER(TRIM(PTYPE)) IN", q.Sql);
    }

    [Fact] public void Bad_master_config_rows_are_rejected()
    {
        var c = GoodCfg(); c.ItemKey = "Bad Key!"; c.TableName = "M_X; DROP TABLE Y"; c.ColumnList = "CODE,USER_PASSWORD";
        var errs = ConfigValidator.Validate(c).Errors;
        Assert.Contains(errs, x => x.StartsWith("Key")); Assert.Contains(errs, x => x.StartsWith("Table")); Assert.Contains(errs, x => x.Contains("credential"));
        c = GoodCfg(); c.ColumnList = ""; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("at least one column"));
        c = GoodCfg(); c.FilterValues = ""; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("no filter values"));
        c = GoodCfg(); c.UpdateCol = "FACE"; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("go together"));
        c = GoodCfg(); c.Options = "oops"; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("Name=Value"));
        c = GoodCfg(); c.AppMode = "MVC"; c.DocYearFilter = true; c.UnitFilter = false; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("year filter"));
        c = GoodCfg(); c.Aliases = "SECRETCOPY=USER_PASSWORD"; Assert.Contains(ConfigValidator.Validate(c).Errors, x => x.Contains("credential"));
        c = GoodCfg(); c.Aliases = "PASSWORD=REMARKS"; Assert.Empty(ConfigValidator.Validate(c).Errors);   // the intended user-login alias is allowed
        c = GoodCfg(); c.UnitFilter = true; Assert.Contains(ConfigValidator.Validate(c).Warnings, x => x.Contains("MVC only"));
    }

    const string K1 = "config-key-one-0123456789-abcdefghij-XYZ";
    const string K2 = "another-key-0123456789-abcdefghijkl-QRS";

    static ClientConfig Cfg(DateTimeOffset exp) => new()
    {
        CtrlId = 3, Company = "Classic Dev", Mode = "VB", Loca = "BTL", DbHost = "10.0.0.5", DbPort = 1521, DbService = "CLASSICDEV", Schema = "PAY_BATE_NOR2027",
        DbUser = "CLASSICNEW", DbPassword = "S3cret!pw;with=odd chars", Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Exp = exp.ToUnixTimeSeconds()
    };

    [Fact] public void Sealed_config_opens_with_the_same_key_and_hides_the_password()
    {
        var s = new ConfigSealer(K1);
        var blob = s.Seal(Cfg(DateTimeOffset.UtcNow.AddDays(10)));
        Assert.StartsWith("c2.", blob);
        Assert.DoesNotContain("CLASSICNEW", blob); Assert.DoesNotContain("S3cret", blob); Assert.DoesNotContain("PAY_BATE", blob);   // nothing readable on the phone
        var (c, problem) = s.Open(blob);
        Assert.Null(problem); Assert.Equal("S3cret!pw;with=odd chars", c!.DbPassword); Assert.Equal("PAY_BATE_NOR2027", c.Schema);
        var row = c.ToControlRow(); Assert.Equal(AppMode.VB, row.Mode); Assert.Equal("CLASSICNEW", row.DbUser); Assert.Equal("10.0.0.5", row.DbHost);
    }

    [Fact] public void Sealed_config_rejects_wrong_key_tampering_and_expiry()
    {
        var s = new ConfigSealer(K1);
        var blob = s.Seal(Cfg(DateTimeOffset.UtcNow.AddDays(10)));
        Assert.Equal("invalid", new ConfigSealer(K2).Open(blob).Problem);
        var tampered = blob[..^3] + (blob[^3] == 'A' ? "B" : "A") + blob[^2..];
        Assert.Equal("invalid", s.Open(tampered).Problem);
        Assert.Equal("invalid", s.Open("c2.garbage").Problem); Assert.Equal("invalid", s.Open(null).Problem); Assert.Equal("invalid", s.Open("token-from-old-design").Problem);
        Assert.Equal("expired", s.Open(s.Seal(Cfg(DateTimeOffset.UtcNow.AddMinutes(-1)))).Problem);
        Assert.Throws<InvalidOperationException>(() => new ConfigSealer("too short"));
    }

    [Fact] public void Db_password_box_roundtrip_and_wrong_key()
    {
        var enc = SecretBox.Encrypt(K1, "sample-db-pw-1");
        Assert.StartsWith("v1.", enc); Assert.DoesNotContain("sample-db-pw-1", enc);
        Assert.Equal("sample-db-pw-1", SecretBox.Decrypt(K1, enc));
        Assert.Null(SecretBox.Decrypt(K2, enc)); Assert.Null(SecretBox.Decrypt(K1, null)); Assert.Null(SecretBox.Decrypt(K1, "plain"));
        Assert.NotEqual(enc, SecretBox.Encrypt(K1, "sample-db-pw-1"));   // a fresh nonce every time
        // a sealed config cannot be opened as a password box and vice versa
        Assert.Null(SecretBox.Decrypt(K1, "v1." + new ConfigSealer(K1).Seal(Cfg(DateTimeOffset.UtcNow.AddDays(1)))[3..]));
    }

    [Fact] public void Plan_is_built_in_order_with_the_dedicated_paths()
    {
        var items = new List<MasterItem>
        {
            new() { ItemKey = "division", SeqNo = 70, Op = OpType.Read, Description = "Division" },
            new() { ItemKey = "control", SeqNo = 10, Op = OpType.Control },
            new() { ItemKey = "import-eye", SeqNo = 220, Op = OpType.UploadInsert }, new() { ItemKey = "device-check", SeqNo = 25, Op = OpType.DeviceCheck },
            new() { ItemKey = "tea-estate", SeqNo = 50, Op = OpType.Read }
        };
        var plan = PlanBuilder.Build(items);
        Assert.Equal(new[] { "device-check", "tea-estate", "division", "import-eye" }, plan.Select(p => p.key));
        Assert.Equal("/mobile/v1/masters/division", plan.First(p => p.key == "division").path);
        Assert.Equal("/mobile/v1/import-eye", plan.First(p => p.key == "import-eye").path); Assert.Equal("POST", plan.First(p => p.key == "import-eye").method);
        Assert.Equal("/mobile/v1/auth/device-check", plan.First(p => p.key == "device-check").path);
    }

    [Fact] public void Illegal_identifier_is_rejected()
    {
        var m = Item(); m.Table = "M_DVSN; DROP TABLE X";
        Assert.Throws<InvalidOperationException>(() => MasterQuery.BuildSelect(m, Vb()));
        var c = Item(); c.Columns.Add("USER_PASSWORD");
        Assert.Throws<InvalidOperationException>(() => MasterQuery.BuildSelect(c, Vb()));
    }

    [Fact] public void Alias_reading_a_credential_column_is_rejected()
    {
        var m = Item(); m.Aliases["PASSWORD"] = "PASSWORD";
        Assert.Throws<InvalidOperationException>(() => MasterQuery.Validate(m, Vb()));
    }

    [Fact] public void Password_hash_roundtrip()
    {
        var h = Hashing.HashPassword("s3cret!");
        Assert.DoesNotContain("s3cret", h);
        Assert.True(Hashing.VerifyPassword("s3cret!", h));
        Assert.False(Hashing.VerifyPassword("wrong", h));
        Assert.False(Hashing.VerifyPassword("x", null));
    }

    [Fact] public void Api_key_hash_roundtrip()
    {
        var k = Hashing.NewApiKey(); var h = Hashing.HashApiKey(k);
        Assert.True(Hashing.VerifyApiKey(k, h)); Assert.False(Hashing.VerifyApiKey("other", h)); Assert.False(Hashing.VerifyApiKey(null, h));
    }

    [Fact] public void Control_row_validity_window()
    {
        var c = Vb(); c.ActiveDate = new DateTime(2026, 5, 1); c.Validity = new DateTime(2026, 12, 31);
        Assert.True(c.IsCurrent(new DateTime(2026, 11, 7)));
        Assert.False(c.IsCurrent(new DateTime(2027, 1, 1)));
        c.Active = false; Assert.False(c.IsCurrent(new DateTime(2026, 11, 7)));
    }
}

