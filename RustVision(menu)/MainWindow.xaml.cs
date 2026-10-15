using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RustVision.Config;

namespace RustVision;

/// <summary>
/// Code-behind for the single application window. Every control here only
/// ever affects this window's own visual state or writes/reads local JSON
/// config files. Nothing in this file touches another process, reads game
/// memory, or performs any kind of injection - it is a UI prototype only.
///
/// The one exception worth calling out explicitly is the "menu hotkey":
/// it uses the standard Win32 RegisterHotKey API to let the user show/hide
/// THIS window from anywhere, the same way any ordinary Windows utility
/// (volume overlays, screenshot tools, etc.) registers a global shortcut.
/// It never reads, writes, or attaches to any other process.
/// </summary>
public partial class MainWindow : Window
{
    // ---- autosave ----
    private DispatcherTimer? _autoSaveTimer;
    private DispatcherTimer? _actionStatusTimer;
    private bool _isApplyingConfig;

    // ---- accent color ----
    private string _currentAccentHex = "#E51E24";

    // ---- compact mode ----
    private bool _isCompact;
    private double _normalWidth = 1280;
    private double _normalHeight = 760;

    // ---- menu hotkey (Win32, window-only) ----
    private const int HotkeyId = 0x4A2B;
    private IntPtr _windowHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set up the Win32 hook first so RegisterMenuHotkey(...) called from
        // ApplyConfig(...) below has a valid window handle to attach to.
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        ConfigManager.EnsureInitialized();

        var startupConfig = ConfigManager.Load("default");
        ApplyConfig(startupConfig);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
        }
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    // ==================================================================
    // WINDOW CHROME
    // ==================================================================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Can happen if the mouse button was released mid-call; safe to ignore.
            }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ==================================================================
    // NAVIGATION
    // ==================================================================

    private void NavTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelAim == null) return; // guard against firing during InitializeComponent

        PanelAim.Visibility = Visibility.Collapsed;
        PanelVisuals.Visibility = Visibility.Collapsed;
        PanelMisc.Visibility = Visibility.Collapsed;
        PanelConfig.Visibility = Visibility.Collapsed;
        PanelSettings.Visibility = Visibility.Collapsed;
        PanelAbout.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(sender, TabAim)) PanelAim.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabVisuals)) PanelVisuals.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabMisc)) PanelMisc.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabConfig)) PanelConfig.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabSettings)) PanelSettings.Visibility = Visibility.Visible;
        else if (ReferenceEquals(sender, TabAbout)) PanelAbout.Visibility = Visibility.Visible;
    }

    // ==================================================================
    // GENERIC HANDLERS (checkboxes / radios / combos with no bespoke logic
    // beyond persisting their value)
    // ==================================================================

    private void Generic_CheckChanged(object sender, RoutedEventArgs e) => ScheduleAutoSave();

    private void Generic_ComboChanged(object sender, SelectionChangedEventArgs e) => ScheduleAutoSave();

    // ==================================================================
    // AIM TAB
    // ==================================================================

    private void SldFov_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtFovValue != null)
            TxtFovValue.Text = $"{(int)e.NewValue}°";
        ScheduleAutoSave();
    }

    private void SldSmoothness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSmoothnessValue != null)
            TxtSmoothnessValue.Text = $"{(int)e.NewValue}";
        ScheduleAutoSave();
    }

    // ==================================================================
    // VISUALS TAB (drives the decorative Live Preview panel only)
    // ==================================================================

    private void VisualsOption_Changed(object sender, RoutedEventArgs e)
    {
        if (PreviewBox == null) return;

        PreviewBox.Visibility = ToVisibility(ChkBox.IsChecked);
        PreviewName.Visibility = ToVisibility(ChkName.IsChecked);
        PreviewHealth.Visibility = ToVisibility(ChkHealth.IsChecked);
        PreviewDistance.Visibility = ToVisibility(ChkDistance.IsChecked);

        ScheduleAutoSave();
    }

    private static Visibility ToVisibility(bool? isChecked) =>
        isChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void CmbColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewBox == null || CmbColor.SelectedItem is not ComboBoxItem item) return;

        var brush = (string)item.Content switch
        {
            "White" => Brushes.White,
            "Green" => Brushes.LimeGreen,
            "Cyan" => Brushes.Cyan,
            _ => (Brush)FindResource("BrushAccent")
        };

        PreviewBox.BorderBrush = brush;
        PreviewName.Foreground = brush;
        ScheduleAutoSave();
    }

    private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtOpacityValue != null)
            TxtOpacityValue.Text = $"{(int)e.NewValue}%";

        if (PreviewBox != null)
            PreviewBox.Opacity = e.NewValue / 100.0;

        ScheduleAutoSave();
    }

    private void CmbLineStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewBox == null || CmbLineStyle.SelectedItem is not ComboBoxItem item) return;

        PreviewBox.BorderThickness = (string)item.Content switch
        {
            "Dashed" => new Thickness(1),
            "Dotted" => new Thickness(1),
            _ => new Thickness(2)
        };
        ScheduleAutoSave();
    }

    // ==================================================================
    // MISC TAB
    // ==================================================================

    private void SldUiScaleMisc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtUiScaleMiscValue != null)
            TxtUiScaleMiscValue.Text = $"{(int)e.NewValue}%";
        ScheduleAutoSave();
    }

    // ==================================================================
    // SETTINGS TAB
    // ==================================================================

    private void SldUiScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtUiScaleValue != null)
            TxtUiScaleValue.Text = $"{(int)e.NewValue}%";
        ScheduleAutoSave();
    }

    private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = ChkAlwaysOnTop.IsChecked == true;
        ScheduleAutoSave();
    }

    // ---- Accent color -------------------------------------------------

    private void AccentSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string hex) return;
        ApplyAccentColor(hex);
        ScheduleAutoSave();
    }

    private void ApplyAccentColor(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);

            if (Application.Current.Resources["BrushAccent"] is SolidColorBrush accentBrush)
                accentBrush.Color = color;

            if (Application.Current.Resources["BrushAccentDark"] is SolidColorBrush darkBrush)
                darkBrush.Color = DarkenForDarkVariant(color);

            _currentAccentHex = hex;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or NotSupportedException)
        {
            // Invalid or corrupted hex string (e.g. from hand-edited JSON) - ignore and keep the previous accent color.
        }
    }

    private void SelectAccentSwatch(string hex)
    {
        foreach (var swatch in new[] { SwRed, SwBlue, SwPurple, SwGreen, SwOrange, SwCyan })
        {
            if (swatch.Tag is string tagHex && string.Equals(tagHex, hex, StringComparison.OrdinalIgnoreCase))
            {
                swatch.IsChecked = true;
                return;
            }
        }
        SwRed.IsChecked = true;
    }

    /// <summary>
    /// Produces a dark variant of an accent color (same hue/saturation, low
    /// lightness) so panels like the active nav tab background stay
    /// legible no matter which accent the user picks.
    /// </summary>
    private static Color DarkenForDarkVariant(Color c)
    {
        RgbToHsl(c.R, c.G, c.B, out var h, out var s, out var l);
        var (r, g, b) = HslToRgb(h, s, 0.13);
        return Color.FromRgb(r, g, b);
    }

    private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 0.0001)
        {
            h = 0; s = 0;
            return;
        }

        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
        else if (max == g) h = ((b - r) / d + 2) / 6.0;
        else h = ((r - g) / d + 4) / 6.0;
    }

    private static (byte r, byte g, byte b) HslToRgb(double h, double s, double l)
    {
        if (s <= 0.0001)
        {
            var v = (byte)Math.Round(l * 255.0);
            return (v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;

        double r = HueToRgb(p, q, h + 1.0 / 3.0);
        double g = HueToRgb(p, q, h);
        double b = HueToRgb(p, q, h - 1.0 / 3.0);

        return ((byte)Math.Round(r * 255.0), (byte)Math.Round(g * 255.0), (byte)Math.Round(b * 255.0));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    // ---- Compact mode ---------------------------------------------------

    private void BtnCompact_Click(object sender, RoutedEventArgs e)
    {
        SetCompactMode(!_isCompact);
        ScheduleAutoSave();
    }

    private void SetCompactMode(bool compact)
    {
        _isCompact = compact;

        if (compact)
        {
            if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
            {
                _normalWidth = Width;
                _normalHeight = Height;
            }

            MinWidth = 380;
            MinHeight = 520;
            Width = 420;
            Height = 600;

            ColNav.Width = new GridLength(52);
            ColSidebar.Width = new GridLength(0);
            SidebarScroll.Visibility = Visibility.Collapsed;
            SetNavLabelsVisibility(Visibility.Collapsed);

            if (BtnCompact != null) BtnCompact.ToolTip = "Звичайний режим";
        }
        else
        {
            MinWidth = 1050;
            MinHeight = 650;
            Width = _normalWidth;
            Height = _normalHeight;

            ColNav.Width = new GridLength(190);
            ColSidebar.Width = new GridLength(280);
            SidebarScroll.Visibility = Visibility.Visible;
            SetNavLabelsVisibility(Visibility.Visible);

            if (BtnCompact != null) BtnCompact.ToolTip = "Компактний режим";
        }
    }

    private void SetNavLabelsVisibility(Visibility v)
    {
        TxtNavAim.Visibility = v;
        TxtNavVisuals.Visibility = v;
        TxtNavMisc.Visibility = v;
        TxtNavConfig.Visibility = v;
        TxtNavSettings.Visibility = v;
        TxtNavAbout.Visibility = v;
    }

    // ---- Menu hotkey (window visibility only) --------------------------

    private void CmbMenuHotkey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMenuHotkey.SelectedItem is not ComboBoxItem item) return;
        RegisterMenuHotkey(item.Content?.ToString() ?? "Insert");
        ScheduleAutoSave();
    }

    private void RegisterMenuHotkey(string keyName)
    {
        if (_windowHandle == IntPtr.Zero) return;

        UnregisterHotKey(_windowHandle, HotkeyId);

        uint vk = keyName switch
        {
            "F9" => 0x78,
            "Home" => 0x24,
            "Delete" => 0x2E,
            _ => 0x2D // Insert
        };

        RegisterHotKey(_windowHandle, HotkeyId, 0 /* no modifiers */, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleMenuVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleMenuVisibility()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Activate();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    // ==================================================================
    // CONFIG TAB
    // ==================================================================

    private string GetSelectedConfigProfile()
    {
        if (RbProfileLegit.IsChecked == true) return "legit";
        if (RbProfileCustom.IsChecked == true) return "custom";
        return "default";
    }

    private void ProfileRadio_Checked(object sender, RoutedEventArgs e)
    {
        // Guard: IsChecked="True" on RbProfileDefault in XAML can raise this
        // event synchronously during InitializeComponent, before elements
        // declared later in the document (like SidebarScroll) exist yet.
        // The initial profile load already happens explicitly in
        // MainWindow_Loaded, so it's safe to simply ignore early firings here.
        if (!IsLoaded) return;

        // Switching the active profile immediately loads its saved data,
        // matching the "auto" spirit of the CONFIG tab. The explicit LOAD
        // button below does the same thing for users who prefer a manual step.
        var profile = GetSelectedConfigProfile();
        var config = ConfigManager.Load(profile);
        ApplyConfig(config);
        if (TxtConfigStatus != null)
            TxtConfigStatus.Text = $"Loaded profile \"{profile}\".";
    }

    private void BtnCfgSave_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedConfigProfile();
        var config = CollectConfig();
        var ok = ConfigManager.Save(profile, config);
        TxtConfigStatus.Text = ok
            ? $"Saved profile \"{profile}\"."
            : "Could not save config. Check folder permissions.";
    }

    private void BtnCfgLoad_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedConfigProfile();
        var config = ConfigManager.Load(profile);
        ApplyConfig(config);
        TxtConfigStatus.Text = $"Loaded profile \"{profile}\".";
    }

    private void BtnCfgReset_Click(object sender, RoutedEventArgs e)
    {
        ApplyConfig(new RustVisionConfig());
        TxtConfigStatus.Text = "Reset to defaults.";
    }

    // ==================================================================
    // RIGHT SIDEBAR ACTIONS
    // ==================================================================

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedConfigProfile();
        var config = CollectConfig();
        var ok = ConfigManager.Save(profile, config);
        ShowActionStatus(ok ? "CONFIG SAVED" : "SAVE FAILED");
    }

    private void BtnResetAll_Click(object sender, RoutedEventArgs e)
    {
        ApplyConfig(new RustVisionConfig());
        ShowActionStatus("ALL RESET");
    }

    private void ShowActionStatus(string message)
    {
        TxtActionStatus.Text = message;

        _actionStatusTimer?.Stop();
        _actionStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _actionStatusTimer.Tick += (_, _) =>
        {
            TxtActionStatus.Text = " ";
            _actionStatusTimer?.Stop();
        };
        _actionStatusTimer.Start();
    }

    // ==================================================================
    // AUTO-SAVE (debounced, writes to the currently selected profile)
    // ==================================================================

    private void ScheduleAutoSave()
    {
        if (!IsLoaded || _isApplyingConfig) return;

        _autoSaveTimer?.Stop();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _autoSaveTimer.Tick += (_, _) =>
        {
            _autoSaveTimer?.Stop();

            var profile = GetSelectedConfigProfile();
            var config = CollectConfig();
            var ok = ConfigManager.Save(profile, config);

            if (TxtConfigStatus != null)
            {
                TxtConfigStatus.Text = ok
                    ? $"Автозбережено ({profile})."
                    : "Автозбереження не вдалося.";
            }
        };
        _autoSaveTimer.Start();
    }

    // ==================================================================
    // CONFIG <-> UI MAPPING
    // ==================================================================

    private RustVisionConfig CollectConfig()
    {
        return new RustVisionConfig
        {
            ProfileName = GetSelectedConfigProfile(),

            AimEnabled = ChkAimEnable.IsChecked == true,
            AimShowFov = ChkAimShowFov.IsChecked == true,
            AimShowTargetIndicator = ChkAimShowIndicator.IsChecked == true,
            AimFov = SldFov.Value,
            AimSmoothness = SldSmoothness.Value,
            AimPriority = RbPriorityDistance.IsChecked == true ? "Distance"
                          : RbPriorityCustom.IsChecked == true ? "Custom"
                          : "Closest",
            AimHotkey = (CmbHotkey.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "F6",

            ShowBox = ChkBox.IsChecked == true,
            ShowName = ChkName.IsChecked == true,
            ShowHealth = ChkHealth.IsChecked == true,
            ShowDistance = ChkDistance.IsChecked == true,
            VisualsOpacity = SldOpacity.Value,
            LineStyle = (CmbLineStyle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Solid",

            NotificationsEnabled = ChkNotifications.IsChecked == true,
            StartWithApplication = ChkStartWithApp.IsChecked == true,
            UiSounds = ChkUiSounds.IsChecked == true,
            AnimationsEnabled = ChkAnimationsMisc.IsChecked == true,
            UiScaleMisc = SldUiScaleMisc.Value,

            Theme = (CmbTheme.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Red",
            UiScale = SldUiScale.Value,
            AlwaysOnTop = ChkAlwaysOnTop.IsChecked == true,
            StartMinimized = ChkStartMinimized.IsChecked == true,
            Language = (CmbLanguage.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "English",

            AccentColorHex = _currentAccentHex,
            CompactMode = _isCompact,
            MenuHotkey = (CmbMenuHotkey.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Insert"
        };
    }

    private void ApplyConfig(RustVisionConfig c)
    {
        _isApplyingConfig = true;

        ChkAimEnable.IsChecked = c.AimEnabled;
        ChkAimShowFov.IsChecked = c.AimShowFov;
        ChkAimShowIndicator.IsChecked = c.AimShowTargetIndicator;
        SldFov.Value = c.AimFov;
        SldSmoothness.Value = c.AimSmoothness;

        RbPriorityClosest.IsChecked = c.AimPriority == "Closest";
        RbPriorityDistance.IsChecked = c.AimPriority == "Distance";
        RbPriorityCustom.IsChecked = c.AimPriority == "Custom";
        SelectComboItem(CmbHotkey, c.AimHotkey);

        ChkBox.IsChecked = c.ShowBox;
        ChkName.IsChecked = c.ShowName;
        ChkHealth.IsChecked = c.ShowHealth;
        ChkDistance.IsChecked = c.ShowDistance;
        SldOpacity.Value = c.VisualsOpacity;
        SelectComboItem(CmbLineStyle, c.LineStyle);

        ChkNotifications.IsChecked = c.NotificationsEnabled;
        ChkStartWithApp.IsChecked = c.StartWithApplication;
        ChkUiSounds.IsChecked = c.UiSounds;
        ChkAnimationsMisc.IsChecked = c.AnimationsEnabled;
        SldUiScaleMisc.Value = c.UiScaleMisc;

        ChkAnimationsSettings.IsChecked = c.AnimationsEnabled;
        ChkNotificationsSettings.IsChecked = c.NotificationsEnabled;
        SelectComboItem(CmbTheme, c.Theme);
        SldUiScale.Value = c.UiScale;
        ChkAlwaysOnTop.IsChecked = c.AlwaysOnTop;
        Topmost = c.AlwaysOnTop;
        ChkStartMinimized.IsChecked = c.StartMinimized;
        SelectComboItem(CmbLanguage, c.Language);

        ApplyAccentColor(c.AccentColorHex);
        SelectAccentSwatch(c.AccentColorHex);

        SelectComboItem(CmbMenuHotkey, c.MenuHotkey);
        RegisterMenuHotkey(c.MenuHotkey);

        SetCompactMode(c.CompactMode);

        // Refresh the decorative preview to match the newly-applied state.
        VisualsOption_Changed(this, new RoutedEventArgs());

        _isApplyingConfig = false;
    }

    private static void SelectComboItem(ComboBox combo, string content)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }
}
