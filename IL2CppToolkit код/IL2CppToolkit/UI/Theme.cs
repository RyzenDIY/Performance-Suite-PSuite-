// language: C#, file: UI/Theme.cs
using System.Drawing;
using System.Windows.Forms;

public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(14, 14, 18);
    public static readonly Color Panel = Color.FromArgb(20, 20, 26);
    public static readonly Color Card = Color.FromArgb(26, 26, 32);
    public static readonly Color Line = Color.FromArgb(42, 42, 52);
    public static readonly Color Accent = Color.FromArgb(220, 60, 70);
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color Dim = Color.FromArgb(150, 150, 165);
    public static readonly Color Ok = Color.FromArgb(90, 220, 130);
    public static readonly Color Warn = Color.FromArgb(230, 180, 90);
    public static readonly Color Err = Color.FromArgb(220, 90, 90);
    public static readonly Color Input = Color.FromArgb(32, 32, 40);
    public static readonly Color BtnBg = Color.FromArgb(38, 38, 48);
    public static readonly Color BtnHover = Color.FromArgb(52, 52, 64);
    public static readonly Color LogBg = Color.FromArgb(10, 10, 13);
    public static readonly Color LogText = Color.FromArgb(180, 220, 180);

    public static Button Btn(string text, int x, int y, int w, System.Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Width = w,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = BtnBg,
            ForeColor = Text,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = BtnHover;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Label Lbl(string text, int x, int y, bool dim = false, bool bold = false)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = dim ? Dim : Text,
            Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
            BackColor = Color.Transparent
        };
    }

    public static TextBox Txt(string text, int x, int y, int w)
    {
        return new TextBox
        {
            Text = text,
            Location = new Point(x, y),
            Width = w,
            Height = 26,
            BackColor = Input,
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = LogBg;
        g.GridColor = Line;
        g.ForeColor = Text;
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Card,
            ForeColor = Text,
            SelectionBackColor = Color.FromArgb(60, 40, 45),
            SelectionForeColor = Text
        };
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Card,
            ForeColor = Dim,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        g.EnableHeadersVisualStyles = false;
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToResizeRows = false;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.Font = new Font("Consolas", 9f);
        g.BorderStyle = BorderStyle.None;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.MultiSelect = false;
    }
}