using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SackMix.Core;

namespace SackMix;

public partial class App : Application
{
    /// <summary>A second copy sets it: the running one opens its window, even from the tray.</summary>
    private const string ShowSignal = @"Local\SackMix.Show";

    private Mutex? _single;
    private EventWaitHandle? _show;
    private MainVm? _vm;
    private Tray? _tray;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two copies would fight over the same volumes: the second one only brings the first to the front.
        _single = new Mutex(true, @"Local\SackMix", out var first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(ShowSignal, out var show))
                using (show) show.Set();
            Shutdown();
            return;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignal);

        base.OnStartup(e);
        // Only the meters redraw, and they are small: drawn on the CPU it was 48 MB private and 0.06 % CPU
        // against 147 MB and 0.10 % through the GPU (Ryzen 5600G, whose integrated GPU keeps its surfaces in our RAM).
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        try
        {
            _vm = new MainVm(ConfigStore.DefaultPath);
            var window = new MainWindow(_vm);
            MainWindow = window;
            _tray = new Tray(ShowMain, Quit);
            // Closing the window leaves the mixer running in the tray; Salir (or Windows shutting down) quits.
            window.Closing += (_, args) =>
            {
                if (_exiting) return;
                args.Cancel = true;
                window.Hide();
                _tray.TellOnce();
            };
            SessionEnding += (_, _) => _exiting = true;
            ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => Dispatcher.InvokeAsync(ShowMain), null, Timeout.Infinite, false);
            // Started with Windows: straight to the tray.
            if (!e.Args.Contains("--autostart")) window.Show();
        }
        catch (Exception ex)
        {
            // Without a window it would linger unseen, holding the mutex: better gone, with the reason in the log.
            Log.Error("start", ex);
            throw;
        }
        // From here on a slip in one handler is logged and the mixer keeps going.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("ui", args.Exception);
            args.Handled = true;
        };
    }

    private void ShowMain()
    {
        if (MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void Quit()
    {
        _exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _vm?.Dispose();
        _show?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
