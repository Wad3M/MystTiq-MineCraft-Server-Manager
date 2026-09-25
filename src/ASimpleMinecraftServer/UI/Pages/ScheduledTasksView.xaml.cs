using ASimpleMinecraftServer.Models;
using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class ScheduledTasksView : UserControl
{
    public ScheduledTasksView() => InitializeComponent();
    public event EventHandler? AddRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? RunNowRequested;
    private void Add_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
    private void Toggle_Click(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke(this, EventArgs.Empty);
    private void RunNow_Click(object sender, RoutedEventArgs e) => RunNowRequested?.Invoke(this, EventArgs.Empty);
    public ScheduledTaskRecord? SelectedTask => TasksGrid.SelectedItem as ScheduledTaskRecord;
    public string SelectedAction => (ActionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Broadcast";
    public int SelectedInterval => int.TryParse((IntervalCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var value) ? value : 60;
}
