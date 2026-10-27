using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SackMix.Audio;

/// <summary>
/// The programs' audio sessions on every active output. All Core Audio work runs on one MTA
/// thread of its own, so the UI never waits on it, and Windows' callbacks are queued onto that
/// thread too. With the meters on, the same thread reads the peaks about 30 times a second;
/// with them off it sleeps until there is work. Events are raised on that thread.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    /// <summary>Marks our own volume changes, to tell them from Windows' mixer and the programs.</summary>
    public static readonly Guid Context = Guid.NewGuid();

    private const int MeterMs = 33;
    private const int DeviceStateActive = 1;
    private const int ClsctxAll = 0x17;
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private readonly BlockingCollection<Action> _jobs = [];
    private readonly Thread _thread;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, (IAudioSessionManager2 Manager, SessionWatcher Watcher)> _outputs = [];
    private IMMDeviceEnumerator? _devices;
    private DeviceWatcher? _deviceWatcher;
    private string? _defaultId;
    private volatile bool _meters;

    /// <summary>Name of Windows' default output, or null when there is none.</summary>
    public event Action<string?>? DefaultOutputChanged;
    public event Action<Session>? SessionAdded;
    public event Action<Session>? SessionRemoved;
    /// <summary>Someone else (Windows' mixer, the program itself) changed a session's level or mute.</summary>
    public event Action<Session, float, bool>? ChangedOutside;

    public AudioEngine()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SackMix audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public void Start() => _thread.Start();

    public bool Meters
    {
        set
        {
            _meters = value;
            Post(static () => { }); // wakes the thread up
        }
    }

    public void SetVolume(Session session, float volume, bool muted) => Post(() => session.Apply(volume, muted));

    public void Post(Action job)
    {
        try { _jobs.Add(job); }
        catch (InvalidOperationException) { } // closing
    }

    private void Run()
    {
        Try(Open);
        var nextPeaks = 0L;
        while (true)
        {
            if (_meters && Environment.TickCount64 >= nextPeaks)
            {
                foreach (var s in _sessions.Values) s.ReadPeak();
                nextPeaks = Environment.TickCount64 + MeterMs;
            }
            var wait = _meters ? (int)Math.Max(0, nextPeaks - Environment.TickCount64) : Timeout.Infinite;
            if (_jobs.TryTake(out var job, wait)) Try(job);
            else if (_jobs.IsCompleted) break;
        }
        Try(Close);
    }

    private static void Try(Action job)
    {
        // One bad session or callback must not take the audio thread down with it.
        try { job(); }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException) { Log.Error("audio", e); }
    }

    private void Open()
    {
        _devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _deviceWatcher = new DeviceWatcher(this);
        _devices.RegisterEndpointNotificationCallback(_deviceWatcher);
        Refresh();
    }

    /// <summary>Follows the outputs that are on now; sessions already known stay as they are.</summary>
    internal void Refresh()
    {
        if (_devices is null) return;
        var hasMain = _devices.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var main) == 0;
        var mainId = hasMain && main.GetId(out var m) == 0 ? m : null;
        // System sounds come only from the main output (each output has its own, at its own level):
        // when it changes, everything is listed again.
        var reopen = mainId != _defaultId;
        _defaultId = mainId;

        var active = new Dictionary<string, IMMDevice>();
        if (_devices.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out var list) == 0 && list.GetCount(out var count) == 0)
            for (var i = 0; i < count; i++)
                if (list.Item(i, out var device) == 0 && device.GetId(out var id) == 0) active[id] = device;

        foreach (var id in _outputs.Keys.Where(id => reopen || !active.ContainsKey(id)).ToList()) CloseOutput(id);
        foreach (var (id, device) in active)
            if (!_outputs.ContainsKey(id)) OpenOutput(id, device);

        DefaultOutputChanged?.Invoke(hasMain ? FriendlyName(main) : null);
    }

    private void OpenOutput(string id, IMMDevice device)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated) != 0) return;
        var manager = (IAudioSessionManager2)activated;
        var watcher = new SessionWatcher(this, id);
        manager.RegisterSessionNotification(watcher);
        _outputs[id] = (manager, watcher);
        // Listing the sessions is also what makes Windows start calling OnSessionCreated.
        if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions.GetCount(out var count) != 0) return;
        for (var i = 0; i < count; i++)
            if (sessions.GetSession(i, out var session) == 0) Add(session, id);
    }

    private void CloseOutput(string id)
    {
        var (manager, watcher) = _outputs[id];
        _outputs.Remove(id);
        manager.UnregisterSessionNotification(watcher);
        foreach (var s in _sessions.Values.Where(s => s.DeviceId == id).ToList()) Remove(s);
    }

    internal void Add(IAudioSessionControl2 control, string deviceId)
    {
        if (control.GetState(out var state) != 0 || state == SessionState.Expired) return;
        if (control.IsSystemSoundsSession() == 0 && deviceId != _defaultId) return;
        if (control.GetSessionInstanceIdentifier(out var id) != 0 || _sessions.ContainsKey(id)) return;
        var session = new Session(this, control, id, deviceId);
        _sessions[id] = session;
        session.Listen();
        SessionAdded?.Invoke(session);
    }

    internal void Remove(Session session)
    {
        if (!_sessions.Remove(session.Id)) return;
        session.Close();
        SessionRemoved?.Invoke(session);
    }

    internal void RaiseChangedOutside(Session session, float volume, bool muted)
    {
        if (_sessions.ContainsKey(session.Id)) ChangedOutside?.Invoke(session, volume, muted);
    }

    private void Close()
    {
        foreach (var id in _outputs.Keys.ToList()) CloseOutput(id);
        if (_devices is not null && _deviceWatcher is not null) _devices.UnregisterEndpointNotificationCallback(_deviceWatcher);
    }

    private static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return null;
        var key = FriendlyNameKey;
        if (store.GetValue(ref key, out var value) != 0) return null;
        try { return value.AsString(); }
        finally { PropVariant.PropVariantClear(ref value); }
    }

    public void Dispose()
    {
        _jobs.CompleteAdding();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));
    }
}

[ComVisible(true)]
internal sealed class SessionWatcher(AudioEngine engine, string deviceId) : IAudioSessionNotification
{
    public void OnSessionCreated(IAudioSessionControl2 session) => engine.Post(() => engine.Add(session, deviceId));
}

[ComVisible(true)]
internal sealed class DeviceWatcher(AudioEngine engine) : IMMNotificationClient
{
    public void OnDeviceStateChanged(string id, int state) => engine.Post(engine.Refresh);
    public void OnDeviceAdded(string id) => engine.Post(engine.Refresh);
    public void OnDeviceRemoved(string id) => engine.Post(engine.Refresh);

    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? id)
    {
        if (flow == EDataFlow.Render && role == ERole.Multimedia) engine.Post(engine.Refresh);
    }

    public void OnPropertyValueChanged(string id, PropertyKey key) { }
}
