using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using ASimpleMinecraftServer.UI.Navigation;
using ASimpleMinecraftServer.Models;

namespace ASimpleMinecraftServer;

public partial class MainWindow
{
    private readonly Dictionary<AppPage, UIElement> _externalPageCache = new();
    public UIElement DetachFunctionalPageHost()
    {
        if (MainTabs.Parent is Border border)
            border.Child = null;
        else if (MainTabs.Parent is Panel panel)
            panel.Children.Remove(MainTabs);
        return MainTabs;
    }


    public UIElement? ExternalGetPageContent(AppPage page)
    {
        if (_externalPageCache.TryGetValue(page, out var cached))
            return cached;

        var index = (int)page;
        if (index < 0 || index >= MainTabs.Items.Count)
            return null;

        if (MainTabs.Items[index] is not TabItem tab || tab.Content is not UIElement content)
            return null;

        // Detach only the functional page itself. The old MainWindow remains a hidden
        // controller/service host; its old TabControl/navigation is never mounted in MystMC.
        tab.Content = null;
        _externalPageCache[page] = content;
        return content;
    }


    public UIElement? ExternalGetPlayerManagementContent()
    {
        if (OnlinePlayersExpander.Parent is Panel panel)
            panel.Children.Remove(OnlinePlayersExpander);
        else if (OnlinePlayersExpander.Parent is Decorator decorator)
            decorator.Child = null;
        return OnlinePlayersExpander;
    }

    public void ExternalNavigate(AppPage page) => Navigate(page);
    public void ExternalStart() => _ = StartServerAsync();
    public void ExternalStop() => Stop_Click(this, new RoutedEventArgs());
    public void ExternalRestart() => Restart_Click(this, new RoutedEventArgs());
    public void ExternalKill() => _ = _launcher.ForceKillAsync();
    public void ExternalBackup() => CreateBackup_Click(this, new RoutedEventArgs());
    public void ExternalOpenFolder() => OpenFolder_Click(this, new RoutedEventArgs());
    public bool ExternalIsRunning => _launcher.IsRunning;
    public string ExternalActiveServerName => _active?.Name ?? "No server selected";
    public IEnumerable<ServerProfile> ExternalServers => _servers;
    public ServerProfile? ExternalActiveServer => _active;
    public bool ExternalHasActiveServer => _active is not null;
    public void ExternalSelectServer(ServerProfile profile) => Select(profile);
    public string ExternalActiveServerSummary => _active is null
        ? "No server selected"
        : $"{_active.Type} {_active.Version}  |  {_active.Jar}";
}
