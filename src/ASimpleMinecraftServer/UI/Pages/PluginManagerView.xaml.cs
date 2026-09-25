using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class PluginManagerView : UserControl
{
    public PluginManagerView() => InitializeComponent();

    public DataGrid PluginsGrid => PluginsGridControl;
    public TextBox PluginSearchBox => PluginSearchBoxControl;
    public TextBlock PluginServerText => PluginServerTextControl;
    public TextBlock PluginStatusText => PluginStatusTextControl;
    public TextBlock PluginDetailNameText => PluginDetailNameTextControl;
    public TextBlock PluginDetailMainText => PluginDetailMainTextControl;
    public TextBlock PluginDetailAuthorsText => PluginDetailAuthorsTextControl;
    public TextBlock PluginDetailDependenciesText => PluginDetailDependenciesTextControl;
    public TextBlock PluginDetailSoftDependenciesText => PluginDetailSoftDependenciesTextControl;
    public TextBlock PluginDetailCompatibilityText => PluginDetailCompatibilityTextControl;
    public TextBlock PluginDetailModifiedText => PluginDetailModifiedTextControl;
    public DataGrid CatalogGrid => CatalogGridControl;
    public TextBox CatalogSearchBox => CatalogSearchBoxControl;
    public TextBlock CatalogServerText => CatalogServerTextControl;
    public TextBlock CatalogStatusText => CatalogStatusTextControl;

    public event RoutedEventHandler? InstallRequested;
    public event RoutedEventHandler? OpenFolderRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? EnableRequested;
    public event RoutedEventHandler? DisableRequested;
    public event RoutedEventHandler? DeleteRequested;
    public event RoutedEventHandler? ClearSearchRequested;
    public event SelectionChangedEventHandler? SelectionChangedRequested;
    public event TextChangedEventHandler? SearchChangedRequested;
    public event DragEventHandler? DragOverRequested;
    public event DragEventHandler? DropRequested;
    public event RoutedEventHandler? CatalogSearchRequested;
    public event RoutedEventHandler? CatalogInstallRequested;
    public event RoutedEventHandler? CheckUpdatesRequested;
    public event RoutedEventHandler? UpdateSelectedRequested;
    public event RoutedEventHandler? UpdateAllRequested;

    private void InstallPlugin_Click(object sender, RoutedEventArgs e) => InstallRequested?.Invoke(sender, e);
    private void OpenPluginFolder_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(sender, e);
    private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void EnablePlugin_Click(object sender, RoutedEventArgs e) => EnableRequested?.Invoke(sender, e);
    private void DisablePlugin_Click(object sender, RoutedEventArgs e) => DisableRequested?.Invoke(sender, e);
    private void DeletePlugin_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(sender, e);
    private void ClearPluginSearch_Click(object sender, RoutedEventArgs e) => ClearSearchRequested?.Invoke(sender, e);
    private void PluginsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectionChangedRequested?.Invoke(sender, e);
    private void PluginSearchBox_TextChanged(object sender, TextChangedEventArgs e) => SearchChangedRequested?.Invoke(sender, e);
    private void PluginArea_DragOver(object sender, DragEventArgs e) => DragOverRequested?.Invoke(sender, e);
    private void PluginArea_Drop(object sender, DragEventArgs e) => DropRequested?.Invoke(sender, e);
    private void SearchCatalog_Click(object sender, RoutedEventArgs e) => CatalogSearchRequested?.Invoke(sender, e);
    private void InstallCatalogPlugin_Click(object sender, RoutedEventArgs e) => CatalogInstallRequested?.Invoke(sender, e);
    private void CheckPluginUpdates_Click(object sender, RoutedEventArgs e) => CheckUpdatesRequested?.Invoke(sender, e);
    private void UpdateSelectedPlugins_Click(object sender, RoutedEventArgs e) => UpdateSelectedRequested?.Invoke(sender, e);
    private void UpdateAllPlugins_Click(object sender, RoutedEventArgs e) => UpdateAllRequested?.Invoke(sender, e);
    private void CatalogSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CatalogSearchRequested?.Invoke(sender, new RoutedEventArgs());
        e.Handled = true;
    }
}
