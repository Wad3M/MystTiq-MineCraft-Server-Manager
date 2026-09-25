using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ASimpleMinecraftServer.Models;

public sealed class ServerProfile : INotifyPropertyChanged
{
    private string _runtimeState = "Stopped";

    public string Name { get; set; } = "Minecraft Server";
    public string Folder { get; set; } = string.Empty;
    public string Type { get; set; } = "Vanilla";
    public string Version { get; set; } = string.Empty;
    public int MemoryGb { get; set; } = 4;
    public string Jar { get; set; } = "server.jar";
    public string JavaPath { get; set; } = "java";
    public bool AutomaticBackupsEnabled { get; set; }
    public int BackupIntervalMinutes { get; set; } = 60;
    public int BackupRetentionCount { get; set; } = 10;
    public DateTimeOffset? LastAutomaticBackupAt { get; set; }
    public bool CrashRecoveryEnabled { get; set; } = true;
    public int CrashRestartLimit { get; set; } = 3;
    public int CrashRestartWindowMinutes { get; set; } = 15;
    public int CrashRestartDelaySeconds { get; set; } = 10;

    [JsonIgnore]
    public string RuntimeState
    {
        get => _runtimeState;
        set
        {
            if (string.Equals(_runtimeState, value, StringComparison.Ordinal)) return;
            _runtimeState = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public int Port => GetPort();

    public string DisplayName => $"{Name}{Environment.NewLine}{Type} • {Version} • Port {GetPort()}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshDerivedProperties()
    {
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(DisplayName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private int GetPort()
    {
        try
        {
            var path = Path.Combine(Folder, "server.properties");
            var line = File.Exists(path)
                ? File.ReadLines(path).FirstOrDefault(value =>
                    value.StartsWith("server-port=", StringComparison.OrdinalIgnoreCase))
                : null;

            return int.TryParse(line?.Split('=', 2).ElementAtOrDefault(1), out var port)
                ? port
                : 25565;
        }
        catch
        {
            return 25565;
        }
    }
}
