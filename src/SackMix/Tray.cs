using System.Windows;
using Forms = System.Windows.Forms;

namespace SackMix;

/// <summary>The icon next to the clock: a click opens the mixer, its menu opens it or quits.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private bool _told;

    public Tray(Action open, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/sackmix.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir SackMix", null, (_, _) => open());
        menu.Items.Add("Salir", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "SackMix",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
        };
    }

    /// <summary>Said once per run, the first time the window goes to the tray.</summary>
    public void TellOnce()
    {
        if (_told) return;
        _told = true;
        _icon.ShowBalloonTip(5000, "SackMix sigue funcionando",
            "Está aquí, en la bandeja. Para cerrarlo del todo: clic derecho › Salir.", Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
