using Mobile.Common;

namespace MobileKeyManager;

// Add / edit one row of CLASSICNEW.MOBILE_MASTER_CONFIG: which master the phone reads, from which table, with which columns and filters.
// Self-sizing layout (no fixed pixel positions) so it fits any display scaling.
public sealed class MasterDialog : Form
{
    readonly ConfigRow _row;
    readonly bool _isNew;
    readonly List<ControlRow> _companies;
    readonly long? _lockedScope;

    readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _key = new() { PlaceholderText = "e.g. tea-estate" }, _desc = new(), _table = new() { PlaceholderText = "e.g. M_TEA_ESTATE" },
        _cols = new() { PlaceholderText = "CODE,DESCN,PTYPE" }, _aliases = new() { PlaceholderText = "OUTNAME=SOURCECOLUMN (optional)" },
        _options = new() { PlaceholderText = "Key=Value;Key=Value (optional)" }, _fcol = new() { PlaceholderText = "e.g. PTYPE" }, _fval = new() { PlaceholderText = "e.g. W,S" },
        _ukey = new() { PlaceholderText = "(optional)" }, _ucol = new() { PlaceholderText = "(optional)" }, _remarks = new();
    readonly NumericUpDown _seq = new() { Minimum = 1, Maximum = 99999, Value = 100 };
    readonly CheckBox _unit = new() { Text = "MVC: filter by LOCA + UNIT", AutoSize = true };
    readonly CheckBox _doc = new() { Text = "MVC: also by DOC_YEAR", AutoSize = true };
    readonly CheckBox _listing = new() { Text = "VB: must be listed in MASTER_TABLE_LOCATION", AutoSize = true };
    readonly CheckBox _active = new() { Text = "Active (the phone is told to read it)", AutoSize = true, Checked = true };
    readonly Label _preview = new() { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.DarkSlateBlue, Font = new Font("Consolas", 9.5f) };

    public ConfigRow Result { get; private set; }

    // lockedScope: the company id a new "override" row is created for (the scope cannot be changed then)
    public MasterDialog(ConfigRow? existing, List<ControlRow> companies, ConfigRow? template = null, long? lockedScope = null)
    {
        _isNew = existing == null;
        _row = existing ?? template?.Clone() ?? new ConfigRow();
        _companies = companies; _lockedScope = lockedScope;
        Result = _row;
        Text = _isNew ? "Add master" : $"Edit master - {existing!.ItemKey}";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 10, 14, 10);
        var wa = Screen.FromControl(this).WorkingArea;
        MaximumSize = new Size(wa.Width, wa.Height); AutoScroll = true;

        _mode.Items.AddRange(new object[] { "VB", "MVC" });
        _type.Items.AddRange(ConfigRow.OpTypes);
        _source.Items.AddRange(new object[] { "CLIENT", "CLASSICNEW" });

        var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        int row = 0;
        Control Lbl(string s) => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) };
        void Put(string l1, Control c1, string? l2 = null, Control? c2 = null)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Lbl(l1), 0, row);
            c1.Dock = DockStyle.Fill; c1.Margin = new Padding(0, 3, 18, 3); t.Controls.Add(c1, 1, row);
            if (l2 != null && c2 != null) { t.Controls.Add(Lbl(l2), 2, row); c2.Dock = DockStyle.Fill; c2.Margin = new Padding(0, 3, 0, 3); t.Controls.Add(c2, 3, row); }
            row++;
        }
        void Span(Control c, Padding m) { t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); c.Margin = m; t.Controls.Add(c, 0, row); t.SetColumnSpan(c, 4); row++; }

        Put("Applies to *", _scope, "App mode *", _mode);
        Put("Key *", _key, "Order (sequence) *", _seq);
        Put("Description", _desc, "Type *", _type);
        Put("Schema from *", _source, "Table *", _table);
        Put("Columns *", _cols, "Aliases", _aliases);
        Put("Filter column", _fcol, "Filter values", _fval);
        Put("Update key column", _ukey, "Update column", _ucol);
        Put("Options", _options, "Remarks", _remarks);
        var checks = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 4, 0, 2), MaximumSize = new Size(760, 0) };
        foreach (var c in new Control[] { _unit, _doc, _listing, _active }) { c.Margin = new Padding(0, 2, 18, 2); checks.Controls.Add(c); }
        Span(checks, new Padding(0));
        Span(new Label { Text = "Query preview (a sample company; values are always sent as bind variables):", AutoSize = true, ForeColor = SystemColors.GrayText }, new Padding(0, 6, 0, 0));
        Span(_preview, new Padding(0, 2, 0, 4));
        Span(new Label
        {
            Text = "Key = the name used in the call (/mobile/v1/masters/<key>).  Order = the sequence the phone reads them in.  App mode = VB (old) or MVC (new): a master can have one row for each.\n" +
                   "Columns = comma list, or Aliases to rename (OUTNAME=SOURCE).  Applies to = every company (default) or one company only (that company's row replaces the default).",
            ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(760, 0)
        }, new Padding(0, 0, 0, 4));

        var ok = new Button { Text = "Save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 4, 16, 4), Margin = new Padding(8, 4, 0, 4) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 4, 16, 4), Margin = new Padding(8, 4, 0, 4), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        var outer = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2 };
        outer.Controls.Add(t, 0, 0); outer.Controls.Add(buttons, 0, 1);
        Controls.Add(outer);
        CancelButton = cancel;

        // scope list: default + every company
        _scope.Items.Add("All companies (default)");
        foreach (var c in _companies) _scope.Items.Add($"{(string.IsNullOrWhiteSpace(c.ClientName) ? "(no name)" : c.ClientName)}  ({c.UserId})  #{c.CtrlId}");
        var scopeId = existing != null ? existing.CtrlId : lockedScope ?? _row.CtrlId;
        _scope.SelectedIndex = scopeId == null ? 0 : Math.Max(0, _companies.FindIndex(c => c.CtrlId == scopeId) + 1);
        _scope.Enabled = _isNew && lockedScope == null;   // the scope of a saved row is fixed

        _mode.SelectedItem = _row.AppMode; _key.Text = _row.ItemKey; _seq.Value = Math.Min(99999, Math.Max(1, _row.SeqNo)); _desc.Text = _row.Description ?? "";
        _type.SelectedItem = _row.OpType; _source.SelectedItem = _row.SchemaSource; _table.Text = _row.TableName; _cols.Text = _row.ColumnList ?? ""; _aliases.Text = _row.Aliases ?? "";
        _fcol.Text = _row.FilterColumn ?? ""; _fval.Text = _row.FilterValues ?? ""; _ukey.Text = _row.UpdateKeyCol ?? ""; _ucol.Text = _row.UpdateCol ?? "";
        _options.Text = _row.Options ?? ""; _remarks.Text = _row.Remarks ?? "";
        _unit.Checked = _row.UnitFilter; _doc.Checked = _row.DocYearFilter; _listing.Checked = _row.RequireListing; _active.Checked = _row.Active;

        foreach (var c in new Control[] { _key, _table, _cols, _aliases, _fcol, _fval, _ukey, _ucol, _options, _seq }) c.TextChanged += (_, _) => Refresh_();
        foreach (var c in new ComboBox[] { _mode, _type, _source }) c.SelectedIndexChanged += (_, _) => Refresh_();
        foreach (var c in new CheckBox[] { _unit, _doc, _listing }) c.CheckedChanged += (_, _) => Refresh_();
        Refresh_();

        ok.Click += (_, _) =>
        {
            var r = Collect();
            var (errors, warnings) = ConfigValidator.Validate(r);
            if (errors.Count > 0) { MessageBox.Show(this, string.Join("\n", errors.Select(e => "• " + e)), "Please correct", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (warnings.Count > 0 && MessageBox.Show(this, string.Join("\n", warnings.Select(w => "• " + w)) + "\n\nSave anyway?", "Check", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Result = r;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;
    }

    ConfigRow Collect() => new()
    {
        CfgId = _row.CfgId,
        CtrlId = _isNew ? (_scope.SelectedIndex <= 0 ? null : _companies[_scope.SelectedIndex - 1].CtrlId) : _row.CtrlId,
        AppMode = _mode.SelectedItem as string ?? "VB", SeqNo = (int)_seq.Value, ItemKey = _key.Text.Trim(), Description = _desc.Text.Trim(),
        OpType = _type.SelectedItem as string ?? "READ", SchemaSource = _source.SelectedItem as string ?? "CLIENT", TableName = _table.Text.Trim(),
        ColumnList = _cols.Text.Trim(), Aliases = _aliases.Text.Trim(), FilterColumn = _fcol.Text.Trim(), FilterValues = _fval.Text.Trim(),
        UnitFilter = _unit.Checked, DocYearFilter = _doc.Checked, RequireListing = _listing.Checked, AllowClients = _row.AllowClients,
        UpdateKeyCol = _ukey.Text.Trim(), UpdateCol = _ucol.Text.Trim(), UpdateMaxLen = _row.UpdateMaxLen, Active = _active.Checked,
        Remarks = _remarks.Text.Trim(), Options = _options.Text.Trim()
    };

    void Refresh_()
    {
        try
        {
            var r = Collect();
            var item = ConfigValidator.ToItem(r);
            if (r.OpType != "READ") { _preview.Text = r.OpType == "DEVICE_CHECK" ? "(device check: read from the user table by its own call)" : "(not a plain SELECT: handled by its own call or an upload)"; return; }
            var sample = new ControlRow { SchemaName = "COMPANY_SCHEMA", Loca = "LOCA", Unit = "UNIT", DocYear = "2027", MainUnit = "MAINUNIT" };
            _preview.Text = MasterQuery.BuildSelect(item, sample).Sql + (item.RequireListing ? "\n+ check MASTER_TABLE_LOCATION (when the company has a MAIN_UNIT)" : "");
        }
        catch (Exception ex) { _preview.Text = "(" + ex.Message + ")"; }
    }
}
