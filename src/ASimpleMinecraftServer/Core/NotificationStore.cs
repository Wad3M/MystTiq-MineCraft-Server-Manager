using ASimpleMinecraftServer.Models;
using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed class NotificationStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASimpleMinecraftServer", "notifications.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public IReadOnlyList<NotificationRecord> Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<List<NotificationRecord>>(File.ReadAllText(_path), Options) ?? [] : []; }
        catch { return []; }
    }
    public void Save(IEnumerable<NotificationRecord> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items.Take(250), Options));
        File.Move(temp, _path, true);
    }
}
