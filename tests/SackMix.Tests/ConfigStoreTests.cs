using SackMix.Core;

namespace SackMix.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sackmix-").FullName;
    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public void RoundTrip()
    {
        var config = MixConfig.Defaults();
        config.Channels[1].Name = "Amigos";
        config.Channels[1].Volume = 0.7f;
        config.Apps["discord.exe"] = new AppConfig { Channel = Rules.Chat, Name = "Discord", Volume = 0.8f, Muted = true };

        ConfigStore.Save(PathOf("config.json"), config);
        var back = ConfigStore.Load(PathOf("config.json"));

        Assert.Equal("Amigos", back.Channels[1].Name);
        Assert.Equal(0.7f, back.Channels[1].Volume);
        Assert.True(back.Apps["discord.exe"].Muted);
        Assert.False(File.Exists(PathOf("config.json.tmp")));
    }

    [Fact]
    public void MissingFileGivesDefaults() => Assert.Equal(4, ConfigStore.Load(PathOf("nada.json")).Channels.Count);

    [Fact]
    public void BrokenFileIsKeptAside()
    {
        File.WriteAllText(PathOf("config.json"), "{ esto no es json");
        Assert.Equal(4, ConfigStore.Load(PathOf("config.json")).Channels.Count);
        Assert.Equal("{ esto no es json", File.ReadAllText(PathOf("config.json.roto")));
    }

    [Fact]
    public void HandEditedValuesAreRepaired()
    {
        File.WriteAllText(PathOf("config.json"), """
            {
              "channels": [ { "id": "a", "name": "  ", "color": "rojo", "volume": 7 }, { "id": "a" }, { "id": "" } ],
              "apps": { "Spotify.EXE": { "volume": -1 } }
            }
            """);
        var c = ConfigStore.Load(PathOf("config.json"));
        var only = Assert.Single(c.Channels);
        Assert.Equal(("Canal", MixConfig.Palette[0], 1f), (only.Name, only.Color, only.Volume));
        Assert.Equal(0f, c.Apps["spotify.exe"].Volume);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
