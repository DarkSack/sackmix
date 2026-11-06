namespace SackMix.Core;

/// <summary>
/// An output with a name of its own ("Discord", "Música"): the user hears it at its own level,
/// and in OBS it becomes a source with that name.
/// </summary>
public sealed class ChannelConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"#RRGGBB".</summary>
    public string Color { get; set; } = MixConfig.Palette[0];
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
    /// <summary>Normalise each of its programs (<see cref="AutoLevel"/>).</summary>
    public bool Auto { get; set; }
    /// <summary>Output its programs play on (Windows' MMDevice id); null = Windows' own output.</summary>
    public string? Device { get; set; }
    /// <summary>That output's name, to show it while it is unplugged.</summary>
    public string? DeviceName { get; set; }
}

/// <summary>What the user chose for one program, kept by exe name ("discord.exe").</summary>
public sealed class AppConfig
{
    /// <summary>Channel id; null = never placed (it goes to the catch-all channel).</summary>
    public string? Channel { get; set; }
    /// <summary>Last name seen, for OBS while the program is closed.</summary>
    public string Name { get; set; } = "";
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
    /// <summary>SackMix sent it to an output: Windows remembers that, so it is undone when its channel goes back to Windows' output.</summary>
    public bool Routed { get; set; }
}

public sealed class MixConfig
{
    public static readonly string[] Palette =
        ["#00E5FF", "#8B5CFF", "#1ED760", "#FFB020", "#FF3D7F", "#FF6B2C", "#3D8BFF", "#E4FF3D"];

    public List<ChannelConfig> Channels { get; set; } = [];
    /// <summary>Key: exe name in lower case.</summary>
    public Dictionary<string, AppConfig> Apps { get; set; } = [];
    /// <summary>Saved mixes by name in lower case (see <see cref="Core.Profiles"/>): channel id → level.</summary>
    public Dictionary<string, Dictionary<string, ChannelLevel>> Profiles { get; set; } = [];

    public static MixConfig Defaults() => new()
    {
        Channels =
        [
            new() { Id = Rules.Game, Name = "Juego", Color = "#00E5FF" },
            new() { Id = Rules.Chat, Name = "Discord", Color = "#8B5CFF" },
            new() { Id = Rules.Music, Name = "Música", Color = "#1ED760" },
            new() { Id = "otros", Name = "Otros", Color = "#FFB020" },
        ],
    };

    /// <summary>Repairs a damaged or hand-edited file instead of refusing it.</summary>
    public MixConfig Normalize()
    {
        Channels = (Channels ?? []).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Id)).DistinctBy(c => c.Id).ToList();
        if (Channels.Count == 0) Channels = Defaults().Channels;
        foreach (var c in Channels)
        {
            c.Name = string.IsNullOrWhiteSpace(c.Name) ? "Canal" : c.Name.Trim();
            c.Color = IsColor(c.Color) ? c.Color.ToUpperInvariant() : Palette[0];
            c.Volume = Level(c.Volume);
        }
        Profiles = (Profiles ?? []).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .GroupBy(kv => Core.Profiles.Key(kv.Key))
            .ToDictionary(g => g.Key, g => g.First().Value.Where(l => l.Value is not null)
                .ToDictionary(l => l.Key, l => { l.Value.Volume = Level(l.Value.Volume); return l.Value; }));
        Apps = (Apps ?? []).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .GroupBy(kv => kv.Key.ToLowerInvariant())
            .ToDictionary(g => g.Key, g =>
            {
                var a = g.First().Value;
                a.Name ??= "";
                a.Volume = Level(a.Volume);
                return a;
            });
        return this;
    }

    internal static float Level(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 1f;

    private static bool IsColor(string? s) => s is { Length: 7 } && s[0] == '#' && !s.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF");
}
