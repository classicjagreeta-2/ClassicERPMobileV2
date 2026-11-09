namespace MobileKeyManager;

// Sets the key that encrypts the client database passwords (the same value as Gateway__DbPasswordKey on API 1).
public sealed class KeyDialog : Form
{
    readonly TextBox _key = new() { UseSystemPasswordChar = true };

    public KeyDialog()
    {
        Text = "Database password encryption key";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 12, 16, 12);

        var col = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        void Add(Control c) { col.RowStyles.Add(new RowStyle(SizeType.AutoSize)); col.Controls.Add(c, 0, col.RowCount++); }
        Add(new Label
        {
            Text = "The database passwords of the client companies are stored ENCRYPTED in MOBILE_CONTROL. This key encrypts them here and is the same value as " +
                   "Gateway__DbPasswordKey on API 1 (which decrypts them). Use at least 32 characters and keep it private. If it is lost, the saved database passwords " +
                   "cannot be recovered and must be entered again.",
            AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 8)
        });
        Add(new Label { Text = $"Current key: {KeyStore.Get() switch { null => "NOT SET", var k => $"set ({k.Length} characters)" }}  -  {KeyStore.Source}", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 8) });
        _key.Dock = DockStyle.Fill; _key.Margin = new Padding(0, 0, 0, 4); _key.PlaceholderText = "paste the key here";
        Add(_key);
        var show = new CheckBox { Text = "Show the key", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !show.Checked;
        Add(show);
        var ok = new Button { Text = "Save", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 4, 16, 4) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16, 4, 16, 4), DialogResult = DialogResult.Cancel };
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        row.Controls.Add(cancel); row.Controls.Add(ok);
        Add(row);
        Controls.Add(col);
        AcceptButton = ok; CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            var k = _key.Text.Trim();
            if (k.Length < 32) { MessageBox.Show(this, "The key must be at least 32 characters.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var old = KeyStore.Get();
            if (old != null && old != k && MessageBox.Show(this, "A different key is already set. Passwords saved with the old key can no longer be read by this tool or API 1 once API 1 uses the new key.\n\nReplace it?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            KeyStore.Set(k);
            DialogResult = DialogResult.OK;
        };
    }
}
