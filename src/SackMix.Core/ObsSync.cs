using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SackMix.Core;

/// <summary>
/// One OBS source per program, so each sound has its own fader. A program OBS already captures
/// (whatever its source is called) is left as it is; a new one goes next to the other programs
/// of its channel, so a scene collection with its own audio scenes ("[AUD] Voz") keeps its shape.
/// </summary>
public static class ObsSync
{
    /// <summary>OBS's "Application Audio Capture" (plugins/win-wasapi).</summary>
    public const string Kind = "wasapi_process_output_capture";

    /// <summary>Filters the old «Mejorar micro» button (up to 0.3.0) put on the microphones; the user's own stay.</summary>
    public const string OldFilterPrefix = "SackMix · ";

    // WINDOW_PRIORITY_EXE in libobs/util/windows/window-helpers.h: find the window by its program, whatever its title.
    private const int PriorityExe = 2;

    // OBS capturing itself would play its own output back into the stream.
    private static readonly HashSet<string> ObsOwn = new(StringComparer.OrdinalIgnoreCase) { "obs64.exe", "obs32.exe", "obs-browser-page.exe" };

    public sealed record Capture(string InputName, string Exe, string Title, string Channel);

    public sealed record Result(int Created, int Kept, IReadOnlyList<string> Errors);

    /// <summary>
    /// The programs the user put in a channel (not the catch-all one), running or not, each named
    /// after itself ("Discord", "Spotify"). Two programs with the same name get their exe added.
    /// </summary>
    public static List<Capture> Plan(Mixer mixer)
    {
        var apps = mixer.Config.Apps
            .Where(kv => kv.Key != Mixer.SystemSounds && !ObsOwn.Contains(kv.Key) && mixer.ChannelOf(kv.Value) != mixer.CatchAll)
            .Select(kv => (Exe: kv.Key, Title: kv.Value.Name.Length > 0 ? kv.Value.Name : Path.GetFileNameWithoutExtension(kv.Key), Channel: mixer.ChannelOf(kv.Value).Name))
            .ToList();
        var twice = apps.GroupBy(a => a.Title, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return apps
            .Select(a => new Capture(twice.Contains(a.Title) ? $"{a.Title} ({a.Exe})" : a.Title, a.Exe, a.Title, a.Channel))
            .OrderBy(c => c.InputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>OBS's window setting: "title:class:exe", with '#' and ':' escaped as libobs does.</summary>
    public static string Window(string title, string windowClass, string exe) => $"{Escape(title)}:{Escape(windowClass)}:{Escape(exe)}";

    /// <summary>The program in an OBS window setting ("Discord:Chrome_WidgetWin_1:Discord.exe" → "discord.exe").</summary>
    public static string? ExeOf(string? window)
    {
        if (string.IsNullOrEmpty(window)) return null;
        var exe = window[(window.LastIndexOf(':') + 1)..].Replace("#3A", ":").Replace("#22", "#");
        return exe.Length > 0 ? exe.ToLowerInvariant() : null;
    }

    private static string Escape(string s) => s.Replace("#", "#22").Replace(":", "#3A");

    /// <summary>"[AUD] Música" and "musica" are the same name.</summary>
    public static string Plain(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        return b.ToString();
    }

    /// <summary>
    /// Where a new program of this channel goes: the scene that holds the sources of the channel's
    /// other programs, else a scene named like the channel ("[AUD] Música" for Música), else the current one.
    /// </summary>
    public static string SceneFor(string channel, IEnumerable<string> siblingScenes, IReadOnlyList<string> scenes, string current)
    {
        if (siblingScenes.FirstOrDefault() is { } sibling) return sibling;
        var plain = Plain(channel);
        return scenes.FirstOrDefault(s => Plain(s) == "aud" + plain)
            ?? scenes.FirstOrDefault(s => Plain(s) == plain)
            ?? current;
    }

    public static async Task<Result> RunAsync(ObsClient obs, IReadOnlyList<Capture> plan, CancellationToken ct)
    {
        var current = await obs.RequestAsync("GetCurrentProgramScene", null, ct);
        // obs-websocket before 5.3 only has the currentProgram- name.
        var currentScene = (current.TryGetProperty("sceneName", out var cs) ? cs : current.GetProperty("currentProgramSceneName")).GetString()!;

        var inputs = new Dictionary<string, string?>();
        foreach (var i in (await obs.RequestAsync("GetInputList", null, ct)).GetProperty("inputs").EnumerateArray())
            inputs[i.GetProperty("inputName").GetString()!] = i.GetProperty("inputKind").GetString();

        // Which program each existing capture follows, whatever the user called it.
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, kind) in inputs)
        {
            if (kind != Kind) continue;
            var settings = (await obs.RequestAsync("GetInputSettings", new JsonObject { ["inputName"] = name }, ct)).GetProperty("inputSettings");
            if (ExeOf(settings.TryGetProperty("window", out var w) ? w.GetString() : null) is { } exe) captured.TryAdd(exe, name);
        }

        // The scene each source sits in.
        var scenes = new List<string>();
        var sceneOf = new Dictionary<string, string>();
        foreach (var sc in (await obs.RequestAsync("GetSceneList", null, ct)).GetProperty("scenes").EnumerateArray())
        {
            var scene = sc.GetProperty("sceneName").GetString()!;
            scenes.Add(scene);
            foreach (var item in (await obs.RequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = scene }, ct)).GetProperty("sceneItems").EnumerateArray())
                sceneOf.TryAdd(item.GetProperty("sourceName").GetString()!, scene);
        }

        int created = 0, kept = 0;
        var errors = new List<string>();
        foreach (var c in plan)
        {
            if (captured.ContainsKey(c.Exe))
            {
                kept++;
                continue;
            }
            if (inputs.ContainsKey(c.InputName))
            {
                errors.Add($"Ya hay en OBS una fuente «{c.InputName}» que no captura {c.Exe}.");
                continue;
            }
            var siblings = plan.Where(o => o.Channel == c.Channel && captured.ContainsKey(o.Exe))
                .Select(o => sceneOf.GetValueOrDefault(captured[o.Exe])).OfType<string>();
            var scene = SceneFor(c.Channel, siblings, scenes, currentScene);
            try
            {
                await obs.RequestAsync("CreateInput", new JsonObject
                {
                    ["sceneName"] = scene,
                    ["inputName"] = c.InputName,
                    ["inputKind"] = Kind,
                    ["inputSettings"] = new JsonObject { ["window"] = Window(c.Title, "", c.Exe), ["priority"] = PriorityExe },
                    ["sceneItemEnabled"] = true,
                }, ct);
                inputs[c.InputName] = Kind;
                captured[c.Exe] = c.InputName;
                sceneOf[c.InputName] = scene;
                created++;
            }
            catch (ObsException e)
            {
                errors.Add($"«{c.InputName}»: {e.Message}");
            }
        }
        await RemoveMicFiltersAsync(obs, inputs, ct);
        return new Result(created, kept, errors);
    }

    private static async Task RemoveMicFiltersAsync(ObsClient obs, Dictionary<string, string?> inputs, CancellationToken ct)
    {
        foreach (var (input, kind) in inputs)
        {
            if (kind != "wasapi_input_capture") continue;
            try
            {
                var filters = (await obs.RequestAsync("GetSourceFilterList", new JsonObject { ["sourceName"] = input }, ct)).GetProperty("filters");
                foreach (var f in filters.EnumerateArray().Select(f => f.GetProperty("filterName").GetString()).Where(n => n?.StartsWith(OldFilterPrefix, StringComparison.Ordinal) == true).ToList())
                    await obs.RequestAsync("RemoveSourceFilter", new JsonObject { ["sourceName"] = input, ["filterName"] = f }, ct);
            }
            catch (ObsException) { } // not worth failing the whole sync over
        }
    }
}
