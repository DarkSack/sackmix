namespace SackMix.Core;

/// <summary>First guess of a channel for a program nobody has placed yet.</summary>
public static class Rules
{
    public const string Game = "juego", Chat = "chat", Music = "musica";

    private static readonly HashSet<string> ChatExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.exe", "discordptb.exe", "discordcanary.exe", "teamspeak.exe", "ts3client_win64.exe", "mumble.exe", "guilded.exe",
    };

    private static readonly HashSet<string> MusicExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "applemusic.exe", "itunes.exe", "tidal.exe", "deezer.exe", "amazon music.exe", "foobar2000.exe",
        "aimp.exe", "musicbee.exe", "youtube music.exe",
    };

    // Minecraft runs as javaw.exe (Java edition) or Minecraft.Windows.exe (Bedrock).
    private static readonly HashSet<string> GameExes = new(StringComparer.OrdinalIgnoreCase) { "javaw.exe", "minecraft.windows.exe" };

    // Installed through Steam, but not games.
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase) { "wallpaper32.exe", "wallpaper64.exe" };

    private static readonly string[] GameFolders =
    [
        @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\xboxgames\", @"\gog games\", @"\ubisoft game launcher\games\", @"\ea games\",
    ];

    public static string? Guess(string exe, string? path) =>
        ChatExes.Contains(exe) ? Chat
        : MusicExes.Contains(exe) ? Music
        : GameExes.Contains(exe) || (!NotGames.Contains(exe) && path is not null && GameFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase))) ? Game
        : null;
}
