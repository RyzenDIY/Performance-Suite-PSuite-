// language: C#, file: UI/SigScanTab.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public class SigScanTab : UserControl
{
    ComboBox _modeBox;
    TextBox _pattern, _name;
    NumericUpDown _operandOff, _limit;
    DataGridView _grid;
    RichTextBox _hexView;
    Label _status;
    Button _scanBtn, _autoBtn, _saveBtn, _liveBtn;

    byte[] _dll;
    string _dllPath;
    List<int> _lastMatches = new();

    CancellationTokenSource _liveCts;
    LiveClassFinder.ClassInfo _lastLiveInfo;

    public SigScanTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        // ═══════════════════════════════════════════════
        //   TOP
        // ═══════════════════════════════════════════════
        var top = new Panel { Dock = DockStyle.Top, Height = 162, BackColor = Theme.Panel };
        top.Paint += (s, e) =>
            e.Graphics.DrawLine(new Pen(Theme.Line), 0, top.Height - 1, top.Width, top.Height - 1);

        top.Controls.Add(Theme.Lbl("mode:", 20, 14, true));
        _modeBox = new ComboBox
        {
            Location = new Point(70, 12),
            Width = 200,
            Height = 24,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f)
        };
        _modeBox.Items.AddRange(new object[]
        {
            VerifyMode.BaseNetworkable,
            VerifyMode.MainCamera,
            VerifyMode.LocalPlayer
        });
        _modeBox.SelectedIndex = 0;
        top.Controls.Add(_modeBox);

        top.Controls.Add(Theme.Lbl("RIP off:", 280, 14, dim: true));
        _operandOff = new NumericUpDown
        {
            Location = new Point(340, 12),
            Width = 60,
            Height = 24,
            Minimum = 0,
            Maximum = 32,
            Value = 3,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        top.Controls.Add(_operandOff);

        top.Controls.Add(Theme.Lbl("limit:", 420, 14, dim: true));
        _limit = new NumericUpDown
        {
            Location = new Point(465, 12),
            Width = 90,
            Height = 24,
            Minimum = 100,
            Maximum = 200000,
            Value = 30000,
            Increment = 5000,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        top.Controls.Add(_limit);

        top.Controls.Add(Theme.Lbl("byte pattern:", 20, 44, true));
        _pattern = Theme.Txt("48 8B 05 ?? ?? ?? ?? 48 8B 88", 130, 42, 700);
        top.Controls.Add(_pattern);

        _scanBtn = Theme.Btn("🔍 Scan", 20, 82, 120, Scan);
        top.Controls.Add(_scanBtn);

        _autoBtn = Theme.Btn("🤖 Auto-verify", 150, 82, 150, AutoVerify);
        _autoBtn.BackColor = Color.FromArgb(60, 90, 140);
        _autoBtn.ForeColor = Color.White;
        top.Controls.Add(_autoBtn);

        top.Controls.Add(Theme.Btn("Load dll", 310, 82, 100, LoadDll));
        top.Controls.Add(Theme.Btn("Open folder", 420, 82, 120, () =>
        {
            if (_dllPath != null && File.Exists(_dllPath))
                System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(_dllPath));
        }));

        top.Controls.Add(Theme.Lbl("name:", 560, 88, dim: true));
        _name = Theme.Txt("", 610, 84, 200);
        top.Controls.Add(_name);

        _saveBtn = Theme.Btn("💾 Save", 820, 82, 130, SaveSig);
        _saveBtn.BackColor = Theme.Accent;
        _saveBtn.ForeColor = Color.White;
        top.Controls.Add(_saveBtn);

        _liveBtn = Theme.Btn("🔎 Live find class (без pattern)", 20, 118, 320, LiveFind);
        _liveBtn.BackColor = Color.FromArgb(60, 90, 140);
        _liveBtn.ForeColor = Color.White;
        top.Controls.Add(_liveBtn);

        top.Controls.Add(Theme.Lbl("— шукає клас по імені в живій пам'яті (потрібен Attach)", 350, 124, dim: true));

        Controls.Add(top);

        // ═══════════════════════════════════════════════
        //   STATUS BAR
        // ═══════════════════════════════════════════════
        var statusBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.Card };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.Dim,
            BackColor = Color.Transparent,
            Font = new Font("Consolas", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Text = "dll не завантажено"
        };
        statusBar.Controls.Add(_status);
        Controls.Add(statusBar);

        // ═══════════════════════════════════════════════
        //   SPLIT
        // ═══════════════════════════════════════════════
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Theme.Line,
            SplitterWidth = 4,
            Panel1MinSize = 200,
            Panel2MinSize = 150
        };

        _grid = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(_grid);
        _grid.Columns.Add("Valid", "✓");
        _grid.Columns.Add("ClassRva", "Class RVA");
        _grid.Columns.Add("Value", "Value");
        _grid.Columns.Add("Slot", "Slot");
        _grid.Columns.Add("Info", "Info");
        _grid.Columns["Valid"].FillWeight = 40;
        _grid.Columns["ClassRva"].FillWeight = 90;
        _grid.Columns["Value"].FillWeight = 100;
        _grid.Columns["Slot"].FillWeight = 70;
        _grid.Columns["Info"].FillWeight = 520;
        _grid.ReadOnly = true;
        _grid.SelectionChanged += (_, _) => ShowContext();
        split.Panel1.Controls.Add(_grid);

        _hexView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 10, 13),
            ForeColor = Color.FromArgb(180, 220, 180),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Text = "вибери рядок щоб побачити вміст"
        };
        split.Panel2.Controls.Add(_hexView);

        Controls.Add(split);
        split.BringToFront();

        split.SizeChanged += (_, _) =>
        {
            try
            {
                int want = (int)(split.Height * 0.5);
                int min = split.Panel1MinSize;
                int max = split.Height - split.Panel2MinSize - split.SplitterWidth;
                if (max > min && split.SplitterDistance != want)
                    split.SplitterDistance = Math.Max(min, Math.Min(want, max));
            }
            catch { }
        };

        AutoLoadDll();
    }

    // ═══════════════════════════════════════════════════════════
    //   dll
    // ═══════════════════════════════════════════════════════════

    void AutoLoadDll()
    {
        string ga = AppState.ActiveGame?.ExecutablePath;
        if (string.IsNullOrEmpty(ga) || !File.Exists(ga))
        {
            var reg = GameRegistry.Load();
            ga = reg.Find("rust")?.ExecutablePath;
        }
        if (!string.IsNullOrEmpty(ga) && File.Exists(ga))
            LoadDllFrom(ga);
    }

    void LoadDllFrom(string path)
    {
        try
        {
            _dll = File.ReadAllBytes(path);
            _dllPath = path;
            _status.Text = $"✓ dll: {Path.GetFileName(path)}  ({_dll.Length / 1024 / 1024} MB)";
            _status.ForeColor = Theme.Ok;
        }
        catch (Exception ex)
        {
            _status.Text = "✗ " + ex.Message;
            _status.ForeColor = Theme.Err;
        }
    }

    void LoadDll()
    {
        using var d = new OpenFileDialog { Filter = "GameAssembly.dll|*.dll|All|*.*" };
        if (d.ShowDialog() == DialogResult.OK) LoadDllFrom(d.FileName);
    }

    // ═══════════════════════════════════════════════════════════
    //   PATTERN SCAN
    // ═══════════════════════════════════════════════════════════

    void Scan()
    {
        if (_dll == null) { MessageBox.Show("Завантаж dll."); return; }

        string pattern = _pattern.Text.Trim();
        if (pattern.Length < 5) { MessageBox.Show("Патерн занадто короткий"); return; }

        int limit = (int)_limit.Value;
        _status.Text = $"сканую (limit={limit})...";
        _status.ForeColor = Theme.Warn;
        Application.DoEvents();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _lastMatches = FindAll(pattern, limit);
        sw.Stop();

        _grid.Rows.Clear();
        if (_lastMatches.Count == 0)
        {
            _status.Text = "✗ не знайдено";
            _status.ForeColor = Theme.Err;
            return;
        }

        int opOff = (int)_operandOff.Value;

        _grid.SuspendLayout();
        foreach (var rva in _lastMatches)
        {
            long target = 0;
            try
            {
                int disp = BitConverter.ToInt32(_dll, rva + opOff);
                target = rva + opOff + 4 + disp;
            }
            catch { }

            string pts = "";
            if (MemoryAdapter.IsAttached && target > 0 && target < MemoryAdapter.ModuleSize)
            {
                try
                {
                    IntPtr slot = MemoryAdapter.ModuleBase + (int)target;
                    if (MemoryAdapter.IsReadable(slot))
                    {
                        IntPtr val = MemoryAdapter.ReadPtr(slot);
                        if (val != IntPtr.Zero && MemoryAdapter.IsReadable(val))
                        {
                            IntPtr np = MemoryAdapter.ReadPtr(val + 0x10);
                            if (np != IntPtr.Zero && MemoryAdapter.IsReadable(np))
                            {
                                string cls = MemoryAdapter.ReadCString(np, 48);
                                if (!string.IsNullOrEmpty(cls) && cls.Length < 48)
                                    pts = "class: " + cls;
                            }
                        }
                    }
                }
                catch { }
            }

            _grid.Rows.Add("?", $"0x{rva:X}", target != 0 ? $"0x{target:X}" : "—", pts, "not verified");
        }
        _grid.ResumeLayout();

        _status.Text = $"✓ {_lastMatches.Count} matches in {sw.ElapsedMilliseconds}ms";
        _status.ForeColor = Theme.Ok;
    }

    // ═══════════════════════════════════════════════════════════
    //   AUTO-VERIFY
    // ═══════════════════════════════════════════════════════════

    void AutoVerify()
    {
        if (_dll == null) { MessageBox.Show("Завантаж dll."); return; }
        if (!MemoryAdapter.IsAttached) { MessageBox.Show("Спочатку Attach."); return; }
        if (_lastMatches.Count == 0) { MessageBox.Show("Спочатку Scan."); return; }

        var mode = (VerifyMode)_modeBox.SelectedItem;
        _status.Text = $"🤖 перевіряю {_lastMatches.Count} matches...";
        _status.ForeColor = Theme.Warn;
        Application.DoEvents();

        var results = AutoVerifyService.Verify(_lastMatches, mode, _dll, (int)_operandOff.Value);

        _grid.Rows.Clear();
        int validCount = 0;

        foreach (var r in results)
        {
            string mark = r.Valid ? "✓" : "✗";
            var row = _grid.Rows[_grid.Rows.Add(
                mark,
                $"0x{r.InsnRva:X}",
                r.TargetRva != 0 ? $"0x{r.TargetRva:X}" : "—",
                "",
                (r.DetectedInfo ?? "") + (r.Reason != "OK" && !string.IsNullOrEmpty(r.Reason) ? "  [" + r.Reason + "]" : "")
            )];

            if (r.Valid)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(30, 60, 30);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(200, 240, 200);
                validCount++;
            }
            else row.DefaultCellStyle.ForeColor = Theme.Dim;
        }

        if (validCount == 0)
        {
            _status.Text = $"✗ жоден з {results.Count} не валідний";
            _status.ForeColor = Theme.Err;
        }
        else if (validCount == 1)
        {
            _status.Text = "✓✓✓ знайдено! тисни Save";
            _status.ForeColor = Theme.Ok;
        }
        else
        {
            _status.Text = $"⚠ {validCount} валідних — перевір вручну";
            _status.ForeColor = Theme.Warn;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //   LIVE FIND
    // ═══════════════════════════════════════════════════════════

    void LiveFind()
    {
        if (_liveCts != null && !_liveCts.IsCancellationRequested)
        {
            _liveCts.Cancel();
            _status.Text = "скасовую...";
            _status.ForeColor = Theme.Warn;
            return;
        }

        if (!MemoryAdapter.IsAttached)
        {
            MessageBox.Show("Спочатку Attach до гри в Runtime tab.");
            return;
        }

        string defCls = "BaseNetworkable";
        if (_modeBox.SelectedItem?.ToString() == VerifyMode.MainCamera.ToString()) defCls = "MainCamera";
        else if (_modeBox.SelectedItem?.ToString() == VerifyMode.LocalPlayer.ToString()) defCls = "BasePlayer";

        string cls = Microsoft.VisualBasic.Interaction.InputBox("Ім'я класу:", "Live find class", defCls);
        if (string.IsNullOrWhiteSpace(cls)) return;

        _liveCts = new CancellationTokenSource();
        var token = _liveCts.Token;

        _status.Text = $"шукаю \"{cls}\"...";
        _status.ForeColor = Theme.Warn;
        _grid.Rows.Clear();
        _hexView.Clear();
        _hexView.Text = $"сканую \"{cls}\"...\r\n\r\n40 ГБ/с швидкість, ~30-60 сек\r\nТисни ще раз кнопку щоб скасувати.";
        _liveBtn.Text = "⏹ Cancel scan";
        _liveBtn.BackColor = Color.FromArgb(120, 40, 40);

        var logList = new List<string>();
        var startTime = DateTime.Now;

        new Thread(() =>
        {
            LiveClassFinder.ClassInfo info = null;
            string error = null;
            try
            {
                info = LiveClassFinder.FindClassAsync(cls.Trim(),
                    s => { lock (logList) logList.Add(s); }, token);
            }
            catch (OperationCanceledException) { error = "cancelled"; }
            catch (Exception ex) { error = ex.Message; }

            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _liveBtn.Text = "🔎 Live find class (без pattern)";
                    _liveBtn.BackColor = Color.FromArgb(60, 90, 140);
                    _liveCts = null;

                    var sb = new StringBuilder();
                    sb.AppendLine($"=== Live find: {cls}  ({elapsed:F0}ms) ===");
                    sb.AppendLine();
                    lock (logList) foreach (var l in logList) sb.AppendLine(l);
                    sb.AppendLine();

                    if (error != null || info == null)
                    {
                        _status.Text = error == "cancelled" ? "скасовано" : $"✗ {error ?? "не знайдено"}";
                        _status.ForeColor = Theme.Err;
                        sb.AppendLine(error == "cancelled" ? "СКАСОВАНО" : "НЕ ЗНАЙДЕНО");
                        _hexView.Text = sb.ToString();
                        return;
                    }

                    _lastLiveInfo = info;

                    sb.AppendLine($"class RVA:      0x{info.ClassRva:X}");
                    sb.AppendLine($"static_fields:  0x{info.StaticFields.ToInt64():X}");
                    sb.AppendLine($"SF_OFFSET:      0xB8");
                    sb.AppendLine();
                    sb.AppendLine($"─── candidates ({info.Candidates.Count}) ───");

                    foreach (var c in info.Candidates)
                    {
                        string mark = c.IsEntityList ? $"✓ LIST size={c.ListSize}" : "";
                        sb.AppendLine($"  +0x{c.Offset:X4}  ptr=0x{c.Value.ToInt64():X}  {c.Note}  {mark}");
                    }
                    _hexView.Text = sb.ToString();

                    _grid.Rows.Clear();
                    foreach (var c in info.Candidates)
                    {
                        var row = _grid.Rows[_grid.Rows.Add(
                            c.IsEntityList ? "✓" : "?",
                            $"0x{info.ClassRva:X}",
                            $"0x{c.Value.ToInt64():X}",
                            $"+0x{c.Offset:X}",
                            c.Note + (c.IsEntityList ? $"  [{c.ListSize} entities]" : "")
                        )];
                        if (c.IsEntityList)
                        {
                            row.DefaultCellStyle.BackColor = Color.FromArgb(30, 60, 30);
                            row.DefaultCellStyle.ForeColor = Color.FromArgb(200, 240, 200);
                        }
                    }

                    int lists = info.Candidates.FindAll(x => x.IsEntityList).Count;
                    _status.Text = $"✓ class 0x{info.ClassRva:X}, {info.Candidates.Count} candidates, {lists} list(s)";
                    _status.ForeColor = lists > 0 ? Theme.Ok : Theme.Warn;
                }));
            }
            catch { _liveCts = null; }
        })
        { IsBackground = true }.Start();
    }

    // ═══════════════════════════════════════════════════════════
    //   CONTEXT
    // ═══════════════════════════════════════════════════════════

    void ShowContext()
    {
        try
        {
            if (_grid.SelectedRows.Count == 0) return;
            var row = _grid.SelectedRows[0];

            string info = row.Cells[4].Value as string;
            string slotStr = row.Cells[3].Value as string;
            string target = row.Cells[2].Value as string;

            // Live find mode — verify
            if (_lastLiveInfo != null && slotStr != null && slotStr.StartsWith("+0x") && MemoryAdapter.IsAttached)
            {
                int slot;
                if (int.TryParse(slotStr.Substring(3),
                    System.Globalization.NumberStyles.HexNumber, null, out slot))
                {
                    IntPtr slotAddr = _lastLiveInfo.StaticFields + slot;
                    string verify = LiveClassFinder.VerifyCandidate(slotAddr, 8);

                    _hexView.Text =
                        $"=== candidate verify ===\r\n\r\n" +
                        $"class:        {_lastLiveInfo.Name}\r\n" +
                        $"class RVA:    0x{_lastLiveInfo.ClassRva:X}\r\n" +
                        $"slot:         {slotStr}\r\n" +
                        $"slot addr:    0x{slotAddr.ToInt64():X}\r\n" +
                        $"value ptr:    {target}\r\n\r\n" +
                        $"--- content ---\r\n\r\n{verify}";
                    return;
                }
            }

            // Pattern mode
            if (_dll == null) { _hexView.Text = $"{slotStr}\n{info}"; return; }

            string insnStr = row.Cells[1].Value as string;
            if (string.IsNullOrEmpty(insnStr) || !insnStr.StartsWith("0x")) return;

            if (!long.TryParse(insnStr.Replace("0x", ""),
                System.Globalization.NumberStyles.HexNumber, null, out long insnRvaLong))
                return;
            int insnRva = (int)insnRvaLong;
            if (insnRva < 0 || insnRva + 96 > _dll.Length) return;

            int start = Math.Max(0, insnRva - 48);
            int end = Math.Min(_dll.Length, insnRva + 48);

            var sb = new StringBuilder();
            sb.AppendLine($"match @ RVA 0x{insnRva:X}");
            sb.AppendLine();

            for (int row16 = start; row16 < end; row16 += 16)
            {
                bool isMatch = insnRva >= row16 && insnRva < row16 + 16;
                var hex = new StringBuilder();
                var ascii = new StringBuilder();
                for (int i = 0; i < 16; i++)
                {
                    int at = row16 + i;
                    if (at >= _dll.Length) { hex.Append("   "); ascii.Append(' '); continue; }
                    byte b = _dll[at];
                    hex.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }
                sb.AppendLine($"{row16:X8}  {hex}  {ascii}{(isMatch ? "  ← MATCH" : "")}");
            }
            _hexView.Text = sb.ToString();
        }
        catch (Exception ex) { _hexView.Text = "EX: " + ex.Message; }
    }

    // ═══════════════════════════════════════════════════════════
    //   SAVE
    // ═══════════════════════════════════════════════════════════

    void SaveSig()
    {
        if (_grid.SelectedRows.Count == 0) { MessageBox.Show("Вибери рядок."); return; }
        if (AppState.ActiveGame == null) { MessageBox.Show("Немає активної гри"); return; }

        var r = _grid.SelectedRows[0];
        string mark = r.Cells[0].Value as string;
        string slotStr = r.Cells[3].Value as string;

        // ─── Live find mode
        if (_lastLiveInfo != null && slotStr != null && slotStr.StartsWith("+0x"))
        {
            if (mark != "✓" && MessageBox.Show("Не LIST — все одно зберегти?",
                "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            int slot;
            try { slot = Convert.ToInt32(slotStr.Substring(3), 16); }
            catch { MessageBox.Show("Bad slot"); return; }

            string name = _name.Text.Trim();
            if (string.IsNullOrEmpty(name))
            { MessageBox.Show("Введи name (напр. BaseNetworkable.clientEntities)"); return; }

            string clsName = _lastLiveInfo.Name;
            string sem = name;
            int dot = name.IndexOf('.');
            if (dot > 0) { clsName = name.Substring(0, dot); sem = name.Substring(dot + 1); }

            var chain = new List<int> { 0xB8, slot };
            string desc = $"static chain: module+0x{_lastLiveInfo.ClassRva:X} → ptr → +0xB8 → +0x{slot:X}";

            LabelService.AddChain(AppState.ActiveGame.Id, sem, clsName, sem,
                _lastLiveInfo.ClassRva, chain, "System.IntPtr", desc);

            MessageBox.Show(
                $"✓ Збережено:\n\n" +
                $"  {clsName}.{sem}\n" +
                $"  chain(0x{_lastLiveInfo.ClassRva:X}, 0xB8, 0x{slot:X})\n\n" +
                "Формула читу:\n" +
                $"  module + 0x{_lastLiveInfo.ClassRva:X} → ptr → read+0xB8 → read+0x{slot:X}",
                "Saved");
            return;
        }

        // ─── Pattern mode (instance)
        string target = r.Cells[2].Value as string;
        string insn = r.Cells[1].Value as string;

        string nm = _name.Text.Trim();
        if (string.IsNullOrEmpty(nm)) { MessageBox.Show("Введи name"); return; }
        if (target == "—") { MessageBox.Show("Немає target"); return; }

        long rva;
        try { rva = Convert.ToInt64(target.Substring(2), 16); }
        catch { MessageBox.Show("Bad RVA"); return; }

        string clsN = "Unknown", sm = nm;
        int d2 = nm.IndexOf('.');
        if (d2 > 0) { clsN = nm.Substring(0, d2); sm = nm.Substring(d2 + 1); }

        var mode = (VerifyMode)_modeBox.SelectedItem;
        string ds = $"{mode} — {nm} [match @ {insn}]";

        LabelService.Add(AppState.ActiveGame.Id, sm, clsN, sm,
                         (int)rva, "System.IntPtr", true, ds);

        MessageBox.Show($"✓ Збережено:\n\n  {clsN}.{sm}\n  RVA: 0x{rva:X}", "Saved");
    }

    // ═══════════════════════════════════════════════════════════
    //   pattern matching
    // ═══════════════════════════════════════════════════════════

    List<int> FindAll(string pattern, int limit)
    {
        var result = new List<int>();
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        var mask = new bool[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "?" || parts[i] == "??") { bytes[i] = 0; mask[i] = false; }
            else { bytes[i] = Convert.ToByte(parts[i], 16); mask[i] = true; }
        }

        int end = _dll.Length - bytes.Length;
        int first = bytes.Length > 0 && mask[0] ? bytes[0] : -1;

        for (int i = 0; i < end && result.Count < limit; i++)
        {
            if (first >= 0 && _dll[i] != first) continue;
            bool ok = true;
            for (int j = 1; j < bytes.Length; j++)
                if (mask[j] && _dll[i + j] != bytes[j]) { ok = false; break; }
            if (ok) result.Add(i);
        }
        return result;
    }
}