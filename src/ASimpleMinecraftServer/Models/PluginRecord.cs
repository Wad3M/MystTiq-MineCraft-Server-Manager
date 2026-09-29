using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASimpleMinecraftServer.Models;

public sealed class PluginRecord : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _latestVersion = "—";
    private string _updateStatus = "Not checked";
    private string? _updateProjectId;
    private Uri? _updateDownloadUri;
    private string? _updateFileName;

    public required string Name { get; init; }
    public string Version { get; init; } = "Unknown";
    public string ApiVersion { get; init; } = "—";
    public string MainClass { get; init; } = "—";
    public string Authors { get; init; } = "—";
    public string Dependencies { get; init; } = "—";
    public string SoftDependencies { get; init; } = "—";
    public required string FileName { get; init; }
    public required string FullPath { get; set; }
    public long SizeBytes { get; init; }
    public string SizeText => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024d:0.0} KB"
        : $"{SizeBytes / 1024d / 1024d:0.0} MB";
    public DateTime LastModified { get; init; }
    public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");
    public bool HasDescriptor { get; init; }
    public bool IsReadableJar { get; init; } = true;
    public bool IsDuplicate { get; set; }
    public string HealthText => !IsReadableJar ? "Malformed JAR" : !HasDescriptor ? "No descriptor" : IsDuplicate ? "Duplicate name" : "Ready";
    public string StatusText => IsEnabled ? "Enabled" : "Disabled";
    public string LatestVersion { get => _latestVersion; set { if (_latestVersion == value) return; _latestVersion = value; OnPropertyChanged(); } }
    public string UpdateStatus { get => _updateStatus; set { if (_updateStatus == value) return; _updateStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUpdate)); } }
    public bool HasUpdate => UpdateStatus == "Update available";
    public string? UpdateProjectId { get => _updateProjectId; set => _updateProjectId = value; }
    public Uri? UpdateDownloadUri { get => _updateDownloadUri; set => _updateDownloadUri = value; }
    public string? UpdateFileName { get => _updateFileName; set => _updateFileName = value; }
    public string? UpdateSha512 { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshHealth() => OnPropertyChanged(nameof(HealthText));
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
