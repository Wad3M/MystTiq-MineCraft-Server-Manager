using ASimpleMinecraftServer.Models;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

/// <summary>
/// Runtime state for one server profile: its Java process, console buffer, and online players.
/// Each profile gets its own instance so several servers can run side by side.
/// </summary>
public sealed class ServerInstance : IDisposable
{
    private const int MaxConsoleLines = 5000;

    private readonly object _consoleLock = new();
    private readonly List<string> _consoleLines = new();
    private readonly List<string> _onlinePlayers = new();

    public ServerInstance(ServerProfile profile)
    {
        Profile = profile;
        Launcher.OutputReceived += Launcher_OutputReceived;
        Launcher.Exited += Launcher_Exited;
    }

    public ServerProfile Profile { get; }
    public ServerLauncher Launcher { get; } = new();
    public DateTimeOffset? StartedAt { get; set; }

    public bool IsRunning => Launcher.IsRunning;
    public bool IsRecoveredProcess => Launcher.IsAttachedProcess;
    public bool CanSendCommands => Launcher.CanSendCommands;
    public int? ProcessId => Launcher.ProcessId;
    public TimeSpan Uptime => StartedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - StartedAt.Value;
    public IReadOnlyList<string> OnlinePlayers { get { lock (_onlinePlayers) return _onlinePlayers.ToArray(); } }

    public event EventHandler<string>? ConsoleLineReceived;
    public event EventHandler? PlayersChanged;
    public event EventHandler<ServerProcessExitedEventArgs>? Exited;

    public IReadOnlyList<string> ConsoleSnapshot()
    {
        lock (_consoleLock) return _consoleLines.ToArray();
    }

    public void AddConsole(string line)
    {
        lock (_consoleLock)
        {
            _consoleLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_consoleLines.Count > MaxConsoleLines) _consoleLines.RemoveRange(0, _consoleLines.Count - MaxConsoleLines);
        }
        ConsoleLineReceived?.Invoke(this, line);
    }

    private void Launcher_OutputReceived(object? sender, string line)
    {
        AddConsole(line);
        ParsePlayerLine(line);
    }

    private void Launcher_Exited(object? sender, ServerProcessExitedEventArgs e)
    {
        StartedAt = null;
        lock (_onlinePlayers) _onlinePlayers.Clear();
        PlayersChanged?.Invoke(this, EventArgs.Empty);
        Exited?.Invoke(this, e);
    }

    private void ParsePlayerLine(string line)
    {
        var list = Regex.Match(line, @"There are \d+ of a max of \d+ players online:?\s*(.*)$", RegexOptions.IgnoreCase);
        if (list.Success)
        {
            var names = list.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lock (_onlinePlayers)
            {
                _onlinePlayers.Clear();
                foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n))) _onlinePlayers.Add(name);
            }
            PlayersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var joined = Regex.Match(line, @":\s*([^\s]+) joined the game", RegexOptions.IgnoreCase);
        if (joined.Success)
        {
            lock (_onlinePlayers) if (!_onlinePlayers.Contains(joined.Groups[1].Value)) _onlinePlayers.Add(joined.Groups[1].Value);
            PlayersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var left = Regex.Match(line, @":\s*([^\s]+) left the game", RegexOptions.IgnoreCase);
        if (left.Success)
        {
            lock (_onlinePlayers) _onlinePlayers.Remove(left.Groups[1].Value);
            PlayersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Launcher.OutputReceived -= Launcher_OutputReceived;
        Launcher.Exited -= Launcher_Exited;
        Launcher.Dispose();
    }
}
