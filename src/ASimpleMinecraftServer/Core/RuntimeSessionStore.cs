using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed record RuntimeSession(int ProcessId, string ServerFolder, DateTimeOffset StartedAt);

/// <summary>
/// Remembers which Java processes MystMC started, one entry per running server,
/// so they can be reattached if the app is closed or crashes while servers keep running.
/// </summary>
public sealed class RuntimeSessionStore
{
    private readonly string _path;
    private readonly string _legacyPath;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RuntimeSessionStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "runtime-sessions.json");
        _legacyPath = Path.Combine(dataDirectory, "runtime-session.json");
    }

    public IReadOnlyList<RuntimeSession> LoadAll()
    {
        lock (_lock)
        {
            var sessions = new List<RuntimeSession>();
            try
            {
                if (File.Exists(_path))
                    sessions.AddRange(JsonSerializer.Deserialize<List<RuntimeSession>>(File.ReadAllText(_path)) ?? []);
            }
            catch { }

            // v2.1.x and earlier stored a single session in runtime-session.json.
            try
            {
                if (File.Exists(_legacyPath))
                {
                    if (JsonSerializer.Deserialize<RuntimeSession>(File.ReadAllText(_legacyPath)) is { } legacy)
                        sessions.Add(legacy);
                    File.Delete(_legacyPath);
                }
            }
            catch { }

            return sessions;
        }
    }

    public void Save(RuntimeSession session)
    {
        lock (_lock)
        {
            var sessions = ReadCurrent();
            sessions.RemoveAll(s => SameFolder(s.ServerFolder, session.ServerFolder));
            sessions.Add(session);
            Write(sessions);
        }
    }

    public void Remove(string serverFolder)
    {
        lock (_lock)
        {
            var sessions = ReadCurrent();
            if (sessions.RemoveAll(s => SameFolder(s.ServerFolder, serverFolder)) > 0) Write(sessions);
        }
    }

    public void ReplaceAll(IEnumerable<RuntimeSession> sessions)
    {
        lock (_lock) Write(sessions.ToList());
    }

    private List<RuntimeSession> ReadCurrent()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<RuntimeSession>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    private void Write(List<RuntimeSession> sessions)
    {
        try
        {
            if (sessions.Count == 0) { if (File.Exists(_path)) File.Delete(_path); return; }
            File.WriteAllText(_path, JsonSerializer.Serialize(sessions, JsonOptions));
        }
        catch { }
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
