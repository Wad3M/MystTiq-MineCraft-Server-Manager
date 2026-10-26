using System.IO;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

/// <summary>Settings for MystTiq itself (not per server).</summary>
public sealed class AppSettings
{
    /// <summary>Closing the window hides MystTiq in the system tray; servers keep running.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Whether the "MystTiq is still running in the tray" tip has been shown once.</summary>
    public bool TrayTipShown { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> as app-settings.json; a broken file just means defaults.</summary>
public sealed class AppSettingsStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(dataDirectory, "app-settings.json");

    public AppSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings() : new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
