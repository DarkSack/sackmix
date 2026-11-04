using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using SackMix.Core;

namespace SackMix.Tests;

public class ObsMicTests
{
    [Fact]
    public void OneInputIsTheMic() =>
        Assert.Equal(["Mic"], ObsMic.PickMics([("Mic", "{id}")]).Mics);

    [Fact]
    public void AmongSeveralTheDefaultMicWins()
    {
        // A capture card's line-in must not get a voice chain.
        var (mics, problem) = ObsMic.PickMics([("Capturadora", "{card}"), ("Mic/Aux", "default"), ("Voz", null)]);
        Assert.Null(problem);
        Assert.Equal(["Mic/Aux", "Voz"], mics);
    }

    [Fact]
    public void ThenTheOneCalledMic() =>
        Assert.Equal(["Micro Captain"], ObsMic.PickMics([("Capturadora", "{card}"), ("Micro Captain", "{headset}")]).Mics);

    [Fact]
    public void NeverAGuessAmongOthers()
    {
        var (mics, problem) = ObsMic.PickMics([("Capturadora", "{card}"), ("Línea", "{line}")]);
        Assert.Empty(mics);
        Assert.Contains("Capturadora, Línea", problem);
        Assert.NotNull(ObsMic.PickMics([]).Problem);
    }

    [Fact]
    public void TheChainCleansThenLevelsThenCaps()
    {
        var chain = ObsMic.Chain("noise_suppress_filter");
        Assert.Equal(["noise_suppress_filter", "expander_filter", "compressor_filter", "limiter_filter"], chain.Select(f => f.Kind));
        Assert.All(chain, f => Assert.StartsWith("SackMix · ", f.Name));
        Assert.Equal("rnnoise", (string?)chain[0].Settings["method"]);
        Assert.True((double)chain[3].Settings["threshold"]! < 0, "the ceiling sits below 0 dB");
    }

    [Fact]
    public async Task UpdatesItsOwnFiltersAddsTheRestAndLeavesTheUsersAlone()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var requests = new List<JsonNode>();
        var server = FakeObs(listener, requests, (type, data) => type switch
        {
            "GetInputList" => new JsonObject { ["inputs"] = new JsonArray(new JsonObject { ["inputName"] = "Mic/Aux", ["inputKind"] = ObsMic.MicKind }) },
            "GetInputSettings" => new JsonObject { ["inputSettings"] = new JsonObject { ["device_id"] = "default" } },
            "GetSourceFilterKindList" => new JsonObject { ["sourceFilterKinds"] = new JsonArray("noise_suppress_filter_v2", "limiter_filter") },
            // The user's EQ, and our noise filter from an earlier run.
            "GetSourceFilterList" => new JsonObject { ["filters"] = new JsonArray(
                new JsonObject { ["filterName"] = "Mi EQ" }, new JsonObject { ["filterName"] = "SackMix · Ruido" }) },
            _ => null,
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (var obs = await ObsClient.ConnectAsync(new Uri($"ws://localhost:{port}/"), null, cts.Token))
        {
            var r = await ObsMic.RunAsync(obs, cts.Token);
            Assert.Null(r.Problem);
            Assert.Equal(["Mic/Aux"], r.Mics);
        }
        await server;

        Assert.Equal(ObsMic.MicKind, (string?)requests.First(q => (string?)q["requestType"] == "GetInputList")["requestData"]!["inputKind"]);
        var created = requests.Where(q => (string?)q["requestType"] == "CreateSourceFilter").Select(q => q["requestData"]!).ToList();
        Assert.Equal(["SackMix · Silencios", "SackMix · Nivel", "SackMix · Tope"], created.Select(c => (string?)c["filterName"]));
        Assert.Equal("compressor_filter", (string?)created[1]["filterKind"]);
        var updated = requests.Single(q => (string?)q["requestType"] == "SetSourceFilterSettings")["requestData"]!;
        Assert.Equal("SackMix · Ruido", (string?)updated["filterName"]);
        Assert.Equal("rnnoise", (string?)updated["filterSettings"]!["method"]);
        Assert.Contains(requests, q => (string?)q["requestType"] == "SetSourceFilterEnabled");
        Assert.DoesNotContain(requests, q => (string?)q["requestData"]?["filterName"] == "Mi EQ");
        // Ours last and in order, behind the user's EQ: indexes 1..4 of 5.
        var order = requests.Where(q => (string?)q["requestType"] == "SetSourceFilterIndex").Select(q => q["requestData"]!).ToList();
        Assert.Equal(["SackMix · Ruido", "SackMix · Silencios", "SackMix · Nivel", "SackMix · Tope"], order.Select(o => (string?)o["filterName"]));
        Assert.Equal([1, 2, 3, 4], order.Select(o => (int)o["filterIndex"]!));
    }

    /// <summary>obs-websocket without a password, answering with <paramref name="answer"/>.</summary>
    private static async Task FakeObs(HttpListener listener, List<JsonNode> requests, Func<string, JsonNode?, JsonNode?> answer)
    {
        var ws = (await (await listener.GetContextAsync()).AcceptWebSocketAsync(null)).WebSocket;
        await Send(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1 } });
        await Receive(ws);
        await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });
        while (await Receive(ws).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null) is { } message)
        {
            var d = message["d"]!;
            requests.Add(d.DeepClone());
            var type = (string)d["requestType"]!;
            await Send(ws, new JsonObject { ["op"] = 7, ["d"] = new JsonObject
            {
                ["requestType"] = type,
                ["requestId"] = (string)d["requestId"]!,
                ["requestStatus"] = new JsonObject { ["result"] = true, ["code"] = 100 },
                ["responseData"] = answer(type, d["requestData"]),
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
