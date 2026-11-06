namespace SackMix.Core;

/// <summary>
/// The output OBS plays its monitored sources on (Settings › Audio › Advanced › Monitoring Device).
/// obs-websocket cannot change it, so it is written in the current profile's basic.ini, which OBS
/// only reads at start and rewrites on exit: OBS has to be closed.
/// </summary>
public static class ObsMonitor
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");

    /// <summary>basic.ini of the profile OBS opens with (user.ini since OBS 31, global.ini before).</summary>
    /// <exception cref="ObsException">OBS is not installed or has no profile.</exception>
    public static string ProfileFile(string obsFolder)
    {
        foreach (var name in new[] { "user.ini", "global.ini" })
        {
            var file = Path.Combine(obsFolder, name);
            if (!File.Exists(file)) continue;
            if (Get(File.ReadAllText(file), "Basic", "ProfileDir") is { Length: > 0 } dir)
            {
                var basic = Path.Combine(obsFolder, "basic", "profiles", dir, "basic.ini");
                if (File.Exists(basic)) return basic;
            }
        }
        throw new ObsException("No encontré tu perfil de OBS.");
    }

    /// <summary>The device id OBS has now ("default" = Windows' default output).</summary>
    public static string Current(string basicIni) => Get(File.ReadAllText(basicIni), "Audio", "MonitoringDeviceId") ?? "default";

    /// <param name="id">A Windows output id, or "default".</param>
    public static void Set(string basicIni, string id, string name)
    {
        var text = File.ReadAllText(basicIni);
        text = With(text, "Audio", "MonitoringDeviceId", id);
        text = With(text, "Audio", "MonitoringDeviceName", name);
        File.WriteAllText(basicIni, text);
    }

    public static string? Get(string ini, string section, string key)
    {
        var inside = false;
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) inside = line == $"[{section}]";
            else if (inside && line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..];
        }
        return null;
    }

    /// <summary>The ini with key=value in [section]: replaced if there, else added (with the section if needed).</summary>
    public static string With(string ini, string section, string key, string value)
    {
        var newline = ini.Contains("\r\n") ? "\r\n" : "\n";
        var lines = ini.Split(newline).ToList();
        var start = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange(["", $"[{section}]", $"{key}={value}", ""]);
            return string.Join(newline, lines);
        }
        var end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        for (var i = start + 1; i < end; i++)
            if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.Ordinal))
            {
                lines[i] = $"{key}={value}";
                return string.Join(newline, lines);
            }
        // After the section's last line with text, before the blank line that separates it.
        var at = end;
        while (at > start + 1 && lines[at - 1].Trim().Length == 0) at--;
        lines.Insert(at, $"{key}={value}");
        return string.Join(newline, lines);
    }
}
