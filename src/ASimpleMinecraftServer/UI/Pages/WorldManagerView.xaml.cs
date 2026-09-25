using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class WorldManagerView : UserControl
{
    public WorldManagerView() => InitializeComponent();

    public DataGrid WorldsGrid => WorldsGridControl;
    public ProgressBar WorldProgressBar => WorldProgressBarControl;
    public TextBlock WorldServerText => WorldServerTextControl;
    public TextBlock WorldStatusText => WorldStatusTextControl;
    public TextBlock WorldDetailDimensionText => WorldDetailDimensionTextControl;
    public TextBlock WorldDetailFolderText => WorldDetailFolderTextControl;
    public TextBlock WorldDetailHealthText => WorldDetailHealthTextControl;
    public TextBlock WorldDetailNameText => WorldDetailNameTextControl;
    public TextBlock WorldDetailStatsText => WorldDetailStatsTextControl;

    public event RoutedEventHandler? BackupRequested;
    public event RoutedEventHandler? DeleteRequested;
    public event RoutedEventHandler? ImportRequested;
    public event RoutedEventHandler? OpenFolderRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? RenameRequested;
    public event RoutedEventHandler? VerifyRequested;
    public event SelectionChangedEventHandler? SelectionChangedRequested;

    private void BackupWorld_Click(object sender, RoutedEventArgs e) => BackupRequested?.Invoke(sender, e);
    private void DeleteWorld_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(sender, e);
    private void ImportWorld_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(sender, e);
    private void OpenWorldFolder_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(sender, e);
    private void RefreshWorlds_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void RenameWorld_Click(object sender, RoutedEventArgs e) => RenameRequested?.Invoke(sender, e);
    private void VerifyWorld_Click(object sender, RoutedEventArgs e) => VerifyRequested?.Invoke(sender, e);
    private void WorldsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectionChangedRequested?.Invoke(sender, e);
}
