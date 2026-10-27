using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SackMix.Core;

public sealed class ObsException(string message) : Exception(message);

/// <summary>OBS's own WebSocket settings, read from its config so the user types nothing.</summary>
public sealed record ObsServer(int Port, string? Password)
{
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");

    /// <exception cref="ObsException">OBS is not installed or its server is off.</exception>
    public static ObsServer Read(string path)
    {
        if (!File.Exists(path)) throw new ObsException("No encontré OBS en este equipo.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var r = doc.RootElement;
        if (!(r.TryGetProperty("server_enabled", out var on) && on.ValueKind == JsonValueKind.True))
            throw new ObsException("Activa el servidor en OBS: Herramientas → Ajustes del servidor WebSocket.");
        var port = r.TryGetProperty("server_port", out var p) && p.TryGetInt32(out var n) ? n : 4455;
        var auth = r.TryGetProperty("auth_required", out var a) && a.ValueKind == JsonValueKind.True;
        return new ObsServer(port, auth && r.TryGetProperty("server_password", out var pw) ? pw.GetString() : null);
    }
}

/// <summary>Just enough of obs-websocket 5: identify, then one request at a time.</summary>
public sealed class ObsClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private int _next;

    public static async Task<ObsClient> ConnectAsync(Uri uri, string? password, CancellationToken ct)
    {
        var obs = new ObsClient();
        try
        {
            try { await obs._ws.ConnectAsync(uri, ct); }
            catch (WebSocketException) { throw new ObsException("OBS no está abierto."); }

            var hello = (await obs.ReceiveAsync(ct)).GetProperty("d");
            string? auth = null;
            if (hello.TryGetProperty("authentication", out var a))
            {
                if (password is null) throw new ObsException("OBS pide contraseña y no está en su configuración.");
                auth = Auth(password, a.GetProperty("salt").GetString()!, a.GetProperty("challenge").GetString()!);
            }
            await obs.SendAsync(new JsonObject
            {
                ["op"] = 1,
                ["d"] = new JsonObject { ["rpcVersion"] = 1, ["authentication"] = auth, ["eventSubscriptions"] = 0 },
            }, ct);
            if ((await obs.ReceiveAsync(ct)).GetProperty("op").GetInt32() != 2) throw new ObsException("OBS no aceptó la conexión.");
            return obs;
        }
        catch
        {
            obs.Dispose();
            throw;
        }
    }

    public static string Auth(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <returns>The responseData (default when there is none).</returns>
    /// <exception cref="ObsException">OBS refused the request.</exception>
    public async Task<JsonElement> RequestAsync(string type, JsonObject? data, CancellationToken ct)
    {
        var id = (++_next).ToString(CultureInfo.InvariantCulture);
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data is not null) d["requestData"] = data;
        await SendAsync(new JsonObject { ["op"] = 6, ["d"] = d }, ct);
        while (true)
        {
            var msg = await ReceiveAsync(ct);
            if (msg.GetProperty("op").GetInt32() != 7) continue;
            var r = msg.GetProperty("d");
            if (r.GetProperty("requestId").GetString() != id) continue;
            var status = r.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new ObsException(status.TryGetProperty("comment", out var c) ? c.GetString() ?? type : type);
            return r.TryGetProperty("responseData", out var response) ? response : default;
        }
    }

    private Task SendAsync(JsonObject message, CancellationToken ct) =>
        _ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, ct);

    private async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        while (true)
        {
            var r = await _ws.ReceiveAsync(buffer.GetMemory(4096), ct);
            if (r.MessageType == WebSocketMessageType.Close)
                throw new ObsException(_ws.CloseStatus == (WebSocketCloseStatus)4009 // AuthenticationFailed
                    ? "OBS rechazó la contraseña."
                    : $"OBS cerró la conexión ({_ws.CloseStatusDescription}).");
            buffer.Advance(r.Count);
            if (!r.EndOfMessage) continue;
            using var doc = JsonDocument.Parse(buffer.WrittenMemory);
            return doc.RootElement.Clone();
        }
    }

    public void Dispose() => _ws.Dispose();
}

/// <summary>One OBS source per program, with the channel's name, so each channel shows up apart in OBS.</summary>
public static class ObsSync
{
    /// <summary>OBS's "Application Audio Capture" (plugins/win-wasapi).</summary>
    public const string Kind = "wasapi_process_output_capture";

    // WINDOW_PRIORITY_EXE in libobs/util/windows/window-helpers.h: find the window by its program, whatever its title.
    private const int PriorityExe = 2;

    public sealed record Capture(string InputName, string Exe, string Title);

    public sealed record Result(int Created, int Updated, IReadOnlyList<string> Errors);

    /// <summary>
    /// The channel's name ("Discord") when it holds one program, "Música · Spotify" when it holds
    /// several. Programs running now, plus closed ones the user or the rules placed.
    /// </summary>
    public static List<Capture> Plan(Mixer mixer, IReadOnlySet<string> running)
    {
        var plan = new List<Capture>();
        foreach (var channel in mixer.Config.Channels)
        {
            var apps = mixer.Config.Apps
                .Where(kv => kv.Key != Mixer.SystemSounds && mixer.ChannelOf(kv.Value) == channel && (running.Contains(kv.Key) || kv.Value.Channel is not null))
                .Select(kv => (Exe: kv.Key, Title: kv.Value.Name.Length > 0 ? kv.Value.Name : Path.GetFileNameWithoutExtension(kv.Key)))
                .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var (exe, title) in apps)
                plan.Add(new Capture(apps.Count == 1 ? channel.Name : $"{channel.Name} · {title}", exe, title));
        }
        return plan;
    }

    /// <summary>OBS's window setting: "title:class:exe", with '#' and ':' escaped as libobs does.</summary>
    public static string Window(string title, string windowClass, string exe) => $"{Escape(title)}:{Escape(windowClass)}:{Escape(exe)}";

    private static string Escape(string s) => s.Replace("#", "#22").Replace(":", "#3A");

    /// <summary>Creates the missing sources in the current scene and points the existing ones at their program again.</summary>
    public static async Task<Result> RunAsync(ObsClient obs, IReadOnlyList<Capture> plan, CancellationToken ct)
    {
        var current = await obs.RequestAsync("GetCurrentProgramScene", null, ct);
        // obs-websocket before 5.3 only has the currentProgram- name.
        var scene = (current.TryGetProperty("sceneName", out var s) ? s : current.GetProperty("currentProgramSceneName")).GetString();
        var inputs = new Dictionary<string, string?>();
        foreach (var i in (await obs.RequestAsync("GetInputList", null, ct)).GetProperty("inputs").EnumerateArray())
            inputs[i.GetProperty("inputName").GetString()!] = i.GetProperty("inputKind").GetString();

        int created = 0, updated = 0;
        var errors = new List<string>();
        foreach (var c in plan)
        {
            var settings = new JsonObject { ["window"] = Window(c.Title, "", c.Exe), ["priority"] = PriorityExe };
            try
            {
                if (!inputs.TryGetValue(c.InputName, out var kind))
                {
                    await obs.RequestAsync("CreateInput", new JsonObject
                    {
                        ["sceneName"] = scene,
                        ["inputName"] = c.InputName,
                        ["inputKind"] = Kind,
                        ["inputSettings"] = settings,
                        ["sceneItemEnabled"] = true,
                    }, ct);
                    inputs[c.InputName] = Kind;
                    created++;
                }
                else if (kind == Kind)
                {
                    await obs.RequestAsync("SetInputSettings", new JsonObject
                    {
                        ["inputName"] = c.InputName,
                        ["inputSettings"] = settings,
                        ["overlay"] = true,
                    }, ct);
                    updated++;
                }
                else errors.Add($"«{c.InputName}» ya existe en OBS y no es una captura de audio.");
            }
            catch (ObsException e)
            {
                errors.Add($"«{c.InputName}»: {e.Message}");
            }
        }
        return new Result(created, updated, errors);
    }
}
