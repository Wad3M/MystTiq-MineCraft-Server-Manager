using ASimpleMinecraftServer.Models;
using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed class ScheduledTaskStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ScheduledTaskStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASimpleMinecraftServer", "scheduled-tasks.json");
    }

    public IReadOnlyList<ScheduledTaskRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<ScheduledTaskRecord>();
            return JsonSerializer.Deserialize<List<ScheduledTaskRecord>>(File.ReadAllText(_path), Options) ?? [];
        }
        catch { return Array.Empty<ScheduledTaskRecord>(); }
    }

    public void Save(IEnumerable<ScheduledTaskRecord> tasks)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(tasks, Options));
        File.Move(temporary, _path, true);
    }
}
