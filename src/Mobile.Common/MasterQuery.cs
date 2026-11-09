namespace Mobile.Common;

public record BuiltQuery(string Sql, Dictionary<string, object?> Binds);

// Builds the SELECT for one READ item. Pure function (no database) so it can be unit-tested.
// Only validated identifiers go into the text; every value is a bind variable.
public static class MasterQuery
{
    public static string Schema(MasterItem m, ControlRow c) => m.ClassicNewSchema ? "CLASSICNEW" : c.SchemaName;

    public static void Validate(MasterItem m, ControlRow? c = null)
    {
        Ident.Need(m.Table, "table of " + m.ItemKey);
        if (c != null) Ident.Need(Schema(m, c), "schema of " + m.ItemKey);
        foreach (var col in m.Columns)
        {
            Ident.Need(col, $"column {col} of {m.ItemKey}");
            if (Ident.LooksLikeCredential(col)) throw new InvalidOperationException($"column {col} of {m.ItemKey} looks like a credential");
        }
        foreach (var (name, src) in m.Aliases)
        {
            Ident.Need(name, $"alias {name} of {m.ItemKey}");
            Ident.Need(src, $"alias source {src} of {m.ItemKey}");
            if (Ident.LooksLikeCredential(src)) throw new InvalidOperationException($"alias of {m.ItemKey} reads credential column {src}");
        }
        if (m.FilterColumn != null) Ident.Need(m.FilterColumn, "filter column of " + m.ItemKey);
        if (m.UpdateKeyColumn != null) Ident.Need(m.UpdateKeyColumn, "update key of " + m.ItemKey);
        if (m.UpdateColumn != null)
        {
            Ident.Need(m.UpdateColumn, "update column of " + m.ItemKey);
            if (Ident.LooksLikeCredential(m.UpdateColumn)) throw new InvalidOperationException("update column looks like a credential");
        }
    }

    public static BuiltQuery BuildSelect(MasterItem m, ControlRow c)
    {
        Validate(m, c);
        if (m.Op != OpType.Read) throw new InvalidOperationException(m.ItemKey + " is not a READ item");
        var binds = new Dictionary<string, object?>();
        var cols = m.Columns.Concat(m.Aliases.Select(a => $"{a.Value} AS {a.Key}")).ToList();
        if (cols.Count == 0) throw new InvalidOperationException(m.ItemKey + " has no columns");
        var sql = $"SELECT {string.Join(", ", cols)} FROM {Schema(m, c)}.{m.Table}";
        var where = new List<string>();
        if (m.FilterColumn != null && m.FilterValues.Count > 0)
        {
            var names = new List<string>();
            for (var i = 0; i < m.FilterValues.Count; i++) { names.Add("UPPER(:f" + i + ")"); binds["f" + i] = m.FilterValues[i]; }
            where.Add($"UPPER(TRIM({m.FilterColumn})) IN ({string.Join(", ", names)})");
        }
        if (m.UnitFilter)   // MVC app: every master is limited to the client's LOCA and UNIT
        {
            if (string.IsNullOrWhiteSpace(c.Loca) || string.IsNullOrWhiteSpace(c.Unit))
                throw new InvalidOperationException(m.ItemKey + " needs LOCA and UNIT on the control row");
            where.Add("LOCA = :loca AND UNIT = :unit");
            binds["loca"] = c.Loca; binds["unit"] = c.Unit;
            if (m.DocYearFilter)
            {
                if (string.IsNullOrWhiteSpace(c.EffectiveDocYear)) throw new InvalidOperationException(m.ItemKey + " needs DOC_YEAR on the control row");
                where.Add("DOC_YEAR = :docyear");
                binds["docyear"] = c.EffectiveDocYear;
            }
        }
        if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
        return new BuiltQuery(sql, binds);
    }

    // The listing check applies only when the client has a MAIN_UNIT. With MAIN_UNIT empty the table is read directly from the schema.
    public static bool NeedsListingCheck(MasterItem m, ControlRow c) => m.RequireListing && !string.IsNullOrWhiteSpace(c.MainUnit);

    // VB app: the table is served only if MASTER_TABLE_LOCATION lists it for the client's MAIN_UNIT
    public static BuiltQuery BuildListingCheck(MasterItem m, ControlRow c) => new(
        "SELECT COUNT(*) FROM CLASSICNEW.MASTER_TABLE_LOCATION WHERE UPPER(TRIM(MAIN_UNIT)) = UPPER(TRIM(:unit)) AND UPPER(TRIM(TBNAME)) = UPPER(TRIM(:tbl))",
        new() { ["unit"] = c.MainUnit, ["tbl"] = m.Table });
}
