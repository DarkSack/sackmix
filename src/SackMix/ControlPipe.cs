using System.IO;
using System.IO.Pipes;
using SackMix.Core;

namespace SackMix;

/// <summary>
/// Listens on a local pipe for one-line commands from SackSpace ("perfil Streaming").
/// Only this Windows user's processes can open it.
/// </summary>
internal sealed class ControlPipe : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public ControlPipe(Action<string> profile)
    {
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(Profiles.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(pipe);
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (Profiles.ParseCommand(line) is { } name) await ui.InvokeAsync(() => profile(name));
                }
                catch (OperationCanceledException) { return; }
                catch (IOException e) { Log.Error("pipe", e); await Task.Delay(1000); }
            }
        });
    }

    public void Dispose() => _cts.Cancel();
}
