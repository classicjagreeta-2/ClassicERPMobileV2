using Mobile.Common;

namespace MobileKeyManager;

// Master data setup: the rows of MOBILE_MASTER_CONFIG in ONE CLIENT'S OWN DATABASE - which masters the phone reads, in which order, from which table.
// Choose the company, open its database (with the encrypted login saved for it), then edit. A row can apply to every company served from that
// database (default) or to one company only (an override that replaces the default for that company).
public sealed class MastersForm : Form
{
    ControlRepository? _repo;
    ControlRow? _open;
    readonly ComboBox _company = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button _openDb = new() { Text = "Open its database" };
    readonly Label _dbInfo = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 8, 0, 0) };
    readonly List<ControlRow> _companies;
    List<ConfigRow> _rows = new();

    readonly ComboBox _modeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _scopeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _search = new() { PlaceholderText = "filter by key, table or description" };
    readonly Button _refresh = new() { Text = "Refresh" };
    readonly Button _add = new() { Text = "Add master…" }, _edit = new() { Text = "Edit…", Enabled = false };
    readonly Button _toggle = new() { Text = "Switch off", Enabled = false };
    readonly Button _override = new() { Text = "Copy to a company…", Enabled = false };
    readonly Button _delete = new() { Text = "Delete company row", Enabled = false };
    readonly Button _close = new() { Text = "Close", DialogResult = DialogResult.Cancel };
    readonly DataGridView _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, ScrollBars = ScrollBars.Both
    };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    long? _pendingSelect;

    public MastersForm(List<ControlRow> companies)
    {
        _companies = companies;
        Text = "Master data setup - MOBILE_MASTER_CONFIG (the client's own database)";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 520);
        CancelButton = _close;

        _modeFilter.Items.AddRange(new object[] { "All modes", "VB", "MVC" }); _modeFilter.SelectedIndex = 0;
        _scopeFilter.Items.Add("All rows"); _scopeFilter.Items.Add("Defaults only");
        foreach (var c in _companies) _scopeFilter.Items.Add($"{Co(c)}  ({c.UserId})  #{c.CtrlId}");
        _scopeFilter.SelectedIndex = 0;

        // which company's database to work in
        foreach (var c in _companies) _company.Items.Add($"{Co(c)}  ({c.UserId})  #{c.CtrlId}" + (string.IsNullOrWhiteSpace(c.DbUser) ? "   - no database login" : ""));
        if (_company.Items.Count > 0) _company.SelectedIndex = 0;
        _openDb.AutoSize = true; _openDb.Padding = new Padding(10, 2, 10, 2);
        var dbBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4), BackColor = Color.FromArgb(235, 242, 250) };
        dbBar.Controls.Add(new Label { Text = "Company database", AutoSize = true, Margin = new Padding(4, 8, 4, 0), Font = new Font(Font, FontStyle.Bold) });
        _company.Width = 420; _company.Margin = new Padding(4, 4, 8, 4); dbBar.Controls.Add(_company);
        _openDb.Margin = new Padding(4, 3, 8, 3); dbBar.Controls.Add(_openDb); dbBar.Controls.Add(_dbInfo);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4) };
        void Add(Control c, int w = 0, Padding? m = null) { if (w > 0) c.Width = w; c.Margin = m ?? new Padding(4, 4, 8, 4); top.Controls.Add(c); }
        Add(new Label { Text = "Mode", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }); Add(_modeFilter, 130);
        Add(new Label { Text = "Applies to", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }); Add(_scopeFilter, 260);
        Add(_search, 260);
        _refresh.AutoSize = true; _refresh.Padding = new Padding(10, 2, 10, 2); Add(_refresh);

        void Col(string name, string header, bool fill = false)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header };
            if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = 160; }
            _grid.Columns.Add(c);
        }
        Col("id", "Id"); Col("scope", "Applies to"); Col("mode", "Mode"); Col("seq", "Order"); Col("key", "Key"); Col("desc", "Description"); Col("type", "Type");
        Col("src", "Schema"); Col("table", "Table"); Col("cols", "Columns", fill: true); Col("filter", "Filter"); Col("unit", "LOCA+UNIT"); Col("year", "Year");
        Col("list", "Listing"); Col("active", "Active"); Col("opts", "Options");
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.DefaultCellStyle.Padding = new Padding(2);

        void Btn(Button b) { b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(12, 4, 12, 4); b.Margin = new Padding(4, 4, 8, 4); }
        foreach (var b in new[] { _add, _edit, _toggle, _override, _delete, _close }) Btn(b);
        _close.Margin = new Padding(40, 4, 8, 4);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(4) };
        bottom.Controls.AddRange(new Control[] { _add, _edit, _toggle, _override, _delete, _close });
        var strip = new StatusStrip { SizingGrip = false }; strip.Items.Add(_status);

        Controls.Add(_grid); Controls.Add(bottom); Controls.Add(top); Controls.Add(dbBar); Controls.Add(strip);

        _openDb.Click += async (_, _) => await OpenDatabaseAsync();
        _refresh.Click += async (_, _) => await LoadAsync();
        _modeFilter.SelectedIndexChanged += (_, _) => Fill();
        _scopeFilter.SelectedIndexChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => Fill();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditAsync(); };
        _add.Click += async (_, _) => await AddAsync();
        _edit.Click += async (_, _) => await EditAsync();
        _toggle.Click += async (_, _) => await ToggleAsync();
        _override.Click += async (_, _) => await CopyToCompanyAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _status.Text = "Choose a company and click \"Open its database\".";
        _add.Enabled = _refresh.Enabled = false;
    }

    // connects to the chosen company's own database with the (decrypted) login saved for it
    async Task OpenDatabaseAsync()
    {
        if (_company.SelectedIndex < 0) return;
        var c = _companies[_company.SelectedIndex];
        if (string.IsNullOrWhiteSpace(c.DbUser) || string.IsNullOrEmpty(c.DbPasswordEnc))
        { MessageBox.Show(this, $"{Co(c)} has no database login saved yet.\nUse Edit company to enter its database user and password first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var key = KeyStore.Get();
        if (key == null) { MessageBox.Show(this, "The encryption key is not set on this PC. Use \"Encryption key…\" in the main window first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var pw = SecretBox.Decrypt(key, c.DbPasswordEnc);
        if (pw == null) { MessageBox.Show(this, "The saved database password cannot be decrypted with the key on this PC (a different key was used when it was saved). Enter the company's database password again in Edit company.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var host = string.IsNullOrWhiteSpace(c.DbHost) ? c.IpAddress : c.DbHost;
        _repo = new ControlRepository($"User Id={c.DbUser};Password={pw};Data Source=//{host}:{c.DatabasePort}/{c.DatabaseName};Connection Timeout=20", "MOBILE_MASTER_CONFIG");
        _open = c;
        _dbInfo.Text = $"{c.DbUser} @ {host}:{c.DatabasePort}/{c.DatabaseName}";
        _add.Enabled = _refresh.Enabled = true;
        await LoadAsync();
    }

    // the table is missing in this client database: offer to create it (and seed the default masters) with the company's own login
    async Task<bool> OfferCreateTableAsync()
    {
        if (_open == null) return false;
        if (MessageBox.Show(this, $"The table MOBILE_MASTER_CONFIG does not exist in {_open.ClientName}'s database yet.\n\nCreate it now and fill it with the default masters (VB and MVC)?\nThis uses the company's own database login ({_open.DbUser}), which needs the right to create a table and a sequence.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        try
        {
            UseWaitCursor = true;
            var con = _dbConnectionString();
            await ClientScript.RunAsync(con);
            AppSettings.AuditConfig("Master setup table created in the client database", 0, $"{_open.ClientName} ({_open.UserId})", "MOBILE_MASTER_CONFIG");
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, "The table could not be created:\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        finally { UseWaitCursor = false; }
    }

    string _dbConnectionString()
    {
        var c = _open!; var pw = SecretBox.Decrypt(KeyStore.Get()!, c.DbPasswordEnc)!;
        var host = string.IsNullOrWhiteSpace(c.DbHost) ? c.IpAddress : c.DbHost;
        return $"User Id={c.DbUser};Password={pw};Data Source=//{host}:{c.DatabasePort}/{c.DatabaseName};Connection Timeout=30";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var wa = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Max(MinimumSize.Width, Math.Min((int)(wa.Width * 0.96), LogicalToDeviceUnits(1500))),
                        Math.Max(MinimumSize.Height, Math.Min((int)(wa.Height * 0.92), LogicalToDeviceUnits(760))));
        CenterToParent();
    }

    static string Co(ControlRow c) => string.IsNullOrWhiteSpace(c.ClientName) ? "(no name)" : c.ClientName;
    string ScopeName(long? id) => id == null ? "All companies" : _companies.FirstOrDefault(c => c.CtrlId == id) is { } c ? $"{Co(c)} ({c.UserId})" : $"company #{id}";
    ConfigRow? Selected() => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ConfigRow : null;

    async Task LoadAsync(bool offered = false)
    {
        if (_repo == null) return;
        try
        {
            UseWaitCursor = true; _status.Text = "Reading MOBILE_MASTER_CONFIG…";
            _rows = await _repo.ListConfig();
            Fill();
            _status.Text = $"{_open?.ClientName}: {_rows.Count} master row(s); {_rows.Count(r => r.CtrlId == null)} default, {_rows.Count(r => r.CtrlId != null)} company override(s).";
        }
        catch (Exception ex) when (!offered && ex.Message.Contains("ORA-00942"))
        {
            UseWaitCursor = false;
            if (await OfferCreateTableAsync()) { await LoadAsync(offered: true); return; }
            _rows.Clear(); Fill(); _status.Text = "The master setup table does not exist in this database yet.";
        }
        catch (Exception ex)
        {
            _rows.Clear(); Fill();
            _status.Text = "Could not read the table.";
            MessageBox.Show(this, "Could not read MOBILE_MASTER_CONFIG from the company's database:\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }

    void Fill()
    {
        var keep = _pendingSelect ?? Selected()?.CfgId; _pendingSelect = null;
        var mode = _modeFilter.SelectedIndex == 0 ? null : _modeFilter.SelectedItem as string;
        long? scopeId = null; var scopeKind = _scopeFilter.SelectedIndex; // 0 all, 1 defaults, 2+ company
        if (scopeKind >= 2) scopeId = _companies[scopeKind - 2].CtrlId;
        var f = _search.Text.Trim();
        _grid.Rows.Clear();
        foreach (var r in _rows.Where(r => (mode == null || r.AppMode == mode) && (scopeKind == 0 || (scopeKind == 1 && r.CtrlId == null) || (scopeKind >= 2 && r.CtrlId == scopeId))
                                          && (f == "" || $"{r.ItemKey} {r.TableName} {r.Description}".Contains(f, StringComparison.OrdinalIgnoreCase))))
        {
            var filter = string.IsNullOrWhiteSpace(r.FilterColumn) ? "" : $"{r.FilterColumn} IN ({r.FilterValues})";
            var i = _grid.Rows.Add(r.CfgId, ScopeName(r.CtrlId), r.AppMode, r.SeqNo, r.ItemKey, r.Description, r.OpType, r.SchemaSource == "CLASSICNEW" ? "CLASSICNEW" : "Company", r.TableName,
                (r.ColumnList + (string.IsNullOrWhiteSpace(r.Aliases) ? "" : "  | " + r.Aliases)).Trim(), filter, r.UnitFilter ? "Yes" : "", r.DocYearFilter ? "Yes" : "",
                r.RequireListing ? "Yes" : "", r.Active ? "Yes" : "OFF", r.Options);
            _grid.Rows[i].Tag = r;
            if (r.CfgId == keep) _grid.Rows[i].Selected = true;
            if (!r.Active) _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
            if (r.CtrlId != null) _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 230);   // company override
        }
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var r = Selected();
        _edit.Enabled = _toggle.Enabled = r != null;
        _override.Enabled = r is { CtrlId: null };
        _delete.Enabled = r is { CtrlId: not null };
        _toggle.Text = r is { Active: false } ? "Switch on" : "Switch off";
    }

    static string First(string s) => s.Split('\n')[0].Trim();
    static bool IsDuplicate(Exception ex) => ControlRepository.IsDuplicateLogin(ex);   // ORA-00001 = same (company, mode, key) already exists

    async Task AddAsync()
    {
        using var d = new MasterDialog(null, _companies);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await SaveNewAsync(d.Result);
    }

    async Task SaveNewAsync(ConfigRow c)
    {
        try
        {
            c.CfgId = await _repo!.InsertConfig(c);
            AppSettings.AuditConfig("Master added", c.CfgId, ScopeName(c.CtrlId), $"{c.AppMode}/{c.ItemKey}");
            _status.Text = $"Master '{c.ItemKey}' ({c.AppMode}) saved for {ScopeName(c.CtrlId)}. The APIs pick it up within about a minute.";
            _pendingSelect = c.CfgId;
            await LoadAsync();
        }
        catch (Exception ex) when (IsDuplicate(ex))
        {
            MessageBox.Show(this, $"{ScopeName(c.CtrlId)} already has a {c.AppMode} master with the key '{c.ItemKey}'.\nEdit that row instead, or use a different key.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, "The master could not be saved (nothing was changed):\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    async Task EditAsync()
    {
        var r = Selected(); if (r == null) return;
        using var d = new MasterDialog(r, _companies);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var c = d.Result;
        try
        {
            if (!await _repo!.UpdateConfig(c)) { MessageBox.Show(this, "The row no longer exists. Refresh the list.", Text); return; }
            AppSettings.AuditConfig("Master edited", c.CfgId, ScopeName(c.CtrlId), $"{c.AppMode}/{c.ItemKey}");
            _status.Text = $"Master '{c.ItemKey}' ({c.AppMode}) saved. The APIs pick it up within about a minute.";
            _pendingSelect = c.CfgId;
            await LoadAsync();
        }
        catch (Exception ex) when (IsDuplicate(ex))
        {
            MessageBox.Show(this, $"{ScopeName(c.CtrlId)} already has a {c.AppMode} master with the key '{c.ItemKey}'. Nothing was changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show(this, "The changes could not be saved (nothing was changed):\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    async Task ToggleAsync()
    {
        var r = Selected(); if (r == null) return;
        var on = !r.Active;
        var msg = on ? $"Switch ON '{r.ItemKey}' ({r.AppMode}) for {ScopeName(r.CtrlId)}?\n\nThe phone will be told to read it again."
                     : $"Switch OFF '{r.ItemKey}' ({r.AppMode}) for {ScopeName(r.CtrlId)}?\n\nThe phone will no longer be told to read it, and API 2 stops serving it.";
        if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            await _repo!.SetConfigActive(r.CfgId, on);
            AppSettings.AuditConfig(on ? "Master switched on" : "Master switched off", r.CfgId, ScopeName(r.CtrlId), $"{r.AppMode}/{r.ItemKey}");
            _pendingSelect = r.CfgId;
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not change the row:\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    // a copy of a default row for one company: it replaces the default for that company only
    async Task CopyToCompanyAsync()
    {
        var r = Selected(); if (r is not { CtrlId: null }) return;
        using var pick = new Form
        {
            Text = "Copy to a company", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Font = Font,
            AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi
        };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, Margin = new Padding(0, 4, 0, 10) };
        foreach (var c in _companies) combo.Items.Add($"{Co(c)}  ({c.UserId})  #{c.CtrlId}");
        if (combo.Items.Count == 0) { MessageBox.Show(this, "There is no company yet.", Text); return; }
        combo.SelectedIndex = 0;
        var ok = new Button { Text = "Copy and edit…", AutoSize = true, Padding = new Padding(12, 4, 12, 4), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(12, 4, 12, 4), DialogResult = DialogResult.Cancel };
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill }; row.Controls.Add(cancel); row.Controls.Add(ok);
        var col = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
        col.Controls.Add(new Label { Text = $"Give one company its own version of '{r.ItemKey}' ({r.AppMode}).\nThat company then uses the copy instead of the default.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        col.Controls.Add(combo, 0, 1); col.Controls.Add(row, 0, 2);
        pick.Controls.Add(col); pick.AcceptButton = ok; pick.CancelButton = cancel;
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        var company = _companies[combo.SelectedIndex];
        if (_rows.Any(x => x.CtrlId == company.CtrlId && x.AppMode == r.AppMode && x.ItemKey == r.ItemKey))
        { MessageBox.Show(this, $"{Co(company)} already has its own {r.AppMode} '{r.ItemKey}'. Edit that row instead.", Text); return; }

        var template = r.Clone(); template.CfgId = 0; template.CtrlId = company.CtrlId; template.Remarks = string.IsNullOrWhiteSpace(r.Remarks) ? "company override" : r.Remarks;
        using var d = new MasterDialog(null, _companies, template, company.CtrlId);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await SaveNewAsync(d.Result);
    }

    async Task DeleteAsync()
    {
        var r = Selected(); if (r is not { CtrlId: not null }) return;
        if (MessageBox.Show(this, $"Delete {ScopeName(r.CtrlId)}'s own '{r.ItemKey}' ({r.AppMode}) row?\n\nThat company then uses the default again. This cannot be undone.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            await _repo!.DeleteCompanyConfig(r.CfgId);
            AppSettings.AuditConfig("Company master row deleted", r.CfgId, ScopeName(r.CtrlId), $"{r.AppMode}/{r.ItemKey}");
            await LoadAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not delete the row:\n" + First(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
