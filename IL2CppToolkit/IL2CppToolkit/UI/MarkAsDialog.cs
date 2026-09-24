// language: C#, file: UI/MarkAsDialog.cs
using System;
using System.Drawing;
using System.Windows.Forms;

public class MarkAsDialog : Form
{
    public string Semantic { get; private set; }
    public string Description { get; private set; }

    public MarkAsDialog(string cls, string field, int offset, string type, bool isStatic)
    {
        Text = "Mark field as semantic";
        Size = new Size(600, 440);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;

        // header
        var header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(600, 100),
            BackColor = Theme.Card
        };
        var l1 = new Label
        {
            Text = $"{cls}.{field}",
            ForeColor = Theme.Text,
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            Location = new Point(20, 16),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        var l2 = new Label
        {
            Text = $"+0x{offset:X}   ·   {type}   ·   {(isStatic ? "static" : "instance")}",
            ForeColor = Theme.Dim,
            Font = new Font("Consolas", 9f),
            Location = new Point(20, 46),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(l1);
        header.Controls.Add(l2);

        // warning якщо static + offset 0x0
        bool susField = isStatic && offset == 0;
        if (susField)
        {
            var warn = new Label
            {
                Text = "⚠  static + offset 0x0 — зазвичай це клас-рівневе поле.\n" +
                       "    Для читу треба INSTANCE поля з offset > 0.",
                ForeColor = Color.FromArgb(230, 180, 90),
                Font = new Font("Consolas", 8.5f),
                Location = new Point(20, 66),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            header.Controls.Add(warn);
        }
        Controls.Add(header);

        // semantic
        Controls.Add(Theme.Lbl("Semantic (назва для семантики):", 20, 116, bold: true));
        var combo = new ComboBox
        {
            Location = new Point(20, 138),
            Width = 540,
            Height = 28,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 10f)
        };
        combo.Items.AddRange(LabelService.PresetSemantics);
        string guess = LabelService.GuessSemantic(field, type);
        combo.Text = guess;
        Controls.Add(combo);

        // warning якщо guess = custom...
        var hint = new Label
        {
            Text = "⚠  Обери зі списку або введи своє (не залишай 'custom...')",
            ForeColor = Color.FromArgb(230, 180, 90),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(20, 172),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        Controls.Add(hint);

        bool UpdateHint()
        {
            string s = combo.Text.Trim();
            bool bad = string.IsNullOrEmpty(s) || s.Equals("custom...", StringComparison.OrdinalIgnoreCase);
            hint.Visible = bad;
            hint.Text = string.IsNullOrEmpty(s)
                ? "⚠  Введи семантику (наприклад: health, position, displayName)"
                : "⚠  Заміни 'custom...' на реальну назву (наприклад: team, rotation)";
            hint.ForeColor = bad ? Color.FromArgb(230, 100, 100) : Theme.Dim;
            return !bad;
        }
        combo.TextChanged += (_, _) => UpdateHint();
        UpdateHint();

        // description
        Controls.Add(Theme.Lbl("Description (необов'язково):", 20, 210, dim: true));
        var desc = Theme.Txt("", 20, 232, 540);
        desc.Text = $"{cls}.{field} @ +0x{offset:X}";
        Controls.Add(desc);

        // summary warning
        var warn2 = new Label
        {
            Text = "Якщо не впевнений що це за поле — залиш як є, потім Edit.",
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(20, 272),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        Controls.Add(warn2);

        // buttons
        var ok = Theme.Btn("Mark it", 380, 340, 90, () =>
        {
            string s = combo.Text.Trim();
            if (string.IsNullOrEmpty(s)) { MessageBox.Show("Семантика пуста"); return; }
            if (s.Equals("custom...", StringComparison.OrdinalIgnoreCase))
            { MessageBox.Show("Заміни 'custom...' на реальну назву\n\nПриклади: health, position, displayName, team, rotation"); return; }

            if (susField)
            {
                var r = MessageBox.Show(
                    "Ти розмічаєш static поле з offset 0x0.\n\n" +
                    "Це зазвичай клас-рівневе поле (або константа).\n" +
                    "Для читу треба INSTANCE поля з offset > 0.\n\n" +
                    "Продовжити?",
                    "Підозріле поле", MessageBoxButtons.YesNo);
                if (r != DialogResult.Yes) return;
            }

            Semantic = s;
            Description = desc.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        });
        ok.BackColor = Theme.Accent;
        ok.ForeColor = Color.White;
        Controls.Add(ok);

        var cancel = Theme.Btn("Cancel", 480, 340, 80, () =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }
}