// language: C#, file: UI/DiffTab.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class DiffTab : UserControl
{
    TextBox _pathOld, _pathNew;
    Label _status;
    DataGridView _grid;
    List<FieldChange> _changes = new();
    Dump _oldDump, _newDump;

    public DiffTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 140, BackColor = Theme.Panel };
        top.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Theme.Line), 0, top.Height - 1, top.Width, top.Height - 1);

        top.Controls.Add(Theme.Lbl("Old snapshot (baseline):", 20, 12, bold: true));
        _pathOld = Theme.Txt("", 20, 34, 800); top.Controls.Add(_pathOld);
        top.Controls.Add(Theme.Btn("Browse", 830, 33, 100, () => BrowseInto(_pathOld)));

        top.Controls.Add(Theme.Lbl("New snapshot (після патчу):", 20, 74, bold: true));
        _pathNew = Theme.Txt("", 20, 96, 800); top.Controls.Add(_pathNew);
        top.Controls.Add(Theme.Btn("Browse", 830, 95, 100, () => BrowseInto(_pathNew)));

        Controls.Add(top);

        var actions = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Card };
        actions.Controls.Add(Theme.Btn("▶ Порівняти", 20, 8, 150, RunCompare));
        actions.Controls.Add(Theme.Btn("✓ Apply to labels", 180, 8, 180, Apply));
        actions.Controls.Add(Theme.Btn("Use current dump", 370, 8, 170, () =>
        {
            if (AppState.CurrentSnapshotPath != null)
                _pathNew.Text = AppState.CurrentSnapshotPath;
        }));

        _status = Theme.Lbl("", 560, 14, dim: true);
        actions.Controls.Add(_status);
        Controls.Add(actions);

        _grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(_grid);
        _grid.Columns.Add("Semantic", "Semantic");
        _grid.Columns.Add("Class", "Class");
        _grid.Columns.Add("Status", "Status");
        _grid.Columns.Add("OldOff", "Old offset");
        _grid.Columns.Add("NewOff", "New offset");
        _grid.Columns.Add("OldField", "Old field");
        _grid.Columns.Add("NewField", "New field");
        _grid.Columns.Add("Score", "Score");
        _grid.Columns.Add("Note", "Note");

        _grid.Columns["Semantic"].FillWeight = 100;
        _grid.Columns["Class"].FillWeight = 100;
        _grid.Columns["Status"].FillWeight = 70;
        _grid.Columns["OldOff"].FillWeight = 60;
        _grid.Columns["NewOff"].FillWeight = 60;
        _grid.Columns["OldField"].FillWeight = 140;
        _grid.Columns["NewField"].FillWeight = 140;
        _grid.Columns["Score"].FillWeight = 50;
        _grid.Columns["Note"].FillWeight = 200;

        Controls.Add(_grid);
        _grid.BringToFront();

        AppState.GameChanged += AutoFill;
        AutoFill();
    }

    void AutoFill()
    {
        try
        {
            if (AppState.ActiveGame == null) return;
            string dir = Paths.SnapshotDir(AppState.ActiveGame.Id);
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*.json").OrderByDescending(x => x).ToList();
            if (files.Count >= 2)
            {
                _pathNew.Text = files[0];
                _pathOld.Text = files[1];
            }
            else if (files.Count == 1)
            {
                _pathNew.Text = files[0];
            }
        }
        catch { }
    }

    void BrowseInto(TextBox target)
    {
        using var d = new OpenFileDialog { Filter = "snapshot json|*.json|All|*.*" };
        if (d.ShowDialog() == DialogResult.OK) target.Text = d.FileName;
    }

    void RunCompare()
    {
        try
        {
            if (AppState.ActiveGame == null) { MessageBox.Show("Немає активної гри."); return; }
            if (!File.Exists(_pathOld.Text) || !File.Exists(_pathNew.Text))
            { MessageBox.Show("Файли не знайдено."); return; }

            _oldDump = Dump.Load(_pathOld.Text);
            _newDump = Dump.Load(_pathNew.Text);
            if (_oldDump == null || _newDump == null) { MessageBox.Show("Не вдалось розпарсити."); return; }

            var labels = LabelCollection.Load(AppState.ActiveGame.Id);
            _changes = DiffService.Compare(labels, _newDump);

            _grid.Rows.Clear();
            foreach (var c in _changes)
            {
                _grid.Rows.Add(c.Semantic, c.Class, c.Status,
                    $"0x{c.OldOffset:X}", $"0x{c.NewOffset:X}",
                    c.OldField ?? "—", c.NewField ?? "—",
                    c.Score.ToString("F2"), c.Note ?? "");
            }

            int ok = _changes.Count(x => x.Status == "ok");
            int moved = _changes.Count(x => x.Status == "moved" || x.Status == "renamed");
            int gone = _changes.Count(x => x.Status == "missing" || x.Status == "class-gone");
            _status.Text = $"  {_changes.Count} labels  ·  ok={ok}  moved={moved}  missing={gone}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    void Apply()
    {
        try
        {
            if (AppState.ActiveGame == null || _changes.Count == 0)
            { MessageBox.Show("Спочатку порівняй."); return; }

            var labels = LabelCollection.Load(AppState.ActiveGame.Id);
            int n = DiffService.Apply(_changes, labels);
            MessageBox.Show($"Оновлено {n} міток.\nПеревір таб Labels.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
}