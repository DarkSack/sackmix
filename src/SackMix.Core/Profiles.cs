namespace SackMix.Core;

/// <summary>A channel's level inside a profile.</summary>
public sealed class ChannelLevel
{
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
    public bool Auto { get; set; }
}

/// <summary>
/// Named mixes ("Streaming", "Gaming"): each channel's volume, mute and normalising.
/// SackSpace asks for one by its workspace's name when the workspace changes; a name
/// without a saved mix leaves everything as it is. Names compare without case.
/// </summary>
public static class Profiles
{
    /// <summary>Local pipe SackSpace writes "perfil &lt;name&gt;" to. Per Windows user.</summary>
    public static string PipeName => "SackMix.Control." + Environment.UserName;

    public static string Key(string name) => name.Trim().ToLowerInvariant();

    public static void Save(MixConfig config, string name) =>
        config.Profiles[Key(name)] = config.Channels.ToDictionary(c => c.Id, c => new ChannelLevel { Volume = c.Volume, Muted = c.Muted, Auto = c.Auto });

    public static bool Forget(MixConfig config, string name) => config.Profiles.Remove(Key(name));

    public static bool Has(MixConfig config, string name) => config.Profiles.ContainsKey(Key(name));

    /// <summary>The saved level of each channel that still exists; empty when there is no such profile.</summary>
    public static IEnumerable<(ChannelConfig Channel, ChannelLevel Level)> Levels(MixConfig config, string name)
    {
        if (!config.Profiles.TryGetValue(Key(name), out var levels)) yield break;
        foreach (var c in config.Channels)
            if (levels.TryGetValue(c.Id, out var l)) yield return (c, l);
    }

    /// <summary>"perfil Streaming" → "Streaming"; anything else → null.</summary>
    public static string? ParseCommand(string? line)
    {
        const string verb = "perfil ";
        var t = line?.Trim();
        return t is not null && t.StartsWith(verb, StringComparison.OrdinalIgnoreCase) && t.Length > verb.Length ? t[verb.Length..].Trim() : null;
    }
}
