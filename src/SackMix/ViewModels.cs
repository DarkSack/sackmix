using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
using SackMix.Audio;
using SackMix.Core;

namespace SackMix;

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void Raise(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
}

/// <summary>Something with a level meter, fed about 30 times a second.</summary>
internal abstract class Metered : Observable
{
    private static readonly PropertyChangedEventArgs PeakArgs = new(nameof(Peak));

    public double Peak { get; private set; }

    /// <summary>Rises at once, falls gently, and skips changes nobody would see.</summary>
    public void ShowPeak(double peak)
    {
        var shown = peak >= Peak ? peak : Peak * 0.82;
        if (shown < 0.005) shown = 0;
        if (Math.Abs(shown - Peak) < 0.004 && (shown > 0 || Peak == 0)) return;
        Peak = shown;
        Raise(PeakArgs);
    }
}

internal sealed class ChannelVm : Metered
{
    private readonly MainVm _main;

    public ChannelVm(MainVm main, ChannelConfig config)
    {
        _main = main;
        Config = config;
        Paint();
    }

    public ChannelConfig Config { get; }
    public ObservableCollection<AppVm> Apps { get; } = [];
    public SolidColorBrush Accent { get; private set; } = null!;
    public SolidColorBrush AccentSoft { get; private set; } = null!;
    public bool IsCatchAll => _main.Mixer.CatchAll == Config;

    public string Name
    {
        get => Config.Name;
        set
        {
            var name = value.Trim();
            if (name.Length > 0 && name != Config.Name)
            {
                Config.Name = name;
                _main.Changed();
            }
            Raise(); // an empty name goes back to the old one
        }
    }

    /// <summary>0..100, as the fader shows it.</summary>
    public double Volume
    {
        get => Config.Volume * 100;
        set
        {
            Config.Volume = (float)Math.Clamp(value / 100, 0, 1);
            Raise();
            _main.Apply(this);
        }
    }

    public bool Muted
    {
        get => Config.Muted;
        set
        {
            Config.Muted = value;
            Raise();
            _main.Apply(this);
        }
    }

    public bool IsDropTarget
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise();
        }
    }

    public void Paint()
    {
        var c = (Color)ColorConverter.ConvertFromString(Config.Color);
        Accent = Frozen(c);
        AccentSoft = Frozen(Color.FromArgb(0x55, c.R, c.G, c.B));
        Raise(nameof(Accent));
        Raise(nameof(AccentSoft));
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}

internal sealed class AppVm(MainVm main, string exe, AppConfig config, ImageSource? icon) : Metered
{
    public string Exe => exe;
    public AppConfig Config => config;
    public ImageSource? Icon => icon;
    /// <summary>A program can have several (Chrome's tabs, Discord's processes, two outputs).</summary>
    public List<Session> Sessions { get; } = [];
    public string Name => config.Name.Length > 0 ? config.Name : exe;
    public string Initial => Name[..1].ToUpperInvariant();

    public ChannelVm Channel
    {
        get;
        set
        {
            field = value;
            Raise();
        }
    } = null!;

    public double Volume
    {
        get => config.Volume * 100;
        set
        {
            config.Volume = (float)Math.Clamp(value / 100, 0, 1);
            Raise();
            main.Apply(this);
        }
    }

    public bool Muted
    {
        get => config.Muted;
        set
        {
            config.Muted = value;
            Raise();
            main.Apply(this);
        }
    }

    public void Refresh()
    {
        Raise(nameof(Volume));
        Raise(nameof(Muted));
    }
}

internal sealed class MainVm : Observable, IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly Dictionary<string, AppVm> _apps = [];
    private readonly DispatcherTimer _save = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _meters = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly string _path;

    public MainVm(string path)
    {
        _path = path;
        Mixer = new Mixer(ConfigStore.Load(path));
        foreach (var c in Mixer.Config.Channels) Channels.Add(new ChannelVm(this, c));
        _save.Tick += (_, _) => Save();
        _meters.Tick += (_, _) => ShowPeaks();

        var ui = Dispatcher.CurrentDispatcher;
        _engine.DefaultOutputChanged += name => ui.InvokeAsync(() => Output = name ?? "Sin salida de audio");
        _engine.SessionAdded += s => ui.InvokeAsync(() => Added(s));
        _engine.SessionRemoved += s => ui.InvokeAsync(() => Removed(s));
        _engine.ChangedOutside += (s, v, m) => ui.InvokeAsync(() => ChangedOutside(s, v, m));
        _engine.Start();
    }

    public Mixer Mixer { get; }
    public ObservableCollection<ChannelVm> Channels { get; } = [];

    /// <summary>Windows' default output, for the header.</summary>
    public string Output
    {
        get;
        private set
        {
            field = value;
            Raise();
        }
    } = "Buscando salida…";

    /// <summary>Off while minimised: then nothing reads or draws the meters.</summary>
    public bool MetersOn
    {
        set
        {
            _engine.Meters = value;
            _meters.IsEnabled = value;
        }
    }

    private void Added(Session s)
    {
        if (!_apps.TryGetValue(s.Exe, out var app))
        {
            app = new AppVm(this, s.Exe, Mixer.App(s.Exe, s.Path, s.Name, s.Volume, s.Muted), Icons.For(s.Path));
            _apps[s.Exe] = app;
            Place(app);
        }
        app.Sessions.Add(s);
        Apply(app);
    }

    private void Removed(Session s)
    {
        if (!_apps.TryGetValue(s.Exe, out var app) || !app.Sessions.Remove(s) || app.Sessions.Count > 0) return;
        _apps.Remove(s.Exe);
        app.Channel.Apps.Remove(app);
    }

    private void ChangedOutside(Session s, float volume, bool muted)
    {
        if (!_apps.TryGetValue(s.Exe, out var app)) return;
        Mixer.External(app.Config, volume, muted);
        app.Refresh();
        Apply(app);
    }

    /// <summary>Into its channel, in alphabetical order.</summary>
    private void Place(AppVm app)
    {
        var channel = Channels.First(c => c.Config == Mixer.ChannelOf(app.Config));
        app.Channel = channel;
        var i = 0;
        while (i < channel.Apps.Count && string.Compare(channel.Apps[i].Name, app.Name, StringComparison.OrdinalIgnoreCase) < 0) i++;
        channel.Apps.Insert(i, app);
    }

    public void Apply(AppVm app)
    {
        var (volume, muted) = Mixer.Effective(app.Config);
        foreach (var s in app.Sessions) _engine.SetVolume(s, volume, muted);
        Changed();
    }

    public void Apply(ChannelVm channel)
    {
        foreach (var app in channel.Apps) Apply(app);
        Changed();
    }

    public void Move(AppVm app, ChannelVm to)
    {
        if (app.Channel == to) return;
        Mixer.Move(app.Config, to.Config);
        app.Channel.Apps.Remove(app);
        Place(app);
        Apply(app);
    }

    public ChannelVm AddChannel()
    {
        var channel = new ChannelVm(this, Mixer.AddChannel());
        Channels.Insert(Channels.Count - 1, channel);
        Changed();
        return channel;
    }

    public void RemoveChannel(ChannelVm channel)
    {
        if (!Mixer.RemoveChannel(channel.Config)) return;
        Channels.Remove(channel);
        foreach (var app in channel.Apps.ToList())
        {
            Place(app);
            Apply(app);
        }
        Changed();
    }

    public void NextColor(ChannelVm channel)
    {
        Mixer.NextColor(channel.Config);
        channel.Paint();
        Changed();
    }

    private void ShowPeaks()
    {
        foreach (var channel in Channels)
        {
            double loudest = 0;
            foreach (var app in channel.Apps)
            {
                // Windows meters a session before its volume and mute: scaled here, the meter shows what is heard.
                var (volume, muted) = Mixer.Effective(app.Config);
                double peak = 0;
                if (!muted)
                    foreach (var s in app.Sessions) peak = Math.Max(peak, s.Peak * volume);
                var shown = Meter(peak);
                app.ShowPeak(shown);
                loudest = Math.Max(loudest, shown);
            }
            channel.ShowPeak(loudest);
        }
    }

    /// <summary>Amplitude to meter height on a decibel scale, -60 dB to 0 dB, as mixers show it.</summary>
    internal static double Meter(double amplitude) => amplitude <= 0.001 ? 0 : Math.Min(1, 1 + Math.Log10(amplitude) / 3);

    /// <summary>Saved 0.8 s after the last change, not on every step of a fader.</summary>
    public void Changed()
    {
        _save.Stop();
        _save.Start();
    }

    private void Save()
    {
        _save.Stop();
        try { ConfigStore.Save(_path, Mixer.Config); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("save", e); }
    }

    /// <summary>A sentence for the user saying what happened.</summary>
    public async Task<string> SendToObsAsync()
    {
        var plan = ObsSync.Plan(Mixer, _apps.Keys.ToHashSet());
        if (plan.Count == 0) return "Todavía no hay programas en los canales.";
        try
        {
            var server = ObsServer.Read(ObsServer.DefaultConfigPath);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var obs = await ObsClient.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}"), server.Password, timeout.Token);
            var r = await ObsSync.RunAsync(obs, plan, timeout.Token);
            var done = $"Listo en OBS: {r.Created} {(r.Created == 1 ? "fuente nueva" : "fuentes nuevas")} y {r.Updated} {(r.Updated == 1 ? "actualizada" : "actualizadas")}.";
            return r.Errors.Count == 0 ? done : done + " " + string.Join(" ", r.Errors);
        }
        catch (ObsException e)
        {
            return e.Message;
        }
        catch (OperationCanceledException)
        {
            return "OBS no respondió a tiempo.";
        }
        catch (Exception e) when (e is IOException or JsonException or WebSocketException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Error("obs", e);
            return "No pude hablar con OBS: " + e.Message;
        }
    }

    public void Dispose()
    {
        _meters.Stop();
        if (_save.IsEnabled) Save();
        _engine.Dispose();
    }
}
