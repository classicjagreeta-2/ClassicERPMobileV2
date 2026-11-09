using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mobile.Common;

// Validation of one IMPORT_EYE row. Ported unchanged from ClassicERPMobileAPI/ImportEye.cs: only the columns marked green in
// "Mobile to ERP Import Transaction data for Employee Attendance" are accepted; every value is checked for type and size.
public static class ImportEyeParser
{
    public enum Kind { Text, Number, Date, Time }
    public record Col(string Json, string Column, Kind Kind, int Size, int Scale = 0);

    static Col S(string n, int len, string? col = null) => new(n, (col ?? n).ToUpperInvariant(), Kind.Text, len);
    static Col N(string n, int p, int s = 2) => new(n, n.ToUpperInvariant(), Kind.Number, p, s);
    static Col Tm(string n, int len) => new(n, n.ToUpperInvariant(), Kind.Time, len);

    public static readonly Col[] Columns =
    {
        S("rectype", 2), new("transdate", "TRANSDATE", Kind.Date, 0),
        S("teaestate", 10), S("division", 10), S("dek", 10), S("sardar", 4, "SARD_CODE"),
        S("emp_div", 4), S("ecode", 10), S("name", 50), S("pfno", 15), S("jcode", 7), S("type", 1), S("subcode", 2),
        S("attendance", 2), S("hazira_code", 2), S("optrcode", 5), S("mcid", 15), N("t", 6, 0), S("leaf", 1), S("ptype", 1), S("entry", 1),
        N("h", 3, 0), S("div1", 4), S("sec1", 10), N("nettwt1", 12), N("nettwt1_ac", 12), N("grwt1", 12), Tm("time1", 10), S("tika1", 1), S("tm", 1),
        N("h2", 3, 0), S("div2", 4), S("sec2", 10), N("nettwt2", 12), N("nettwt2_ac", 12), N("grwt2", 12), Tm("time2", 5), S("tika2", 1), S("tm2", 1),
        N("h3", 3, 0), S("div3", 4), S("sec3", 10), N("nettwt3", 12), N("nettwt3_ac", 12), N("grwt3", 12), Tm("time3", 5), S("tika3", 1), S("tm3", 1),
        S("div4", 4), S("sec4", 7), N("nettwt4", 12), N("nettwt4_ac", 12), N("grwt4", 12), Tm("time4", 5), S("tika4", 1),
        N("totalnett", 12), N("totalgrwt", 12), N("task", 12), N("nirikh", 12), Tm("starttime", 5), Tm("endtime", 5), N("rate", 12),
        S("tuom", 4), S("cash", 1), N("ot_hrs", 5), S("app_record_id", 40),
        S("u_namenew", 10), new("u_entdtnew", "U_ENTDTNEW", Kind.Date, 0), S("dtag", 1), S("o_usernew", 40), S("t_idnew", 40),
    };

    static readonly HashSet<string> Required = new() { "rectype", "transdate", "ecode", "app_record_id" };
    static readonly string[] HaziraCodes = { "N1", "E1", "O1" };
    static readonly Regex TimeRx = new(@"^([01]?\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "dd-MMM-yyyy", "dd-MMM-yy HH:mm:ss" };

    // checks one row and turns it into column values; returns the errors (empty = fine). forceMcid: when set, MCID is always that value.
    public static List<string> Parse(JsonObject row, out Dictionary<string, object?> values, string? forceMcid = null)
    {
        var errors = new List<string>();
        values = new Dictionary<string, object?>();
        var known = Columns.Select(c => c.Json).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var k in row.Select(p => p.Key).Where(k => !known.Contains(k))) errors.Add($"{k}: unknown field.");
        foreach (var c in Columns)
        {
            var node = row.FirstOrDefault(p => string.Equals(p.Key, c.Json, StringComparison.OrdinalIgnoreCase)).Value;
            var text = node is JsonValue jv ? (jv.TryGetValue<string>(out var s) ? s : jv.ToString()) : node is null ? null : "\0";
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                if (Required.Contains(c.Json)) errors.Add($"{c.Json}: required.");
                else if (c.Kind == Kind.Number) values[c.Column] = 0m;   // numeric field null / empty / not sent is stored as 0
                continue;
            }
            if (text == "\0") { errors.Add($"{c.Json}: must be a single value."); continue; }
            switch (c.Kind)
            {
                case Kind.Text:
                    if (text.Length > c.Size) errors.Add($"{c.Json}: up to {c.Size} characters.");
                    else if (c.Json == "hazira_code" && !HaziraCodes.Contains(text)) errors.Add("hazira_code: use N1, E1 or O1.");
                    else values[c.Column] = text;
                    break;
                case Kind.Time:
                    if (!TimeRx.IsMatch(text) || text.Length > c.Size) errors.Add($"{c.Json}: use HH:mm.");
                    else values[c.Column] = text;
                    break;
                case Kind.Date:
                    if (!DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) errors.Add($"{c.Json}: use yyyy-MM-dd.");
                    else values[c.Column] = d.Date;
                    break;
                case Kind.Number:
                    if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { errors.Add($"{c.Json}: not a number."); break; }
                    if (c.Scale == 0 && n != Math.Truncate(n)) { errors.Add($"{c.Json}: whole number only."); break; }
                    n = Math.Round(n, c.Scale, MidpointRounding.AwayFromZero);
                    if (Math.Abs(n) >= (decimal)Math.Pow(10, c.Size - c.Scale)) errors.Add($"{c.Json}: too large (up to {c.Size - c.Scale} digits before the decimal point).");
                    else values[c.Column] = n;
                    break;
            }
        }
        if (!string.IsNullOrEmpty(forceMcid)) values["MCID"] = forceMcid;
        return errors;
    }
}
