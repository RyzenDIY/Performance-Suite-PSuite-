// language: C#, file: UI/RuntimeTab.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class RuntimeTab : UserControl
{
    Label _status;
    TextBox _addrBox, _chainBox, _templateName;
    ComboBox _classBox, _templateBox, _typeBox;
    Button _attachBtn, _detachBtn, _readBtn, _walkBtn, _saveTplBtn, _loadTplBtn, _delTplBtn, _dumpEntBtn;
    RichTextBox _hexView, _logView;
    DataGridView _fieldGrid, _entityGrid;
    System.Windows.Forms.Timer _timer;
    CheckBox _autoRefresh;

    TemplatesCollection _templates;

    static void L(string s)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-error.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [runtime] " + s + "\n"); }
        catch { }
    }

    public RuntimeTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ═══════ row 1: templates + dump entities
        var tplPanel = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel };
        tplPanel.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, tplPanel.Height - 1, tplPanel.Width, tplPanel.Height - 1);

        tplPanel.Controls.Add(Theme.Lbl("template:", 20, 14, true));
        _templateBox = new ComboBox
        {
            Location = new Point(110, 10),
            Width = 280,
            Height = 24,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 9f)
        };
        _templateBox.SelectedIndexChanged += (_, _) => LoadTemplateIntoChain();
        tplPanel.Controls.Add(_templateBox);

        _loadTplBtn = Theme.Btn("Load", 400, 9, 70, LoadTemplateIntoChain);
        tplPanel.Controls.Add(_loadTplBtn);

        tplPanel.Controls.Add(Theme.Lbl("save as:", 480, 14, dim: true));
        _templateName = Theme.Txt("", 545, 10, 220);
        tplPanel.Controls.Add(_templateName);

        _saveTplBtn = Theme.Btn("Save", 775, 9, 70, SaveTemplate);
        tplPanel.Controls.Add(_saveTplBtn);

        _delTplBtn = Theme.Btn("Delete", 855, 9, 80, DeleteTemplate);
        tplPanel.Controls.Add(_delTplBtn);

        _dumpEntBtn = Theme.Btn("👥 Dump all players", 945, 9, 180, DumpEntities);
        _dumpEntBtn.BackColor = Color.FromArgb(60, 110, 60);
        _dumpEntBtn.ForeColor = Color.White;
        tplPanel.Controls.Add(_dumpEntBtn);

        Controls.Add(tplPanel);

        // ═══════ row 2: chain + attach
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Card };
        top.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, top.Height - 1, top.Width, top.Height - 1);

        top.Controls.Add(Theme.Lbl("chain (rva + offsets):", 20, 14, true));
        _chainBox = Theme.Txt("0x0", 175, 10, 420);
        top.Controls.Add(_chainBox);

        top.Controls.Add(Theme.Lbl("as:", 605, 14, dim: true));
        _typeBox = new ComboBox
        {
            Location = new Point(635, 10),
            Width = 110,
            Height = 24,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 9f)
        };
        foreach (var t in new[] { "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Boolean", "String", "Pointer", "Vector3" })
            _typeBox.Items.Add(t);
        _typeBox.SelectedIndex = 0;
        top.Controls.Add(_typeBox);

        _walkBtn = Theme.Btn("▶ Walk chain", 755, 9, 140, WalkChainProc);
        _walkBtn.BackColor = Theme.Accent;
        _walkBtn.ForeColor = Color.White;
        top.Controls.Add(_walkBtn);

        top.Controls.Add(Theme.Btn("Use module base", 905, 9, 160, () => _chainBox.Text = "0x0"));

        Controls.Add(top);

        // ═══════ row 3: attach bar
        var attachBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel };
        attachBar.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, attachBar.Height - 1, attachBar.Width, attachBar.Height - 1);

        _attachBtn = Theme.Btn("Attach to RustClient", 20, 8, 180, AttachProc);
        _attachBtn.BackColor = Theme.Accent;
        _attachBtn.ForeColor = Color.White;
        attachBar.Controls.Add(_attachBtn);

        _detachBtn = Theme.Btn("Detach", 210, 8, 90, DetachProc);
        _detachBtn.Enabled = false;
        attachBar.Controls.Add(_detachBtn);

        _status = Theme.Lbl("не підключено", 320, 15, dim: true);
        attachBar.Controls.Add(_status);

        _autoRefresh = new CheckBox
        {
            Text = "auto-refresh 500ms",
            Location = new Point(860, 13),
            Width = 180,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f)
        };
        _autoRefresh.CheckedChanged += (_, _) =>
        {
            if (_autoRefresh.Checked) _timer.Start();
            else _timer.Stop();
        };
        attachBar.Controls.Add(_autoRefresh);

        Controls.Add(attachBar);

        // ═══════ body: left log+hex | right grids
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        // ─── right panel: field grid + entity grid
        var right = new Panel { Dock = DockStyle.Right, Width = 520, BackColor = Theme.Card };
        right.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, 0, 0, right.Height);

        right.Controls.Add(Theme.Lbl("Fields @ address / Entities", 12, 8, true));

        _fieldGrid = new DataGridView
        {
            Location = new Point(8, 30),
            Size = new Size(506, 260),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Theme.StyleGrid(_fieldGrid);
        _fieldGrid.BorderStyle = BorderStyle.FixedSingle;
        _fieldGrid.Columns.Add("Name", "Field");
        _fieldGrid.Columns.Add("Offset", "Offset");
        _fieldGrid.Columns.Add("Type", "Type");
        _fieldGrid.Columns.Add("Value", "Value");
        _fieldGrid.Columns["Name"].FillWeight = 110;
        _fieldGrid.Columns["Offset"].FillWeight = 60;
        _fieldGrid.Columns["Type"].FillWeight = 90;
        _fieldGrid.Columns["Value"].FillWeight = 200;
        right.Controls.Add(_fieldGrid);

        _entityGrid = new DataGridView
        {
            Location = new Point(8, 300),
            Size = new Size(506, 340),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Theme.StyleGrid(_entityGrid);
        _entityGrid.BorderStyle = BorderStyle.FixedSingle;
        _entityGrid.Columns.Add("Name", "Name");
        _entityGrid.Columns.Add("Health", "HP");
        _entityGrid.Columns.Add("Pos", "Position");
        _entityGrid.Columns.Add("Flags", "Flags");
        _entityGrid.Columns.Add("Addr", "Address");
        _entityGrid.Columns["Name"].FillWeight = 130;
        _entityGrid.Columns["Health"].FillWeight = 50;
        _entityGrid.Columns["Pos"].FillWeight = 160;
        _entityGrid.Columns["Flags"].FillWeight = 60;
        _entityGrid.Columns["Addr"].FillWeight = 100;
        _entityGrid.Visible = false;
        right.Controls.Add(_entityGrid);

        body.Controls.Add(right);

        // ─── left: address + hex + log
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        var addrBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Card };
        addrBar.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, addrBar.Height - 1, addrBar.Width, addrBar.Height - 1);

        addrBar.Controls.Add(Theme.Lbl("address (hex):", 12, 14, true));
        _addrBox = Theme.Txt("", 130, 10, 240);
        addrBar.Controls.Add(_addrBox);

        _readBtn = Theme.Btn("Read", 380, 9, 100, ReadProc);
        addrBar.Controls.Add(_readBtn);

        addrBar.Controls.Add(Theme.Lbl("class:", 500, 14, dim: true));
        _classBox = new ComboBox
        {
            Location = new Point(550, 10),
            Width = 280,
            Height = 24,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 9f)
        };
        addrBar.Controls.Add(_classBox);
        left.Controls.Add(addrBar);

        _hexView = new RichTextBox
        {
            Dock = DockStyle.Top,
            Height = 280,
            BackColor = Color.FromArgb(10, 10, 13),
            ForeColor = Color.FromArgb(180, 220, 180),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Text = "hex viewer\n\nAttach → введи address або chain → Read / Walk"
        };
        left.Controls.Add(_hexView);

        _logView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 10, 13),
            ForeColor = Color.FromArgb(180, 200, 240),
            Font = new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        left.Controls.Add(_logView);
        _logView.BringToFront();

        body.Controls.Add(left);
        left.BringToFront();

        Controls.Add(body);

        // timer
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) =>
        {
            if (_autoRefresh.Checked && RuntimeService.IsAttached)
                ReadProc();
        };

        AppState.DumpChanged += () => LoadClasses();
        LoadClasses();
        ReloadTemplates();
    }

    // ═══════ log

    void Log(string s)
    {
        try
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Log(s))); return; }
            _logView.SelectionStart = _logView.TextLength;
            _logView.SelectionColor = Color.FromArgb(180, 200, 240);
            _logView.AppendText(s + "\n");
            _logView.ScrollToCaret();
        }
        catch { }
    }

    // ═══════ attach / detach

    void AttachProc()
    {
        try
        {
            if (RuntimeService.AttachRust())
            {
                _status.Text = $"● attached · pid={RuntimeService.Pid} · base=0x{RuntimeService.ModuleBase.ToInt64():X}";
                _status.ForeColor = Theme.Ok;
                _attachBtn.Enabled = false;
                _detachBtn.Enabled = true;
                Log($"attach OK · pid={RuntimeService.Pid} · module 0x{RuntimeService.ModuleBase.ToInt64():X} · size 0x{RuntimeService.ModuleSize:X}");
            }
            else
            {
                _status.Text = "Rust не запущено";
                _status.ForeColor = Theme.Err;
                Log("attach FAIL — RustClient.exe не знайдено");
            }
        }
        catch (Exception ex) { L("Attach: " + ex); Log("EX: " + ex.Message); }
    }

    void DetachProc()
    {
        try
        {
            RuntimeService.Detach();
            _status.Text = "відключено";
            _status.ForeColor = Theme.Dim;
            _attachBtn.Enabled = true;
            _detachBtn.Enabled = false;
            _timer.Stop();
            _autoRefresh.Checked = false;
            Log("detached");
        }
        catch (Exception ex) { L("Detach: " + ex); }
    }

    // ═══════ chain walk

    void WalkChainProc()
    {
        try
        {
            if (!RuntimeService.IsAttached) { MessageBox.Show("Спочатку Attach."); return; }

            string txt = _chainBox.Text.Trim();
            var parts = txt.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count == 0) { MessageBox.Show("Chain пустий."); return; }

            // перший — rva
            if (!long.TryParse(parts[0].Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out long rva))
            { MessageBox.Show("Не вдалось розпарсити base RVA."); return; }

            var offsets = new List<int>();
            for (int i = 1; i < parts.Count; i++)
            {
                if (!int.TryParse(parts[i].Replace("0x", "").Replace("0X", ""),
                    System.Globalization.NumberStyles.HexNumber, null, out int off))
                { MessageBox.Show($"Не вдалось розпарсити offset #{i}: {parts[i]}"); return; }
                offsets.Add(off);
            }

            Log("── walk: base RVA 0x" + rva.ToString("X") + " + [" +
                string.Join(", ", offsets.Select(x => "0x" + x.ToString("X"))) + "]");

            var addr = RuntimeService.WalkChain(rva, offsets, out var steps);
            foreach (var s in steps) Log("  " + s);

            if (addr == IntPtr.Zero) { Log("  ✗ ланцюг перерваний"); return; }

            string type = _typeBox.Text;
            string val = RuntimeService.ReadAs(addr, type);
            Log($"  ✓ final 0x{addr.ToInt64():X}   [{type}] = {val}");

            if (MemoryAdapter.IsReadable(addr))
                _hexView.Text = $"addr 0x{addr.ToInt64():X}\n\n" + RuntimeService.HexDump(addr, 16);

            // field dump if class selected
            string cls = _classBox.Text.Trim();
            var dump = AppState.CurrentDump;
            if (dump != null && !string.IsNullOrEmpty(cls) && dump.Classes.TryGetValue(cls, out var model))
            {
                _entityGrid.Visible = false;
                _fieldGrid.Visible = true;
                _fieldGrid.Rows.Clear();
                int shown = 0;
                foreach (var f in model.Fields.Where(x => !x.IsStatic).OrderBy(x => x.Offset))
                {
                    string v = RuntimeService.ReadFieldValue(addr, f);
                    _fieldGrid.Rows.Add(f.Name, $"0x{f.Offset:X}", f.Type, v);
                    if (++shown >= 300) break;
                }
                Log($"  fields: {shown} читано з класу {cls}");
            }
        }
        catch (Exception ex) { Log("EX: " + ex.Message); }
    }

    // ═══════ read by raw address

    void ReadProc()
    {
        try
        {
            if (!RuntimeService.IsAttached) return;

            string s = _addrBox.Text.Trim().Replace("0x", "").Replace("0X", "");
            if (!long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out long addrLong))
            { Log("invalid address"); return; }

            IntPtr addr = (IntPtr)addrLong;
            if (!MemoryAdapter.IsReadable(addr)) { Log($"0x{addr.ToInt64():X} unreadable"); return; }

            _hexView.Text = $"addr 0x{addr.ToInt64():X}\n\n" + RuntimeService.HexDump(addr, 16);

            string cls = _classBox.Text.Trim();
            var dump = AppState.CurrentDump;
            if (dump != null && !string.IsNullOrEmpty(cls) && dump.Classes.TryGetValue(cls, out var model))
            {
                _entityGrid.Visible = false;
                _fieldGrid.Visible = true;
                _fieldGrid.Rows.Clear();
                int shown = 0;
                foreach (var f in model.Fields.Where(x => !x.IsStatic).OrderBy(x => x.Offset))
                {
                    string v = RuntimeService.ReadFieldValue(addr, f);
                    _fieldGrid.Rows.Add(f.Name, $"0x{f.Offset:X}", f.Type, v);
                    if (++shown >= 300) break;
                }
            }
        }
        catch (Exception ex) { L("Read: " + ex.Message); }
    }

    // ═══════ entity list

    void DumpEntities()
    {
        try
        {
            if (!RuntimeService.IsAttached) { MessageBox.Show("Спочатку Attach."); return; }

            var dump = AppState.CurrentDump;
            if (dump == null) { MessageBox.Show("Спочатку зроби Dump."); return; }

            var gameId = AppState.ActiveGame?.Id ?? "rust";
            var col = LabelCollection.Load(gameId);

            long rvaBN = FindStaticRva(col, "BaseNetworkable", "clientEntities");
            int offCE = GetOff(col, "clientEntities", 0x58);
            int offVals = GetOff(col, "vals", 0x10);
            int offItems = 0x10, offSize = 0x18, offArr = 0x20;
            int offName = GetOff(col, "displayName", 0x2C0);
            int offHP = GetOff(col, "health", 0x1D0);
            int offFlg = GetOff(col, "playerFlags", 0x6D8);
            int offTf = 0x30;
            int offPos = 0x90;

            Log("── dump entities");
            Log($"  rvaBN=0x{rvaBN:X}  ce=0x{offCE:X}  vals=0x{offVals:X}  name=0x{offName:X}  hp=0x{offHP:X}");

            if (rvaBN == 0)
            {
                Log("  ✗ BaseNetworkable RVA невідомий");
                MessageBox.Show(
                    "Треба знати RVA BaseNetworkable (статик-поле в GameAssembly.dll).\n\n" +
                    "Варіанти:\n" +
                    "  • Додати мітку в Browser: BaseNetworkable.clientEntities з RVA (як static)\n" +
                    "  • Імпортувати RustOffsets.txt з community (з RVA)\n" +
                    "  • Ввести RVA вручну в labels.json",
                    "RVA відсутній");
                return;
            }

            IntPtr localPlayer = IntPtr.Zero;
            long rvaLP = FindStaticRva(col, "BasePlayer", "localPlayer");
            if (rvaLP != 0)
            {
                IntPtr slot = MemoryAdapter.ModuleBase + (int)rvaLP;
                localPlayer = MemoryAdapter.ReadPtr(slot);
            }

            var ents = RuntimeService.ReadEntities(
                rvaBN, offCE, offVals, offItems, offSize, offArr,
                offName, offHP, offFlg, offTf, offPos, localPlayer, Log);

            _fieldGrid.Visible = false;
            _entityGrid.Visible = true;
            _entityGrid.Rows.Clear();

            foreach (var e in ents)
            {
                string flags = e.Flags == 0 ? "—" : $"0x{e.Flags:X}";
                _entityGrid.Rows.Add(
                    (e.IsLocal ? "★ " : "") + e.Name,
                    e.Health.ToString("F0"),
                    $"({e.X:F0}, {e.Y:F0}, {e.Z:F0})",
                    flags,
                    $"0x{e.Address.ToInt64():X}");
            }

            Log($"  ✓ {ents.Count} players");
        }
        catch (Exception ex) { Log("EX: " + ex.Message); }
    }

    static long FindStaticRva(LabelCollection col, string cls, string semantic)
    {
        var l = col.Labels.FirstOrDefault(x => x.Class == cls && x.Semantic == semantic && x.IsStatic);
        return l != null ? l.Offset : 0;
    }

    static int GetOff(LabelCollection col, string semantic, int fallback)
    {
        var l = col.Labels.FirstOrDefault(x => x.Semantic == semantic && !x.IsStatic);
        return l != null ? l.Offset : fallback;
    }

    // ═══════ templates

    void ReloadTemplates()
    {
        try
        {
            _templateBox.Items.Clear();
            if (AppState.ActiveGame == null) return;
            _templates = TemplatesCollection.Load(AppState.ActiveGame.Id);
            foreach (var t in _templates.Templates.OrderBy(x => x.Name))
                _templateBox.Items.Add(t.Name);
        }
        catch (Exception ex) { L("ReloadTemplates: " + ex.Message); }
    }

    void LoadTemplateIntoChain()
    {
        try
        {
            if (_templates == null || _templateBox.SelectedItem == null) return;
            var name = _templateBox.SelectedItem.ToString();
            var t = _templates.Templates.FirstOrDefault(x => x.Name == name);
            if (t == null) return;

            var parts = new List<string> { $"0x{t.BaseRva:X}" };
            parts.AddRange(t.Offsets.Select(x => $"0x{x:X}"));
            _chainBox.Text = string.Join(", ", parts);

            if (!string.IsNullOrEmpty(t.ValueType))
            {
                for (int i = 0; i < _typeBox.Items.Count; i++)
                {
                    if (string.Equals(_typeBox.Items[i].ToString(), t.ValueType, StringComparison.OrdinalIgnoreCase))
                    { _typeBox.SelectedIndex = i; break; }
                }
            }
            Log($"template '{t.Name}' → {_chainBox.Text}");
        }
        catch (Exception ex) { L("LoadTemplate: " + ex.Message); }
    }

    void SaveTemplate()
    {
        try
        {
            if (AppState.ActiveGame == null) { MessageBox.Show("Немає активної гри."); return; }
            string name = _templateName.Text.Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("Введи ім'я шаблону."); return; }

            var parts = _chainBox.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count < 1) { MessageBox.Show("Chain пустий."); return; }

            if (!long.TryParse(parts[0].Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out long rva))
            { MessageBox.Show("Bad base RVA."); return; }

            var offsets = new List<int>();
            for (int i = 1; i < parts.Count; i++)
            {
                if (!int.TryParse(parts[i].Replace("0x", "").Replace("0X", ""),
                    System.Globalization.NumberStyles.HexNumber, null, out int off)) continue;
                offsets.Add(off);
            }

            _templates = _templates ?? TemplatesCollection.Load(AppState.ActiveGame.Id);
            _templates.Upsert(new ChainTemplate
            {
                Id = $"{AppState.ActiveGame.Id}.{name}",
                GameId = AppState.ActiveGame.Id,
                Name = name,
                BaseRva = rva,
                Offsets = offsets,
                ValueType = _typeBox.Text,
                Description = $"base 0x{rva:X} + {offsets.Count} offsets"
            });
            _templates.Save(AppState.ActiveGame.Id);
            ReloadTemplates();
            _templateBox.SelectedItem = name;
            Log($"✓ template '{name}' saved");
        }
        catch (Exception ex) { Log("SaveTemplate: " + ex.Message); }
    }

    void DeleteTemplate()
    {
        try
        {
            if (AppState.ActiveGame == null || _templateBox.SelectedItem == null) return;
            string name = _templateBox.SelectedItem.ToString();
            if (MessageBox.Show($"Видалити шаблон '{name}'?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _templates.Remove($"{AppState.ActiveGame.Id}.{name}");
            _templates.Save(AppState.ActiveGame.Id);
            ReloadTemplates();
            Log($"template '{name}' removed");
        }
        catch (Exception ex) { L("DeleteTemplate: " + ex.Message); }
    }

    void LoadClasses()
    {
        try
        {
            var d = AppState.CurrentDump;
            if (d == null) return;
            _classBox.Items.Clear();
            foreach (var name in d.Classes.Keys.OrderBy(x => x).Take(1000))
                _classBox.Items.Add(name);
            if (_classBox.Items.Count > 0 && _classBox.SelectedIndex < 0)
                _classBox.SelectedIndex = 0;
        }
        catch (Exception ex) { L("LoadClasses: " + ex.Message); }
    }
}