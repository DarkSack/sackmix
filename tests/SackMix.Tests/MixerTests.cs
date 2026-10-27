using SackMix.Core;

namespace SackMix.Tests;

public class MixerTests
{
    private static Mixer Fresh() => new(MixConfig.Defaults());

    [Fact]
    public void FirstSightPlacesByRulesAndKeepsWindowsLevel()
    {
        var m = Fresh();
        var discord = m.App("Discord.exe", @"C:\Users\a\AppData\Local\Discord\app-1\Discord.exe", "Discord", 0.4f, false);
        Assert.Equal(Rules.Chat, discord.Channel);
        Assert.Equal(0.4f, discord.Volume);
        Assert.True(m.Config.Apps.ContainsKey("discord.exe"));

        // Seen again: what the user set stays, Windows' level does not overwrite it.
        Assert.Same(discord, m.App("discord.exe", null, "", 1f, true));
        Assert.Equal(0.4f, discord.Volume);
        Assert.False(discord.Muted);
    }

    [Theory]
    [InlineData("spotify.exe", null, Rules.Music)]
    [InlineData("javaw.exe", null, Rules.Game)]
    [InlineData("eldenring.exe", @"D:\SteamLibrary\steamapps\common\ELDEN RING\Game\eldenring.exe", Rules.Game)]
    [InlineData("chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", null)]
    [InlineData("wallpaper64.exe", @"C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe", null)]
    public void Rules_GuessTheUsualSuspects(string exe, string? path, string? channel) => Assert.Equal(channel, Rules.Guess(exe, path));

    [Fact]
    public void EffectiveIsProgramTimesChannelAndEitherMute()
    {
        var m = Fresh();
        var app = m.App("spotify.exe", null, "Spotify", 0.5f, false);
        m.ChannelOf(app).Volume = 0.6f;
        Assert.Equal((0.3f, false), m.Effective(app));
        m.ChannelOf(app).Muted = true;
        Assert.True(m.Effective(app).Muted);
    }

    [Fact]
    public void WindowsMixerChangeBecomesTheProgramLevel()
    {
        var m = Fresh();
        var app = m.App("spotify.exe", null, "Spotify", 1f, false);
        var channel = m.ChannelOf(app);
        channel.Volume = 0.5f;

        m.External(app, 0.25f, true);
        Assert.Equal(0.5f, app.Volume);
        Assert.True(app.Muted);

        // Above what the channel allows: the program goes to full, the channel still caps it.
        m.External(app, 0.9f, false);
        Assert.Equal(1f, app.Volume);
        Assert.Equal(0.5f, m.Effective(app).Volume);

        // A channel at 0 or muted cannot tell the program's own level or mute.
        channel.Volume = 0;
        channel.Muted = true;
        m.External(app, 0.3f, true);
        Assert.Equal(1f, app.Volume);
        Assert.False(app.Muted);
    }

    [Fact]
    public void ChannelsAreAddedBeforeTheCatchAllWhichCannotBeRemoved()
    {
        var m = Fresh();
        var catchAll = m.CatchAll;
        var added = m.AddChannel();
        Assert.Same(catchAll, m.CatchAll);
        Assert.Equal(added, m.Config.Channels[^2]);
        Assert.DoesNotContain(m.Config.Channels.Where(c => c != added), c => c.Color == added.Color);
        Assert.False(m.RemoveChannel(catchAll));

        var app = m.App("obs64.exe", null, "OBS", 1f, false);
        Mixer.Move(app, added);
        Assert.True(m.RemoveChannel(added));
        Assert.Same(catchAll, m.ChannelOf(app));
    }

    [Fact]
    public void NextColorCyclesThePalette()
    {
        var c = new ChannelConfig { Color = MixConfig.Palette[^1] };
        Mixer.NextColor(c);
        Assert.Equal(MixConfig.Palette[0], c.Color);
    }
}
