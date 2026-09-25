using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Commands;

public sealed class CommandRouter
{
    private readonly Dictionary<string, AppCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<AppCommand> Commands { get; } = new();

    public void Register(AppCommand command)
    {
        _commands[command.Id] = command;
        Commands.Add(command);
    }

    public void RefreshAvailability()
    {
        foreach (var command in Commands)
        {
            command.IsEnabled = command.CanExecute();
            command.DisabledReason = command.IsEnabled ? string.Empty : command.GetDisabledReason();
        }
    }

    public async Task<(bool Executed, string Message)> ExecuteAsync(AppCommand? command)
    {
        if (command is null) return (false, "No command selected.");
        command.IsEnabled = command.CanExecute();
        command.DisabledReason = command.IsEnabled ? string.Empty : command.GetDisabledReason();
        if (!command.IsEnabled) return (false, string.IsNullOrWhiteSpace(command.DisabledReason) ? "This command is currently unavailable." : command.DisabledReason);
        await command.ExecuteAsync();
        return (true, string.Empty);
    }

    public AppCommand? FindShortcut(Key key, ModifierKeys modifiers) =>
        Commands.FirstOrDefault(command => command.Gesture is not null && command.Gesture.Key == key && command.Gesture.Modifiers == modifiers);
}
