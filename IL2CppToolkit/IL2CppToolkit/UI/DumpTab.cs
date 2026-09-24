// language: C#, file: UI/DumpTab.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

public class DumpTab : UserControl
{
    RichTextBox _log;
    Button _run, _stop, _parseExisting;
    Label _status;
    ProgressBar _bar;
    System.Windows.Forms.Timer _timer;
    Stopwatch _sw;
    CancellationTokenSource _cts;

    public DumpTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Panel };
        _run = Theme.Btn("▶  Run Dump", 20, 15, 160, RunDump);
        _stop = Theme.Btn("■  Stop", 190, 15, 100, StopDump);
        _stop.Enabled = false;
        _parseExisting = Theme.Btn("Parse existing dump.cs", 300, 15, 200, ParseExisting);
        top.Controls.Add(_run);
        top.Controls.Add(_stop);
        top.Controls.Add(_parseExisting);
        _status = new Label
        {
            Location = new Point(520, 22),
            AutoSize = true,
            ForeColor = Theme.Dim,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.Transparent
        };
        top.Controls.Add(_status);
        Controls.Add(top);

        _bar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 4,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 0,
            BackColor = Theme.Panel
        };
        Controls.Add(_bar);
        _bar.BringToFront();

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.LogBg,
            ForeColor = Theme.LogText,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = false
        };
        Controls.Add(_log);
        _log.BringToFront();

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) =>
        {
            if (_sw != null && _sw.IsRunning)
                _status.Text = $"running… {_sw.Elapsed.TotalSeconds:F0}s";
        };
    }

    void RunDump()
    {
        if (AppState.ActiveGame == null)
        {
            var g = GameRegistry.Load().Find("rust");
            if (g == null) { Warn("немає активної гри — відкрий вкладку Games"); return; }
            AppState.SetGame(g);
        }

        string dumperExe = Il2CppDumperAdapter.AutoFind();
        if (dumperExe == null) { Warn("Il2CppDumper.exe не знайдено"); return; }

        var game = AppState.ActiveGame;
        if (game.ExecutablePath == null || game.MetadataPath == null)
        { Warn("шляхи гри неповні — перевір вкладку Games"); return; }

        // вбиваємо старі
        foreach (var proc in Process.GetProcessesByName("Il2CppDumper"))
        { try { proc.Kill(); } catch { } }

        _log.Clear();
        _run.Enabled = false;
        _stop.Enabled = true;
        _bar.MarqueeAnimationSpeed = 30;
        _sw = Stopwatch.StartNew();
        _timer.Start();
        _cts = new CancellationTokenSource();

        var token = _cts.Token;
        new Thread(() =>
        {
            try
            {
                var res = DumpService.Acquire(game, dumperExe, line =>
                {
                    if (InvokeRequired) BeginInvoke(new Action(() => Log(line)));
                    else Log(line);
                }, token);

                BeginInvoke(new Action(() =>
                {
                    _bar.MarqueeAnimationSpeed = 0;
                    _run.Enabled = true;
                    _stop.Enabled = false;
                    _timer.Stop();
                    _sw.Stop();

                    if (res.Success)
                    {
                        Status($"done · {_sw.Elapsed.TotalSeconds:F0}s", Theme.Ok);
                        Log($"");
                        Log($"✓ SUCCESS · {res.Dump.Classes.Count} classes · {_sw.Elapsed.TotalSeconds:F1}s");
                        AppState.SetDump(res.Dump, res.SnapshotPath);
                    }
                    else
                    {
                        Status("failed", Theme.Err);
                        Log("");
                        Log($"✗ {res.Error}");
                    }
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(() =>
                {
                    _bar.MarqueeAnimationSpeed = 0;
                    _run.Enabled = true;
                    _stop.Enabled = false;
                    _timer.Stop();
                    Log("EX: " + ex.Message);
                }));
            }
        })
        { IsBackground = true }.Start();
    }

    void StopDump()
    {
        try { _cts?.Cancel(); } catch { }
    }

    void ParseExisting()
    {
        if (AppState.ActiveGame == null) { Warn("немає активної гри"); return; }
        string p = Path.Combine(AppContext.BaseDirectory, "dump_out", AppState.ActiveGame.Id, "dump.cs");
        if (!File.Exists(p)) { Warn($"не знайдено: {p}"); return; }

        _log.Clear();
        _run.Enabled = false; _stop.Enabled = false;
        _bar.MarqueeAnimationSpeed = 30;
        Status("parsing…", Theme.Warn);

        new Thread(() =>
        {
            var sw = Stopwatch.StartNew();
            var dump = DumpParser.Parse(p, AppState.ActiveGame.Id);
            sw.Stop();

            BeginInvoke(new Action(() =>
            {
                _bar.MarqueeAnimationSpeed = 0;
                _run.Enabled = true;
                Status($"parsed · {sw.ElapsedMilliseconds}ms", Theme.Ok);
                Log($"✓ parsed {dump.Classes.Count} classes in {sw.ElapsedMilliseconds}ms");

                string snapDir = Paths.SnapshotDir(AppState.ActiveGame.Id);
                string snapPath = Path.Combine(snapDir, dump.SuggestFilename());
                dump.Save(snapPath);
                Log($"✓ snapshot: {snapPath}");
                AppState.SetDump(dump, snapPath);
            }));
        })
        { IsBackground = true }.Start();
    }

    void Log(string s)
    {
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = Theme.LogText;
        _log.AppendText(s + "\n");
        _log.ScrollToCaret();
    }

    void Status(string s, Color c) { _status.Text = s; _status.ForeColor = c; }
    void Warn(string s) { MessageBox.Show(s, "Dump"); }
}