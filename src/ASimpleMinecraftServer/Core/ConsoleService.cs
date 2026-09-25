using System.Collections.ObjectModel;
using System.Text;

namespace ASimpleMinecraftServer.Core;

public enum ConsoleLevel { All, Info, Warning, Error, Command, Manager }

public sealed record ConsoleEntry(DateTimeOffset Timestamp, string Message, ConsoleLevel Level)
{
    public string Format(bool includeTimestamp) => includeTimestamp
        ? $"[{Timestamp:HH:mm:ss}] {Message}"
        : Message;
}

public sealed class ConsoleService
{
    private readonly List<ConsoleEntry> _entries = new();
    private readonly Queue<ConsoleEntry> _pausedBuffer = new();
    public IReadOnlyList<ConsoleEntry> Entries => _entries;
    public int BufferedCount => _pausedBuffer.Count;
    public bool IsPaused { get; private set; }

    public ConsoleEntry Add(string message)
    {
        var level = Classify(message);
        var entry = new ConsoleEntry(DateTimeOffset.Now, message, level);
        _entries.Add(entry);
        if (IsPaused) _pausedBuffer.Enqueue(entry);
        return entry;
    }

    public void Pause() => IsPaused = true;
    public IReadOnlyList<ConsoleEntry> Resume()
    {
        IsPaused = false;
        var items = _pausedBuffer.ToArray();
        _pausedBuffer.Clear();
        return items;
    }
    public void Clear() { _entries.Clear(); _pausedBuffer.Clear(); }

    public string BuildText(ConsoleLevel filter, bool includeTimestamp)
    {
        var sb = new StringBuilder();
        foreach (var entry in _entries)
            if (filter == ConsoleLevel.All || entry.Level == filter)
                sb.AppendLine(entry.Format(includeTimestamp));
        return sb.ToString();
    }

    private static ConsoleLevel Classify(string message)
    {
        if (message.StartsWith("> ", StringComparison.Ordinal)) return ConsoleLevel.Command;
        if (message.StartsWith("[Manager]", StringComparison.OrdinalIgnoreCase)) return ConsoleLevel.Manager;
        if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || message.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) || message.Contains("Exception", StringComparison.OrdinalIgnoreCase)) return ConsoleLevel.Error;
        if (message.Contains("WARN", StringComparison.OrdinalIgnoreCase)) return ConsoleLevel.Warning;
        return ConsoleLevel.Info;
    }
}
