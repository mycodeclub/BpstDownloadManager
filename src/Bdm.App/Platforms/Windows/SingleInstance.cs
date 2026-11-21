using System.IO.Pipes;

namespace Bdm.App;

/// <summary>
/// One BDM window per user. "BDM.exe &lt;link&gt;" (shortcut, browser hand-off, second launch) passes the link
/// to the running instance over a current-user-only named pipe, then exits.
/// </summary>
internal static class SingleInstance
{
    private static readonly string PipeName = "BDM.Pipe." + Environment.UserName;
    private static Mutex? _mutex;

    /// <summary>Returns false if another instance is running (the arguments were forwarded to it).</summary>
    public static bool Claim(string[] args)
    {
        _mutex = new Mutex(true, @"Local\BDM.SingleInstance", out bool first);
        if (first) return true;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(args.Length > 0 ? string.Join(" ", args) : "");
        }
        catch (Exception) { }
        return false;
    }

    public static void Listen(Action<string> onMessage)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync();
                    using var reader = new StreamReader(pipe);
                    while (await reader.ReadLineAsync() is { } line) onMessage(line.Trim());
                }
                catch (IOException) { }
            }
        });
    }
}
