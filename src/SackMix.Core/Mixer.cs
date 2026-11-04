namespace SackMix.Core;

/// <summary>
/// Channels and programs. What reaches Windows for a program is its own level times its
/// channel's level, and it is muted if either one is.
/// </summary>
public sealed class Mixer(MixConfig config)
{
    /// <summary>Key of Windows' "System sounds" session, which has no program behind it.</summary>
    public const string SystemSounds = "#sistema";

    public MixConfig Config { get; } = config;

    /// <summary>The last channel takes every program nobody placed; it cannot be deleted.</summary>
    public ChannelConfig CatchAll => Config.Channels[^1];

    /// <summary>The program's settings; the first time it is seen, it is placed by <see cref="Rules"/> and keeps the level Windows had for it.</summary>
    public AppConfig App(string exe, string? path, string name, float windowsVolume, bool windowsMuted)
    {
        exe = exe.ToLowerInvariant();
        if (!Config.Apps.TryGetValue(exe, out var app))
            Config.Apps[exe] = app = new AppConfig { Channel = Rules.Guess(exe, path), Volume = MixConfig.Level(windowsVolume), Muted = windowsMuted };
        // Never placed: newer rules get a say. A program the user moved keeps its channel.
        else if (app.Channel is null) app.Channel = Rules.Guess(exe, path);
        if (name.Length > 0) app.Name = name;
        return app;
    }

    public ChannelConfig ChannelOf(AppConfig app) => Config.Channels.Find(c => c.Id == app.Channel) ?? CatchAll;

    public (float Volume, bool Muted) Effective(AppConfig app)
    {
        var c = ChannelOf(app);
        return (MixConfig.Level(app.Volume * c.Volume), app.Muted || c.Muted);
    }

    /// <summary>Someone moved the program in Windows' own mixer: that becomes the program's level.</summary>
    public void External(AppConfig app, float volume, bool muted)
    {
        var c = ChannelOf(app);
        // At 0 the channel says nothing about the program's own level.
        if (c.Volume > 0.01f) app.Volume = MixConfig.Level(volume / c.Volume);
        // A muted channel mutes it again anyway.
        if (!c.Muted) app.Muted = muted;
    }

    /// <summary>A new channel just before the catch-all one, in a colour not taken yet.</summary>
    public ChannelConfig AddChannel()
    {
        var used = Config.Channels.Select(c => c.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var channel = new ChannelConfig
        {
            Id = "c" + Guid.NewGuid().ToString("N")[..8],
            Name = "Nuevo canal",
            Color = MixConfig.Palette.FirstOrDefault(p => !used.Contains(p)) ?? MixConfig.Palette[Config.Channels.Count % MixConfig.Palette.Length],
        };
        Config.Channels.Insert(Config.Channels.Count - 1, channel);
        return channel;
    }

    /// <summary>Its programs go to the catch-all channel.</summary>
    public bool RemoveChannel(ChannelConfig channel)
    {
        if (channel == CatchAll || !Config.Channels.Remove(channel)) return false;
        foreach (var app in Config.Apps.Values)
            if (app.Channel == channel.Id) app.Channel = CatchAll.Id;
        return true;
    }

    public static void Move(AppConfig app, ChannelConfig channel) => app.Channel = channel.Id;

    public static void NextColor(ChannelConfig channel) =>
        channel.Color = MixConfig.Palette[(Array.IndexOf(MixConfig.Palette, channel.Color.ToUpperInvariant()) + 1) % MixConfig.Palette.Length];
}
