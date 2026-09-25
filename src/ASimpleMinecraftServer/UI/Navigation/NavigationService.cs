using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Navigation;

public sealed class NavigationService
{
    private readonly TabControl _tabs;
    private readonly TextBlock _pageTitle;
    private readonly IReadOnlyList<Button> _buttons;
    private readonly Dictionary<AppPage, Action> _activationActions = new();

    public NavigationService(TabControl tabs, TextBlock pageTitle, params Button[] buttons)
    {
        _tabs = tabs;
        _pageTitle = pageTitle;
        _buttons = buttons;
    }

    public AppPage CurrentPage => Enum.IsDefined(typeof(AppPage), _tabs.SelectedIndex)
        ? (AppPage)_tabs.SelectedIndex
        : AppPage.Dashboard;

    public void RegisterActivation(AppPage page, Action action) => _activationActions[page] = action;

    public void Navigate(AppPage page)
    {
        _tabs.SelectedIndex = (int)page;
        UpdateVisualState();
        if (_activationActions.TryGetValue(page, out var action)) action();
    }

    public void SynchronizeFromTabSelection() => UpdateVisualState();

    private void UpdateVisualState()
    {
        for (var index = 0; index < _buttons.Count; index++)
            _buttons[index].Tag = index == _tabs.SelectedIndex ? "Selected" : null;

        _pageTitle.Text = CurrentPage switch
        {
            AppPage.CreateServer => "Create Server",
            AppPage.Console => "Console",
            AppPage.Configuration => "Server Configuration",
            AppPage.Backups => "Backups",
            AppPage.Plugins => "Plugin Manager",
            AppPage.Worlds => "World Manager",
            AppPage.Management => "Server Management",
            AppPage.Updates => "Update Center",
            AppPage.ScheduledTasks => "Scheduled Tasks",
            AppPage.Notifications => "Notification Center",
            AppPage.HealthAnalyzer => "Health Analyzer",
            AppPage.StartupAnalyzer => "Startup Analyzer",
            AppPage.LogAnalyzer => "Log Analyzer",
            AppPage.PluginCompatibility => "Plugin Compatibility",
            AppPage.PerformanceDashboard => "Performance Dashboard",
            AppPage.Optimization => "Optimization Recommendations",
            AppPage.FirstRunWizard => "First Run Wizard",
            AppPage.About => "About",
            _ => "Dashboard"
        };
    }
}
