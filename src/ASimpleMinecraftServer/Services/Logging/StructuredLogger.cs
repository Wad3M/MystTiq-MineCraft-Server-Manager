using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Services.Logging;

public sealed class StructuredLogger : IDisposable
{
    private const int RetentionDays = 14;
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public StructuredLogger(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            DeleteExpiredLogs(logDirectory);
            var path = Path.Combine(logDirectory, $"manager-{DateTime.Now:yyyy-MM-dd}.jsonl");
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous))
            {
                AutoFlush = true
            };
        }
        catch (IOException)
        {
            // Logging is diagnostic support and must never prevent the manager from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // Continue without file logging when the profile directory is not writable.
        }
    }

    public void Debug(string message, object? data = null) => Write("Debug", message, data);
    public void Information(string message, object? data = null) => Write("Information", message, data);
    public void Warning(string message, object? data = null) => Write("Warning", message, data);
    public void Error(string message, Exception? exception = null, object? data = null) =>
        Write("Error", message, new { Data = data, Exception = exception?.ToString() });

    public void WriteFromConsoleText(string text)
    {
        var level = text.Contains("error", StringComparison.OrdinalIgnoreCase) || text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "Error"
            : text.Contains("warn", StringComparison.OrdinalIgnoreCase)
                ? "Warning"
                : "Information";
        Write(level, text);
    }

    private void Write(string level, string message, object? data = null)
    {
        if (_disposed || _writer is null) return;

        try
        {
            var record = JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.Now,
                Level = level,
                Message = message,
                Data = data
            });
            lock (_gate) _writer.WriteLine(record);
        }
        catch (IOException)
        {
            // Do not allow a transient logging failure to interrupt server management.
        }
        catch (ObjectDisposedException)
        {
            // A final event can race with application shutdown.
        }
    }

    private static void DeleteExpiredLogs(string logDirectory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        foreach (var path in Directory.EnumerateFiles(logDirectory, "manager-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            catch (IOException)
            {
                // A log can be open in another viewer; retry during a later startup.
            }
            catch (UnauthorizedAccessException)
            {
                // Retention cleanup is best effort.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_writer is null) return;
        lock (_gate) _writer.Dispose();
    }
}
