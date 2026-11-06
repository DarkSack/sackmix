using SackMix.Core;

namespace SackMix.Tests;

public class ProfilesTests
{
    [Fact]
    public void Saves_and_reads_back_levels_by_name_without_case()
    {
        var config = MixConfig.Defaults();
        config.Channels[0].Volume = 0.4f;
        config.Channels[1].Muted = true;
        config.Channels[2].Auto = true;
        Profiles.Save(config, " Streaming ");
        config.Channels[0].Volume = 1f;

        Assert.True(Profiles.Has(config, "streaming"));
        var levels = Profiles.Levels(config, "STREAMING").ToList();
        Assert.Equal(4, levels.Count);
        Assert.Equal(0.4f, levels[0].Level.Volume);
        Assert.True(levels[1].Level.Muted);
        Assert.True(levels[2].Level.Auto);
        Assert.Empty(Profiles.Levels(config, "Gaming"));
        Assert.True(Profiles.Forget(config, "streaming"));
        Assert.False(Profiles.Has(config, "Streaming"));
    }

    [Fact]
    public void Channels_deleted_after_saving_are_skipped()
    {
        var config = MixConfig.Defaults();
        Profiles.Save(config, "Gaming");
        config.Channels.RemoveAt(1);
        Assert.Equal(3, Profiles.Levels(config, "Gaming").Count());
    }

    [Fact]
    public void Parses_commands()
    {
        Assert.Equal("Streaming", Profiles.ParseCommand("perfil Streaming\r"));
        Assert.Equal("Mi espacio", Profiles.ParseCommand("PERFIL  Mi espacio "));
        Assert.Null(Profiles.ParseCommand("perfil "));
        Assert.Null(Profiles.ParseCommand("borrar todo"));
        Assert.Null(Profiles.ParseCommand(null));
    }

    [Fact]
    public void Repairs_profiles_from_a_hand_edited_file()
    {
        var config = new MixConfig
        {
            Profiles = new()
            {
                ["Streaming"] = new() { ["juego"] = new ChannelLevel { Volume = 3f } },
                ["streaming"] = new() { ["juego"] = new ChannelLevel { Volume = 0.5f } },
                [" "] = new(),
            },
        }.Normalize();
        var only = Assert.Single(config.Profiles);
        Assert.Equal("streaming", only.Key);
        Assert.InRange(only.Value["juego"].Volume, 0f, 1f);
    }
}

public class ProfilesStoreTests
{
    [Fact]
    public void Profiles_survive_saving()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("sackmix-").FullName, "config.json");
        var config = MixConfig.Defaults();
        config.Channels[0].Volume = 0.25f;
        Profiles.Save(config, "Streaming");
        ConfigStore.Save(path, config);
        Assert.Equal(0.25f, ConfigStore.Load(path).Profiles["streaming"][config.Channels[0].Id].Volume);
    }
}
