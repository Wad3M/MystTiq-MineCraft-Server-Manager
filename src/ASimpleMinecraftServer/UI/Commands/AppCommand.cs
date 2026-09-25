using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Commands;

public sealed class AppCommand : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string _disabledReason = string.Empty;

    public AppCommand(string id, string icon, string title, string description, string shortcut, KeyGesture? gesture,
        Func<bool> canExecute, Func<string> disabledReason, Func<Task> executeAsync)
    {
        Id = id; Icon = icon; Title = title; Description = description; Shortcut = shortcut; Gesture = gesture;
        CanExecute = canExecute; GetDisabledReason = disabledReason; ExecuteAsync = executeAsync;
    }

    public string Id { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }
    public string Shortcut { get; }
    public KeyGesture? Gesture { get; }
    public Visibility ShortcutVisibility => string.IsNullOrWhiteSpace(Shortcut) ? Visibility.Collapsed : Visibility.Visible;
    internal Func<bool> CanExecute { get; }
    internal Func<string> GetDisabledReason { get; }
    internal Func<Task> ExecuteAsync { get; }

    public bool IsEnabled { get => _isEnabled; internal set { if (_isEnabled == value) return; _isEnabled = value; OnChanged(nameof(IsEnabled)); } }
    public string DisabledReason { get => _disabledReason; internal set { if (_disabledReason == value) return; _disabledReason = value; OnChanged(nameof(DisabledReason)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
