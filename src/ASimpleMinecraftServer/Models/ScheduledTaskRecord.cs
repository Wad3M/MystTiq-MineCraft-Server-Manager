using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASimpleMinecraftServer.Models;

public sealed class ScheduledTaskRecord : INotifyPropertyChanged
{
    private bool _enabled = true;
    private DateTimeOffset? _lastRunAt;
    private string _lastResult = "Never run";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string ServerFolder { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Name { get; set; } = "Scheduled task";
    public string Action { get; set; } = "Broadcast";
    public string Payload { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); }
    }

    public DateTimeOffset? LastRunAt
    {
        get => _lastRunAt;
        set { if (_lastRunAt == value) return; _lastRunAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(NextRunAt)); OnPropertyChanged(nameof(NextRunText)); }
    }

    public string LastResult
    {
        get => _lastResult;
        set { if (_lastResult == value) return; _lastResult = value; OnPropertyChanged(); }
    }

    public DateTimeOffset NextRunAt => (LastRunAt ?? CreatedAt).AddMinutes(Math.Max(1, IntervalMinutes));
    public string NextRunText => Enabled ? NextRunAt.LocalDateTime.ToString("g") : "Disabled";
    public string Status => Enabled ? "Enabled" : "Disabled";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshSchedule() { OnPropertyChanged(nameof(NextRunAt)); OnPropertyChanged(nameof(NextRunText)); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
