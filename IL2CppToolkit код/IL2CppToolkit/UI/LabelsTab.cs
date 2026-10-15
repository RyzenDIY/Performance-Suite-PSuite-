// language: C#, file: UI/LabelsTab.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class LabelsTab : UserControl
{
    DataGridView _grid;
    Label _status;
    List<SemLabel> _current = new();

    public LabelsTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Panel };
        top.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, top.Height - 1, top.Width, top.Height - 1);

        top.Controls.Add(Theme.Btn("Refresh", 20, 15, 100, Reload));
        top.Controls.Add(Theme.Btn("Import labels.json", 130, 15, 170, ImportJson));
        top.Controls.Add(Theme.Btn("Import RustOffsets.txt", 310, 15, 210, ImportTxt));
        top.Controls.Add(Theme.Btn("Delete selected", 530, 15, 140, DeleteSelected));
        top.Controls.Add(Theme.Btn("Edit selected", 680, 15, 120, EditSelected));
        top.Controls.Add(Theme.Btn("Export .txt/.hpp/.cs", 810, 15, 180, Export));
        top.Controls.Add(Theme.Btn("Open folder", 1000, 15, 120, OpenFolder));
        Controls.Add(top);

        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Theme.Card,
            ForeColor = Theme.Dim,
            Font = new Font("Consolas", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Text = "no labels"
        };
        Controls.Add(_status);

        _grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(_grid);
        _grid.Columns.Add("Semantic", "Semantic");
        _grid.Columns.Add("Class", "Class");
        _grid.Columns.Add("Field", "Field");
        _grid.Columns.Add("Offset", "Offset");
        _grid.Columns.Add("Type", "Type");
        _grid.Columns.Add("Static", "Static");
        _grid.Columns.Add("Verified", "Verified");
        _grid.Columns.Add("Description", "Description");

        _grid.Columns["Semantic"].FillWeight = 120;
        _grid.Columns["Class"].FillWeight = 120;
        _grid.Columns["Field"].FillWeight = 200;
        _grid.Columns["Offset"].FillWeight = 70;
        _grid.Columns["Type"].FillWeight = 130;
        _grid.Columns["Static"].FillWeight = 50;
        _grid.Columns["Verified"].FillWeight = 130;
        _grid.Columns["Description"].FillWeight = 220;

        Controls.Add(_grid);
        _grid.BringToFront();

        AppState.GameChanged += Reload;
        AppState.DumpChanged += Reload;
        Reload();
    }

    void Reload()
    {
        if (InvokeRequired) { BeginInvoke(new Action(Reload)); return; }
        if (AppState.ActiveGame == null)
        {
            _current = new List<SemLabel>();
            RefreshGrid();
            return;
        }
        _current = LabelService.GetAll(AppState.ActiveGame.Id);
        RefreshGrid();
    }

    void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var l in _current)
        {
            _grid.Rows.Add(
                l.Semantic,
                l.Class,
                l.Field,
                $"0x{l.Offset:X}",
                l.Type,
                l.IsStatic ? "yes" : "",
                l.Verified.ToString("yyyy-MM-dd HH:mm"),
                l.Description ?? "");
        }
        _status.Text = AppState.ActiveGame == null
            ? "no active game"
            : $"  game: {AppState.ActiveGame.Id}    ·    {_current.Count} labels    ·    {Paths.LabelsFile(AppState.ActiveGame.Id)}";
    }

    // ───── import/export

    void ImportJson()
    {
        if (AppState.ActiveGame == null) { MessageBox.Show("Спочатку гра."); return; }
        using var d = new OpenFileDialog { Filter = "labels.json|*.json|All files|*.*", Title = "Import labels.json" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            var json = File.ReadAllText(d.FileName);
            var col = JsonStore.Deserialize<LabelCollection>(json);
            if (col == null || col.Labels == null) { MessageBox.Show("Invalid JSON."); return; }

            var current = LabelCollection.Load(AppState.ActiveGame.Id);
            int added = 0, updated = 0;
            foreach (var l in col.Labels)
            {
                l.GameId = AppState.ActiveGame.Id;
                bool exists = current.Labels.Any(x => x.Semantic == l.Semantic);
                current.Upsert(l);
                if (exists) updated++; else added++;
            }
            current.Save();
            Reload();
            MessageBox.Show($"✓ Імпортовано\n\n  {added} нових\n  {updated} оновлено");
        }
        catch (Exception ex) { MessageBox.Show("Помилка: " + ex.Message); }
    }

    void ImportTxt()
    {
        if (AppState.ActiveGame == null) { MessageBox.Show("Спочатку гра."); return; }
        using var d = new OpenFileDialog { Filter = "RustOffsets|*.txt|All files|*.*", Title = "Import RustOffsets.txt" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            var lines = File.ReadAllLines(d.FileName);
            var col = LabelCollection.Load(AppState.ActiveGame.Id);
            int n = 0;

            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("[")) continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) val = val.Substring(2);

                if (!int.TryParse(val, System.Globalization.NumberStyles.HexNumber, null, out int off)) continue;

                int dot = key.IndexOf('.');
                if (dot < 0) continue;
                string cls = key.Substring(0, dot);
                string sem = key.Substring(dot + 1);

                col.Upsert(new SemLabel
                {
                    Id = $"{AppState.ActiveGame.Id}.{cls}.{sem}",
                    GameId = AppState.ActiveGame.Id,
                    Semantic = sem,
                    Class = cls,
                    Field = "",
                    Offset = off,
                    Type = "",
                    Verified = DateTime.UtcNow,
                });
                n++;
            }
            col.Save();
            Reload();
            MessageBox.Show($"✓ Імпортовано {n} offset'ів з файлу.");
        }
        catch (Exception ex) { MessageBox.Show("Помилка: " + ex.Message); }
    }

    void DeleteSelected()
    {
        if (_grid.SelectedRows.Count == 0 || AppState.ActiveGame == null) return;
        int i = _grid.SelectedRows[0].Index;
        if (i < 0 || i >= _current.Count) return;
        var l = _current[i];
        if (MessageBox.Show($"Видалити '{l.Semantic}'?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        LabelService.Remove(AppState.ActiveGame.Id, l.Semantic);
        Reload();
    }

    void EditSelected()
    {
        if (_grid.SelectedRows.Count == 0 || AppState.ActiveGame == null) return;
        int i = _grid.SelectedRows[0].Index;
        if (i < 0 || i >= _current.Count) return;
        var l = _current[i];

        using var dlg = new MarkAsDialog(l.Class, l.Field ?? l.Semantic, l.Offset, l.Type, l.IsStatic);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        LabelService.Remove(AppState.ActiveGame.Id, l.Semantic);
        LabelService.Add(AppState.ActiveGame.Id, dlg.Semantic, l.Class,
                         l.Field ?? l.Semantic, l.Offset, l.Type, l.IsStatic, dlg.Description);
        Reload();
    }

    // ───── export з вибором формату

    void Export()
    {
        if (AppState.ActiveGame == null) { MessageBox.Show("Немає активної гри."); return; }
        try
        {
            var col = LabelCollection.Load(AppState.ActiveGame.Id);
            if (col.Labels.Count == 0) { MessageBox.Show("Немає міток для експорту."); return; }

            string dir = AppContext.BaseDirectory;

            using var dlg = new Form
            {
                Text = "Export offsets",
                Size = new Size(440, 360),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label
            {
                Text = "Оберіть формати:",
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            dlg.Controls.Add(lbl);

            var cbTxt = new CheckBox { Text = "RustOffsets.txt   (для читу)", Location = new Point(20, 50), Width = 380, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
            var cbHpp = new CheckBox { Text = "offsets.hpp       (C++ header)", Location = new Point(20, 78), Width = 380, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
            var cbCs = new CheckBox { Text = "Offsets.cs        (C# class)", Location = new Point(20, 106), Width = 380, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
            var cbJson = new CheckBox { Text = "offsets.json      (raw)", Location = new Point(20, 134), Width = 380, Checked = false, ForeColor = Theme.Text, BackColor = Color.Transparent };
            dlg.Controls.AddRange(new Control[] { cbTxt, cbHpp, cbCs, cbJson });

            var info = new Label
            {
                Text = $"Папка:  {dir}",
                ForeColor = Theme.Dim,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(20, 176),
                Size = new Size(400, 40),
                BackColor = Color.Transparent
            };
            dlg.Controls.Add(info);

            var countLbl = new Label
            {
                Text = $"Міток: {col.Labels.Count}",
                ForeColor = Theme.Dim,
                Font = new Font("Consolas", 9f),
                Location = new Point(20, 224),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            dlg.Controls.Add(countLbl);

            var ok = new Button
            {
                Text = "Export",
                Location = new Point(220, 268),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (_, _) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(320, 268),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.BtnBg,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            cancel.FlatAppearance.BorderSize = 0;
            cancel.Click += (_, _) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

            dlg.Controls.Add(ok);
            dlg.Controls.Add(cancel);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog() != DialogResult.OK) return;

            int n = 0;
            if (cbTxt.Checked)
            {
                File.WriteAllText(Path.Combine(dir, "RustOffsets.txt"), ExportService.RenderTxt(col));
                n++;
            }
            if (cbHpp.Checked)
            {
                File.WriteAllText(Path.Combine(dir, "offsets.hpp"), ExportService.RenderCppHeader(col));
                n++;
            }
            if (cbCs.Checked)
            {
                File.WriteAllText(Path.Combine(dir, "Offsets.cs"), ExportService.RenderCsClass(col));
                n++;
            }
            if (cbJson.Checked)
            {
                File.WriteAllText(Path.Combine(dir, "offsets.json"), ExportService.RenderJson(col));
                n++;
            }

            MessageBox.Show($"✓ Експортовано {n} файлів у:\n{dir}");
        }
        catch (Exception ex) { MessageBox.Show("Помилка: " + ex.Message); }
    }

    void OpenFolder()
    {
        if (AppState.ActiveGame == null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", Paths.LabelsDir); } catch { }
    }
}