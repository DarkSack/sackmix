using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SackMix.Core;

namespace SackMix.Tests;

public class ObsTests
{
    // Example from obs-websocket's protocol.md; the expected string was computed apart, with Python's hashlib.
    private const string Salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=";
    private const string Challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=";
    private const string Expected = "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=";

    [Fact]
    public void AuthMatchesTheProtocolExample() => Assert.Equal(Expected, ObsClient.Auth("supersecretpassword", Salt, Challenge));

    [Fact]
    public void WindowEscapesLikeLibobs() => Assert.Equal("a#3Ab#22c::x.exe", ObsSync.Window("a:b#c", "", "x.exe"));

    [Fact]
    public void ReadsOnlyAnEnabledServer()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{ "server_enabled": true, "server_port": 24455, "auth_required": true, "server_password": "pw" }""");
            Assert.Equal(new ObsServer(24455, "pw"), ObsServer.Read(file));
            File.WriteAllText(file, """{ "server_enabled": false, "server_port": 4455 }""");
            Assert.Throws<ObsException>(() => ObsServer.Read(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PlanNamesSourcesAfterTheirChannel()
    {
        var m = new Mixer(MixConfig.Defaults());
        m.App("discord.exe", null, "Discord", 1, false);
        m.App("spotify.exe", null, "Spotify", 1, false);
        m.App("tidal.exe", null, "TIDAL", 1, false);
        m.App("chrome.exe", null, "Google Chrome", 1, false);   // unplaced and running: goes to Otros
        m.App("notepad.exe", null, "Bloc de notas", 1, false);  // unplaced and closed: left out
        m.App(Mixer.SystemSounds, null, "Sonidos del sistema", 1, false);

        var plan = ObsSync.Plan(m, new HashSet<string> { "chrome.exe", "discord.exe", Mixer.SystemSounds });

        Assert.Equal(
            ["Discord", "Música · Spotify", "Música · TIDAL", "Otros"],
            plan.Select(p => p.InputName));
        Assert.Equal("chrome.exe", plan[^1].Exe);
    }

    [Fact]
    public async Task SyncCreatesMissingSourcesAndUpdatesOurs()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var requests = new List<JsonNode>();
        var server = FakeObs(listener, "pw", requests);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (var obs = await ObsClient.ConnectAsync(new Uri($"ws://localhost:{port}/"), "pw", cts.Token))
        {
            var r = await ObsSync.RunAsync(obs, [
                new("Discord", "discord.exe", "Discord"),
                new("Música", "spotify.exe", "Spotify"),
                new("Juego", "javaw.exe", "Minecraft (Java)"),
            ], cts.Token);

            Assert.Equal(1, r.Created);
            Assert.Equal(1, r.Updated);
            Assert.Contains("«Juego»", Assert.Single(r.Errors));
        }
        await server;

        var create = requests.Single(q => (string?)q["requestType"] == "CreateInput")["requestData"]!;
        Assert.Equal("Escena", (string?)create["sceneName"]);
        Assert.Equal("Música", (string?)create["inputName"]);
        Assert.Equal(ObsSync.Kind, (string?)create["inputKind"]);
        Assert.Equal("Spotify::spotify.exe", (string?)create["inputSettings"]!["window"]);
        Assert.Equal(2, (int?)create["inputSettings"]!["priority"]);
        var update = requests.Single(q => (string?)q["requestType"] == "SetInputSettings")["requestData"]!;
        Assert.Equal("Discord", (string?)update["inputName"]);
    }

    [Fact]
    public async Task WrongPasswordIsReported()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var server = FakeObs(listener, "pw", []);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var e = await Assert.ThrowsAsync<ObsException>(() => ObsClient.ConnectAsync(new Uri($"ws://localhost:{port}/"), "otra", cts.Token));
        Assert.Equal("OBS rechazó la contraseña.", e.Message);
        await server;
    }

    /// <summary>Just enough of obs-websocket to answer what <see cref="ObsSync"/> asks.</summary>
    private static async Task FakeObs(HttpListener listener, string password, List<JsonNode> requests)
    {
        var ws = (await (await listener.GetContextAsync()).AcceptWebSocketAsync(null)).WebSocket;
        await Send(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject
        {
            ["rpcVersion"] = 1,
            ["authentication"] = new JsonObject { ["challenge"] = Challenge, ["salt"] = Salt },
        } });
        var identify = await Receive(ws);
        if ((string?)identify!["d"]!["authentication"] != ObsClient.Auth(password, Salt, Challenge))
        {
            await ws.CloseOutputAsync((WebSocketCloseStatus)4009, "Authentication failed.", CancellationToken.None);
            return;
        }
        await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });

        // The client hangs up without a close handshake when it is done.
        while (await Receive(ws).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null) is { } message)
        {
            var d = message["d"]!;
            requests.Add(d.DeepClone());
            var type = (string)d["requestType"]!;
            JsonNode? data = type switch
            {
                "GetCurrentProgramScene" => new JsonObject { ["sceneName"] = "Escena" },
                "GetInputList" => new JsonObject { ["inputs"] = new JsonArray(
                    new JsonObject { ["inputName"] = "Discord", ["inputKind"] = ObsSync.Kind },
                    new JsonObject { ["inputName"] = "Juego", ["inputKind"] = "image_source" }) },
                _ => null,
            };
            await Send(ws, new JsonObject { ["op"] = 7, ["d"] = new JsonObject
            {
                ["requestType"] = type,
                ["requestId"] = (string)d["requestId"]!,
                ["requestStatus"] = new JsonObject { ["result"] = true, ["code"] = 100 },
                ["responseData"] = data,
            } });
        }
    }

    private static Task Send(WebSocket ws, JsonObject message) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonNode?> Receive(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
        return r.MessageType == WebSocketMessageType.Close ? null : JsonNode.Parse(buffer.AsSpan(0, r.Count));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
