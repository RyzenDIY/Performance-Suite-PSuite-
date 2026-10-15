// language: C#, file: UI/SnapshotsTab.cs
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class SnapshotsTab : UserControl
{
    ListView _list;
    Label _info;

    public SnapshotsTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Panel };
        top.Controls.Add(Theme.Btn("Load", 20, 15, 100, LoadSnapshot));
        top.Controls.Add(Theme.Btn("Delete", 130, 15, 100, DeleteSnapshot));
        top.Controls.Add(Theme.Btn("Open folder", 240, 15, 140, OpenFolder));
        top.Controls.Add(Theme.Btn("Refresh", 390, 15, 100, Reload));
        Controls.Add(top);

        _list = new ListView
        {
            Dock = DockStyle.Left,
            Width = 600,
            View = View.Details,
            BackColor = Theme.LogBg,
            ForeColor = Theme.Text,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            FullRowSelect = true,
            MultiSelect = false
        };
        _list.Columns.Add("Date", 160);
        _list.Columns.Add("Size", 90);
        _list.Columns.Add("File", 340);
        _list.SelectedIndexChanged += (_, _) => ShowInfo();
        Controls.Add(_list);
        _list.BringToFront();

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(20) };
        _info = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = new Font("Consolas", 10f),
            BackColor = Color.Transparent,
            AutoSize = false
        };
        right.Controls.Add(_info);
        Controls.Add(right);
        right.BringToFront();

        AppState.GameChanged += Reload;
        Reload();
    }

    void Reload()
    {
        if (InvokeRequired) { BeginInvoke(new Action(Reload)); return; }

        _list.Items.Clear();

        // якщо немає активної гри — ставимо rust
        if (AppState.ActiveGame == null)
        {
            var g = GameRegistry.Load().Find("rust");
            if (g != null) AppState.SetGame(g);
        }
        if (AppState.ActiveGame == null) return;

        string dir = Paths.SnapshotDir(AppState.ActiveGame.Id);
        if (!Directory.Exists(dir)) return;

        foreach (var f in Directory.GetFiles(dir, "*.json").OrderByDescending(x => x))
        {
            var fi = new FileInfo(f);
            var item = new ListViewItem(new[]
            {
                fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                $"{fi.Length / 1024} KB",
                fi.Name
            });
            item.Tag = f;
            _list.Items.Add(item);
        }
    }

    void LoadSnapshot()
    {
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show("Виберіть snapshot у списку.");
            return;
        }
        string path = _list.SelectedItems[0].Tag as string;
        if (path == null || !File.Exists(path)) return;

        var dump = Dump.Load(path);
        if (dump == null)
        {
            MessageBox.Show("Не вдалось завантажити snapshot.");
            return;
        }
        AppState.SetDump(dump, path);
        MessageBox.Show($"Завантажено {dump.Classes.Count} класів з {Path.GetFileName(path)}.\n\n" +
                        "Перейди на таб Browser щоб подивитись.");
    }

    void DeleteSnapshot()
    {
        if (_list.SelectedItems.Count == 0) return;
        string path = _list.SelectedItems[0].Tag as string;
        if (path == null) return;

        if (MessageBox.Show($"Видалити {Path.GetFileName(path)}?",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        try { File.Delete(path); } catch (Exception ex) { MessageBox.Show(ex.Message); }
        Reload();
    }

    void OpenFolder()
    {
        if (AppState.ActiveGame == null) return;
        string dir = Paths.SnapshotDir(AppState.ActiveGame.Id);
        try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
    }

    void ShowInfo()
    {
        if (_list.SelectedItems.Count == 0) { _info.Text = ""; return; }
        string path = _list.SelectedItems[0].Tag as string;
        if (path == null || !File.Exists(path)) { _info.Text = ""; return; }

        var fi = new FileInfo(path);
        _info.Text =
            $"  File:          {fi.Name}\r\n" +
            $"  Full path:     {fi.FullName}\r\n" +
            $"  Size:          {fi.Length / 1024} KB\r\n" +
            $"  Modified:      {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\n" +
            $"\r\n" +
            $"  Click Load щоб завантажити як поточний dump.";
    }
}