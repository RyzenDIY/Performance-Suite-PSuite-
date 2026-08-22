using System;
using System.Threading;
using System.Windows;

namespace RustVision;

/// <summary>
/// Application entry point. RustVision is a standalone WPF UI prototype only.
/// It does not read process memory, inject into any process, or interact
/// with any game in any way. All controls affect only this window's UI state.
///
/// Single instance is enforced with a named Mutex: if RustVision is already
/// running, this second process exits immediately instead of opening a
/// second MainWindow.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "RustVision.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var isNewInstance);

        if (!isNewInstance)
        {
            // RustVision вже запущений в іншому процесі - цей екземпляр
            // завершується без відкриття другого вікна.
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Mutex вже міг бути звільнений або не належав цьому потоку - безпечно ігнорувати.
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
