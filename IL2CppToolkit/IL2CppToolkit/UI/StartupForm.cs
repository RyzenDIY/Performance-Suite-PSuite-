// language: C#, file: UI/StartupForm.cs
using System;
using System.Drawing;
using System.Windows.Forms;

public class StartupForm : Form
{
    public StartupForm()
    {
        Text = "IL2CppToolkit — швидкий старт";
        Size = new Size(720, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // ─── header
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Card };
        header.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, header.Height - 1, header.Width, header.Height - 1);

        header.Controls.Add(new Label
        {
            Text = "IL2CppToolkit",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            Location = new Point(24, 14),
            AutoSize = true,
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "unity il2cpp offset extractor · quick start",
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(26, 46),
            AutoSize = true,
            BackColor = Color.Transparent
        });
        Controls.Add(header);

        // ─── body — RichTextBox замість купи Label
        var body = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 10, 13),
            ForeColor = Color.FromArgb(220, 220, 230),
            Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Padding = new Padding(20),
            Text = GetHelpText()
        };
        Controls.Add(body);
        body.BringToFront();

        // ─── bottom bar
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 80, BackColor = Theme.Card };
        bottom.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, 0, bottom.Width, 0);

        var chk = new CheckBox
        {
            Text = "показувати при кожному запуску (рекомендовано)",
            Location = new Point(20, 12),
            Width = 400,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f),
            Checked = AppConfig.Current.ShowStartupAlways
        };
        bottom.Controls.Add(chk);

        var warn = new Label
        {
            Text = "⚠  Якщо пропустиш — можеш повернутись через Settings → Show help",
            ForeColor = Color.FromArgb(230, 180, 90),
            Font = new Font("Segoe UI", 8f),
            Location = new Point(20, 36),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        bottom.Controls.Add(warn);

        var skip = new Button
        {
            Text = "Skip (не рекомендовано)",
            Location = new Point(440, 44),
            Size = new Size(180, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 30, 30),
            ForeColor = Color.FromArgb(220, 140, 140),
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        skip.FlatAppearance.BorderSize = 0;
        skip.FlatAppearance.BorderColor = Color.FromArgb(120, 50, 50);
        skip.Click += (_, _) =>
        {
            var cfg = AppConfig.Current;
            cfg.ShowStartupHelp = false;
            cfg.Save();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        bottom.Controls.Add(skip);

        var ok = new Button
        {
            Text = "✓  Зрозуміло, продовжити",
            Location = new Point(440, 8),
            Size = new Size(230, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) =>
        {
            var cfg = AppConfig.Current;
            cfg.ShowStartupAlways = chk.Checked;
            cfg.ShowStartupHelp = true;
            cfg.LastSeenVersion = "0.1";
            cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
        };
        bottom.Controls.Add(ok);

        Controls.Add(bottom);
        AcceptButton = ok;
    }

    static string GetHelpText()
    {
        return
            "═══ ЩО ЦЕ ═══\r\n\r\n" +
            "IL2CppToolkit — інструмент для витягування офсетів з Unity IL2CPP ігор.\r\n" +
            "Працює офлайн (з файлів гри) + може читати живу пам'ять.\r\n\r\n" +

            "═══ ЩО ВМІЄ ═══\r\n\r\n" +
            "  • Dump — витягує всі класи/поля/методи з GameAssembly.dll\r\n" +
            "  • Browser — дерево 11 000+ класів, пошук, фільтри\r\n" +
            "  • Labels — розмічаєш поля семантикою (health, position, ...)\r\n" +
            "  • Diff — порівнює два snapshots після патчу гри\r\n" +
            "  • Runtime — attach до живої гри, hex viewer, читання полів\r\n" +
            "  • Export — генерує RustOffsets.txt / offsets.hpp / Offsets.cs\r\n" +
            "  • CLI — командний рядок для batch-скриптів\r\n\r\n" +

            "═══ ЩО ПОТРІБНО ═══\r\n\r\n" +
            "1. ГРА встановлена (Rust / Among Us / інша Unity IL2CPP)\r\n" +
            "2. Il2CppDumper.exe (безкоштовно, ~10 МБ)\r\n" +
            "   → https://github.com/Perfare/Il2CppDumper/releases\r\n" +
            "   → покласти в папку Il2CppDumper\\ поряд з цим exe\r\n" +
            "3. .NET 8 Runtime (вже є, бо ця прога запустилась)\r\n\r\n" +

            "═══ ПОРЯДОК РОБОТИ ═══\r\n\r\n" +
            "  КРОК 1. Games → Detect from Steam (знайде Rust автоматично)\r\n" +
            "  КРОК 2. Dump → Run Il2CppDumper (2-5 хв першого разу)\r\n" +
            "  КРОК 3. Browser → шукаєш поля (BasePlayer, Transform, Camera)\r\n" +
            "  КРОК 4. Позначаєш важливі поля: Mark as health / position / ...\r\n" +
            "  КРОК 5. Labels → Export → готовий RustOffsets.txt для читу\r\n\r\n" +
            "  Після патчу гри:\r\n" +
            "  КРОК 6. Dump заново → Diff → Apply → Export\r\n\r\n" +

            "═══ ⚠ ВАЖЛИВО ПРО EAC ═══\r\n\r\n" +
            "External RPM (цей інструмент) ПРАЦЮЄ тільки на серверах БЕЗ EAC.\r\n" +
            "На офіційних серверах Rust з EAC — потрібен kernel driver або DMA.\r\n" +
            "Для навчання / тестів грай на серверах без EAC (часто community).\r\n\r\n" +

            "═══ CLI ШВИДКІ КОМАНДИ ═══\r\n\r\n" +
            "  IL2CppToolkit.exe --cli --list\r\n" +
            "  IL2CppToolkit.exe --cli --info rust\r\n" +
            "  IL2CppToolkit.exe --cli --dump rust\r\n" +
            "  IL2CppToolkit.exe --cli --diff rust --apply\r\n" +
            "  IL2CppToolkit.exe --cli --export rust --format=txt,hpp,cs\r\n\r\n" +

            "═══ ФАЙЛИ ═══\r\n\r\n" +
            "  %APPDATA%\\IL2CppToolkit\\\r\n" +
            "    games\\rust\\            ← дані гри\r\n" +
            "      labels.json          ← твої семантичні мітки\r\n" +
            "      snapshots\\           ← історія дампів\r\n" +
            "      exports\\             ← готові файли для читу\r\n" +
            "    config.json            ← налаштування (можна змінити в Settings)\r\n\r\n" +

            "═══ НАЛАШТУВАННЯ ═══\r\n\r\n" +
            "Settings tab:\r\n" +
            "  • Custom output folder (якщо хочеш зберігати дані не в %APPDATA%)\r\n" +
            "  • Il2CppDumper.exe path (можеш вказати вручну)\r\n" +
            "  • Show help on startup (можна вимкнути)\r\n\r\n" +

            "═══ ПОСИЛАННЯ ═══\r\n\r\n" +
            "  Il2CppDumper:  https://github.com/Perfare/Il2CppDumper/releases\r\n" +
            "  .NET 8:        https://dotnet.microsoft.com/download/dotnet/8.0\r\n";
    }
}