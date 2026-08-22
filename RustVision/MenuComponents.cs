using System;
using System.Drawing;
using System.Windows.Forms;

public static class MenuComponents
{
    // Створення текстових підписів
    public static Label CreateLabel(string text, Point location, Color color, Font font)
    {
        return new Label()
        {
            Text = text,
            Location = location,
            AutoSize = true,
            ForeColor = color,
            Font = font
        };
    }

    // Створення стильних темно-сірих кнопок із підсвічуванням меж
    public static Button CreateButton(string text, Point location, Size size, Color borderAndText, Font font)
    {
        Button btn = new Button()
        {
            Text = text,
            Location = location,
            Size = size,
            Font = font,
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = borderAndText,
            FlatStyle = FlatStyle.Flat
        };
        btn.FlatAppearance.BorderColor = borderAndText;
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    // Створення гладких повзунків (слайдерів)
    public static TrackBar CreateSlider(Point location, Size size, int min, int max, int value)
    {
        return new TrackBar()
        {
            Location = location,
            Size = size,
            Minimum = min,
            Maximum = max,
            Value = value,
            TickStyle = TickStyle.None,
            BackColor = Color.FromArgb(15, 15, 15)
        };
    }

    // Створення випадаючих списків (для кісток та кольорів)
    public static ComboBox CreateDropDown(Point location, Size size, string[] items, int defaultIndex)
    {
        ComboBox cb = new ComboBox()
        {
            Location = location,
            Size = size,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        cb.Items.AddRange(items);
        if (items.Length > 0) cb.SelectedIndex = defaultIndex;
        return cb;
    }
}
