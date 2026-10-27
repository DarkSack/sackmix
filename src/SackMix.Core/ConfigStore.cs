using System.Text.Json;
using System.Text.Json.Serialization;

namespace SackMix.Core;

/// <summary>The settings file, %LOCALAPPDATA%\SackMix\config.json.</summary>
public static class ConfigStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SackMix");
    public static string DefaultPath => Path.Combine(Folder, "config.json");

    public static MixConfig Load(string path)
    {
        if (!File.Exists(path)) return MixConfig.Defaults();
        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllBytes(path), ConfigJson.Default.MixConfig) ?? MixConfig.Defaults()).Normalize();
        }
        catch (JsonException)
        {
            // Kept aside, so the next save does not wipe what the user had.
            File.Copy(path, path + ".roto", overwrite: true);
            return MixConfig.Defaults();
        }
    }

    /// <summary>Written next to the old file and swapped in: a crash never leaves half a file.</summary>
    public static void Save(string path, MixConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(config, ConfigJson.Default.MixConfig));
        File.Move(tmp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MixConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
