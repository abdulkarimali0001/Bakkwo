using System.IO.Pipes;
using System.Text;

namespace Bakkwo.App;

/// <summary>
/// Keeps one Bakkwo window. When Explorer's "Convert with 바꿔" or "Open with" starts the app once per
/// selected file, the extra copies send their files to the first one over a local pipe and exit, and the
/// first window gathers them into one ring.
/// </summary>
internal static class SingleInstance
{
    private const string ActivateMessage = "--activate";

    private static readonly string Id = "Bakkwo.SingleInstance." + Sanitize(Environment.UserName);
    private static Mutex? _mutex;

    private static readonly object Gate = new();
    private static readonly List<IReadOnlyList<string>> Early = [];
    private static Action<IReadOnlyList<string>>? _handler;

    /// <summary>
    /// Receives paths sent by another copy (empty = just bring the window forward), on a background thread.
    /// Anything that arrived before the window was ready is delivered straight away.
    /// </summary>
    public static void OnReceived(Action<IReadOnlyList<string>> handler)
    {
        List<IReadOnlyList<string>> early;
        lock (Gate)
        {
            _handler = handler;
            early = [.. Early];
            Early.Clear();
        }
        foreach (var paths in early) handler(paths);
    }

    private static void Deliver(IReadOnlyList<string> paths)
    {
        Action<IReadOnlyList<string>>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null) Early.Add(paths);
        }
        handler?.Invoke(paths);
    }

    /// <summary>True if this is the first copy; it then owns the name until the process exits.</summary>
    public static bool TryBecomePrimary()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, Id, out var createdNew);
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }
            return createdNew;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            return true; // can't coordinate: behave like a normal app
        }
    }

    /// <summary>Sends paths to the running copy. Retries briefly, because Explorer may start several copies at once.</summary>
    public static bool Forward(IReadOnlyList<string> paths)
    {
        var message = paths.Count == 0 ? ActivateMessage : string.Join('\n', paths);
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
                client.Connect(500);
                var bytes = Encoding.UTF8.GetBytes(message);
                client.Write(bytes);
                client.Flush();
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }
        return false;
    }

    /// <summary>Starts listening for other copies (call once, from the first copy).</summary>
    public static void Listen()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Id, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync();
                    var paths = text == ActivateMessage
                        ? []
                        : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    Deliver(paths);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
                {
                    await Task.Delay(200);
                }
            }
        });
    }

    private static string Sanitize(string value) =>
        new(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
