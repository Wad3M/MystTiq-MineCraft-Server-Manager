using ASimpleMinecraftServer.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASimpleMinecraftServer.Services.State;

public sealed class ApplicationState : INotifyPropertyChanged
{
    private ServerProfile? _selectedServer;
    private ServerProfile? _runningServer;
    private string _runtimeState = "Stopped";

    public ObservableCollection<ServerProfile> Servers { get; } = new();

    public ServerProfile? SelectedServer
    {
        get => _selectedServer;
        set => SetField(ref _selectedServer, value);
    }

    public ServerProfile? RunningServer
    {
        get => _runningServer;
        set => SetField(ref _runningServer, value);
    }

    public string RuntimeState
    {
        get => _runtimeState;
        set => SetField(ref _runtimeState, value);
    }

    public bool HasSelection => SelectedServer is not null;
    public bool IsServerRunning => RunningServer is not null;

    public void ReplaceServers(IEnumerable<ServerProfile> profiles)
    {
        Servers.Clear();
        foreach (var profile in profiles) Servers.Add(profile);
        OnPropertyChanged(nameof(Servers));
    }

    public bool TryGetSelectedServer(out ServerProfile profile)
    {
        profile = SelectedServer!;
        return profile is not null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(SelectedServer)) OnPropertyChanged(nameof(HasSelection));
        if (propertyName == nameof(RunningServer)) OnPropertyChanged(nameof(IsServerRunning));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
