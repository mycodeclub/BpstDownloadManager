using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDM.App.Services;

/// <summary>
/// Named pipe used so that "VeloxDM.exe &lt;url&gt;" (e.g. from a browser extension or a shortcut)
/// hands the URL to the already-running instance. The pipe accepts the current user only.
/// </summary>
public sealed class SingleInstanceServer : IDisposable
{
    private static readonly string PipeName = "VeloxDM.Pipe." + Environment.UserName;
    private readonly Action<string> _onMessage;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstanceServer(Action<string> onMessage) => _onMessage = onMessage;

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(pipe);
                string? line;
                while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
                    _onMessage(line.Trim());
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* client disconnected early; keep listening */ }
        }
    }

    public static void Send(string[] messages)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            foreach (var m in messages) writer.WriteLine(m);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { }
    }

    public void Dispose() => _cts.Cancel();
}
