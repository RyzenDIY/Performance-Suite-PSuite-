// language: C#, file: UI/GamesTab.cs
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class GamesTab : UserControl
{
    ListBox _list;
    Label _info;
    TextBox _treeBox;
    Button _openFolderBtn, _refreshBtn;

    public GamesTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ─── top bar
        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Panel };
        top.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, top.Height - 1, top.Width, top.Height - 1);

        top.Controls.Add(Theme.Btn("Detect from Steam", 20, 15, 180, DetectSteam));
        top.Controls.Add(Theme.Btn("Add manually", 210, 15, 150, AddManual));
        top.Controls.Add(Theme.Btn("Set as active", 370, 15, 140, SetActive));
        top.Controls.Add(Theme.Btn("Remove", 520, 15, 100, Remove));
        Controls.Add(top);

        // ─── list on the left
        _list = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 340,
            BackColor = Theme.LogBg,
            ForeColor = Theme.Text,
            Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        _list.SelectedIndexChanged += (_, _) => ShowInfo();
        Controls.Add(_list);
        _list.BringToFront();

        // ─── right side: info + tree
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(20) };

        _info = new Label
        {
            Dock = DockStyle.Top,
            Height = 200,
            ForeColor = Theme.Text,
            Font = new Font("Consolas", 10f),
            BackColor = Color.Transparent,
            AutoSize = false,
            Padding = new Padding(4)
        };
        right.Controls.Add(_info);

        var treeHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Theme.Card
        };
        treeHeader.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, treeHeader.Height - 1, treeHeader.Width, treeHeader.Height - 1);

        var treeLbl = new Label
        {
            Text = "Folder structure",
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Location = new Point(12, 10),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        treeHeader.Controls.Add(treeLbl);

        _openFolderBtn = new Button
        {
            Text = "📂 Open folder",
            Location = new Point(180, 6),
            Size = new Size(140, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.BtnBg,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        _openFolderBtn.FlatAppearance.BorderSize = 0;
        _openFolderBtn.FlatAppearance.MouseOverBackColor = Theme.BtnHover;
        _openFolderBtn.Click += (_, _) => OpenFolder();
        treeHeader.Controls.Add(_openFolderBtn);

        _refreshBtn = new Button
        {
            Text = "↻ Refresh",
            Location = new Point(330, 6),
            Size = new Size(100, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.BtnBg,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        };
        _refreshBtn.FlatAppearance.BorderSize = 0;
        _refreshBtn.FlatAppearance.MouseOverBackColor = Theme.BtnHover;
        _refreshBtn.Click += (_, _) => RefreshTree();
        treeHeader.Controls.Add(_refreshBtn);

        right.Controls.Add(treeHeader);

        _treeBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            BackColor = Theme.LogBg,
            ForeColor = Color.FromArgb(180, 220, 180),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            WordWrap = false,
            Text = "(no folder yet)"
        };
        right.Controls.Add(_treeBox);
        _treeBox.BringToFront();

        // порядок dock: top додано першим → він буде найнижче. Треба перевернути.
        // Насправді right.Controls.Add(_info) вже додано першим — він стане внизу.
        // treeHeader додано другим → він вище info. _treeBox останній → Fill.
        // Але нам треба: info зверху, treeHeader під ним, treeBox — Fill.
        // Виправлення: переставляємо через BringToFront у зворотному порядку.

        Controls.Add(right);
        right.BringToFront();

        Reload();
    }

    void Reload()
    {
        _list.Items.Clear();
        var reg = GameRegistry.Load();
        foreach (var g in reg.Games) _list.Items.Add(g);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    // ─── дії

    void DetectSteam()
    {
        try
        {
            int added = GameRegistryHelper.AutoDetectAll();
            Reload();

            if (added == 0)
                MessageBox.Show(
                    "Не знайдено нових ігор.\n\n" +
                    "Можливі причини:\n" +
                    "  • Всі відомі ігри вже зареєстровані\n" +
                    "  • Steam не встановлено за стандартним шляхом\n" +
                    "  • Гра не встановлена\n\n" +
                    "Спробуй 'Add manually'.",
                    "Detect");
            else
                MessageBox.Show(
                    $"Додано {added} нових ігор.\n\n" +
                    $"Папки створено у:\n{Paths.Root}\\games\\",
                    "Detect");
        }
        catch (Exception ex) { MessageBox.Show("Помилка: " + ex.Message); }
    }

    void AddManual()
    {
        using var d = new FolderBrowserDialog
        {
            Description = "Виберіть папку гри (де лежить GameAssembly.dll)"
        };
        if (d.ShowDialog() != DialogResult.OK) return;

        string root = d.SelectedPath;
        string ga = Path.Combine(root, "GameAssembly.dll");
        if (!File.Exists(ga)) { MessageBox.Show("GameAssembly.dll не знайдено в цій папці."); return; }

        // шукаємо metadata
        string md = null;
        foreach (var sub in Directory.GetDirectories(root))
        {
            string candidate = Path.Combine(sub, "il2cpp_data", "Metadata", "global-metadata.dat");
            if (File.Exists(candidate)) { md = candidate; break; }
        }

        string gameId = Path.GetFileName(root).ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
        if (string.IsNullOrEmpty(gameId)) gameId = "game-" + DateTime.Now.Ticks;

        string procName = Path.GetFileName(root).Replace(" ", "");
        string name = Path.GetFileName(root);

        var game = new Game
        {
            Id = gameId,
            Name = name,
            Process = procName,
            Module = "GameAssembly.dll",
            InstallDir = root,
            ExecutablePath = ga,
            MetadataPath = md,
            Launcher = "manual",
            SteamAppId = 0,
            LastSeenAt = DateTime.UtcNow
        };

        GameRegistryHelper.RegisterAndPrepare(game);
        Reload();

        MessageBox.Show(
            $"Додано '{name}'.\n\n" +
            $"Id:        {gameId}\n" +
            $"Metadata:  {(md != null ? "OK" : "MISSING")}\n" +
            $"Folder:    {Paths.GameDir(gameId)}",
            "Manual add");
    }

    void SetActive()
    {
        if (_list.SelectedItem is not Game g) return;
        AppState.SetGame(g);
        MessageBox.Show($"Active game: {g.Name}");
        ShowInfo();
    }

    void Remove()
    {
        if (_list.SelectedItem is not Game g) return;
        if (MessageBox.Show(
            $"Видалити '{g.Name}' з реєстру?\n\n" +
            "Папка %APPDATA%\\IL2CppToolkit\\games\\" + g.Id + " НЕ буде видалена.",
            "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        var reg = GameRegistry.Load();
        reg.Games.RemoveAll(x => x.Id == g.Id);
        reg.Save();
        Reload();
    }

    // ─── показ

    void ShowInfo()
    {
        if (_list.SelectedItem is not Game g) { _info.Text = ""; _treeBox.Text = ""; return; }

        _info.Text =
            $"  Name:          {g.Name}\r\n" +
            $"  Id:            {g.Id}\r\n" +
            $"  Process:       {g.Process}\r\n" +
            $"  Module:        {g.Module}\r\n" +
            $"  Launcher:      {g.Launcher}   appid: {(g.SteamAppId != 0 ? g.SteamAppId.ToString() : "—")}\r\n" +
            $"\r\n" +
            $"  Install dir:   {g.InstallDir}\r\n" +
            $"  GameAssembly:  {(g.ExecutablePath != null && File.Exists(g.ExecutablePath) ? "OK" : "MISSING")}   {g.ExecutablePath ?? "—"}\r\n" +
            $"  Metadata:      {(g.MetadataPath != null && File.Exists(g.MetadataPath) ? "OK" : "MISSING")}   {g.MetadataPath ?? "—"}\r\n" +
            $"\r\n" +
            $"  Added:         {g.AddedAt:yyyy-MM-dd HH:mm}\r\n" +
            $"  Last seen:     {g.LastSeenAt:yyyy-MM-dd HH:mm}";

        RefreshTree();
    }

    void RefreshTree()
    {
        if (_list.SelectedItem is not Game g) { _treeBox.Text = "(no game)"; return; }
        try
        {
            // перевіряємо що структура існує
            string gameDir = Paths.GameDir(g.Id);
            if (!Directory.Exists(gameDir)) { _treeBox.Text = "(folder not created yet)"; return; }

            _treeBox.Text = GameFolders.GetTreeString(g.Id);
        }
        catch (Exception ex) { _treeBox.Text = "error: " + ex.Message; }
    }

    void OpenFolder()
    {
        if (_list.SelectedItem is not Game g) return;
        try
        {
            string dir = Paths.GameDir(g.Id);
            if (!Directory.Exists(dir)) GameFolders.EnsureStructure(g);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
}