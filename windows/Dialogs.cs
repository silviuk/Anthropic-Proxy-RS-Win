using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AnthropicProxyDesktop
{
    sealed class ExportOptions : Form
    {
        internal readonly CheckBox IncludeKey = new CheckBox();
        internal readonly Label Warning = new Label();
        internal ExportOptions()
        {
            Text = "Export settings"; Font = new Font("Segoe UI", 10);
            ClientSize = new Size(440, 175); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Controls.Add(new Label { Text = "Save these settings as a JSON file.", AutoSize = true, Left = 16, Top = 15 });
            IncludeKey.Text = "Include API key"; IncludeKey.Checked = false; IncludeKey.AutoSize = true; IncludeKey.Left = 16; IncludeKey.Top = 48;
            Warning.Text = "don't share with others"; Warning.AutoSize = true; Warning.Left = 16; Warning.Top = 79; Warning.Visible = false;
            IncludeKey.CheckedChanged += delegate { Warning.Visible = IncludeKey.Checked; Warning.ForeColor = Color.DarkOrange; };
            Controls.Add(IncludeKey); Controls.Add(Warning);
            var save = new Button { Text = "Export...", DialogResult = DialogResult.OK, Left = 236, Top = 124, Width = 90, Height = 30 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 334, Top = 124, Width = 90, Height = 30 };
            Controls.Add(save); Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
        }
    }
    sealed class EntryEditor : Form
    {
        readonly ListBox entries = new ListBox();
        readonly TextBox value = new TextBox();
        internal string Value { get { return String.Join(";", entries.Items.Cast<string>()); } }
        internal EntryEditor(string title, string initial)
        {
            Text = title; Font = new Font("Segoe UI", 10); ClientSize = new Size(560, 350); MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            entries.Dock = DockStyle.Fill; entries.HorizontalScrollbar = true;
            entries.Items.AddRange(initial.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray());
            entries.SelectedIndexChanged += delegate { if (entries.SelectedIndex >= 0) value.Text = (string)entries.SelectedItem; };
            value.Dock = DockStyle.Fill; value.AccessibleName = "Entry value";
            layout.Controls.Add(entries, 0, 0); layout.Controls.Add(value, 0, 1);
            var edits = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "Add", AutoSize = true }; var update = new Button { Text = "Update", AutoSize = true };
            var remove = new Button { Text = "Remove", AutoSize = true }; var up = new Button { Text = "Move up", AutoSize = true }; var down = new Button { Text = "Move down", AutoSize = true };
            add.Click += delegate { if (!String.IsNullOrWhiteSpace(value.Text)) { entries.Items.Add(value.Text.Trim()); value.Clear(); } };
            update.Click += delegate { if (entries.SelectedIndex >= 0 && !String.IsNullOrWhiteSpace(value.Text)) entries.Items[entries.SelectedIndex] = value.Text.Trim(); };
            remove.Click += delegate { if (entries.SelectedIndex >= 0) entries.Items.RemoveAt(entries.SelectedIndex); };
            up.Click += delegate { MoveEntry(-1); }; down.Click += delegate { MoveEntry(1); };
            edits.Controls.AddRange(new Control[] { add, update, remove, up, down }); layout.Controls.Add(edits, 0, 2);
            var done = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "Use entries", DialogResult = DialogResult.OK, AutoSize = true };
            done.Controls.Add(cancel); done.Controls.Add(save); layout.Controls.Add(done, 0, 3);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        }
        void MoveEntry(int direction)
        {
            int from = entries.SelectedIndex, to = from + direction;
            if (from < 0 || to < 0 || to >= entries.Items.Count) return;
            object item = entries.Items[from]; entries.Items.RemoveAt(from); entries.Items.Insert(to, item); entries.SelectedIndex = to;
        }
    }
}
