// language: C#, file: UI/SettingsTab.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

public class SettingsTab : UserControl
{
    TextBox _txtGamesRoot, _txtDumper;
    CheckBox _chkStartupHelp, _chkStartupAlways;
    Label _status;

    public SettingsTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        AutoScroll = true;

        int y = 20;

        // ═════════════════════════════════════════
        //   SECTION: Output folder
        // ═════════════════════════════════════════
        Section("Output folder", ref y);
        Label("Де зберігати дані гри (labels, snapshots, exports)", ref y, dim: true);

        _txtGamesRoot = TextBoxInput(Paths.GamesRoot, 600, ref y);
        int rowY = y - 30;

        Button("Browse", 620, rowY, 90, () =>
        {
            using var d = new FolderBrowserDialog { Description = "Games root folder" };
            if (d.ShowDialog() == DialogResult.OK) _txtGamesRoot.Text = d.SelectedPath;
        });
        Button("Reset to default", 720, rowY, 180, () =>
        {
            var cfg = AppConfig.Current;
            cfg.CustomGamesRoot = null;
            cfg.Save();
            _txtGamesRoot.Text = Paths.GamesRoot;
            Info("Скинуто до %APPDATA%\\IL2CppToolkit\\games");
        });

        y += 4;
        Label("Поточний шлях:", ref y, dim: true);
        Label(Paths.GamesRoot, ref y, dim: true, bold: true);

        Button("Save output folder", 20, y, 200, () =>
        {
            try
            {
                var cfg = AppConfig.Current;
                string custom = _txtGamesRoot.Text.Trim();
                if (!string.IsNullOrEmpty(custom) && !Directory.Exists(custom))
                    Directory.CreateDirectory(custom);
                cfg.CustomGamesRoot = custom;
                cfg.Save();
                Info("✓ Output folder збережено: " + Paths.GamesRoot);
            }
            catch (Exception ex) { Error("Помилка: " + ex.Message); }
        });
        Button("Open folder", 230, y, 140, () =>
        {
            try { Process.Start("explorer.exe", Paths.GamesRoot); } catch { }
        });
        y += 44;

        // ═════════════════════════════════════════
        //   SECTION: Il2CppDumper
        // ═════════════════════════════════════════
        Section("Il2CppDumper", ref y);
        Label("Шлях до Il2CppDumper.exe (потрібен для dump)", ref y, dim: true);

        _txtDumper = TextBoxInput(
            AppConfig.Current.Il2CppDumperPath ?? Il2CppDumperAdapter.AutoFind() ?? "",
            600, ref y);
        rowY = y - 30;

        Button("Browse", 620, rowY, 90, () =>
        {
            using var d = new OpenFileDialog { Filter = "Il2CppDumper|Il2CppDumper.exe|All|*.*" };
            if (d.ShowDialog() == DialogResult.OK) _txtDumper.Text = d.FileName;
        });
        y += 4;

        Button("Save path", 20, y, 140, () =>
        {
            var cfg = AppConfig.Current;
            cfg.Il2CppDumperPath = _txtDumper.Text.Trim();
            cfg.Save();
            Info("✓ Il2CppDumper path збережено");
        });
        Button("Test run", 170, y, 110, () =>
        {
            if (!File.Exists(_txtDumper.Text)) { Error("Файл не існує"); return; }
            try { Process.Start(new ProcessStartInfo { FileName = _txtDumper.Text, UseShellExecute = true }); }
            catch (Exception ex) { Error(ex.Message); }
        });
        Button("Download Il2CppDumper (GitHub)", 290, y, 260, () =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Perfare/Il2CppDumper/releases",
                UseShellExecute = true
            });
        });
        Button("Open folder", 560, y, 120, () =>
        {
            string p = _txtDumper.Text.Trim();
            if (File.Exists(p))
            {
                try { Process.Start("explorer.exe", Path.GetDirectoryName(p)); } catch { }
            }
        });
        y += 44;

        // ═════════════════════════════════════════
        //   SECTION: Startup
        // ═════════════════════════════════════════
        Section("Startup", ref y);

        _chkStartupHelp = new CheckBox
        {
            Text = "Показувати довідку при запуску",
            Location = new Point(20, y),
            Width = 400,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f),
            Checked = AppConfig.Current.ShowStartupHelp
        };
        Controls.Add(_chkStartupHelp);
        y += 28;

        _chkStartupAlways = new CheckBox
        {
            Text = "Показувати завжди (не тільки перший раз)",
            Location = new Point(40, y),
            Width = 400,
            ForeColor = Theme.Dim,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f),
            Checked = AppConfig.Current.ShowStartupAlways
        };
        Controls.Add(_chkStartupAlways);
        y += 32;

        Button("Save startup settings", 20, y, 200, () =>
        {
            var cfg = AppConfig.Current;
            cfg.ShowStartupHelp = _chkStartupHelp.Checked;
            cfg.ShowStartupAlways = _chkStartupAlways.Checked;
            cfg.Save();
            Info("✓ Startup налаштування збережено");
        });
        Button("Show help now", 230, y, 140, () =>
        {
            using var h = new StartupForm();
            h.ShowDialog(this);
        });
        y += 44;

        // ═════════════════════════════════════════
        //   SECTION: About
        // ═════════════════════════════════════════
        Section("About", ref y);
        Label("IL2CppToolkit v0.1 — unity il2cpp offset extractor", ref y, dim: true);
        Label("Config: " + Path.Combine(Paths.Root, "config.json"), ref y, dim: true);

        Button("Open config folder", 20, y, 180, () =>
        {
            try { Process.Start("explorer.exe", Paths.Root); } catch { }
        });
        Button("Reset all settings", 210, y, 170, () =>
        {
            if (MessageBox.Show("Скинути config.json? (не видалить дані ігор)",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                File.Delete(Path.Combine(Paths.Root, "config.json"));
                AppConfig.Reload();
                Info("Скинуто. Перезапусти програму.");
            }
            catch (Exception ex) { Error(ex.Message); }
        });
        y += 44;

        // ═════════════════════════════════════════
        //   SECTION: Danger zone
        // ═════════════════════════════════════════
        Section("Danger zone", ref y);

        Button("Delete all snapshots", 20, y, 180, () =>
        {
            if (AppState.ActiveGame == null) { Error("Немає активної гри"); return; }
            if (MessageBox.Show("Видалити всі snapshots для поточної гри?",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                string dir = Paths.SnapshotsDir(AppState.ActiveGame.Id);
                foreach (var f in Directory.GetFiles(dir, "*.json")) File.Delete(f);
                Info("Snapshots видалено");
            }
            catch (Exception ex) { Error(ex.Message); }
        });
        Button("Delete all labels", 210, y, 170, () =>
        {
            if (AppState.ActiveGame == null) { Error("Немає активної гри"); return; }
            if (MessageBox.Show("Видалити labels.json?",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                File.Delete(Paths.LabelsFile(AppState.ActiveGame.Id));
                Info("Labels видалено");
            }
            catch (Exception ex) { Error(ex.Message); }
        });
        Button("Clear dump folder", 390, y, 170, () =>
        {
            if (AppState.ActiveGame == null) { Error("Немає активної гри"); return; }
            if (MessageBox.Show("Видалити dump\\ для поточної гри?",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                string dir = Paths.DumpDir(AppState.ActiveGame.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Info("Dump видалено");
            }
            catch (Exception ex) { Error(ex.Message); }
        });
        y += 50;

        // ─── status
        _status = new Label
        {
            Location = new Point(20, y),
            Size = new Size(900, 60),
            ForeColor = Theme.Dim,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9f),
            Text = ""
        };
        Controls.Add(_status);
    }

    // ═══════════════════════════════════════════════
    //   helpers
    // ═══════════════════════════════════════════════

    void Section(string text, ref int y)
    {
        y += 10;

        var lbl = new Label
        {
            Text = text,
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        Controls.Add(lbl);
        y += 26;

        var line = new Panel
        {
            Location = new Point(20, y),
            Size = new Size(880, 1),
            BackColor = Theme.Line
        };
        Controls.Add(line);
        y += 10;
    }

    void Label(string text, ref int y, bool dim = false, bool bold = false)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = dim ? Theme.Dim : Theme.Text,
            Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
            BackColor = Color.Transparent
        };
        Controls.Add(lbl);
        y += 20;
    }

    TextBox TextBoxInput(string text, int w, ref int y)
    {
        var tb = new TextBox
        {
            Text = text,
            Location = new Point(20, y),
            Width = w,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        Controls.Add(tb);
        y += 30;
        return tb;
    }

    Button Button(string text, int x, int y, int w, Action action)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.BtnBg,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Theme.BtnHover;
        b.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { Error(ex.Message); }
        };
        Controls.Add(b);
        return b;
    }

    void Info(string s) { _status.Text = s; _status.ForeColor = Theme.Ok; }
    void Error(string s) { _status.Text = s; _status.ForeColor = Theme.Err; }
}