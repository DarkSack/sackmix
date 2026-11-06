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
    public void PlanSendsTheProgramsPlacedInAChannel()
    {
        var m = new Mixer(MixConfig.Defaults());
        m.App("discord.exe", null, "Discord", 1, false);
        m.App("spotify.exe", null, "Spotify", 1, false);
        m.App("tidal.exe", null, "TIDAL", 1, false);
        m.App("chrome.exe", null, "Google Chrome", 1, false);   // unplaced: Otros, not sent
        m.App("obs64.exe", null, "OBS Studio", 1, false);
        Mixer.Move(m.Config.Apps["obs64.exe"], m.Config.Channels[0]); // never OBS itself
        m.App(Mixer.SystemSounds, null, "Sonidos del sistema", 1, false);

        var plan = ObsSync.Plan(m);

        Assert.Equal(["Discord", "Spotify", "TIDAL"], plan.Select(p => p.InputName));
        Assert.Equal("Música", plan[1].Channel);
    }

    [Fact]
    public void ReadsTheProgramOfAWindowSetting()
    {
        Assert.Equal("discord.exe", ObsSync.ExeOf("Discord:Chrome_WidgetWin_1:Discord.exe"));
        Assert.Equal("fortniteclient-win64-shipping.exe", ObsSync.ExeOf("Fortnite  :UnrealWindow:FortniteClient-Win64-Shipping.exe"));
        Assert.Equal("a:b.exe", ObsSync.ExeOf(ObsSync.Window("x", "", "a:b.exe")));
        Assert.Null(ObsSync.ExeOf(""));
    }

    [Fact]
    public void NewProgramsGoNextToTheirChannel()
    {
        string[] scenes = ["02 GAMEPLAY", "[AUD] Voz", "[AUD] Juego", "[AUD] Musica"];
        Assert.Equal("[AUD] Voz", ObsSync.SceneFor("Discord", ["[AUD] Voz"], scenes, "02 GAMEPLAY"));
        Assert.Equal("[AUD] Musica", ObsSync.SceneFor("Música", [], scenes, "02 GAMEPLAY"));
        Assert.Equal("02 GAMEPLAY", ObsSync.SceneFor("Chat", [], scenes, "02 GAMEPLAY"));
    }

    [Fact]
    public async Task SyncKeepsWhatOBSCapturesAndAddsTheRest()
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
                new("Discord", "discord.exe", "Discord", "Discord"),
                new("Brave", "brave.exe", "Brave", "Música"),
                new("Spotify", "spotify.exe", "Spotify", "Música"),
                new("Minecraft (Java)", "javaw.exe", "Minecraft (Java)", "Juego"),
            ], cts.Token);

            Assert.Equal(1, r.Created);
            Assert.Equal(2, r.Kept);
            Assert.Contains("«Minecraft (Java)»", Assert.Single(r.Errors));
        }
        await server;

        var create = requests.Single(q => (string?)q["requestType"] == "CreateInput")["requestData"]!;
        Assert.Equal("[AUD] Musica", (string?)create["sceneName"]); // next to Brave, the other program of Música
        Assert.Equal("Spotify", (string?)create["inputName"]);
        Assert.Equal(ObsSync.Kind, (string?)create["inputKind"]);
        Assert.Equal("Spotify::spotify.exe", (string?)create["inputSettings"]!["window"]);
        Assert.Equal(2, (int?)create["inputSettings"]!["priority"]);
        Assert.DoesNotContain(requests, q => (string?)q["requestType"] == "SetInputSettings");
        // The old «Mejorar micro» filters go; the user's own filter stays.
        Assert.Equal(["SackMix · Ruido", "SackMix · Tope"],
            requests.Where(q => (string?)q["requestType"] == "RemoveSourceFilter").Select(q => (string?)q["requestData"]!["filterName"]));
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
                    new JsonObject { ["inputName"] = "Voz Discord", ["inputKind"] = ObsSync.Kind },
                    new JsonObject { ["inputName"] = "Musica Navegador", ["inputKind"] = ObsSync.Kind },
                    new JsonObject { ["inputName"] = "Minecraft (Java)", ["inputKind"] = "image_source" },
                    new JsonObject { ["inputName"] = "Mic", ["inputKind"] = "wasapi_input_capture" }) },
                "GetInputSettings" => new JsonObject { ["inputSettings"] = new JsonObject
                {
                    ["window"] = (string?)d["requestData"]!["inputName"] == "Voz Discord" ? "Discord:Chrome_WidgetWin_1:Discord.exe" : "Brave:Chrome_WidgetWin_1:brave.exe",
                } },
                "GetSceneList" => new JsonObject { ["scenes"] = new JsonArray(
                    new JsonObject { ["sceneName"] = "Escena" },
                    new JsonObject { ["sceneName"] = "[AUD] Voz" },
                    new JsonObject { ["sceneName"] = "[AUD] Musica" }) },
                "GetSceneItemList" => new JsonObject { ["sceneItems"] = (string?)d["requestData"]!["sceneName"] switch
                {
                    "[AUD] Voz" => new JsonArray(new JsonObject { ["sourceName"] = "Voz Discord" }, new JsonObject { ["sourceName"] = "Mic" }),
                    "[AUD] Musica" => new JsonArray(new JsonObject { ["sourceName"] = "Musica Navegador" }),
                    _ => new JsonArray(new JsonObject { ["sourceName"] = "[AUD] Voz" }),
                } },
                "GetSourceFilterList" => new JsonObject { ["filters"] = new JsonArray(
                    new JsonObject { ["filterName"] = "SackMix · Ruido" },
                    new JsonObject { ["filterName"] = "Mi compresor" },
                    new JsonObject { ["filterName"] = "SackMix · Tope" }) },
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
