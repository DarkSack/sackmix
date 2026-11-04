using System.Text.Json;
using System.Text.Json.Nodes;

namespace SackMix.Core;

/// <summary>
/// The streamer's voice in OBS, cleaned by OBS's own audio filters: background noise out, the
/// room quiet between words, one steady level and never a clip. SackMix only sets them up;
/// OBS does the processing in real time, with no extra delay and no driver. Only filters named
/// "SackMix · …" are created or changed: the user's own filters are left as they are.
/// </summary>
public static class ObsMic
{
    /// <summary>OBS's "Audio Input Capture" (plugins/win-wasapi).</summary>
    public const string MicKind = "wasapi_input_capture";

    public sealed record Filter(string Name, string Kind, JsonObject Settings);

    public sealed record Result(IReadOnlyList<string> Mics, string? Problem);

    /// <summary>In this order, each one working on what the previous one left.</summary>
    public static List<Filter> Chain(string noiseKind) =>
    [
        // RNNoise: a neural network that keeps the voice and drops the fan, the keyboard, the street.
        new("SackMix · Ruido", noiseKind, new JsonObject { ["method"] = "rnnoise" }),
        // A soft gate: what is left of the room between words goes quiet, without cutting soft words.
        new("SackMix · Silencios", "expander_filter", new JsonObject
        {
            ["presets"] = "expander", ["ratio"] = 3.0, ["threshold"] = -45.0, ["attack_time"] = 10, ["release_time"] = 120,
            ["detector"] = "RMS", ["output_gain"] = 0.0,
        }),
        // One level: loud speech comes down, everything goes up 6 dB, so whispers and shouts sit close.
        new("SackMix · Nivel", "compressor_filter", new JsonObject
        {
            ["ratio"] = 4.0, ["threshold"] = -20.0, ["attack_time"] = 5, ["release_time"] = 80, ["output_gain"] = 6.0,
        }),
        // A ceiling at -2 dB: a sudden shout or a slap on the desk never clips the stream.
        new("SackMix · Tope", "limiter_filter", new JsonObject { ["threshold"] = -2.0, ["release_time"] = 60 }),
    ];

    /// <summary>Puts the chain on the user's microphone in OBS (see <see cref="PickMics"/>).</summary>
    public static async Task<Result> RunAsync(ObsClient obs, CancellationToken ct)
    {
        var inputs = new List<(string Name, string? Device)>();
        foreach (var i in (await obs.RequestAsync("GetInputList", new JsonObject { ["inputKind"] = MicKind }, ct)).GetProperty("inputs").EnumerateArray())
        {
            var name = i.GetProperty("inputName").GetString()!;
            var settings = (await obs.RequestAsync("GetInputSettings", new JsonObject { ["inputName"] = name }, ct)).GetProperty("inputSettings");
            inputs.Add((name, settings.TryGetProperty("device_id", out var d) ? d.GetString() : null));
        }
        var (mics, problem) = PickMics(inputs);
        if (problem is not null) return new Result([], problem);

        var chain = Chain(await NoiseKindAsync(obs, ct));
        foreach (var mic in mics) await ApplyAsync(obs, mic, chain, ct);
        return new Result(mics, null);
    }

    /// <summary>
    /// The one microphone, or the ones on Windows' default microphone, or the ones with "mic" in
    /// their name. Never a guess among several other inputs (a capture card's line-in is not a voice).
    /// </summary>
    public static (List<string> Mics, string? Problem) PickMics(IReadOnlyList<(string Name, string? Device)> inputs)
    {
        if (inputs.Count == 0)
            return ([], "No encontré tu micrófono en OBS: añade una «Captura de entrada de audio» o activa Mic/Aux en Ajustes › Audio.");
        if (inputs.Count == 1) return ([inputs[0].Name], null);
        // An unset device_id is OBS's default: Windows' default microphone.
        var picked = inputs.Where(i => i.Device is null or "default").Select(i => i.Name).ToList();
        if (picked.Count == 0) picked = inputs.Where(i => i.Name.Contains("mic", StringComparison.OrdinalIgnoreCase)).Select(i => i.Name).ToList();
        return picked.Count > 0
            ? (picked, null)
            : ([], $"Tienes varias entradas de audio en OBS ({string.Join(", ", inputs.Select(i => i.Name))}): ponle «Mic» en el nombre a la de tu micrófono.");
    }

    // obs-websocket 5.4+ lists the filter kinds; an older one (or OBS's own id) gets the classic name.
    private static async Task<string> NoiseKindAsync(ObsClient obs, CancellationToken ct)
    {
        try
        {
            var kinds = (await obs.RequestAsync("GetSourceFilterKindList", null, ct)).GetProperty("sourceFilterKinds").EnumerateArray().Select(k => k.GetString()).ToList();
            if (kinds.Contains("noise_suppress_filter_v2")) return "noise_suppress_filter_v2";
        }
        catch (ObsException) { }
        catch (KeyNotFoundException) { }
        return "noise_suppress_filter";
    }

    private static async Task ApplyAsync(ObsClient obs, string mic, List<Filter> chain, CancellationToken ct)
    {
        var existing = (await obs.RequestAsync("GetSourceFilterList", new JsonObject { ["sourceName"] = mic }, ct))
            .GetProperty("filters").EnumerateArray().Select(f => f.GetProperty("filterName").GetString()).ToHashSet();
        var total = existing.Count;
        foreach (var f in chain)
        {
            if (existing.Contains(f.Name))
            {
                await obs.RequestAsync("SetSourceFilterSettings", new JsonObject
                {
                    ["sourceName"] = mic, ["filterName"] = f.Name, ["filterSettings"] = f.Settings.DeepClone(), ["overlay"] = true,
                }, ct);
                await obs.RequestAsync("SetSourceFilterEnabled", new JsonObject { ["sourceName"] = mic, ["filterName"] = f.Name, ["filterEnabled"] = true }, ct);
            }
            else
            {
                await obs.RequestAsync("CreateSourceFilter", new JsonObject
                {
                    ["sourceName"] = mic, ["filterName"] = f.Name, ["filterKind"] = f.Kind, ["filterSettings"] = f.Settings.DeepClone(),
                }, ct);
                total++;
            }
        }
        // Ours go last, in order, after whatever the user already had.
        for (var i = 0; i < chain.Count; i++)
            await obs.RequestAsync("SetSourceFilterIndex", new JsonObject
            {
                ["sourceName"] = mic, ["filterName"] = chain[i].Name, ["filterIndex"] = total - chain.Count + i,
            }, ct);
    }
}
