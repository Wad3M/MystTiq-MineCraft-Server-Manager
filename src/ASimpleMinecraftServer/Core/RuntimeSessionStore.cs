using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed record RuntimeSession(int ProcessId, string ServerFolder, DateTimeOffset StartedAt);

public sealed class RuntimeSessionStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RuntimeSessionStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "runtime-session.json");
    }

    public RuntimeSession? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<RuntimeSession>(File.ReadAllText(_path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(RuntimeSession session) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(session, JsonOptions));

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { }
    }
}
