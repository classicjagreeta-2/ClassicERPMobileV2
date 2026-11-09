using System.Security.Cryptography;
using Mobile.Common;

namespace MobileKeyManager;

// Lists the companies in CLASSICNEW.MOBILE_CONTROL and generates their API key (and password) for the mobile app.
// Only hashes are written to the database; the clear value is shown once.
public sealed class MainForm : Form
{
    const string PasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";   // no look-alike characters

    readonly AppSettings _s = AppSettings.Load();
    ControlRepository? _repo;
    List<ControlRow> _rows = new();

    readonly TextBox _host = new(), _port = new() , _service = new(), _user = new(), _pass = new() { UseSystemPasswordChar = true };
    readonly TextBox _filter = new() { PlaceholderText = "type to filter by company, login or unit" };
    readonly TextBox _api1 = new() { PlaceholderText = "e.g. http://103.234.116.64:90/  (printed on the phone setup text)" };
    readonly Button _connect = new() { Text = "Connect" }, _refresh = new() { Text = "Refresh", Enabled = false };
    readonly Button _add = new() { Text = "Add company…", Enabled = false };
    readonly Button _edit = new() { Text = "Edit company…", Enabled = false };
    long? _pendingSelect;
    readonly Button _genKey = new() { Text = "Generate API key", Enabled = false };
    readonly Button _genPass = new() { Text = "Generate new password", Enabled = false };
    readonly Button _clearDevice = new() { Text = "Clear device binding", Enabled = false };
    readonly Button _auditLog = new() { Text = "Open audit log" };
    readonly Button _masters = new() { Text = "Master data setup…", Enabled = false };
    readonly Button _keyBtn = new() { Text = "Encryption key…" };
    readonly DataGridView _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
        // every column is as wide as its widest text (header included), so nothing is cut off; the Company column takes the spare width
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        ScrollBars = ScrollBars.Both
    };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Not connected." };

    public MainForm()
    {
        Text = "Mobile Key Manager - ClassicERP Mobile";
        // scale with the display (125 % / 150 % ...) and size every row to its content, so no text or column is cut off
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 560);

        _host.Text = _s.Host; _port.Text = _s.Port; _service.Text = _s.Service; _user.Text = _s.User; _api1.Text = _s.Api1Address;

        // --- database connection row (grows with its content, wraps on a narrow window)
        var db = new GroupBox { Text = "Database (CLASSICNEW)", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8) };
        var dbFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        void Field(string label, TextBox t, int w) { dbFlow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 8, 2, 0) }); t.Width = w; t.Margin = new Padding(0, 4, 8, 0); dbFlow.Controls.Add(t); }
        Field("Host", _host, 150); Field("Port", _port, 60); Field("Service", _service, 130); Field("User", _user, 130); Field("Password", _pass, 150);
        _connect.AutoSize = true; _connect.AutoSizeMode = AutoSizeMode.GrowAndShrink; _connect.Padding = new Padding(10, 2, 10, 2); _connect.Margin = new Padding(8, 3, 0, 0);
        dbFlow.Controls.Add(_connect);
        db.Controls.Add(dbFlow);

        // --- filter row
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 4, 4, 4) };
        _filter.Width = 440; _filter.Margin = new Padding(4, 3, 8, 3);
        _refresh.AutoSize = true; _refresh.AutoSizeMode = AutoSizeMode.GrowAndShrink; _refresh.Padding = new Padding(10, 2, 10, 2);
        top.Controls.AddRange(new Control[] { _filter, _refresh });

        // --- grid: all columns sized to their text; Company takes the spare width
        void Col(string name, string header, bool fill = false)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header };
            if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = 200; c.FillWeight = 100; }
            _grid.Columns.Add(c);
        }
        Col("id", "Id"); Col("company", "Company", fill: true); Col("login", "Login"); Col("loca", "LOCA"); Col("unit", "Unit");
        Col("mode", "Mode"); Col("status", "Status"); Col("valid", "Valid until"); Col("key", "API key set"); Col("pwd", "Password set"); Col("dbl", "Database login"); Col("dev", "Device ID");
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.DefaultCellStyle.Padding = new Padding(2, 2, 2, 2);

        // --- actions: buttons size to their text
        void Btn(Button b) { b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(12, 4, 12, 4); b.Margin = new Padding(4, 4, 8, 4); }
        foreach (var b in new[] { _add, _edit, _genKey, _genPass, _clearDevice, _masters, _keyBtn, _auditLog }) Btn(b);
        _auditLog.Margin = new Padding(40, 4, 8, 4);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(4, 6, 4, 4) };
        var addrRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        addrRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addrRow.Controls.Add(new Label { Text = "API 1 address", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 8, 6) }, 0, 0);
        _api1.Dock = DockStyle.Fill; _api1.Margin = new Padding(0, 4, 8, 4);
        addrRow.Controls.Add(_api1, 1, 0);
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        btnRow.Controls.AddRange(new Control[] { _add, _edit, _genKey, _genPass, _clearDevice, _masters, _keyBtn, _auditLog });
        bottom.Controls.Add(addrRow, 0, 0);
        bottom.Controls.Add(btnRow, 0, 1);

        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.Add(_status);

        Controls.Add(_grid);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(db);
        Controls.Add(strip);

        _connect.Click += async (_, _) => await ConnectAsync();
        _refresh.Click += async (_, _) => await LoadAsync();
        _filter.TextChanged += (_, _) => Fill();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _masters.Click += (_, _) => { if (_repo != null) { using var m = new MastersForm(_rows); m.ShowDialog(this); } };
        _keyBtn.Click += (_, _) => { using var k = new KeyDialog(); if (k.ShowDialog(this) == DialogResult.OK) _status.Text = "Encryption key saved for this Windows user."; };
        _add.Click += async (_, _) => await AddCompanyAsync();
        _edit.Click += async (_, _) => await EditCompanyAsync();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditCompanyAsync(); };
        _genKey.Click += async (_, _) => await GenerateKeyAsync();
        _genPass.Click += async (_, _) => await GeneratePasswordAsync();
        _clearDevice.Click += async (_, _) => await ClearDeviceAsync();
        _auditLog.Click += (_, _) =>
        {
            if (File.Exists(AppSettings.AuditPath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppSettings.AuditPath) { UseShellExecute = true });
            else MessageBox.Show(this, "Nothing has been logged yet.", Text);
        };
        FormClosing += (_, _) => { _s.Api1Address = _api1.Text.Trim(); _s.Save(); };
    }

    // open at a comfortable size for this screen (never larger than the screen), then widen to fit all columns once the list is loaded
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var wa = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Max(MinimumSize.Width, Math.Min((int)(wa.Width * 0.95), LogicalToDeviceUnits(1250))),
                        Math.Max(MinimumSize.Height, Math.Min((int)(wa.Height * 0.90), LogicalToDeviceUnits(720))));
        CenterToScreen();
    }

    // makes the window wide enough for every grid column (as far as the screen allows; otherwise the grid scrolls sideways)
    void FitWidthToColumns()
    {
        var need = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill ? c.MinimumWidth : c.Width)
                   + SystemInformation.VerticalScrollBarWidth + 8;
        var extra = need - _grid.ClientSize.Width;
        if (extra <= 0) return;
        var wa = Screen.FromControl(this).WorkingArea;
        Width = Math.Min(Width + extra, wa.Width);
        if (Right > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
    }

    // ---------------------------------------------------------------- connection / list
    async Task ConnectAsync()
    {
        if (!int.TryParse(_port.Text, out var port) || _host.Text.Trim() == "" || _service.Text.Trim() == "" || _user.Text.Trim() == "" || _pass.Text == "")
        { MessageBox.Show(this, "Fill in host, port, service, user and password.", Text); return; }
        _s.Host = _host.Text.Trim(); _s.Port = _port.Text.Trim(); _s.Service = _service.Text.Trim(); _s.User = _user.Text.Trim(); _s.Save();
        _repo = new ControlRepository($"User Id={_user.Text.Trim()};Password={_pass.Text};Data Source=//{_host.Text.Trim()}:{port}/{_service.Text.Trim()};Connection Timeout=20");
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        if (_repo == null) return;
        try
        {
            UseWaitCursor = true; _status.Text = "Reading companies…";
            _rows = await _repo.ListAll();
            _refresh.Enabled = true;
            Fill();
            FitWidthToColumns();
            _status.Text = $"Connected to {_service.Text.Trim()} - {_rows.Count} company login(s).";
        }
        catch (Exception ex)
        {
            _repo = null; _refresh.Enabled = false; _rows.Clear(); Fill();
            _status.Text = "Not connected.";
            MessageBox.Show(this, "Could not read MOBILE_CONTROL:\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }

    void Fill()
    {
        var keep = _pendingSelect ?? Selected()?.CtrlId; _pendingSelect = null;
        var f = _filter.Text.Trim();
        _grid.Rows.Clear();
        foreach (var r in _rows.Where(r => f == "" || $"{r.ClientName} {r.UserId} {r.Unit} {r.Loca}".Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            var i = _grid.Rows.Add(r.CtrlId, r.ClientName, r.UserId, r.Loca, r.Unit ?? "-", r.Mode.ToString(), r.Active ? "Active" : "Inactive",
                r.Validity?.ToString("dd-MMM-yyyy") ?? "", !string.IsNullOrEmpty(r.ApiKeyHash) ? "Yes" : "NO",
                !string.IsNullOrEmpty(r.PasswordHash) ? "Yes" : "NO",
                string.IsNullOrWhiteSpace(r.DbUser) ? "NOT SET" : r.DbUser + (string.IsNullOrEmpty(r.DbPasswordEnc) ? " (no password)" : ""), r.PhoneId ?? "");
            _grid.Rows[i].Tag = r;
            if (r.CtrlId == keep) _grid.Rows[i].Selected = true;
            if (!r.Active) _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        UpdateButtons();
    }

    ControlRow? Selected() => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ControlRow : null;

    void UpdateButtons()
    {
        _add.Enabled = _masters.Enabled = _repo != null;
        _edit.Enabled = _genKey.Enabled = _genPass.Enabled = _clearDevice.Enabled = _repo != null && Selected() != null;
    }

    static string FirstLine(string s) => s.Split('\n')[0].Trim();

    string Co(ControlRow r) => string.IsNullOrWhiteSpace(r.ClientName) ? "(no name)" : r.ClientName;

    string Card(ControlRow r, params (string Label, string Value)[] extra)
    {
        var lines = new List<string> { $"Company:  {Co(r)}" };
        if (_api1.Text.Trim() != "") lines.Add($"API address:  {_api1.Text.Trim()}");
        lines.Add($"Username:  {r.UserId}");
        foreach (var (l, v) in extra) lines.Add($"{l}:  {v}");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    // the key that encrypts client database passwords; asks for it once if it has not been set on this PC yet
    string? NeedEncryptionKey()
    {
        if (KeyStore.Get() is { } k) return k;
        MessageBox.Show(this, "The encryption key for the database passwords is not set on this PC yet. Enter it now (the same value as Gateway__DbPasswordKey on API 1).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var d = new KeyDialog();
        d.ShowDialog(this);
        return KeyStore.Get();
    }

    // ---------------------------------------------------------------- master data
    // a new company: saves the row, generates its password and API key, and shows both once
    async Task AddCompanyAsync()
    {
        if (_repo == null) return;
        using var dlg = new CompanyDialog(null);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var c = dlg.Result;
        if (dlg.NewDbPassword != null)   // the client database password is saved ENCRYPTED
        {
            var dbKey = NeedEncryptionKey(); if (dbKey == null) return;
            c.DbPasswordEnc = SecretBox.Encrypt(dbKey, dlg.NewDbPassword);
        }
        var key = Hashing.NewApiKey();
        var pw = RandomNumberGenerator.GetString(PasswordChars, 12);
        try
        {
            c.CtrlId = await _repo.Insert(c, Hashing.HashPassword(pw), Hashing.HashApiKey(key));
        }
        catch (Exception ex) when (ControlRepository.IsDuplicateLogin(ex))
        {
            MessageBox.Show(this, $"Another ACTIVE company already uses the login {c.UserId}.\nUse a different login, or make the other one inactive first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        catch (Exception ex) { MessageBox.Show(this, "The company could not be saved (nothing was changed):\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        AppSettings.Audit("Company added (key and password generated)", c.CtrlId, Co(c), c.UserId);
        _status.Text = $"Company {Co(c)} saved (Id {c.CtrlId}) with a new API key and password.";
        _pendingSelect = c.CtrlId;
        using (var d = new SecretDialog("New company - key and password", Co(c), c.UserId, "API key", key, Card(c, ("Password", pw), ("API key", key)),
                   $"company_{Safe(c.UserId)}_{c.CtrlId}.txt", "Password", pw))
            d.ShowDialog(this);
        await LoadAsync();
    }

    // changes the master data of the selected company; its password, API key and registered phone stay as they are
    async Task EditCompanyAsync()
    {
        var old = Selected(); if (old == null || _repo == null) return;
        using var dlg = new CompanyDialog(old);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var c = dlg.Result;
        string? encrypted = null;
        if (dlg.NewDbPassword != null)
        {
            var dbKey = NeedEncryptionKey(); if (dbKey == null) return;
            encrypted = SecretBox.Encrypt(dbKey, dlg.NewDbPassword);
        }
        try
        {
            if (!await _repo.UpdateMaster(c)) { MessageBox.Show(this, "The row no longer exists. Refresh the list.", Text); return; }
            if (encrypted != null) await _repo.SetDbPasswordEnc(c.CtrlId, encrypted);
        }
        catch (Exception ex) when (ControlRepository.IsDuplicateLogin(ex))
        {
            MessageBox.Show(this, $"Another ACTIVE company already uses the login {c.UserId}.\nNothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        catch (Exception ex) { MessageBox.Show(this, "The changes could not be saved (nothing was changed):\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        AppSettings.Audit("Company edited", c.CtrlId, Co(c), c.UserId);
        _status.Text = $"Company {Co(c)} ({c.UserId}) saved.";
        _pendingSelect = c.CtrlId;
        await LoadAsync();
    }

    // ---------------------------------------------------------------- actions
    async Task GenerateKeyAsync()
    {
        var r = Selected(); if (r == null || _repo == null) return;
        if (!string.IsNullOrEmpty(r.ApiKeyHash) && MessageBox.Show(this,
                $"{Co(r)} ({r.UserId}) already has an API key.\n\nA new key makes the OLD key stop working immediately. Every phone of this company must be given the new key.\n\nGenerate a new key?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            var key = Hashing.NewApiKey();
            if (!await _repo.SetApiKeyHash(r.CtrlId, Hashing.HashApiKey(key))) { MessageBox.Show(this, "The row no longer exists. Refresh the list.", Text); return; }
            AppSettings.Audit("API key generated", r.CtrlId, Co(r), r.UserId);
            _status.Text = $"New API key stored for {Co(r)} ({r.UserId}).";
            using var d = new SecretDialog("New API key", Co(r), r.UserId, "API key", key, Card(r, ("API key", key)), $"apikey_{Safe(r.UserId)}_{r.CtrlId}.txt");
            d.ShowDialog(this);
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, "The key could not be saved (nothing was changed):\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    async Task GeneratePasswordAsync()
    {
        var r = Selected(); if (r == null || _repo == null) return;
        if (MessageBox.Show(this, $"Set a new login password for {Co(r)} ({r.UserId})?\n\nThe old password stops working immediately.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            var pw = RandomNumberGenerator.GetString(PasswordChars, 12);
            if (!await _repo.SetPasswordHash(r.CtrlId, Hashing.HashPassword(pw))) { MessageBox.Show(this, "The row no longer exists. Refresh the list.", Text); return; }
            AppSettings.Audit("Password reset", r.CtrlId, Co(r), r.UserId);
            _status.Text = $"New password stored for {Co(r)} ({r.UserId}).";
            using var d = new SecretDialog("New password", Co(r), r.UserId, "Password", pw, Card(r, ("Password", pw)), $"password_{Safe(r.UserId)}_{r.CtrlId}.txt");
            d.ShowDialog(this);
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, "The password could not be saved (nothing was changed):\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    async Task ClearDeviceAsync()
    {
        var r = Selected(); if (r == null || _repo == null) return;
        if (r.PhoneId == null) { MessageBox.Show(this, "No device is bound to this login.", Text); return; }
        if (MessageBox.Show(this, $"Remove the registered phone from {Co(r)} ({r.UserId})?\n\nThe next phone that signs in will be registered instead.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            await _repo.ClearDevice(r.CtrlId);
            AppSettings.Audit("Device binding cleared", r.CtrlId, Co(r), r.UserId);
            _status.Text = $"Device binding cleared for {Co(r)} ({r.UserId}).";
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not clear the device:\n" + FirstLine(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    static string Safe(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
}
