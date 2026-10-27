using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SackMix.Core;

namespace SackMix;

public partial class App : Application
{
    private Mutex? _single;
    private MainVm? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two copies would fight over the same volumes: the second one only brings the first to the front.
        _single = new Mutex(true, @"Local\SackMix", out var first);
        if (!first)
        {
            var running = FindWindow(null, "SackMix");
            if (running != IntPtr.Zero)
            {
                ShowWindow(running, 9 /* SW_RESTORE */);
                SetForegroundWindow(running);
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        // Only the meters redraw, and they are small: drawn on the CPU it was 48 MB private and 0.06 % CPU
        // against 147 MB and 0.10 % through the GPU (Ryzen 5600G, whose integrated GPU keeps its surfaces in our RAM).
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        try
        {
            _vm = new MainVm(ConfigStore.DefaultPath);
            new MainWindow(_vm).Show();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string title);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
