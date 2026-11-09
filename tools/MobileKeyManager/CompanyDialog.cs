using Mobile.Common;

namespace MobileKeyManager;

// Add / edit the master data of one company row of CLASSICNEW.MOBILE_CONTROL. Password and API key are not edited here.
// The layout is a self-sizing table (no fixed pixel positions), so it fits any display scaling and nothing is cut off.
public sealed class CompanyDialog : Form
{
    readonly ControlRow _row;
    readonly bool _isNew;

    readonly TextBox _name = new(), _login = new(), _loca = new(), _unit = new(), _main = new(), _doc = new(), _ip = new(), _db = new(), _schema = new(), _clients = new(),
        _device = new() { PlaceholderText = "blank = none (first phone to sign in is registered)" };
    readonly NumericUpDown _hostedPort = new() { Minimum = 1, Maximum = 65535, Value = 90 };
    readonly NumericUpDown _dbPort = new() { Minimum = 1, Maximum = 65535, Value = 1521 };
    readonly TextBox _dbUser = new() { PlaceholderText = "the client's Oracle login" }, _dbHost = new() { PlaceholderText = "blank = the API address" };
    readonly TextBox _dbPass = new() { UseSystemPasswordChar = true };
    readonly NumericUpDown _tokenMin = new() { Minimum = 0, Maximum = 44640, Value = 0 };   // 0 = the saved config lasts until the valid-until date
    readonly CheckBox _active = new() { Text = "Active (may sign in)", Checked = true, AutoSize = true };
    readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy", ShowCheckBox = true, Checked = true };
    readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy", ShowCheckBox = true, Checked = true };
    readonly Label _mode = new() { AutoSize = true, ForeColor = Color.DarkSlateBlue, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

    // the row as filled in (CtrlId, password and key are not part of it)
    public ControlRow Result { get; private set; }
    // the client database password typed in (clear, in memory only); the caller encrypts it before it is saved. Blank = keep the saved one.
    public string? NewDbPassword { get; private set; }

    public CompanyDialog(ControlRow? existing)
    {
        _isNew = existing == null;
        _row = existing ?? new ControlRow();
        Result = _row;
        Text = _isNew ? "Add company" : $"Edit company - {existing!.ClientName}";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 12, 14, 12);

        // 4 columns: label | input | label | input
        var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        int row = 0;
        Control Lbl(string s) => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 10, 8) };
        void Put(string l1, Control c1, string? l2 = null, Control? c2 = null)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Lbl(l1), 0, row);
            c1.Dock = DockStyle.Fill; c1.Margin = new Padding(0, 4, 18, 4); t.Controls.Add(c1, 1, row);
            if (l2 != null && c2 != null)
            {
                t.Controls.Add(Lbl(l2), 2, row);
                c2.Dock = DockStyle.Fill; c2.Margin = new Padding(0, 4, 0, 4); t.Controls.Add(c2, 3, row);
            }
            row++;
        }
        Put("Company name *", _name, "Login *", _login);
        Put("LOCA", _loca, "Unit  (blank = VB)", _unit);
        Put("Main unit", _main, "Doc year", _doc);
        Put("API address *", _ip, "Hosted port *", _hostedPort);
        Put("Database *", _db, "Database port *", _dbPort);
        Put("Schema *", _schema, "Database host", _dbHost);
        Put("Database user *", _dbUser, "Database password *", _dbPass);
        Put("Config minutes", _tokenMin, "Device ID", _device);
        Put("API clients", _clients);
        Put("Active from", _from, "Valid until", _to);
        // status, mode and hint rows span the table
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _active.Margin = new Padding(0, 6, 0, 2); t.Controls.Add(_active, 1, row); t.SetColumnSpan(_active, 3); row++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _mode.Margin = new Padding(0, 6, 0, 2); t.Controls.Add(_mode, 0, row); t.SetColumnSpan(_mode, 4); row++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label
        {
            Text = "* required.   API address = the server that runs API 2.   Database = Oracle service name.   Database host = the Oracle host (blank = the API address).\n" +
                   "Database user / password = the client's own Oracle login. The password is saved ENCRYPTED and goes to the phone only inside a sealed config that the phone cannot read. Leave the password blank when editing to keep the saved one.\n" +
                   "Unit blank = VB app (no filter).   Unit filled = MVC app (LOCA + UNIT + DOC_YEAR).   Doc year blank = year at the end of the schema name.\n" +
                   "Config minutes: 0 = the saved config on the phone lasts until the valid-until date.   Device ID = the phone this login is registered to (clear it to let the next phone register).",
            ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 2, 0, 6)
        };
        t.Controls.Add(hint, 0, row); t.SetColumnSpan(hint, 4);

        var ok = new Button { Text = _isNew ? "Save and generate key" : "Save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(8, 4, 0, 4) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(8, 4, 0, 4), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0), Anchor = AnchorStyles.Right };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);   // right to left: Save on the left of Cancel
        // one auto-sizing container is the only child of the form: the form takes its width and height from it
        var outer = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2 };
        outer.Controls.Add(t, 0, 0);
        outer.Controls.Add(buttons, 0, 1);
        Controls.Add(outer);
        CancelButton = cancel;

        if (existing != null)
        {
            _name.Text = existing.ClientName; _login.Text = existing.UserId; _loca.Text = existing.Loca ?? ""; _unit.Text = existing.Unit ?? ""; _main.Text = existing.MainUnit ?? "";
            _doc.Text = existing.DocYear ?? ""; _ip.Text = existing.IpAddress; _hostedPort.Value = Clamp(existing.HostedPort, 1, 65535); _db.Text = existing.DatabaseName;
            _dbPort.Value = Clamp(existing.DatabasePort, 1, 65535); _schema.Text = existing.SchemaName; _tokenMin.Value = Clamp(existing.TokenMinutes, 0, 44640);
            _dbUser.Text = existing.DbUser ?? ""; _dbHost.Text = existing.DbHost ?? "";
            _dbPass.PlaceholderText = string.IsNullOrEmpty(existing.DbPasswordEnc) ? "(not saved yet)" : "(saved - leave blank to keep)";
            _clients.Text = existing.ApiClients ?? ""; _active.Checked = existing.Active; _device.Text = existing.PhoneId ?? "";
            if (existing.ActiveDate is { } a) _from.Value = a; else _from.Checked = false;
            if (existing.Validity is { } v) _to.Value = v; else _to.Checked = false;
        }
        else
        {
            _from.Value = DateTime.Today; _to.Value = new DateTime(DateTime.Today.Year, 12, 31);
        }
        _unit.TextChanged += (_, _) => ShowMode();
        ShowMode();

        ok.Click += (_, _) =>
        {
            var r = Collect();
            var (errors, warnings) = ControlValidator.Validate(r);
            if (errors.Count > 0) { MessageBox.Show(this, string.Join("\n", errors.Select(e => "• " + e)), "Please correct", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!_isNew && !string.Equals(existing!.PhoneId ?? "", r.PhoneId ?? "", StringComparison.Ordinal))
                warnings.Add(string.IsNullOrEmpty(r.PhoneId) ? "The registered device is removed: the next phone to sign in becomes the registered one." : $"The registered device becomes '{r.PhoneId}': only that phone can sign in.");
            if (warnings.Count > 0 && MessageBox.Show(this, string.Join("\n", warnings.Select(w => "• " + w)) + "\n\nSave anyway?", "Check", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (_dbPass.Text.Length == 0 && (_isNew || string.IsNullOrEmpty(existing!.DbPasswordEnc)))
                if (MessageBox.Show(this, "No database password is entered: API 1 cannot issue this company's config until one is saved.\n\nSave anyway?", "Check", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Result = r;
            NewDbPassword = _dbPass.Text.Length == 0 ? null : _dbPass.Text;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;
    }

    static decimal Clamp(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    void ShowMode() => _mode.Text = string.IsNullOrWhiteSpace(_unit.Text) ? "Mode: VB  (no LOCA / unit / year filter)" : "Mode: MVC  (filtered by LOCA + UNIT + DOC_YEAR)";

    ControlRow Collect() => new()
    {
        CtrlId = _row.CtrlId,
        ClientName = _name.Text.Trim(), UserId = _login.Text.Trim(), Loca = _loca.Text.Trim(), Unit = _unit.Text.Trim(), MainUnit = _main.Text.Trim(), DocYear = _doc.Text.Trim(),
        IpAddress = _ip.Text.Trim(), HostedPort = (int)_hostedPort.Value, DatabaseName = _db.Text.Trim(), DatabasePort = (int)_dbPort.Value, SchemaName = _schema.Text.Trim(),
        TokenMinutes = (int)_tokenMin.Value, ApiClients = _clients.Text.Trim(), Active = _active.Checked,
        DbUser = string.IsNullOrWhiteSpace(_dbUser.Text) ? null : _dbUser.Text.Trim(), DbHost = string.IsNullOrWhiteSpace(_dbHost.Text) ? null : _dbHost.Text.Trim(),
        DbPasswordEnc = _row.DbPasswordEnc,
        PhoneId = string.IsNullOrWhiteSpace(_device.Text) ? null : _device.Text.Trim(),
        ActiveDate = _from.Checked ? _from.Value.Date : null, Validity = _to.Checked ? _to.Value.Date : null,
    };
}
