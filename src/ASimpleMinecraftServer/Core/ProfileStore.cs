using ASimpleMinecraftServer.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed class ProfileStore
{
    private readonly string _profilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _profilePath = Path.Combine(dataDirectory, "servers.json");
    }

    public ObservableCollection<ServerProfile> Load()
    {
        var result = new ObservableCollection<ServerProfile>();
        if (!File.Exists(_profilePath)) return result;

        var profiles = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_profilePath)) ?? [];
        foreach (var profile in profiles.Where(profile => Directory.Exists(profile.Folder))) result.Add(profile);
        return result;
    }

    public void Save(IEnumerable<ServerProfile> profiles) =>
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profiles, JsonOptions));
}
