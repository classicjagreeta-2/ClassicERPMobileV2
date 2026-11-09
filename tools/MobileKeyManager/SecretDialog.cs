using System.Text;

namespace MobileKeyManager;

// Shows a newly generated key (and, for a new company, password) ONCE. Only hashes are stored, so they cannot be shown again.
// Self-sizing layout: fits any display scaling and nothing is cut off.
public sealed class SecretDialog : Form
{
    public SecretDialog(string title, string company, string login, string secretLabel, string secret, string card, string fileName,
                        string? label2 = null, string? secret2 = null)
    {
        Text = title;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 12, 16, 12);

        var col = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 640));
        void Add(Control c) { col.RowStyles.Add(new RowStyle(SizeType.AutoSize)); col.Controls.Add(c, 0, col.RowCount++); }

        Add(new Label
        {
            Text = "Copy it now. It is shown only once: only a hash is stored, so it cannot be recovered. It is removed from the clipboard after 1 minute.",
            ForeColor = Color.Firebrick, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 0, 0, 8)
        });
        Add(new Label { Text = $"Company: {company}\nLogin: {login}", AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        Add(new Label { Text = secretLabel, AutoSize = true });
        Add(new TextBox { Text = secret, ReadOnly = true, Font = new Font("Consolas", 12f), Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 8) });
        if (secret2 != null)
        {
            Add(new Label { Text = label2, AutoSize = true });
            Add(new TextBox { Text = secret2, ReadOnly = true, Font = new Font("Consolas", 12f), Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 8) });
        }

        Button B(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 4, 8, 4) };
        var copy = B($"Copy {secretLabel.ToLowerInvariant()}");
        var copy2 = secret2 != null ? B($"Copy {label2!.ToLowerInvariant()}") : null;
        var copyCard = B("Copy phone setup text");
        var save = B("Save to file…");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        buttons.Controls.Add(copy); if (copy2 != null) buttons.Controls.Add(copy2); buttons.Controls.Add(copyCard); buttons.Controls.Add(save);
        Add(buttons);

        var close = new Button { Text = "I have saved it – Close", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 4, 14, 4), DialogResult = DialogResult.OK };
        var closeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        closeRow.Controls.Add(close);
        Add(closeRow);
        Controls.Add(col);
        AcceptButton = close;

        copy.Click += (_, _) => ClipboardGuard.Copy(secret);
        copy2?.Let(b => b.Click += (_, _) => ClipboardGuard.Copy(secret2!));
        copyCard.Click += (_, _) => ClipboardGuard.Copy(card);
        save.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { FileName = fileName, Filter = "Text file (*.txt)|*.txt", Title = "Save the setup text (keep it private)" };
            if (dlg.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dlg.FileName, card, Encoding.UTF8);
        };
        Shown += (_, _) =>
        {
            if (col.GetControlFromPosition(0, 3) is TextBox tb) { tb.SelectAll(); tb.Focus(); }
        };
    }
}

static class Ext
{
    public static void Let<T>(this T v, Action<T> a) => a(v);
}
