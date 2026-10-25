using ASimpleMinecraftServer.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

/// <summary>
/// Reads and writes the server list (servers.json). Profiles whose folder is missing are kept,
/// so a disconnected drive never silently removes servers. Writes are atomic with a .bak copy.
/// </summary>
public sealed class ProfileStore
{
    private readonly string _profilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _profilePath = Path.Combine(dataDirectory, "servers.json");
    }

    /// <summary>Set when servers.json couldn't be read and was set aside; shown to the user once.</summary>
    public string? LoadWarning { get; private set; }

    public ObservableCollection<ServerProfile> Load()
    {
        LoadWarning = null;
        var result = new ObservableCollection<ServerProfile>();
        if (!File.Exists(_profilePath) && !File.Exists(BackupPath)) return result;

        var profiles = TryRead(_profilePath);
        if (profiles is null)
        {
            profiles = TryRead(BackupPath);
            if (File.Exists(_profilePath))
            {
                var setAside = _profilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                try { File.Move(_profilePath, setAside); } catch (IOException) { }
                LoadWarning = profiles is null
                    ? $"The server list could not be read and was moved to {setAside}. Your server folders were not touched; add them again with Add Existing Server."
                    : $"The server list could not be read, so the last good copy was used. The unreadable file was moved to {setAside}.";
            }
        }

        foreach (var profile in profiles ?? []) result.Add(profile);
        return result;
    }

    public void Save(IEnumerable<ServerProfile> profiles)
    {
        var temporary = _profilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profiles, JsonOptions));
        if (File.Exists(_profilePath)) File.Copy(_profilePath, BackupPath, overwrite: true);
        File.Move(temporary, _profilePath, overwrite: true);
    }

    private string BackupPath => _profilePath + ".bak";

    private static List<ServerProfile>? TryRead(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(path)) ?? [] : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}
