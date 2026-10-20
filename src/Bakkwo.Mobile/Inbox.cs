namespace Bakkwo.Mobile;

/// <summary>
/// Files shared to 바꿔 from another app. Android can hand them over before the screen exists, so they wait
/// here until <see cref="MainView"/> is ready.
/// </summary>
public static class Inbox
{
    private static readonly object Gate = new();
    private static readonly List<IReadOnlyList<string>> Waiting = [];
    private static Action<IReadOnlyList<string>>? _handler;

    public static void Deliver(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Action<IReadOnlyList<string>>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null) Waiting.Add(paths);
        }
        handler?.Invoke(paths);
    }

    internal static void OnReceived(Action<IReadOnlyList<string>> handler)
    {
        List<IReadOnlyList<string>> waiting;
        lock (Gate)
        {
            _handler = handler;
            waiting = [.. Waiting];
            Waiting.Clear();
        }
        foreach (var paths in waiting) handler(paths);
    }
}
