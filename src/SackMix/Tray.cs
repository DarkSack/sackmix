using System.Windows;
using SackMix.Core;
using Forms = System.Windows.Forms;

namespace SackMix;

/// <summary>The icon next to the clock: a click opens the mixer, its menu opens it or quits.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private bool _told;

    public Tray(Action open, Action exit, MainVm vm)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/sackmix.ico"))!.Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir SackMix", null, (_, _) => open());
        // Profiles follow SackSpace's workspaces: the menu offers the one it asked for last.
        var save = new Forms.ToolStripMenuItem();
        var forget = new Forms.ToolStripMenuItem();
        save.Click += (_, _) => { if (vm.RequestedProfile is { } p) vm.SaveProfile(p); };
        forget.Click += (_, _) => { if (vm.RequestedProfile is { } p) vm.ForgetProfile(p); };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(save);
        menu.Items.Add(forget);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Opening += (_, _) =>
        {
            var p = vm.RequestedProfile;
            var has = p is not null && Profiles.Has(vm.Mixer.Config, p);
            save.Text = p is null ? "Perfiles: SackSpace los pide al cambiar de espacio" : $"Guardar esta mezcla para «{p}»";
            save.Enabled = p is not null;
            forget.Text = $"Olvidar la mezcla de «{p}»";
            forget.Visible = has;
        };
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
