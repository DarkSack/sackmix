using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SackMix.Core;

namespace SackMix.Audio;

/// <summary>
/// One audio stream of one program on one output. Made and used on the audio thread; the UI
/// only reads the fixed fields and <see cref="Peak"/>.
/// </summary>
[ComVisible(true)]
internal sealed class Session : IAudioSessionEvents
{
    private readonly AudioEngine _engine;
    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation _meter;
    private Process? _process;
    private bool _closed;
    private volatile float _peak;

    public string Id { get; }
    public string DeviceId { get; }
    public int Pid { get; }
    /// <summary>Exe name in lower case; <see cref="Mixer.SystemSounds"/> for Windows' sounds.</summary>
    public string Exe { get; }
    public string? Path { get; }
    public string Name { get; }
    /// <summary>Level and mute Windows had when the session appeared.</summary>
    public float Volume { get; }
    public bool Muted { get; }
    /// <summary>Last peak read, 0..1, while the meters are on.</summary>
    public float Peak => _peak;

    public Session(AudioEngine engine, IAudioSessionControl2 control, string id, string deviceId)
    {
        _engine = engine;
        _control = control;
        _volume = (ISimpleAudioVolume)control;
        _meter = (IAudioMeterInformation)control;
        Id = id;
        DeviceId = deviceId;
        control.GetProcessId(out var pid);
        Pid = pid;
        if (control.IsSystemSoundsSession() == 0)
        {
            Exe = Mixer.SystemSounds;
            Name = "Sonidos del sistema";
        }
        else
        {
            Path = ProcessInfo.ImagePath(pid);
            Exe = (Path is null ? ProcessInfo.ExeName(pid) : System.IO.Path.GetFileName(Path)).ToLowerInvariant();
            control.GetDisplayName(out var display);
            Name = ProcessInfo.Describe(Exe, Path, display);
        }
        _volume.GetMasterVolume(out var volume);
        _volume.GetMute(out var muted);
        Volume = volume;
        Muted = muted;
    }

    public void Listen()
    {
        _control.RegisterAudioSessionNotification(this);
        if (Pid == 0) return;
        try
        {
            // A closed program's sessions do not always expire (EarTrumpet watches processes for the same reason).
            _process = Process.GetProcessById(Pid);
            _process.Exited += (_, _) => _engine.Post(() => _engine.Remove(this));
            _process.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Apply(float volume, bool muted)
    {
        if (_closed) return;
        var context = AudioEngine.Context;
        _volume.SetMasterVolume(volume, ref context);
        _volume.SetMute(muted, ref context);
    }

    public void ReadPeak()
    {
        if (!_closed && _meter.GetPeakValue(out var peak) == 0) _peak = peak;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _peak = 0;
        _control.UnregisterAudioSessionNotification(this);
        _process?.Dispose();
        Marshal.FinalReleaseComObject(_control);
    }

    // Windows calls these on its own threads: they only queue work.
    void IAudioSessionEvents.OnSimpleVolumeChanged(float volume, bool muted, IntPtr context)
    {
        if (context != IntPtr.Zero && Marshal.PtrToStructure<Guid>(context) == AudioEngine.Context) return;
        _engine.Post(() => _engine.RaiseChangedOutside(this, volume, muted));
    }

    void IAudioSessionEvents.OnStateChanged(SessionState state)
    {
        if (state == SessionState.Expired) _engine.Post(() => _engine.Remove(this));
    }

    void IAudioSessionEvents.OnSessionDisconnected(int reason) => _engine.Post(() => _engine.Remove(this));

    void IAudioSessionEvents.OnDisplayNameChanged(string name, IntPtr context) { }
    void IAudioSessionEvents.OnIconPathChanged(string path, IntPtr context) { }
    void IAudioSessionEvents.OnChannelVolumeChanged(int count, IntPtr volumes, int changed, IntPtr context) { }
    void IAudioSessionEvents.OnGroupingParamChanged(IntPtr group, IntPtr context) { }
}
