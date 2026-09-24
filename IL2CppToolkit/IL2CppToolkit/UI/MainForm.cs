// language: C#, file: UI/MainForm.cs
using System;
using System.Drawing;
using System.Windows.Forms;

public class MainForm : Form
{
    TabControl _tabs;

    public MainForm()
    {
        Text = "IL2CppToolkit";
        Size = new Size(1360, 840);
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        // ═══════════════════════════════════════════════
        //   header
        // ═══════════════════════════════════════════════
        var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Panel };
        header.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, header.Height - 1, header.Width, header.Height - 1);

        var title = new Label
        {
            Text = "IL2CppToolkit",
            ForeColor = Theme.Accent,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            Location = new Point(20, 12),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var sub = new Label
        {
            Text = "unity il2cpp offline dumper · diff + runtime + sigscan + export",
            ForeColor = Theme.Dim,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(22, 34),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var active = new Label
        {
            Text = "no active game",
            ForeColor = Theme.Dim,
            Font = new Font("Consolas", 9f),
            Location = new Point(1120, 18),
            AutoSize = true,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Name = "ActiveLabel"
        };

        header.Controls.AddRange(new Control[] { title, sub, active });
        Controls.Add(header);

        // ═══════════════════════════════════════════════
        //   tabs (lazy loading)
        // ═══════════════════════════════════════════════
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(86, 34),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(8, 6),
            Font = new Font("Segoe UI", 9f)
        };
        _tabs.DrawItem += DrawTab;
        Controls.Add(_tabs);
        _tabs.BringToFront();

        _tabs.TabPages.Add(MakeLazyTab("Games", () => new GamesTab()));
        _tabs.TabPages.Add(MakeLazyTab("Dump", () => new DumpTab()));
        _tabs.TabPages.Add(MakeLazyTab("Browser", () => new BrowserTab()));
        _tabs.TabPages.Add(MakeLazyTab("Labels", () => new LabelsTab()));
        _tabs.TabPages.Add(MakeLazyTab("SigScan", () => new SigScanTab()));
        _tabs.TabPages.Add(MakeLazyTab("Diff", () => new DiffTab()));
        _tabs.TabPages.Add(MakeLazyTab("Runtime", () => new RuntimeTab()));
        _tabs.TabPages.Add(MakeLazyTab("Snapshots", () => new SnapshotsTab()));
        _tabs.TabPages.Add(MakeLazyTab("Settings", () => new SettingsTab()));

        _tabs.SelectedIndexChanged += (_, _) => EnsureLazyTabLoaded(_tabs);
        _tabs.SelectedIndex = 0;
        EnsureLazyTabLoaded(_tabs);

        // ═══════════════════════════════════════════════
        //   auto-select active game on start
        // ═══════════════════════════════════════════════
        var reg = GameRegistry.Load();
        var rust = reg.Find("rust");
        if (rust != null) AppState.SetGame(rust);

        AppState.GameChanged += () =>
        {
            if (InvokeRequired) { BeginInvoke(new Action(UpdateActive)); return; }
            UpdateActive();
        };
        UpdateActive();
    }

    void DrawTab(object sender, DrawItemEventArgs e)
    {
        var g = e.Graphics;
        var tab = _tabs.TabPages[e.Index];
        var r = _tabs.GetTabRect(e.Index);
        bool sel = e.Index == _tabs.SelectedIndex;

        using var bg = new SolidBrush(sel ? Theme.Card : Theme.Panel);
        g.FillRectangle(bg, r);

        if (sel)
        {
            using var acc = new SolidBrush(Theme.Accent);
            g.FillRectangle(acc, r.X, r.Bottom - 3, r.Width, 3);
        }

        using var fg = new SolidBrush(sel ? Theme.Text : Theme.Dim);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(tab.Text,
                     sel ? new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold) : Font,
                     fg, r, sf);
    }

    // ═══════════════════════════════════════════════
    //   lazy-tab loading
    // ═══════════════════════════════════════════════

    TabPage MakeLazyTab(string name, Func<Control> factory)
    {
        var p = new TabPage(name)
        {
            BackColor = Theme.Bg,
            Padding = new Padding(0)
        };
        p.Tag = factory;
        return p;
    }

    void EnsureLazyTabLoaded(TabControl tabs)
    {
        var p = tabs.SelectedTab;
        if (p == null) return;
        if (!(p.Tag is Func<Control> factory)) return;
        if (p.Controls.Count > 0) return;

        try
        {
            var ctrl = factory();
            ctrl.Dock = DockStyle.Fill;
            p.Controls.Add(ctrl);
            p.Tag = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Tab error (" + p.Text + "): " + ex.Message, "Lazy tab");
        }
    }

    void UpdateActive()
    {
        var lbl = Controls.Find("ActiveLabel", true);
        if (lbl.Length == 0) return;
        var l = lbl[0] as Label;
        if (l == null) return;
        if (AppState.ActiveGame != null)
        {
            l.Text = $"● active: {AppState.ActiveGame.Name}";
            l.ForeColor = Theme.Ok;
        }
        else
        {
            l.Text = "no active game";
            l.ForeColor = Theme.Dim;
        }
    }
}