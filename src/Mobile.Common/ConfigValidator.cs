using System.Text.RegularExpressions;

namespace Mobile.Common;

// Checks a MOBILE_MASTER_CONFIG row before it is saved. Table and column names from this table go into SQL text at run time,
// so every identifier is validated here exactly as API 2 validates it when it loads the row.
public static class ConfigValidator
{
    static readonly Regex Key = new("^[a-z0-9-]{1,40}$", RegexOptions.Compiled);

    public static MasterItem ToItem(ConfigRow r)
    {
        static List<string> Csv(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new MasterItem
        {
            CfgId = r.CfgId, Mode = r.AppMode == "MVC" ? AppMode.MVC : AppMode.VB, SeqNo = r.SeqNo, ItemKey = r.ItemKey, Description = r.Description ?? "",
            Op = r.OpType switch { "CONTROL" => OpType.Control, "READ" => OpType.Read, "UPLOAD_INSERT" => OpType.UploadInsert, "DEVICE_CHECK" => OpType.DeviceCheck, _ => OpType.UpdateColumn },
            ClassicNewSchema = r.SchemaSource == "CLASSICNEW", Table = (r.TableName ?? "").Trim(), Columns = Csv(r.ColumnList),
            Aliases = Csv(r.Aliases).Select(a => a.Split('=', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0].Trim(), a => a[1].Trim()),
            FilterColumn = string.IsNullOrWhiteSpace(r.FilterColumn) ? null : r.FilterColumn.Trim(), FilterValues = Csv(r.FilterValues),
            UnitFilter = r.UnitFilter, DocYearFilter = r.DocYearFilter, RequireListing = r.RequireListing, AllowClients = r.AllowClients,
            UpdateKeyColumn = string.IsNullOrWhiteSpace(r.UpdateKeyCol) ? null : r.UpdateKeyCol.Trim(),
            UpdateColumn = string.IsNullOrWhiteSpace(r.UpdateCol) ? null : r.UpdateCol.Trim(), UpdateMaxLength = r.UpdateMaxLen ?? 4000,
            Remarks = r.Remarks, Options = MasterItem.ParseOptions(r.Options)
        };
    }

    public static (List<string> Errors, List<string> Warnings) Validate(ConfigRow r)
    {
        var e = new List<string>(); var w = new List<string>();
        if (!Key.IsMatch(r.ItemKey ?? "")) e.Add("Key: 1-40 characters, lowercase letters, digits and - only (e.g. tea-estate).");
        if (r.AppMode is not ("VB" or "MVC")) e.Add("Mode must be VB or MVC.");
        if (Array.IndexOf(ConfigRow.OpTypes, r.OpType) < 0) e.Add("Type is not valid.");
        if (r.SchemaSource is not ("CLIENT" or "CLASSICNEW")) e.Add("Schema source must be the company's schema or CLASSICNEW.");
        if (r.SeqNo is < 1 or > 99999) e.Add("Order (sequence) must be 1-99999.");
        if ((r.Description ?? "").Length > 100) e.Add("Description is up to 100 characters.");
        if ((r.ColumnList ?? "").Length > 1000) e.Add("Columns list is up to 1000 characters.");
        if ((r.Aliases ?? "").Length > 200) e.Add("Aliases is up to 200 characters.");
        if ((r.FilterValues ?? "").Length > 100) e.Add("Filter values is up to 100 characters.");
        if ((r.Options ?? "").Length > 300) e.Add("Options is up to 300 characters.");
        if ((r.Remarks ?? "").Length > 300) e.Add("Remarks is up to 300 characters.");
        if (!Ident.IsValid((r.TableName ?? "").Trim())) e.Add("Table must be an Oracle name (letters, digits, _ $ #; starts with a letter; up to 30).");

        var item = ToItem(r);
        // ident / credential checks (the same rules API 2 applies when it loads the row), all problems listed
        foreach (var col in item.Columns)
        {
            if (!Ident.IsValid(col)) e.Add($"Column '{col}' is not a valid Oracle name.");
            else if (Ident.LooksLikeCredential(col)) e.Add($"Column '{col}' looks like a credential and cannot be read.");
        }
        foreach (var (name, src) in item.Aliases)
        {
            if (!Ident.IsValid(name)) e.Add($"Alias name '{name}' is not a valid Oracle name.");
            if (!Ident.IsValid(src)) e.Add($"Alias source '{src}' is not a valid Oracle name.");
            else if (Ident.LooksLikeCredential(src)) e.Add($"Alias '{name}' reads '{src}', which looks like a credential column.");
        }
        if (item.FilterColumn != null && !Ident.IsValid(item.FilterColumn)) e.Add($"Filter column '{item.FilterColumn}' is not a valid Oracle name.");
        if (item.UpdateKeyColumn != null && !Ident.IsValid(item.UpdateKeyColumn)) e.Add($"Update key column '{item.UpdateKeyColumn}' is not a valid Oracle name.");
        if (item.UpdateColumn != null)
        {
            if (!Ident.IsValid(item.UpdateColumn)) e.Add($"Update column '{item.UpdateColumn}' is not a valid Oracle name.");
            else if (Ident.LooksLikeCredential(item.UpdateColumn)) e.Add($"Update column '{item.UpdateColumn}' looks like a credential and cannot be updated.");
        }
        foreach (var a in (r.Aliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!a.Contains('=')) e.Add($"Alias '{a}' must look like OUTNAME=SOURCECOLUMN.");

        if (r.OpType == "READ" && item.Columns.Count + item.Aliases.Count == 0) e.Add("A READ master needs at least one column.");
        if (r.OpType == "DEVICE_CHECK" && item.Columns.Count < 2) e.Add("Device check needs two columns: the user column, then the device column.");
        if (!string.IsNullOrWhiteSpace(r.FilterColumn) && item.FilterValues.Count == 0) e.Add("Filter column is set but there are no filter values.");
        if (string.IsNullOrWhiteSpace(r.FilterColumn) && item.FilterValues.Count > 0) e.Add("Filter values are set but there is no filter column.");
        if (!string.IsNullOrWhiteSpace(r.UpdateKeyCol) != !string.IsNullOrWhiteSpace(r.UpdateCol)) e.Add("Update key column and update column go together: fill in both or neither.");
        if (r.UpdateMaxLen is < 1 or > 4000) e.Add("Update max length must be 1-4000.");
        foreach (var part in (r.Options ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!part.Contains('=')) e.Add($"Option '{part}' must look like Name=Value.");
        if (r.DocYearFilter && !r.UnitFilter) e.Add("The year filter works together with the LOCA + UNIT filter: switch that on too.");

        if (r.AppMode == "VB" && (r.UnitFilter || r.DocYearFilter)) w.Add("VB companies are never filtered by LOCA / UNIT / year: these filters apply to MVC only.");
        if (r.OpType == "UPLOAD_INSERT" && r.ItemKey is "face-consent" or "face-recognition" or "import-eye")
            w.Add($"'{r.ItemKey}' is served by its own call with business rules; only its table, scope and options are read from here.");
        else if (r.OpType == "UPLOAD_INSERT") w.Add("A generic upload accepts only the columns listed here, and fills LOCA / UNIT from the company (MVC).");
        if (ConfigRow.DedicatedKeys.Contains(r.ItemKey ?? "") && r.OpType == "READ" && r.ItemKey is "user-login" or "employee")
            w.Add($"'{r.ItemKey}' is also used by other calls (the signed-in user check, face enrolment): keep its table and columns working.");
        if (!r.Active) w.Add("The master is saved as Inactive: the phone will not be told to read it.");
        return (e, w);
    }
}
