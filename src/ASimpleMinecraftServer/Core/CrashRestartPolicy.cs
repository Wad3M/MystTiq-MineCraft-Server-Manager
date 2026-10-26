namespace ASimpleMinecraftServer.Core;

/// <summary>
/// Decides whether a crashed server should be restarted automatically: at most
/// <c>limit</c> restarts within a rolling <c>window</c>, so a server that crashes on
/// every start doesn't restart forever.
/// </summary>
public sealed class CrashRestartPolicy
{
    private readonly Queue<DateTimeOffset> _restarts = new();

    /// <summary>
    /// A crash is an exit nobody asked for that also didn't end cleanly. Exit code 0 means the
    /// server shut itself down normally (for example an operator typed /stop in game).
    /// </summary>
    public static bool IsCrash(ServerProcessExitedEventArgs exit) => !exit.StopWasRequested && exit.ExitCode is not 0;

    /// <summary>
    /// Records a restart if the limit allows it. <paramref name="attempt"/> is this restart's
    /// number within the window (1-based), or how many already happened when refused.
    /// </summary>
    public bool TryRegisterRestart(DateTimeOffset now, int limit, TimeSpan window, out int attempt)
    {
        while (_restarts.Count > 0 && now - _restarts.Peek() > window) _restarts.Dequeue();
        if (_restarts.Count >= Math.Max(1, limit))
        {
            attempt = _restarts.Count;
            return false;
        }
        _restarts.Enqueue(now);
        attempt = _restarts.Count;
        return true;
    }

    /// <summary>Forgets earlier crashes, e.g. after the user starts or stops the server themselves.</summary>
    public void Reset() => _restarts.Clear();
}
