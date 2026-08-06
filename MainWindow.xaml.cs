using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PSuite.Core;
using PSuite.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace PSuite
{
    public sealed partial class MainWindow : Window
    {
        private readonly StateStore _stateStore = new();
        private readonly LogStore _logStore = new();
        private readonly SettingsStore _settingsStore = new();
        private readonly string _modulesRoot = Path.Combine(AppContext.BaseDirectory, "Modules");
        private readonly ModuleManager _moduleManager;

        private readonly Dictionary<string, InstalledModule> _modulesById = new();

        private readonly ObservableCollection<TweakItem> _allTweaks = new();
        private readonly ObservableCollection<LogEntryView> _logEntries = new();
        public ObservableCollection<TweakItem> Tweaks { get; } = new();

        private bool _isInitializing = true;
        private bool _systemInfoLoaded = false;
        private string _currentTag = "All";
        private string _riskFilter = "All";

        public MainWindow()
        {
            InitializeComponent();
            RootGrid.DataContext = this;

            SetWindowIcon();

            _moduleManager = new ModuleManager(_modulesRoot);

            ShowExperimentalToggle.IsOn = _settingsStore.Current.ShowExperimentalModules;
            ApplyExperimentalFilterButtonVisibility();
            LaunchOnStartupToggle.IsOn = false; // real state loaded async below (StartupTask API)
            RestorePointBeforeApplyToggle.IsOn = _settingsStore.Current.CreateRestorePointBeforeApply;
            ApplyAccentColor(_settingsStore.Current.AccentColor);
            ModulesPathText.Text = _modulesRoot;

            ColorFilterToggle.IsOn = _settingsStore.Current.ColorFilterEnabled;
            ColorBrightnessSlider.Value = _settingsStore.Current.ColorFilterBrightness;
            ColorContrastSlider.Value = _settingsStore.Current.ColorFilterContrastPercent;
            ColorGammaSlider.Value = _settingsStore.Current.ColorFilterGamma;
            UpdateColorFilterValueTexts();
            if (_settingsStore.Current.ColorFilterEnabled)
                ApplyColorFilterFromSettings();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"PSuite v{version?.ToString(3) ?? "1.0.0"}";

            UpdateRiskFilterButtonStyles();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var stepMinDuration = TimeSpan.FromMilliseconds(1100);

                SetSplashStatus("Загрузка модулей...");
                var modulesStart = DateTime.UtcNow;
                await LoadFromModulesAsync();
                await DelayRemaining(modulesStart, stepMinDuration);

                ApplyFilter("All");
                CategoryList.SelectedIndex = 0;

                SetSplashStatus("Анализ ПК...");
                var analysisStart = DateTime.UtcNow;
                var (startupEnabled, _) = await Core.SystemIntegration.GetStartupStateAsync();
                LaunchOnStartupToggle.IsOn = startupEnabled;
                await DelayRemaining(analysisStart, stepMinDuration);

                SetSplashStatus("Проверка целостности данных...");
                var integrityStart = DateTime.UtcNow;
                // Genuine check, not decoration: modules that failed
                // manifest validation during LoadFromModulesAsync above
                // are real, already-tracked problems — not a fabricated
                // status.
                var hasIssues = _moduleManager.LastValidationErrors.Count > 0;
                SetSplashStatus(hasIssues
                    ? $"Пропущено модулей с ошибками: {_moduleManager.LastValidationErrors.Count}."
                    : "Готово.");
                await DelayRemaining(integrityStart, stepMinDuration);

                _isInitializing = false;
            }
            finally
            {
                HideSplashOverlay();
            }
        }

        // Keeps each splash step visible for at least `minDuration` —
        // real work underneath, just not allowed to flash by faster than
        // a person can read the label. Never adds delay beyond topping up
        // to the minimum; a step that's already slow isn't padded further.
        private static async Task DelayRemaining(DateTime stepStartUtc, TimeSpan minDuration)
        {
            var elapsed = DateTime.UtcNow - stepStartUtc;
            var remaining = minDuration - elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);
        }

        private void SetSplashStatus(string text)
        {
            try
            {
                if (SplashStatusText != null) SplashStatusText.Text = text;
            }
            catch
            {
                // Purely cosmetic — never worth taking the app down over.
            }
        }

        // Fades the splash out and hides it. Deliberately defensive: if
        // the animation itself throws for any reason, the overlay is
        // still forced to Collapsed in the finally block, so a splash bug
        // can never trap the person behind an opaque screen forever.
        private void HideSplashOverlay()
        {
            try
            {
                if (SplashOverlay == null || SplashOverlay.Visibility != Visibility.Visible) return;

                var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(280)) };
                Storyboard.SetTarget(fadeOut, SplashOverlay);
                Storyboard.SetTargetProperty(fadeOut, "Opacity");

                var sb = new Storyboard();
                sb.Children.Add(fadeOut);
                sb.Completed += (_, _) =>
                {
                    try
                    {
                        SplashOverlay.Visibility = Visibility.Collapsed;
                        SplashProgressRing.IsActive = false; // stop spinning once hidden — no reason to burn cycles on an invisible control
                    }
                    catch { /* best-effort */ }
                };
                sb.Begin();
            }
            catch
            {
                try { SplashOverlay.Visibility = Visibility.Collapsed; } catch { /* nothing further we can do */ }
            }
        }

        // WinUI3 does NOT automatically use <ApplicationIcon> from the
        // .csproj for the window itself — that only covers the taskbar/
        // Task Manager entry for the .exe. The title-bar/Alt+Tab icon has
        // to be set explicitly on the AppWindow, or it renders blank.
        private void SetWindowIcon()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
                if (File.Exists(iconPath))
                    appWindow.SetIcon(iconPath);
            }
            catch
            {
                // Best-effort: a missing/locked icon file should never
                // prevent the window from opening.
            }
        }

        private async Task LoadFromModulesAsync()
        {
            await _moduleManager.RefreshAsync();

            _allTweaks.Clear();
            _modulesById.Clear();

            foreach (var module in _moduleManager.Modules)
            {
                if (!_settingsStore.Current.ShowExperimentalModules &&
                    string.Equals(module.Manifest.Risk, "Experimental", StringComparison.OrdinalIgnoreCase))
                    continue;

                _modulesById[module.Manifest.Id] = module;

                var stateEntry = _stateStore.Get(module.Manifest.Id);
                var isApplied = stateEntry?.LastKnownState == ModuleState.Applied;

                _allTweaks.Add(new TweakItem
                {
                    Id = module.Manifest.Id,
                    Title = module.Manifest.Name,
                    Description = module.Manifest.Description,
                    Category = module.Manifest.Category,
                    IconGlyph = "\uE9A1",
                    Risk = ParseRisk(module.Manifest.Risk),
                    RequiresRestart = module.Manifest.RequiresRestart,
                    IsApplied = isApplied
                });
            }

            if (_moduleManager.LastValidationErrors.Count > 0)
            {
                ShowStatus(
                    $"Пропущено модулей с ошибками: {_moduleManager.LastValidationErrors.Count}. " +
                    $"Первая: {_moduleManager.LastValidationErrors[0].Reason}");
            }
        }

        private static TweakRisk ParseRisk(string risk) => risk switch
        {
            "Safe" => TweakRisk.Safe,
            "Advanced" => TweakRisk.Advanced,
            "Experimental" => TweakRisk.Experimental,
            _ => TweakRisk.Experimental
        };

        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                ApplyFilter(tag);
        }

        // Colors the active tab's icon+label with the accent colour (the
        // same one from Настройки → Цвет акцента), grey for the rest —
        // a second, more visible cue than just the thin selection bar.
        private void UpdateNavIconColors(string activeTag)
        {
            var items = new (string Tag, Microsoft.UI.Xaml.Controls.FontIcon Icon, TextBlock Text)[]
            {
                ("All", NavIconAll, NavTextAll),
                ("Analyzer", NavIconAnalyzer, NavTextAnalyzer),
                ("Rollback", NavIconRollback, NavTextRollback),
                ("Logs", NavIconLogs, NavTextLogs),
                ("Benchmark", NavIconBenchmark, NavTextBenchmark),
                ("Settings", NavIconSettings, NavTextSettings)
            };

            var activeBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteAccentTealLightBrush"];
            var inactiveBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextSecondaryBrush"];

            foreach (var (tag, icon, text) in items)
            {
                var brush = tag == activeTag ? activeBrush : inactiveBrush;
                icon.Foreground = brush;
                text.Foreground = brush;
            }
        }

        private void ApplyFilter(string tag)
        {
            _currentTag = tag;
            UpdateNavIconColors(tag);

            bool isLogs = tag == "Logs";
            bool isSettings = tag == "Settings";
            bool isBenchmark = tag == "Benchmark";
            bool isAnalyzer = tag == "Analyzer";
            bool isTweaksView = !isLogs && !isSettings && !isBenchmark && !isAnalyzer;

            TweaksGridView.Visibility = isTweaksView ? Visibility.Visible : Visibility.Collapsed;
            RiskFilterBar.Visibility = isTweaksView ? Visibility.Visible : Visibility.Collapsed;
            LogsPlaceholder.Visibility = isLogs ? Visibility.Visible : Visibility.Collapsed;
            SettingsScrollViewer.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
            BenchmarkScrollViewer.Visibility = isBenchmark ? Visibility.Visible : Visibility.Collapsed;
            AnalyzerScrollViewer.Visibility = isAnalyzer ? Visibility.Visible : Visibility.Collapsed;
            if (isAnalyzer)
            {
                SystemInfoPanel.Visibility = Visibility.Visible;
                if (!_systemInfoLoaded)
                {
                    _systemInfoLoaded = true;
                    _ = PopulateSystemInfoAsync();
                }
            }
            RefreshButton.Visibility = isTweaksView ? Visibility.Visible : Visibility.Collapsed;
            ClearLogsButton.Visibility = isLogs ? Visibility.Visible : Visibility.Collapsed;

            // Reset unconditionally — only the tweaks branch below (if it
            // runs) is allowed to turn this back on. Without this, leaving
            // it Visible from a previous "Твики" visit would bleed through
            // and overlap the Logs/Benchmark/Settings text underneath.
            TweaksEmptyText.Visibility = Visibility.Collapsed;

            if (isSettings)
            {
                SectionTitleText.Text = "Настройки";
                SectionSubtitleText.Text = "Параметры приложения";
                return;
            }

            if (isBenchmark)
            {
                SectionTitleText.Text = "Бенчмарк";
                SectionSubtitleText.Text = "Быстрая проверка CPU и памяти";
                return;
            }

            if (isAnalyzer)
            {
                SectionTitleText.Text = "Анализ";
                SectionSubtitleText.Text = "Железо, Windows и безопасность";
                return;
            }

            if (isLogs)
            {
                SectionTitleText.Text = "Логи";
                SectionSubtitleText.Text = "Журнал операций";
                RenderLogs();
                return;
            }

            IEnumerable<TweakItem> filtered = tag switch
            {
                "System" => _allTweaks.Where(t => t.Category == "System" && !t.IsApplied),
                "Rollback" => _allTweaks.Where(t => t.IsApplied),
                _ => _allTweaks.Where(t => !t.IsApplied)
            };

            if (_riskFilter != "All")
                filtered = filtered.Where(t => t.Risk.ToString() == _riskFilter);

            Tweaks.Clear();
            foreach (var tweak in filtered)
                Tweaks.Add(tweak);

            SectionTitleText.Text = tag switch
            {
                "System" => "Система",
                "Rollback" => "Применённые твики",
                _ => "Твики системы"
            };

            SectionSubtitleText.Text = tag == "Rollback"
                ? $"{Tweaks.Count} применено"
                : $"{Tweaks.Count} доступно";

            TweaksEmptyText.Text = _riskFilter != "All"
                ? $"Нет твиков с меткой «{_riskFilter}» в этом разделе."
                : tag switch
                {
                    "Rollback" => "Вы ещё не применяли ни одного твика — здесь появится история для отката.",
                    "System" => "Пока нет твиков в категории «Система». Импортируйте модуль в Настройках.",
                    _ => "Пока нет доступных твиков. Импортируйте модуль в Настройках."
                };

            TweaksEmptyText.Visibility = Tweaks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderLogs()
        {
            _logEntries.Clear();

            foreach (var entry in _logStore.ReadRecent())
            {
                var actionLabel = entry.Action switch
                {
                    "Apply" => "Применить",
                    "Rollback" => "Rollback",
                    "Benchmark" => "Бенчмарк",
                    _ => entry.Action
                };

                _logEntries.Add(new LogEntryView
                {
                    IconGlyph = entry.Success ? "\uE73E" : "\uE783",
                    Title = $"{entry.ModuleName} — {actionLabel}",
                    Subtitle = $"{entry.TimestampUtc.ToLocalTime():dd.MM.yyyy HH:mm} · " +
                               (entry.Success ? "успешно" : $"ошибка: {entry.Details}")
                });
            }

            LogsListView.ItemsSource = _logEntries;
            LogsListView.Visibility = _logEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            LogsEmptyText.Visibility = _logEntries.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnClearLogsClick(object sender, RoutedEventArgs e)
        {
            _logStore.Clear();
            RenderLogs();
        }

        private async void OnLaunchOnStartupToggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var requested = LaunchOnStartupToggle.IsOn;
            var (success, message) = await Core.SystemIntegration.SetStartupEnabledAsync(requested);

            if (success)
            {
                ShowStatus(requested
                    ? "PSuite добавлен в автозагрузку Windows."
                    : "PSuite убран из автозагрузки Windows.", StatusKind.Success);
            }
            else
            {
                // Didn't actually happen — revert the toggle instead of
                // showing a state that doesn't match reality. Guard with
                // _isInitializing so setting IsOn here doesn't re-enter
                // this handler and retry the same failing call.
                _isInitializing = true;
                LaunchOnStartupToggle.IsOn = !requested;
                _isInitializing = false;
                ShowStatus($"Не удалось изменить автозагрузку: {message}");
            }
        }

        private void UpdateColorFilterValueTexts()
        {
            ColorBrightnessValueText.Text = $"{(int)ColorBrightnessSlider.Value}";
            ColorContrastValueText.Text = $"{(int)ColorContrastSlider.Value}%";
            ColorGammaValueText.Text = ColorGammaSlider.Value.ToString("0.00");
        }

        // Set once Windows/the GPU driver has refused a gamma-ramp change.
        // Some drivers (mostly laptop iGPU/hybrid-graphics setups) reject
        // SetDeviceGammaRamp entirely — that's a real hardware/driver
        // limitation, not something retrying fixes. Without this guard,
        // every slider drag re-attempted the call and re-showed the same
        // error, which is exactly what made this feel broken/annoying
        // rather than "this PC doesn't support it, once, clearly".
        private bool _colorFilterUnsupported;

        private void ApplyColorFilterFromSettings()
        {
            if (_colorFilterUnsupported) return;

            var s = _settingsStore.Current;
            var ok = Core.ColorFilterManager.Apply(s.ColorFilterBrightness, s.ColorFilterContrastPercent, s.ColorFilterGamma);
            if (ok) return;

            _colorFilterUnsupported = true;
            _settingsStore.Current.ColorFilterEnabled = false;
            _settingsStore.Save();

            _isInitializing = true;
            ColorFilterToggle.IsOn = false;
            _isInitializing = false;

            ColorFilterToggle.IsEnabled = false;
            ColorBrightnessSlider.IsEnabled = false;
            ColorContrastSlider.IsEnabled = false;
            ColorGammaSlider.IsEnabled = false;

            ShowStatus("Экранные цветофильтры недоступны на этой системе — драйвер видеокарты отказал в изменении гамма-рампы. Это ограничение драйвера/GPU, не PSuite; повторные попытки ничего не изменят, поэтому раздел отключён.");
        }

        private void OnColorFilterToggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _colorFilterUnsupported) return;

            _settingsStore.Current.ColorFilterEnabled = ColorFilterToggle.IsOn;
            _settingsStore.Save();

            if (ColorFilterToggle.IsOn)
                ApplyColorFilterFromSettings();
            else
                Core.ColorFilterManager.ResetToDefault();
        }

        private DispatcherTimer? _colorFilterDebounceTimer;

        private void OnColorFilterSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializing || _colorFilterUnsupported) return;

            // Just the number labels update immediately — cheap, UI-only.
            UpdateColorFilterValueTexts();

            // Everything expensive (disk write + native SetDeviceGammaRamp
            // call) is debounced: dragging a slider fires this handler on
            // every single pixel of movement, and doing a synchronous file
            // save + Win32 call on every one of those ticks was flooding
            // the UI thread badly enough to freeze the window solid
            // (reported as a white screen) during a fast drag. Only the
            // LAST value after the person pauses actually gets applied.
            _colorFilterDebounceTimer?.Stop();
            _colorFilterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _colorFilterDebounceTimer.Tick += (_, _) =>
            {
                _colorFilterDebounceTimer?.Stop();

                _settingsStore.Current.ColorFilterBrightness = (int)ColorBrightnessSlider.Value;
                _settingsStore.Current.ColorFilterContrastPercent = (int)ColorContrastSlider.Value;
                _settingsStore.Current.ColorFilterGamma = ColorGammaSlider.Value;
                _settingsStore.Save();

                if (ColorFilterToggle.IsOn)
                    ApplyColorFilterFromSettings();
            };
            _colorFilterDebounceTimer.Start();
        }

        private void OnColorFilterResetClick(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            ColorBrightnessSlider.Value = 0;
            ColorContrastSlider.Value = 100;
            ColorGammaSlider.Value = 1.0;
            _isInitializing = false;

            UpdateColorFilterValueTexts();
            _settingsStore.Current.ColorFilterBrightness = 0;
            _settingsStore.Current.ColorFilterContrastPercent = 100;
            _settingsStore.Current.ColorFilterGamma = 1.0;
            _settingsStore.Save();

            if (ColorFilterToggle.IsOn)
                ApplyColorFilterFromSettings();
        }

        // Language: WinUI resolves x:Uid-tagged strings from Strings/{lang}/Resources.resw
        // based on ApplicationLanguages.PrimaryLanguageOverride, but only
        // re-resolves them on next launch — not live. Setting the
        // override here persists automatically (it's a WinRT-managed
        // per-app setting, no need to store it ourselves), then we ask to
        // restart so the new language actually takes effect.
        private async void OnLanguageClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string langTag }) return;

            var bcp47 = langTag switch
            {
                "uk" => "uk-UA",
                "ru" => "ru-RU",
                "en" => "en-US",
                _ => "ru-RU"
            };

            if (Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride == bcp47)
                return; // already this language, nothing to do

            var dialog = new ContentDialog
            {
                Title = "Сменить язык?",
                Content = "Новый язык интерфейса вступит в силу после перезапуска PSuite. Перезапустить сейчас?",
                PrimaryButtonText = "Перезапустить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            var choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary) return;

            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = bcp47;

            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    ShowStatus("Не удалось определить путь к PSuite.exe для перезапуска. Перезапустите вручную.");
                    return;
                }

                App.ReleaseSingleInstanceMutexForRelaunch();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                // The mutex was already released above in anticipation of
                // the new process starting. If Process.Start itself
                // failed, that release needs to be undone — otherwise
                // this still-running instance has no single-instance
                // guard for the rest of its life, and a later launch
                // attempt could open a second PSuite window.
                App.ReacquireSingleInstanceMutex();
                ShowStatus($"Язык сохранён, но авто-перезапуск не удался ({ex.Message}). Перезапустите PSuite вручную.");
            }
        }

        private void OnRestorePointBeforeApplyToggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _settingsStore.Current.CreateRestorePointBeforeApply = RestorePointBeforeApplyToggle.IsOn;
            _settingsStore.Save();
        }

        private async void OnCreateRestorePointNowClick(object sender, RoutedEventArgs e)
        {
            if (!Core.SystemIntegration.IsRunningElevated())
            {
                var relaunched = await PromptRelaunchElevatedAsync("создание точки восстановления");
                if (!relaunched)
                    ShowStatus("Точка восстановления требует прав администратора. Перезапуск отменён.");
                return;
            }

            CreateRestorePointNowButton.IsEnabled = false;
            var previousContent = CreateRestorePointNowButton.Content;
            CreateRestorePointNowButton.Content = "Создаю точку восстановления...";

            try
            {
                var (success, message) = await Core.SystemIntegration.TryCreateRestorePointAsync("PSuite — ручной запуск");
                ShowStatus(message, success ? StatusKind.Success : StatusKind.Error);
            }
            finally
            {
                CreateRestorePointNowButton.Content = previousContent;
                CreateRestorePointNowButton.IsEnabled = true;
            }
        }

        private async void OnShowExperimentalToggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            _settingsStore.Current.ShowExperimentalModules = ShowExperimentalToggle.IsOn;
            _settingsStore.Save();

            ApplyExperimentalFilterButtonVisibility();

            await LoadFromModulesAsync();
            if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                ApplyFilter(tag);
        }

        // Shows/hides the "Experimental" risk-filter button (over the tweak
        // grid) based on the "Показывать Experimental твики" toggle in
        // Настройки. Must be called both from the constructor (so a saved
        // "off" setting hides it on startup) and from the toggle handler
        // (so flipping it live updates immediately) — missing either call
        // was a real bug caught before.
        private void ApplyExperimentalFilterButtonVisibility()
        {
            var showExperimental = _settingsStore.Current.ShowExperimentalModules;
            RiskFilterExperimentalButton.Visibility = showExperimental ? Visibility.Visible : Visibility.Collapsed;

            // If Experimental modules are being hidden while that filter is
            // the active one, fall back to "All" instead of leaving the
            // grid stuck showing a filter whose button just disappeared.
            if (!showExperimental && _riskFilter == "Experimental")
            {
                _riskFilter = "All";
                UpdateRiskFilterButtonStyles();
            }
        }

        // Subtle hover "lift" on tweak cards — each card gets its own
        // ScaleTransform instance from the DataTemplate (declared inline,
        // not as a shared StaticResource, so cards don't fight over one
        // transform), animated smoothly instead of snapping instantly.
        private void OnTweakCardPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border { RenderTransform: ScaleTransform scale })
                AnimateScale(scale, 1.035, 140);
        }

        private void OnTweakCardPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border { RenderTransform: ScaleTransform scale })
                AnimateScale(scale, 1.0, 140);
        }

        private static void AnimateScale(ScaleTransform target, double to, double durationMs)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var animX = new DoubleAnimation { To = to, Duration = duration, EasingFunction = easing };
            var animY = new DoubleAnimation { To = to, Duration = duration, EasingFunction = easing };
            Storyboard.SetTarget(animX, target);
            Storyboard.SetTargetProperty(animX, "ScaleX");
            Storyboard.SetTarget(animY, target);
            Storyboard.SetTargetProperty(animY, "ScaleY");

            var sb = new Storyboard();
            sb.Children.Add(animX);
            sb.Children.Add(animY);
            sb.Begin();
        }

        // Plays a short scale-pulse on the Performance Score text plus a
        // fade-in badge, but ONLY when BenchmarkHistoryStore confirms this
        // run genuinely beat the best score ever measured on this machine
        // — never a decorative animation pretending an achievement that
        // didn't happen.
        private void PlayScoreRecordAnimation()
        {
            if (BenchmarkScoreText.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform { CenterX = 0, CenterY = 0 };
                BenchmarkScoreText.RenderTransform = scale;
            }

            var duration = new Duration(TimeSpan.FromMilliseconds(240));
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

            var pulseX = new DoubleAnimation { From = 1.0, To = 1.14, Duration = duration, AutoReverse = true, EasingFunction = easing };
            var pulseY = new DoubleAnimation { From = 1.0, To = 1.14, Duration = duration, AutoReverse = true, EasingFunction = easing };
            Storyboard.SetTarget(pulseX, scale);
            Storyboard.SetTargetProperty(pulseX, "ScaleX");
            Storyboard.SetTarget(pulseY, scale);
            Storyboard.SetTargetProperty(pulseY, "ScaleY");

            var sb = new Storyboard();
            sb.Children.Add(pulseX);
            sb.Children.Add(pulseY);
            sb.Begin();

            BenchmarkRecordBadge.Opacity = 0;
            BenchmarkRecordBadge.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(350)) };
            Storyboard.SetTarget(fadeIn, BenchmarkRecordBadge);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var badgeSb = new Storyboard();
            badgeSb.Children.Add(fadeIn);
            badgeSb.Begin();
        }

        private void OnOpenModulesFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_modulesRoot);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_modulesRoot}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowStatus($"Не удалось открыть папку модулей: {ex.Message}");
            }
        }

        private async void OnImportFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FolderPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                if (folder == null)
                {
                    // This used to return with zero feedback — which is
                    // indistinguishable from "the import silently failed"
                    // if the picker ever returns null for a reason other
                    // than a deliberate cancel (it has happened in some
                    // packaged-app window states). Now it's explicit.
                    ShowStatus("Импорт из папки отменён (папка не выбрана).", StatusKind.Info, autoHideSeconds: 3);
                    return;
                }

                var error = _moduleManager.TryImportFolder(folder.Path);
                if (error != null)
                {
                    ShowStatus($"Импорт не удался ({folder.Path}): {error}");
                }
                else
                {
                    ShowStatus($"Импортировано из '{folder.Path}'.", StatusKind.Success);
                    await LoadFromModulesAsync();
                    if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                        ApplyFilter(tag);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Ошибка импорта: {ex.Message}");
            }
        }

        private async void OnImportZipClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add(".zip");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                var error = _moduleManager.TryImportZip(file.Path);
                if (error != null)
                {
                    ShowStatus($"Импорт не удался: {error}");
                }
                else
                {
                    ShowStatus($"Импортировано из '{file.Name}'.", StatusKind.Success);
                    await LoadFromModulesAsync();
                    if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                        ApplyFilter(tag);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Ошибка импорта: {ex.Message}");
            }
        }

        private void OnCopyPromptClick(object sender, RoutedEventArgs e)
        {
            const string prompt = """
Ты — официальный архитектор модулей PSuite (Script Engine V6).
Твоя задача — не просто сгенерировать модуль, а создать полностью
готовый, проверенный и безопасный модуль, без заглушек.

СТРУКТУРА: один твик = одна папка = 4 файла:
manifest.json, detect.*, apply.*, rollback.*

ФОРМАТ СКРИПТОВ:
Script Engine сам определяет Runner по расширению файла (BAT Runner,
CMD Runner, Registry Runner, PowerShell Runner — в этом порядке приоритета).
- Основной формат — BAT/CMD. Пиши на нём по умолчанию.
- .reg поддерживается напрямую (Registry Runner делает reg import) —
  но если apply.reg меняет что-то без предварительного сохранения
  старых значений, rollback всё равно обязан работать через CapturePath
  (например rollback.bat, который читает CapturePath и делает reg add
  с восстановленными значениями) — просто "rollback.reg с дефолтами"
  не годится.
- PowerShell — только если BAT/CMD/REG реально недостаточно.
- CapturePath передаётся ПЕРВЫМ ПОЗИЦИОННЫМ аргументом: %1 в .bat,
  $args[0] в .ps1.

ЕСЛИ ПОЛЬЗОВАТЕЛЬ ДАЁТ BAT / CMD / REG / PS1 / старый модуль PSuite:
1. полностью проанализируй, что он меняет (Registry / BCD / Services /
   Scheduled Tasks / Powercfg / Netsh / DISM / Files / Environment /
   Drivers / Windows Features — определи сам, не гадай);
2. построй логику apply на основе этого анализа;
3. построй логику detect (как реально проверить, применён ли твик);
4. построй логику rollback (как реально вернуть именно то, что было);
5. не теряй и не упрощай существующую логику исходного BAT/REG —
   переноси её целиком в новый формат, не переписывай "по памяти".
Если старый модуль PSuite дан на модернизацию — не копируй его старые
ошибки, но и не пиши модуль с нуля мимо того, что он реально делал.

=== manifest.json ===
{
  "id": "kebab-case-unique-id",
  "name": "Человекочитаемое название на русском",
  "description": "1-2 предложения на русском, что делает твик",
  "author": "имя автора",
  "version": "1.0.0",
  "category": "System | Games | Network | Storage | Power | Other",
  "risk": "Safe | Advanced | Experimental",
  "requiresAdmin": true или false,
  "supportsRollback": true (почти всегда),
  "requiresRestart": true или false,
  "engine": "Script",
  "entry": { "detect": "detect.bat", "apply": "apply.bat", "rollback": "rollback.bat" },
  "compatibility": {
    "supportedWindows": [{ "min": "10.0.17763", "max": null }],
    "notes": "определи по факту анализа твика, не ставь \"Windows 10+\" не проверив"
  },
  "knownSideEffects": ["честный список последствий — не скрывай негативные"],
  "expectedEffect": "консервативная формулировка, БЕЗ конкретных цифр вроде \"+20 FPS\"/\"-50ms\" — таких обещаний не давать"
}
Все поля заполнены честно, без фейковых данных. Category — на английском
(как в списке выше). "risk":"Blocked" использовать НЕЛЬЗЯ — этот статус
назначает только само приложение.

=== detect.bat / detect.ps1 ===
НИЧЕГО не меняет в системе — только читает текущее состояние.
Последняя строка stdout — ровно один JSON-объект:
{"success":true,"state":"Applied","details":"..."}
state: Applied | NotApplied | Partial | Unknown

=== apply.bat / apply.ps1 ===
Перед ЛЮБЫМ изменением системы, в этом порядке:
1. считать текущие (старые) значения;
2. записать их в файл по пути CapturePath (%1 / $args[0]);
3. только после этого вносить реальное изменение.
Последняя строка stdout:
{"success":true,"requiresRestart":false,"details":"..."}

=== rollback.bat / rollback.ps1 ===
Rollback ОБЯЗАТЕЛЕН. Он читает CapturePath и возвращает ИМЕННО то
состояние, которое там сохранил apply — никогда не "дефолты Windows по
памяти", если они не были реально считаны до применения. Если
автоматический rollback построить невозможно — не выдумывай его,
а объясни в ответе (текстом, вне модуля), почему это невозможно.
Последняя строка stdout:
{"success":true,"details":"..."}

КРИТИЧНО — САМАЯ ЧАСТАЯ ПРИЧИНА ОТКАЗА "Скрипт не вернул вывод":
Движок читает ПОСЛЕДНЮЮ непустую строку stdout как JSON. Если её нет —
твик не работает, даже если весь остальной .bat написан правильно.
Рабочий минимальный пример (именно так, без вариаций):

    @echo off
    setlocal
    rem ... логика твика ...
    echo {"success":true,"state":"Applied","details":"OK"}
    endlocal
    exit /b 0

Из-за этого регулярно ломается:
- Любой "pause" или "set /p" — движок не даёт stdin, скрипт зависает
  до таймаута и падает без вывода. НИКОГДА не используй их.
- "exit /b 1" (или любой ранний exit/goto :eof) ДО строки с echo JSON —
  движок видит пустой stdout и код возврата ≠0, даже если сам твик
  применился. echo с JSON должен быть последней командой перед выходом
  на КАЖДОМ пути выполнения скрипта (успех и ошибка — оба варианта
  должны сами напечатать JSON перед exit, не просто вернуть код).
  Пример ветки ошибки:
    echo {"success":false,"details":"причина"}
    exit /b 0
  (exit /b оставь 0, если хочешь чтобы движок прочитал именно JSON, а
  не трактовал это как "скрипт упал с кодом X" — состояние успеха/
  неудачи операции передавай через поле "success" в самом JSON).
- "> nul" или "2>&1 > log.txt" на всю команду — перенаправляет и глотает
  echo с JSON тоже, если поставлен неаккуратно в конце файла.
- Пустая строка/дополнительный echo/comment ПОСЛЕ строки с JSON — движок
  берёт именно последнюю НЕПУСТУЮ строку, лишний вывод после JSON не
  страшен, но лучше не рисковать и ничего не печатать после него.
- В PowerShell аналогично: последняя строка, попадающая в stdout, должна
  быть Write-Output с валидным JSON — не Write-Host (Write-Host обычно
  тоже долетает до консоли, но безопаснее всегда Write-Output).

ЖЁСТКО ЗАПРЕЩЕНО (это ошибка, а не стиль):
TODO / FIXME / placeholder / пустой detect / пустой rollback /
"echo Done" или "echo Success" без реального выполнения / выдуманные
проверки, registry-ключи, службы или rollback / смена системы в detect /
дефолты Windows вместо реального rollback / потеря логики исходного
BAT / файлы в entry, которых не существует.

ПРАВИЛА КОДА:
- Никаких интерактивных запросов (Read-Host, choice без /n, диалоги).
- Не проверяй/не запрашивай права администратора внутри скрипта — это
  делает само приложение по полю requiresAdmin.
- BAT — читаемый, структурированный, с проверкой ошибок, без дублирования
  и мусора. Только cmd-builtin и стандартные системные утилиты (reg, sc,
  powercfg, netsh, bcdedit, schtasks, dism и т.п.).
- Не используй PowerShell-модули, которых может не быть в системе.

ВНУТРЕННИЙ АНАЛИЗ ПЕРЕД ГЕНЕРАЦИЕЙ (сделай для себя, не показывай):
что меняется · как это проверить · как это вернуть · нужен ли restart ·
нужен ли admin · какие есть риски.

САМОПРОВЕРКА ПЕРЕД ВЫДАЧЕЙ РЕЗУЛЬТАТА:
✓ manifest валиден, все поля честны
✓ detect реально работает и ничего не меняет
✓ apply реально работает и пишет CapturePath ДО изменений
✓ rollback реально читает CapturePath и восстанавливает именно старое состояние
✓ JSON в конце каждого скрипта валиден
✓ entry указывает только на реально существующие файлы
✓ нет TODO/FIXME/placeholder/заглушек
✓ модуль готов к использованию без ручных правок
Если хоть один пункт не выполнен — не показывай результат, исправь.

Твик (или старый модуль/BAT/REG/PS1 на переделку), с которым нужно работать:
""";

            try
            {
                var package = new DataPackage();
                package.SetText(prompt);
                Clipboard.SetContent(package);
                ShowStatus("Промт скопирован в буфер обмена.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                ShowStatus($"Не удалось скопировать: {ex.Message}");
            }
        }

        private void OnAccentColorClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string hex) return;
            SetAccentColor(hex);
        }

        private void OnAccentColorPickerChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            var c = args.NewColor;
            SetAccentColor($"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
        }

        private void SetAccentColor(string hex)
        {
            _settingsStore.Current.AccentColor = hex;
            _settingsStore.Save();
            ApplyAccentColor(hex);
        }

        // The whole UI references PSuiteAccentTealBrush / PSuiteAccentTealLightBrush
        // as shared StaticResource instances — mutating their .Color here
        // propagates everywhere that already uses them, with no need to
        // touch every individual control in XAML. The "light" variant is
        // derived algorithmically (blended toward white) so ANY chosen
        // colour — preset or from the picker — gets a sensible hover/
        // highlight tint, not just the three hardcoded presets.
        private void ApplyAccentColor(string hex)
        {
            var main = ColorFromHex(hex);
            var light = Lighten(main, 0.32);

            if (Application.Current.Resources["PSuiteAccentTealBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush mainBrush)
                mainBrush.Color = main;

            if (Application.Current.Resources["PSuiteAccentTealLightBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush lightBrush)
                lightBrush.Color = light;

            if (AccentColorPicker != null)
                AccentColorPicker.Color = main;
        }

        private static Windows.UI.Color Lighten(Windows.UI.Color c, double amount)
        {
            byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
            return Windows.UI.Color.FromArgb(255, Blend(c.R), Blend(c.G), Blend(c.B));
        }

        // Defensive on purpose: an old build of PSuite stored AccentColor
        // as a plain name ("Yellow") instead of a hex string. Anything
        // that doesn't parse cleanly falls back to the default green
        // rather than crashing the app on startup.
        private static Windows.UI.Color ColorFromHex(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length != 6 && hex.Length != 8)
                    return DefaultAccentColor;

                var offset = hex.Length == 8 ? 2 : 0; // skip alpha if present
                return Windows.UI.Color.FromArgb(
                    255,
                    byte.Parse(hex.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex.Substring(offset + 2, 2), System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex.Substring(offset + 4, 2), System.Globalization.NumberStyles.HexNumber));
            }
            catch
            {
                return DefaultAccentColor;
            }
        }

        private static readonly Windows.UI.Color DefaultAccentColor = Windows.UI.Color.FromArgb(255, 0x1D, 0x9E, 0x75);

        // Turns a raw measured result (+ optional previous run) into a
        // card view model: value text, delta with noise-detection, and
        // the hover explanation. This is the piece that was missing —
        // BenchmarkResultsList's ItemsSource used to call this but the
        // method itself had never actually been written.
        private Models.BenchmarkResultCardView BuildBenchmarkResultCard(Core.BenchmarkResult result, Core.BenchmarkResult? previous)
        {
            var card = new Models.BenchmarkResultCardView
            {
                Title = result.Name,
                ValueText = result.Score == 0 ? result.Unit : $"{result.Score} {result.Unit}",
                Tooltip = GetBenchmarkHint(result.Name)
            };

            if (previous != null && previous.Score != 0 && result.Score != 0)
            {
                var rawDeltaPercent = (result.Score - previous.Score) / previous.Score * 100.0;
                var isBetter = Core.BenchmarkRunner.IsHigherBetter(result.Name) ? rawDeltaPercent > 0 : rawDeltaPercent < 0;
                var arrow = Math.Abs(rawDeltaPercent) < 0.5 ? "≈" : (isBetter ? "▲" : "▼");

                // A single-shot disk/GPU test in particular can swing many
                // times over between runs because of AV scans, other I/O,
                // or thermal throttling — that's noise, not a real result
                // of anything the person did. A plain percentage only ever
                // catches big INCREASES (a drop is capped at -100%), so use
                // the ratio between the two numbers instead — symmetric,
                // catches "10x slower" just as well as "10x faster".
                var higher = Math.Max(Math.Abs(result.Score), Math.Abs(previous.Score));
                var lower = Math.Min(Math.Abs(result.Score), Math.Abs(previous.Score));
                var isImplausible = lower > 0 && higher / lower >= 2.5;

                card.DeltaText = isImplausible ? $"{arrow} шум измерения" : $"{arrow} {Math.Abs(rawDeltaPercent):0.#}%";

                var brushKey = isImplausible || Math.Abs(rawDeltaPercent) < 0.5
                    ? "PSuiteTextMutedBrush"
                    : (isBetter ? "PSuiteAccentTealLightBrush" : "PSuiteStatusBlockedFgBrush");
                card.DeltaBrush = Application.Current.Resources.TryGetValue(brushKey, out var brush)
                    ? (Microsoft.UI.Xaml.Media.Brush)brush
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextSecondaryBrush"];
            }
            else
            {
                card.DeltaText = previous == null ? "первый замер" : "";
                card.DeltaBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextMutedBrush"];
            }

            return card;
        }

        // Same masonry approach as Analysis, and for the same reason:
        // ItemsWrapGrid would clip a card whose title wraps to 2 lines
        // while its neighbours are 1 line. Manual columns, balanced by
        // line count, size to their own content instead.
        private void DistributeBenchmarkCards(List<Models.BenchmarkResultCardView> cards)
        {
            var columns = new[] { BenchmarkColumn0, BenchmarkColumn1, BenchmarkColumn2 };
            var lineCounts = new int[columns.Length];

            columns[0].Children.Clear();
            columns[1].Children.Clear();
            columns[2].Children.Clear();

            foreach (var card in cards)
            {
                var shortest = 0;
                for (int i = 1; i < columns.Length; i++)
                    if (lineCounts[i] < lineCounts[shortest]) shortest = i;

                columns[shortest].Children.Add(BuildBenchmarkCardElement(card));
                // Title can wrap to 2 lines for longer names — weight it
                // a little heavier than a guaranteed-single-line value/delta.
                lineCounts[shortest] += 4;
            }
        }

        private Border BuildBenchmarkCardElement(Models.BenchmarkResultCardView card)
        {
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = card.Title,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextPrimaryBrush"]
            });

            // The delta % is the number a person actually understands at a
            // glance — "14.9 million ops/sec" means nothing to most people,
            // "▼3.9%" does. So the % leads, big and coloured; the raw
            // measured value is still there underneath for anyone who
            // wants it, just smaller.
            var hasDelta = !string.IsNullOrEmpty(card.DeltaText);
            stack.Children.Add(new TextBlock
            {
                Text = hasDelta ? card.DeltaText : card.ValueText,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = hasDelta
                    ? card.DeltaBrush
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteAccentTealLightBrush"]
            });

            if (hasDelta)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = card.ValueText,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextSecondaryBrush"]
                });
            }

            // Mini tip: a couple of words, not a paragraph. The full
            // explanation still lives in the tooltip (hover to read it).
            stack.Children.Add(new TextBlock
            {
                Text = Core.BenchmarkRunner.IsHigherBetter(card.Title) ? "выше = лучше" : "ниже = лучше",
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextMutedBrush"]
            });

            var border = new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteBackgroundBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteBorderBrush"],
                BorderThickness = new Thickness(0.5),
                Child = stack
            };

            if (!string.IsNullOrEmpty(card.Tooltip))
                ToolTipService.SetToolTip(border, card.Tooltip);

            return border;
        }

        private void OnResetBenchmarkBaselineClick(object sender, RoutedEventArgs e)
        {
            Core.BenchmarkHistoryStore.ResetBaseline();
            Core.BenchmarkHistoryStore.ResetBestScore();
            ShowStatus("Базовый замер сброшен. Следующий запуск бенчмарка станет новой точкой отсчёта (1000).", StatusKind.Info);
        }

        private async void OnExportLogsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                savePicker.SuggestedFileName = $"psuite-logs-{DateTime.Now:yyyyMMdd-HHmm}";
                savePicker.FileTypeChoices.Add("Text file", new List<string> { ".txt" });

                var file = await savePicker.PickSaveFileAsync();
                if (file == null) return;

                var entries = _logStore.ReadRecent(int.MaxValue);

                if (entries.Count == 0)
                {
                    ShowStatus("Лог-файл ещё пуст — нечего экспортировать.");
                    return;
                }

                var lines = new List<string>
                {
                    "PSuite — журнал операций",
                    $"Экспортировано: {DateTime.Now:dd.MM.yyyy HH:mm}",
                    new string('-', 60)
                };

                foreach (var entry in entries)
                {
                    var action = entry.Action == "Apply" ? "Применить" : "Rollback";
                    var status = entry.Success ? "успешно" : $"ОШИБКА: {entry.Details}";
                    lines.Add($"{entry.TimestampUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} | {entry.ModuleName} | {action} | {status}");
                }

                await File.WriteAllLinesAsync(file.Path, lines, Encoding.UTF8);
                ShowStatus($"Логи сохранены: {file.Path}", StatusKind.Success);
            }
            catch (Exception ex)
            {
                ShowStatus($"Не удалось экспортировать логи: {ex.Message}");
            }
        }

        private async void OnTweakActionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TweakItem tweak)
                return;

            if (!_modulesById.TryGetValue(tweak.Id, out var module))
            {
                ShowStatus($"Модуль '{tweak.Id}' не найден на диске.");
                return;
            }

            tweak.IsBusy = true;

            try
            {
                if (!tweak.IsApplied)
                {
                    if (module.Manifest.RequiresAdmin && !Core.SystemIntegration.IsRunningElevated())
                    {
                        var relaunched = await PromptRelaunchElevatedAsync(module.Manifest.Name);
                        if (!relaunched)
                            ShowStatus($"'{module.Manifest.Name}' требует прав администратора. Перезапуск отменён.");
                        return;
                    }

                    var capturePath = _stateStore.GetCapturePath(module.Manifest.Id);

                    if (_settingsStore.Current.CreateRestorePointBeforeApply)
                    {
                        var (rpSuccess, rpMessage) = await Core.SystemIntegration.TryCreateRestorePointAsync(
                            $"PSuite — перед применением '{module.Manifest.Name}'");
                        if (!rpSuccess)
                        {
                            // Best-effort: warn, but don't block the apply
                            // the person actually asked for over a restore
                            // point that's a safety net, not the goal.
                            ShowStatus($"Точка восстановления не создана ({rpMessage}). Продолжаю применение твика...");
                        }
                    }

                    var result = await ModuleLoader.ApplyAsync(module, capturePath);

                    if (result.Success)
                    {
                        _stateStore.RecordApplied(module.Manifest.Id, module.Manifest.Version, capturePath);
                        tweak.IsApplied = true;
                        ShowStatus($"'{module.Manifest.Name}' применён.", StatusKind.Success, autoHideSeconds: 3);

                        _logStore.Append(new LogEntry
                        {
                            TimestampUtc = DateTime.UtcNow,
                            ModuleId = module.Manifest.Id,
                            ModuleName = module.Manifest.Name,
                            Action = "Apply",
                            Success = true
                        });

                        // V5: after a successful Apply, run Validate if the
                        // module declares one. Diagnostic only — a failed
                        // or unsupported Validate does NOT roll back the
                        // apply that just succeeded; it's logged so the
                        // person can see the module thinks something's off.
                        var verifyTask = ModuleLoader.VerifyAsync(module);
                        if (verifyTask != null)
                        {
                            var verifyResult = await verifyTask;
                            if (!verifyResult.Success || verifyResult.State != ModuleState.Applied)
                            {
                                _logStore.Append(new LogEntry
                                {
                                    TimestampUtc = DateTime.UtcNow,
                                    ModuleId = module.Manifest.Id,
                                    ModuleName = module.Manifest.Name,
                                    Action = "Validate",
                                    Success = false,
                                    Details = verifyResult.Error ?? $"Validate вернул состояние '{verifyResult.State}' вместо Applied."
                                });
                            }
                        }
                    }
                    else
                    {
                        _stateStore.RecordError(module.Manifest.Id, result.Error ?? "Неизвестная ошибка применения.");
                        ShowStatus($"Не удалось применить '{module.Manifest.Name}': {result.Error}");

                        _logStore.Append(new LogEntry
                        {
                            TimestampUtc = DateTime.UtcNow,
                            ModuleId = module.Manifest.Id,
                            ModuleName = module.Manifest.Name,
                            Action = "Apply",
                            Success = false,
                            Details = result.Error
                        });
                    }
                }
                else
                {
                    if (module.Manifest.RequiresAdmin && !Core.SystemIntegration.IsRunningElevated())
                    {
                        var relaunched = await PromptRelaunchElevatedAsync(module.Manifest.Name);
                        if (!relaunched)
                            ShowStatus($"Откат '{module.Manifest.Name}' требует прав администратора. Перезапуск отменён.");
                        return;
                    }

                    var capturePath = _stateStore.GetCapturePath(module.Manifest.Id);
                    var result = await ModuleLoader.RollbackAsync(module, capturePath);

                    if (result.Success)
                    {
                        _stateStore.RecordRolledBack(module.Manifest.Id);
                        tweak.IsApplied = false;
                        ShowStatus($"'{module.Manifest.Name}' откачен.", StatusKind.Success, autoHideSeconds: 3);

                        _logStore.Append(new LogEntry
                        {
                            TimestampUtc = DateTime.UtcNow,
                            ModuleId = module.Manifest.Id,
                            ModuleName = module.Manifest.Name,
                            Action = "Rollback",
                            Success = true
                        });
                    }
                    else
                    {
                        _stateStore.RecordError(module.Manifest.Id, result.Error ?? "Неизвестная ошибка отката.");
                        ShowStatus($"Не удалось откатить '{module.Manifest.Name}': {result.Error}");

                        _logStore.Append(new LogEntry
                        {
                            TimestampUtc = DateTime.UtcNow,
                            ModuleId = module.Manifest.Id,
                            ModuleName = module.Manifest.Name,
                            Action = "Rollback",
                            Success = false,
                            Details = result.Error
                        });
                    }
                }
            }
            finally
            {
                tweak.IsBusy = false;

                if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                    ApplyFilter(tag);
            }
        }

        private int _refreshClickCount = 0;

        private void OnRiskFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string risk) return;

            _riskFilter = risk;
            UpdateRiskFilterButtonStyles();
            ApplyFilter(_currentTag);
        }

        private void UpdateRiskFilterButtonStyles()
        {
            var buttons = new[]
            {
                RiskFilterAllButton, RiskFilterSafeButton, RiskFilterAdvancedButton,
                RiskFilterExperimentalButton, RiskFilterBlockedButton
            };

            var activeBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteAccentTealBrush"];

            foreach (var button in buttons)
            {
                var isActive = button.Tag is string risk && risk == _riskFilter;
                if (isActive)
                    button.Background = activeBrush;
                else
                    button.ClearValue(Button.BackgroundProperty);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadFromModulesAsync();
            if (CategoryList.SelectedItem is ListViewItem item && item.Tag is string tag)
                ApplyFilter(tag);

            // Easter egg: nothing more is going to appear from mashing
            // this button — but if someone does it 15 times anyway, they
            // deserve to know that.
            _refreshClickCount++;
            if (_refreshClickCount % 15 == 0)
                ShowStatus("🔄 Новых твиков от этого не появится, честно.");
        }

        private async Task PopulateSystemInfoAsync()
        {
            SystemInfoLoadingText.Visibility = Visibility.Visible;
            AnalysisCardsGrid.Visibility = Visibility.Collapsed;

            try
            {
                // SystemAnalyzer/WindowsInspector are cheap (registry only);
                // SecurityAnalyzer shells out to bcdedit/manage-bde, so it's
                // the only one that actually needs to run off the UI thread.
                var hardware = Core.SystemAnalyzer.Analyze();
                var windows = Core.WindowsInspector.Inspect();
                var security = await Core.SecurityAnalyzer.AnalyzeAsync();

                SecurityScoreText.Text = $"Security Score: {security.SecurityScore}/100";

                var cards = new List<AnalysisCardView>();

                var cpuCard = new AnalysisCardView { Title = "CPU" };
                cpuCard.Lines.AddLine(hardware.CpuName);
                cpuCard.Lines.AddLine($"{hardware.LogicalProcessors} потоков" +
                    (hardware.PhysicalCores > 0 ? $" ({hardware.PhysicalCores} ядер)" : "") +
                    (hardware.CpuClockMhz > 0 ? $" · ~{hardware.CpuClockMhz} МГц (базовая)" : ""));
                if (hardware.L2CacheKb > 0 || hardware.L3CacheKb > 0)
                    cpuCard.Lines.AddLine(
                        (hardware.L2CacheKb > 0 ? $"L2: {hardware.L2CacheKb / 1024.0:0.#} МБ  " : "") +
                        (hardware.L3CacheKb > 0 ? $"L3: {hardware.L3CacheKb / 1024.0:0.#} МБ" : ""));
                cards.Add(cpuCard);

                var ramCard = new AnalysisCardView { Title = "Память" };
                ramCard.Lines.AddLine($"Всего: {hardware.TotalRamGb:0.#} ГБ (свободно {hardware.AvailableRamGb:0.#} ГБ)");
                if (hardware.RamModules.Count > 0)
                    foreach (var m in hardware.RamModules)
                        ramCard.Lines.AddLine($"{m.CapacityGb:0} ГБ {(m.SpeedMhz > 0 ? $"{m.SpeedMhz} МГц " : "")}{m.Manufacturer}".Trim());
                else
                    ramCard.Lines.AddLine("Планки: не удалось определить");
                cards.Add(ramCard);

                var gpuCard = new AnalysisCardView { Title = "GPU" };
                if (hardware.GpuNames.Count > 0)
                    foreach (var name in hardware.GpuNames)
                        gpuCard.Lines.AddLine(name);
                else
                    gpuCard.Lines.AddLine("Не удалось определить");
                if (hardware.GpuDriverVersions.Count > 0)
                    gpuCard.Lines.AddLine("Драйвер: " + string.Join(", ", hardware.GpuDriverVersions));
                if (hardware.GpuVramGb.Count > 0)
                    foreach (var vram in hardware.GpuVramGb)
                        gpuCard.Lines.AddLine($"VRAM: {vram:0.#} ГБ");
                cards.Add(gpuCard);

                var diskCard = new AnalysisCardView { Title = "Диски" };
                if (hardware.Disks.Count > 0)
                    foreach (var d in hardware.Disks)
                        diskCard.Lines.AddLine($"{d.Name}: {d.FreeGb:0}/{d.TotalGb:0} ГБ свободно");
                else
                    diskCard.Lines.AddLine("Не удалось определить");
                if (hardware.PhysicalDiskTypes.Count > 0)
                    foreach (var t in hardware.PhysicalDiskTypes)
                        diskCard.Lines.AddLine(t);
                cards.Add(diskCard);

                var boardCard = new AnalysisCardView { Title = "Материнская плата" };
                boardCard.Lines.AddLine(hardware.MotherboardInfo);
                boardCard.Lines.AddLine($"BIOS: {hardware.BiosInfo}");
                cards.Add(boardCard);

                var windowsCard = new AnalysisCardView { Title = "Windows" };
                windowsCard.Lines.AddLine($"{windows.Edition}");
                windowsCard.Lines.AddLine($"Build {hardware.WindowsBuild} · {windows.BootMode}");
                windowsCard.Lines.AddLine($"Активация: {hardware.ActivationStatus}");
                windowsCard.Lines.AddLine($"Аптайм: {FormatUptime(hardware.Uptime)}");
                windowsCard.Lines.AddLine(hardware.WindowsInstallDate.HasValue
                    ? $"Установлена: {hardware.WindowsInstallDate.Value:d MMMM yyyy}"
                    : "Установлена: неизвестно");
                cards.Add(windowsCard);

                var systemCard = new AnalysisCardView { Title = "Система" };
                systemCard.Lines.AddLine($"Процессов запущено: {hardware.RunningProcessCount}");
                systemCard.Lines.AddLine($"Рантайм: {hardware.DotNetRuntimeVersion}");
                systemCard.Lines.AddLine(hardware.PageFileSizeMb.HasValue
                    ? $"Файл подкачки: {hardware.PageFileSizeMb} МБ ({hardware.PageFileLocation})"
                    : $"Файл подкачки: {hardware.PageFileLocation}");
                cards.Add(systemCard);

                var updatesCard = new AnalysisCardView { Title = "Обновления и защита" };
                updatesCard.Lines.AddLine($"Антивирус: {hardware.AntivirusProductName}",
                    hardware.AntivirusProductName == "Не определён" ? AnalysisLineStatus.Warning : AnalysisLineStatus.Neutral,
                    "Активные антивирусы по данным Windows Security Center.");
                updatesCard.Lines.AddLine($"Виртуализация в BIOS (VT-x/AMD-V): {FormatFlag(hardware.VirtualizationFirmwareEnabled)}",
                    AnalysisLineStatus.Neutral,
                    "Нужна для WSL2, Hyper-V, VirtualBox/VMware и Android-эмуляторов. Включается в настройках BIOS/UEFI, не в Windows.");
                updatesCard.Lines.AddLine(hardware.LastWindowsUpdateDate.HasValue
                    ? $"Последнее обновление: {hardware.LastWindowsUpdateDate.Value:d MMMM yyyy} · всего записей: {hardware.RecentWindowsUpdateCount}"
                    : $"Обновления: не удалось определить (записей: {hardware.RecentWindowsUpdateCount})");
                cards.Add(updatesCard);

                if (hardware.Displays.Count > 0)
                {
                    var displaysCard = new AnalysisCardView { Title = "Экраны" };
                    var i = 1;
                    foreach (var d in hardware.Displays)
                    {
                        var hz = d.RefreshHz > 0 ? $" · {d.RefreshHz:0} Гц" : "";
                        displaysCard.Lines.AddLine($"Экран {i}: {d.WidthPx}×{d.HeightPx}{hz}");
                        i++;
                    }
                    cards.Add(displaysCard);
                }

                if (hardware.UsbDevices.Count > 0)
                {
                    var usbCard = new AnalysisCardView { Title = $"USB-устройства ({hardware.UsbDevices.Count})" };
                    foreach (var device in hardware.UsbDevices.Take(8))
                        usbCard.Lines.AddLine(device);
                    if (hardware.UsbDevices.Count > 8)
                        usbCard.Lines.AddLine($"...и ещё {hardware.UsbDevices.Count - 8}");
                    cards.Add(usbCard);
                }

                var featuresCard = new AnalysisCardView { Title = "Права и функции" };
                featuresCard.Lines.AddLine($"Администратор: {(windows.IsRunningAsAdministrator ? "да" : "нет")} · UAC: {FormatFlag(windows.UacEnabled)}",
                    StatusFromFlag(windows.UacEnabled),
                    "Контроль учётных записей — спрашивает подтверждение перед действиями, требующими прав администратора. Отключать не рекомендуется даже ради удобства.");
                featuresCard.Lines.AddLine($"Fast Startup: {FormatFlag(windows.FastStartupEnabled)}",
                    AnalysisLineStatus.Neutral,
                    "Ускоряет включение ПК за счёт гибридного сна ядра Windows. Иногда мешает полной перезагрузке драйверов/BIOS-настроек — тогда его стоит выключить.");
                featuresCard.Lines.AddLine($"HAGS: {FormatFlag(windows.HagsEnabled)}",
                    AnalysisLineStatus.Neutral,
                    "Hardware-Accelerated GPU Scheduling — планирование GPU на уровне видеокарты вместо CPU. По-разному влияет на разные игры и видеокарты — нет универсально «правильного» значения.");
                featuresCard.Lines.AddLine($"Игровой режим: {FormatFlag(windows.GameModeEnabled)}");
                featuresCard.Lines.AddLine($"Hyper-V: {FormatFlag(windows.HyperVInstalled)}",
                    AnalysisLineStatus.Neutral,
                    "Встроенная виртуализация Windows. Конфликтует с VMware/VirtualBox — если используешь их, Hyper-V обычно стоит выключить.");
                var firewallStatus = (windows.FirewallDomainEnabled == false || windows.FirewallPrivateEnabled == false || windows.FirewallPublicEnabled == false)
                    ? AnalysisLineStatus.Warning
                    : (windows.FirewallDomainEnabled == null ? AnalysisLineStatus.Unknown : AnalysisLineStatus.Ok);
                featuresCard.Lines.AddLine($"Файрвол: домен {FormatFlag(windows.FirewallDomainEnabled)}, частная {FormatFlag(windows.FirewallPrivateEnabled)}, публичная {FormatFlag(windows.FirewallPublicEnabled)}",
                    firewallStatus,
                    "Брандмауэр Windows по трём профилям сети. Отключение публичного профиля особенно рискованно вне доверенных сетей (кафе, аэропорты).");
                cards.Add(featuresCard);

                var securityCard = new AnalysisCardView { Title = "Безопасность" };
                securityCard.Lines.AddLine($"Secure Boot: {FormatFlag(security.SecureBootEnabled)}",
                    StatusFromFlag(security.SecureBootEnabled),
                    "Проверяет, что при загрузке ПК запускается только доверенный, неизменённый код Windows — защита от буткитов/руткитов.");
                securityCard.Lines.AddLine($"VBS: {FormatFlag(security.VbsEnabled)}",
                    StatusFromFlag(security.VbsEnabled),
                    "Virtualization-Based Security — изолирует ключевые части Windows в защищённой виртуальной среде, недоступной даже вредоносному коду с правами администратора.");
                securityCard.Lines.AddLine($"Memory Integrity (HVCI): {FormatFlag(security.HvciEnabled)}",
                    StatusFromFlag(security.HvciEnabled),
                    "Проверяет, что все драйверы ядра подписаны и не изменены — блокирует один из самых опасных классов вредоносного ПО. Иногда снижает производительность в играх (поэтому часть твиков в PSuite предлагает его выключить).");
                securityCard.Lines.AddLine($"TPM: {FormatFlag(security.TpmPresent)}",
                    StatusFromFlag(security.TpmPresent),
                    "Trusted Platform Module — отдельный чип для хранения ключей шифрования. Обязателен для Windows 11 и BitLocker.");
                securityCard.Lines.AddLine($"BitLocker: {security.BitLockerStatus}");
                securityCard.Lines.AddLine($"Defender: {FormatFlag(security.DefenderRealtimeProtectionEnabled)}",
                    StatusFromFlag(security.DefenderRealtimeProtectionEnabled),
                    "Защита в реальном времени Windows Defender. Если выключена сторонним антивирусом — это нормально, посмотри поле «Антивирус» в карточке «Обновления и защита».");
                securityCard.Lines.AddLine(
                    $"Test Mode: {FormatFlag(security.TestModeEnabled)} · Kernel Debug: {FormatFlag(security.KernelDebugEnabled)}",
                    StatusFromFlag(security.TestModeEnabled | security.KernelDebugEnabled, trueIsGood: false),
                    "Test Mode позволяет запускать неподписанные драйверы (часто включается модами для игр/железа) — ослабляет защиту ядра. Обычно должно быть выключено, если ты не занимаешься разработкой драйверов.");
                cards.Add(securityCard);

                var powerPlan = await Core.SystemAnalyzer.ReadActivePowerPlanAsync();
                var powerCard = new AnalysisCardView { Title = "Питание" };
                powerCard.Lines.AddLine($"План: {powerPlan}");
                if (hardware.HasBattery)
                {
                    var chargeState = hardware.BatteryCharging == true ? "заряжается" : "от батареи";
                    powerCard.Lines.AddLine($"Батарея: {hardware.BatteryPercent}% ({chargeState})");
                }
                cards.Add(powerCard);

                if (hardware.NetworkAdapters.Count > 0)
                {
                    var networkCard = new AnalysisCardView { Title = "Сеть" };
                    foreach (var adapter in hardware.NetworkAdapters)
                    {
                        var speed = adapter.SpeedMbps > 0 ? $" · {adapter.SpeedMbps:0} Мбит/с" : "";
                        var status = adapter.IsUp ? "активен" : "неактивен";
                        networkCard.Lines.AddLine($"{adapter.Name} ({status}{speed})");
                    }
                    cards.Add(networkCard);
                }

                featuresCard.Lines.AddLine($"В автозагрузке: {hardware.StartupAppCount} программ");

                _lastAnalysisCards = cards;
                DistributeAnalysisCards(cards);

                // Обзор + рекомендации по безопасности вместе — это то,
                // на что человек должен посмотреть перед применением твиков.
                SysRecommendationsList.ItemsSource = hardware.Recommendations
                    .Concat(security.Recommendations)
                    .Select(r => "• " + r)
                    .ToList();
            }
            catch (Exception ex)
            {
                SysRecommendationsList.ItemsSource = new[] { $"• Не удалось полностью собрать информацию о системе: {ex.Message}" };
            }
            finally
            {
                SystemInfoLoadingText.Visibility = Visibility.Collapsed;
                AnalysisCardsGrid.Visibility = Visibility.Visible;
            }
        }

        // ItemsWrapGrid assumes uniform item size and clips anything
        // taller than its first-measured row — wrong for cards ranging
        // from 2 to 7 lines. Building the cards by hand and dropping each
        // one into whichever of the 3 columns is currently shortest (by
        // line count) gives a proper masonry-style layout with no clipping.
        private List<AnalysisCardView> _lastAnalysisCards = new();

        private void OnAnalysisSearchChanged(object sender, TextChangedEventArgs e)
        {
            var query = AnalysisSearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                DistributeAnalysisCards(_lastAnalysisCards);
                return;
            }

            var filtered = _lastAnalysisCards
                .Where(card =>
                    card.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    card.Lines.Any(line => line.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            DistributeAnalysisCards(filtered);
        }

        private void DistributeAnalysisCards(List<AnalysisCardView> cards)
        {
            var columns = new[] { AnalysisColumn0, AnalysisColumn1, AnalysisColumn2 };
            var lineCounts = new int[columns.Length];

            columns[0].Children.Clear();
            columns[1].Children.Clear();
            columns[2].Children.Clear();

            foreach (var card in cards)
            {
                var shortest = 0;
                for (int i = 1; i < columns.Length; i++)
                    if (lineCounts[i] < lineCounts[shortest]) shortest = i;

                columns[shortest].Children.Add(BuildAnalysisCard(card));
                lineCounts[shortest] += card.Lines.Count + 1; // +1 for the title line
            }
        }

        private Border BuildAnalysisCard(AnalysisCardView card)
        {
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(new TextBlock
            {
                Text = card.Title,
                FontSize = 12.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextPrimaryBrush"]
            });

            foreach (var line in card.Lines)
            {
                var brush = line.Status switch
                {
                    AnalysisLineStatus.Ok => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteSuccessBrush"],
                    AnalysisLineStatus.Warning => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteStatusAdvancedFgBrush"],
                    AnalysisLineStatus.Unknown => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteAnalysisUnknownBrush"],
                    _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteTextSecondaryBrush"]
                };

                var textBlock = new TextBlock
                {
                    Text = line.Text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = brush
                };
                if (!string.IsNullOrEmpty(line.Tooltip))
                    ToolTipService.SetToolTip(textBlock, line.Tooltip);

                stack.Children.Add(textBlock);
            }

            return new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteBackgroundBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PSuiteBorderBrush"],
                BorderThickness = new Thickness(0.5),
                Child = stack
            };
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays} дн {uptime.Hours} ч";
            if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours} ч {uptime.Minutes} мин";
            return $"{uptime.Minutes} мин";
        }

        // Maps a tri-state flag to a status color. trueIsGood=false flips
        // it for the handful of flags where "on" is actually the risky
        // state (Test Mode, Kernel Debug) rather than the safe one.
        private static AnalysisLineStatus StatusFromFlag(bool? value, bool trueIsGood = true)
        {
            if (value == null) return AnalysisLineStatus.Unknown;
            var isGood = trueIsGood ? value.Value : !value.Value;
            return isGood ? AnalysisLineStatus.Ok : AnalysisLineStatus.Warning;
        }

        private static string FormatFlag(bool? value) => value switch
        {
            true => "Вкл",
            false => "Выкл",
            null => "?"
        };

        private async void OnRunSpeedTestClick(object sender, RoutedEventArgs e)
        {
            if (!Core.NetworkSpeedTest.IsAvailable())
            {
                ShowStatus("librespeed-cli.exe не найден в Assets. Скачайте с github.com/librespeed/speedtest-cli/releases и положите рядом с PSuite.exe.");
                return;
            }

            // Explicit, specific consent — this is the one benchmark test
            // that leaves the machine and talks to a server PSuite
            // doesn't control, so it never runs without the person
            // seeing exactly that stated first.
            ContentDialogResult choice;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Speed-тест отправит данные на внешний сервер",
                    Content = "Это единственный тест в PSuite, который выходит в интернет. Используется открытый LibreSpeed CLI — он подключится к ближайшему публичному LibreSpeed-серверу и передаст ему тестовые данные для замера скорости. Твой IP-адрес будет виден этому серверу (как при открытии любого сайта). Продолжить?",
                    PrimaryButtonText = "Запустить тест",
                    CloseButtonText = "Отмена",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = RootGrid.XamlRoot
                };
                choice = await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"Не удалось показать диалог подтверждения: {ex.Message}");
                return;
            }

            if (choice != ContentDialogResult.Primary) return;

            RunSpeedTestButton.IsEnabled = false;
            RunBenchmarkButton.IsEnabled = false;
            RunStabilityButton.IsEnabled = false;
            var originalContent = RunSpeedTestButton.Content;
            RunSpeedTestButton.Content = "Измеряю скорость...";
            SpeedTestResultText.Visibility = Visibility.Collapsed;

            try
            {
                var (success, error, result) = await Core.NetworkSpeedTest.RunAsync();
                if (!success || result == null)
                {
                    ShowStatus($"Speed-тест не удался: {error}");
                    return;
                }

                SpeedTestResultText.Text =
                    $"Speed-тест ({result.ServerName}): ping {result.PingMs:0.#} мс, " +
                    $"загрузка {result.DownloadMbps:0.#} Мбит/с, отдача {result.UploadMbps:0.#} Мбит/с.";
                SpeedTestResultText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowStatus($"Speed-тест не удался: {ex.Message}");
            }
            finally
            {
                RunSpeedTestButton.IsEnabled = true;
                RunBenchmarkButton.IsEnabled = true;
                RunStabilityButton.IsEnabled = true;
                RunSpeedTestButton.Content = originalContent;
            }
        }

        private async void OnRunStabilityClick(object sender, RoutedEventArgs e)
        {
            RunStabilityButton.IsEnabled = false;
            RunBenchmarkButton.IsEnabled = false;
            RunSpeedTestButton.IsEnabled = false;
            var originalContent = RunStabilityButton.Content;
            StabilityResultText.Visibility = Visibility.Collapsed;

            try
            {
                var stabilityProgress = new Progress<int>(percent => RunStabilityButton.Content = $"Идёт нагрузка... {percent}%");
                RunStabilityButton.Content = "Идёт нагрузка... 0%";
                var result = await Core.BenchmarkRunner.RunStabilityTestAsync(stabilityProgress);

                var isStable = result.Score >= 92; // matches the <8% drop tolerance in the test itself
                StabilityResultText.Text = $"{result.Name}: {result.Score} {result.Unit}";
                StabilityResultText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    isStable ? "PSuiteAccentTealLightBrush" : "PSuiteStatusBlockedFgBrush"];
                StabilityResultText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowStatus($"Тест стабильности не удался: {ex.Message}");
            }
            finally
            {
                RunStabilityButton.IsEnabled = true;
                RunBenchmarkButton.IsEnabled = true;
                RunSpeedTestButton.IsEnabled = true;
                RunStabilityButton.Content = originalContent;
            }
        }

        private async void OnRunBenchmarkClick(object sender, RoutedEventArgs e)
        {
            RunBenchmarkButton.IsEnabled = false;
            RunStabilityButton.IsEnabled = false;
            RunSpeedTestButton.IsEnabled = false;
            var originalContent = RunBenchmarkButton.Content;

            var progress = new Progress<string>(step => RunBenchmarkButton.Content = step);
            var previous = Core.BenchmarkHistoryStore.LoadLast();
            var baseline = Core.BenchmarkHistoryStore.LoadBaseline();
            BenchmarkRecordBadge.Visibility = Visibility.Collapsed;

            try
            {
                var suite = await Core.BenchmarkRunner.RunFullSuiteAsync(progress);

                Core.BenchmarkHistoryStore.SaveBaselineIfMissing(suite);
                var score = Core.BenchmarkRunner.ComputeScore(suite, baseline ?? suite);

                _logStore.Append(new LogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    ModuleId = "benchmark",
                    ModuleName = "Бенчмарк",
                    Action = "Benchmark",
                    Success = true,
                    Details = score.HasValue ? $"Performance Score: {score:0}" : "Базовый замер (1000)"
                });

                BenchmarkScoreText.Text = baseline == null ? "Performance Score: 1000 (базовый замер)" : $"Performance Score: {score:0}";
                BenchmarkScoreText.Visibility = Visibility.Visible;
                BenchmarkScoreSubtitleText.Visibility = Visibility.Collapsed;

                // Only a REAL, persisted best-score comparison earns the
                // celebration — not the first-ever baseline run (nothing
                // to beat yet), and only when the stored best is genuinely
                // exceeded.
                if (baseline != null && score.HasValue &&
                    Core.BenchmarkHistoryStore.SaveBestScoreIfHigher(score.Value))
                {
                    PlayScoreRecordAnimation();
                }

                var verdict = BuildBenchmarkVerdict(suite);
                BenchmarkVerdictText.Text = verdict;
                BenchmarkVerdictText.Visibility = string.IsNullOrEmpty(verdict) ? Visibility.Collapsed : Visibility.Visible;

                var cards = suite.Results
                    .Select(result => BuildBenchmarkResultCard(result, previous?.Results.FirstOrDefault(r => r.Name == result.Name)))
                    .ToList();
                DistributeBenchmarkCards(cards);

                Core.BenchmarkHistoryStore.SaveLast(suite);

                BenchmarkTotalText.Text = previous == null
                    ? $"Всего: {suite.TotalDuration.TotalSeconds:0.0} с. Сохранено как база — сравни после твика."
                    : $"Всего: {suite.TotalDuration.TotalSeconds:0.0} с";
            }
            catch (Exception ex)
            {
                ShowStatus($"Бенчмарк не удался: {ex.Message}");
            }
            finally
            {
                RunBenchmarkButton.IsEnabled = true;
                RunStabilityButton.IsEnabled = true;
                RunSpeedTestButton.IsEnabled = true;
                RunBenchmarkButton.Content = originalContent;
            }
        }

        // One honest, defensible sentence instead of six raw numbers:
        // CPU scaling efficiency (naive, vs logical processor count) and
        // a rough SSD/HDD speed classification. No fabricated grades —
        // only conclusions that follow directly from the measured numbers.
        private static string BuildBenchmarkVerdict(Core.BenchmarkSuiteResult suite)
        {
            var single = suite.Results.FirstOrDefault(r => r.Name == "CPU (1 поток)");
            var multi = suite.Results.FirstOrDefault(r => r.Name.StartsWith("CPU (все ядра"));
            var diskWrite = suite.Results.FirstOrDefault(r => r.Name == "Диск: последовательная запись");

            // Only the single most actionable finding — not everything we
            // could say, just the one thing worth reading in two seconds.
            var threads = Environment.ProcessorCount;
            if (single != null && multi != null && single.Score > 0 && threads > 1)
            {
                var efficiencyPercent = multi.Score / single.Score / threads * 100.0;
                if (efficiencyPercent < 60)
                    return $"Многопоточность слабая (≈{efficiencyPercent:0}%) — вероятен троттлинг под нагрузкой.";
            }

            if (diskWrite != null && diskWrite.Score < 100)
                return "Скорость записи на диск на уровне HDD.";

            return string.Empty;
        }

        // Same direction table as the hints below, kept separate because
        // it drives colour/arrow logic, not just the text.
        // Plain-language context for someone who doesn't know what
        // "нс/обращение" or "STREAM Triad" means — same numbers, but
        // with "higher/lower is better" spelled out.
        private static string GetBenchmarkHint(string resultName) => resultName switch
        {
            "CPU (1 поток)" => "Выше = быстрее. Скорость одного ядра — важна для игр и фонового ПО.",
            var n when n.StartsWith("CPU (все ядра") =>
                "Выше = быстрее. Как проц справляется, когда работают сразу все ядра — важно для рендера, архивации, стриминга.",
            "Память: пропускная способность (STREAM Triad)" =>
                "Выше = быстрее. Сколько данных в секунду проц может прочитать/записать в ОЗУ.",
            "Память: случайный доступ (латентность)" =>
                "Ниже = быстрее. Задержка при обращении к «случайному» месту в памяти — сильно зависит от разгона/таймингов ОЗУ.",
            "Диск: последовательная запись" => "Выше = быстрее.",
            "Диск: последовательное чтение" => "Выше = быстрее (число может быть завышено, если файл ещё в кэше ОС).",
            "Диск: случайный доступ (4K IOPS)" =>
                "Выше = быстрее. Мелкие операции в случайных местах диска (как реальная работа ОС/игр), а не один большой файл подряд — честнее показывает SSD/HDD под настоящей нагрузкой.",
            "Сеть: localhost TCP (не интернет)" =>
                "Выше = быстрее. Замер через loopback (127.0.0.1) — трафик не выходит за пределы этого ПК. Показывает накладные расходы TCP/IP-стека Windows, а НЕ скорость интернета/провайдера.",
            "GPU: копирование видеопамяти" => "Выше = быстрее. Скорость GPU-GPU копирования в видеопамяти — грубый показатель пропускной способности VRAM. «Недоступно» — обычно нет совместимого GPU/драйвера (например, в виртуалке без passthrough).",
            _ => string.Empty
        };

        private DispatcherTimer? _statusHideTimer;

        private enum StatusKind { Info, Success, Error }

        private void ShowStatus(string message, double autoHideSeconds = 5) =>
            ShowStatus(message, StatusKind.Error, autoHideSeconds);

        private void ShowStatus(string message, StatusKind kind, double autoHideSeconds = 5)
        {
            var (icon, brush) = kind switch
            {
                StatusKind.Success => ("\u2713", (Brush)Application.Current.Resources["PSuiteSuccessBrush"]),
                StatusKind.Info => ("\u2139", (Brush)Application.Current.Resources["PSuiteTextSecondaryBrush"]),
                _ => ("\u2715", (Brush)Application.Current.Resources["PSuiteErrorBrush"])
            };

            StatusBannerIcon.Text = icon;
            StatusBannerIcon.Foreground = brush;
            StatusBannerText.Text = message;
            StatusBannerText.Foreground = brush;
            StatusBanner.Background = kind switch
            {
                StatusKind.Success => (Brush)Application.Current.Resources["PSuiteSuccessBgBrush"],
                StatusKind.Info => (Brush)Application.Current.Resources["PSuiteSurfaceRaisedBrush"],
                _ => (Brush)Application.Current.Resources["PSuiteErrorBgBrush"]
            };
            StatusBanner.BorderBrush = brush;
            StatusBanner.BorderThickness = new Thickness(0.5);
            StatusBanner.Opacity = 1;
            StatusBanner.Visibility = Visibility.Visible;

            // Stop+recreate rather than reuse: if a previous call is mid-tick
            // when a new one arrives, this guarantees only one timer is ever
            // ticking for the banner — no chance of it firing early/late or
            // getting stuck visible because an old timer got orphaned.
            _statusHideTimer?.Stop();
            _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoHideSeconds) };
            _statusHideTimer.Tick += (_, _) =>
            {
                _statusHideTimer?.Stop();
                ClearStatus();
            };
            _statusHideTimer.Start();
        }

        private void ClearStatus()
        {
            _statusHideTimer?.Stop();
            if (StatusBanner.Visibility != Visibility.Visible) return;

            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fadeOut, StatusBanner);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            var slideUp = new DoubleAnimation { From = 0, To = -8, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            Storyboard.SetTarget(slideUp, StatusBannerTranslate);
            Storyboard.SetTargetProperty(slideUp, "Y");

            var sb = new Storyboard();
            sb.Children.Add(fadeOut);
            sb.Children.Add(slideUp);
            sb.Completed += (_, _) =>
            {
                StatusBanner.Visibility = Visibility.Collapsed;
                StatusBannerTranslate.Y = 0;
            };
            sb.Begin();
        }

        // Self-elevation via Process.Start(exePath, Verb="runas") was
        // tried extensively and confirmed unreliable for this packaged
        // (MSIX) app: no crash.log entry ever appears when it fails,
        // meaning the failure happens BELOW the managed-exception layer
        // — inside Windows' packaged-app activation mechanism itself,
        // not in anything our C# try/catch can see or fix. The correct
        // fix for a packaged app is the COM elevation moniker for
        // IApplicationActivationManager, which needs exact CLSID/IID
        // P/Invoke signatures — not something to guess at without being
        // able to test live. Rather than keep shipping fragile attempts,
        // this gives a plain, 100%-reliable manual instruction instead.
        private async Task<bool> PromptRelaunchElevatedAsync(string moduleName)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Требуются права администратора",
                    Content = $"'{moduleName}' требует прав администратора.\n\nЗакройте PSuite и запустите его заново через правый клик по ярлыку → «Запуск от имени администратора».\n\n(Автоматический перезапуск с правами администратора не работает надёжно для этой сборки — не переживайте, это не удалит и не сломает никакие настройки.)",
                    PrimaryButtonText = "Понятно",
                    XamlRoot = RootGrid.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"'{moduleName}' требует прав администратора. Перезапустите PSuite от имени администратора вручную. ({ex.Message})");
            }

            return false;
        }
    }
}