using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
    public event SelectionChangedEventHandler? ServerSelectionChanged;
    public event RoutedEventHandler? CreateServerRequested;
    public event RoutedEventHandler? BrowseExistingRequested;
    public ListBox Servers => ServerList;
    public void BindServers(IEnumerable servers) => ServerList.ItemsSource = servers;
    public object? SelectedServer { get => ServerList.SelectedItem; set => ServerList.SelectedItem = value; }
    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ServerSelectionChanged?.Invoke(sender, e);
    private void CreateServer_Click(object sender, RoutedEventArgs e) => CreateServerRequested?.Invoke(sender, e);
    private void BrowseExisting_Click(object sender, RoutedEventArgs e) => BrowseExistingRequested?.Invoke(sender, e);
}
