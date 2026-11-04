using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace SackMix;

public partial class MainWindow : Window
{
    private readonly MainVm _vm;
    private readonly DispatcherTimer _toast = new() { Interval = TimeSpan.FromSeconds(7) };
    private AppVm? _dragging;
    private Point _dragFrom;

    internal MainWindow(MainVm vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        _toast.Tick += (_, _) =>
        {
            _toast.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };
        // Meters only while someone can see them: not minimised, not in the tray.
        IsVisibleChanged += (_, _) => vm.MetersOn = IsVisible && WindowState != WindowState.Minimized;
        StateChanged += (_, _) =>
        {
            vm.MetersOn = IsVisible && WindowState != WindowState.Minimized;
            // A maximised WindowChrome window spills past the screen by its resize border.
            Frame.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : default;
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        var round = 2;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE: round (Windows 11) */, ref round, sizeof(int));
    }

    // ── Header ──────────────────────────────────────────────

    private async void Obs_Click(object sender, RoutedEventArgs e)
    {
        ObsButton.IsEnabled = false;
        try { ShowToast(await _vm.SendToObsAsync()); }
        finally { ObsButton.IsEnabled = true; }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        // Read out by screen readers too.
        UIElementAutomationPeer.CreatePeerForElement(ToastText).RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        _toast.Stop();
        _toast.Start();
    }

    // ── Channels ────────────────────────────────────────────

    private void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        var channel = _vm.AddChannel();
        // Straight into its name, ready to type.
        Dispatcher.InvokeAsync(() =>
        {
            if (ChannelList.ItemContainerGenerator.ContainerFromItem(channel) is not FrameworkElement strip) return;
            strip.BringIntoView();
            if (Find<TextBox>(strip) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }

    private void Color_Click(object sender, RoutedEventArgs e) => _vm.NextColor((ChannelVm)((FrameworkElement)sender).DataContext);

    private void ChannelMenu_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var channel = (ChannelVm)button.DataContext;
        var menu = button.ContextMenu!;
        menu.Items.Clear();
        menu.Items.Add(Item("Cambiar color", () => _vm.NextColor(channel)));
        var remove = Item("Eliminar canal", () => _vm.RemoveChannel(channel));
        // The last channel takes the programs nobody placed.
        remove.IsEnabled = !channel.IsCatchAll;
        menu.Items.Add(remove);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Name_KeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        else if (e.Key == Key.Escape) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        else return;
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    // ── Moving programs: drag and drop, or the right-click menu ──

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The row's own slider and mute button keep their clicks.
        _dragging = IsInside<RangeBase>(e.OriginalSource) || IsInside<ButtonBase>(e.OriginalSource)
            ? null
            : ((FrameworkElement)sender).DataContext as AppVm;
        _dragFrom = e.GetPosition(this);
    }

    private void Row_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(this) - _dragFrom;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var app = _dragging;
        _dragging = null;
        DragDrop.DoDragDrop((DependencyObject)sender, app, DragDropEffects.Move);
    }

    private void Strip_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(AppVm)) is AppVm;
        e.Effects = dragged ? DragDropEffects.Move : DragDropEffects.None;
        if (dragged) ((ChannelVm)((FrameworkElement)sender).DataContext).IsDropTarget = true;
        e.Handled = true;
    }

    private void Strip_DragLeave(object sender, DragEventArgs e)
    {
        var strip = (FrameworkElement)sender;
        // DragLeave also fires when passing from one child to another inside the strip.
        var p = e.GetPosition(strip);
        if (p.X >= 0 && p.Y >= 0 && p.X < strip.ActualWidth && p.Y < strip.ActualHeight) return;
        ((ChannelVm)strip.DataContext).IsDropTarget = false;
    }

    private void Strip_Drop(object sender, DragEventArgs e)
    {
        var channel = (ChannelVm)((FrameworkElement)sender).DataContext;
        channel.IsDropTarget = false;
        if (e.Data.GetData(typeof(AppVm)) is AppVm app) _vm.Move(app, channel);
        e.Handled = true;
    }

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (FrameworkElement)sender;
        var app = (AppVm)row.DataContext;
        var menu = row.ContextMenu!;
        menu.Items.Clear();
        foreach (var channel in _vm.Channels.Where(c => c != app.Channel))
            menu.Items.Add(Item($"Mover a {channel.Name}", () => _vm.Move(app, channel)));
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var d = source as DependencyObject; d is not null;
             d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
