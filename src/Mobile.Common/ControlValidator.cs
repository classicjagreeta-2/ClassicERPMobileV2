using System.Text.RegularExpressions;

namespace Mobile.Common;

// Checks a MOBILE_CONTROL row before it is saved (used by tools/MobileKeyManager). Errors block the save; warnings are shown but allowed.
public static class ControlValidator
{
    static readonly Regex Login = new(@"^[A-Za-z0-9._-]{1,30}$", RegexOptions.Compiled);
    static readonly Regex Host = new(@"^[A-Za-z0-9._-]{1,60}$", RegexOptions.Compiled);

    public static (List<string> Errors, List<string> Warnings) Validate(ControlRow c)
    {
        var e = new List<string>(); var w = new List<string>();
        if (string.IsNullOrWhiteSpace(c.ClientName)) e.Add("Company name is required.");
        else if (c.ClientName.Trim().Length > 100) e.Add("Company name is up to 100 characters.");
        if (!Login.IsMatch(c.UserId ?? "")) e.Add("Login: 1-30 characters, letters, digits, . _ - only.");
        if (!Host.IsMatch(c.IpAddress ?? "")) e.Add("API address (IP or host name) is required, letters/digits . _ - only.");
        if (c.HostedPort is < 1 or > 65535) e.Add("Hosted port must be 1-65535.");
        if (c.DatabasePort is < 1 or > 65535) e.Add("Database port must be 1-65535.");
        if (!Host.IsMatch(c.DatabaseName ?? "")) e.Add("Database (Oracle service name) is required.");
        if (!Ident.IsValid(c.SchemaName)) e.Add("Schema must be an Oracle name (letters, digits, _ $ #; starts with a letter; up to 30).");
        if (!string.IsNullOrWhiteSpace(c.Unit) && string.IsNullOrWhiteSpace(c.Loca)) e.Add("LOCA is required when a Unit is given (MVC app).");
        if ((c.Loca ?? "").Length > 10) e.Add("LOCA is up to 10 characters.");
        if ((c.Unit ?? "").Length > 20) e.Add("Unit is up to 20 characters.");
        if ((c.MainUnit ?? "").Length > 10) e.Add("Main unit is up to 10 characters.");
        if ((c.DocYear ?? "").Length > 10) e.Add("Doc year is up to 10 characters.");
        if ((c.ApiClients ?? "").Length > 100) e.Add("API clients is up to 100 characters.");
        if ((c.PhoneId ?? "").Trim().Length > 60) e.Add("Device ID is up to 60 characters.");
        if (c.TokenMinutes != 0 && c.TokenMinutes is < 5 or > 60 * 24 * 31) e.Add("Config minutes: 0 (until the valid-until date) or 5-44640.");
        if (!string.IsNullOrWhiteSpace(c.DbUser) && !Regex.IsMatch(c.DbUser.Trim(), @"^[A-Za-z0-9_$#.@-]{1,60}$")) e.Add("Database user: letters, digits and _ $ # . @ - only (up to 60).");
        if (!string.IsNullOrWhiteSpace(c.DbHost) && !Host.IsMatch(c.DbHost.Trim())) e.Add("Database host: letters, digits and . _ - only (blank = the API address).");
        if (c.ActiveDate != null && c.Validity != null && c.Validity.Value.Date < c.ActiveDate.Value.Date) e.Add("Valid-until date is before the active date.");

        if (!string.IsNullOrWhiteSpace(c.Unit) && c.Unit.Trim().Length > 4)
            w.Add("IMPORT_EYE.UNIT holds only 4 characters: attendance upload will fail for a longer unit code.");
        if (c.Validity != null && c.Validity.Value.Date < DateTime.Today)
            w.Add("The valid-until date is in the past: this company cannot sign in.");
        if (!c.Active) w.Add("The company is saved as Inactive: it cannot sign in.");
        if (string.IsNullOrWhiteSpace(c.DbUser)) w.Add("No database user is set: API 1 cannot issue this company's config until one is saved.");
        return (e, w);
    }
}
