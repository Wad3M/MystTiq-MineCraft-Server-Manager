using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class ServerManagementView : UserControl
{
    public ServerManagementView() => InitializeComponent();

    public TextBox ManagementFolderBox => ManagementFolderBoxControl;
    public TextBox ManagementJavaBox => ManagementJavaBoxControl;
    public TextBlock ManagementJavaStatusText => ManagementJavaStatusTextControl;
    public TextBox ManagementNameBox => ManagementNameBoxControl;
    public ProgressBar ManagementProgressBar => ManagementProgressBarControl;
    public TextBlock ManagementStatusText => ManagementStatusTextControl;
    public TextBlock ManagementVersionText => ManagementVersionTextControl;
    public ItemsControl ServerHealthList => ServerHealthListControl;

    public event RoutedEventHandler? AutoDetectJavaRequested;
    public event RoutedEventHandler? BrowseJavaRequested;
    public event RoutedEventHandler? DuplicateRequested;
    public event RoutedEventHandler? ExportProfileRequested;
    public event RoutedEventHandler? ExportServerZipRequested;
    public event RoutedEventHandler? ImportProfileRequested;
    public event RoutedEventHandler? MoveRequested;
    public event RoutedEventHandler? SaveProfileRequested;
    public event RoutedEventHandler? TestJavaRequested;
    public event RoutedEventHandler? VerifyRequested;

    private void AutoDetectJava_Click(object sender, RoutedEventArgs e) => AutoDetectJavaRequested?.Invoke(sender, e);
    private void BrowseJava_Click(object sender, RoutedEventArgs e) => BrowseJavaRequested?.Invoke(sender, e);
    private void DuplicateServer_Click(object sender, RoutedEventArgs e) => DuplicateRequested?.Invoke(sender, e);
    private void ExportProfile_Click(object sender, RoutedEventArgs e) => ExportProfileRequested?.Invoke(sender, e);
    private void ExportServerZip_Click(object sender, RoutedEventArgs e) => ExportServerZipRequested?.Invoke(sender, e);
    private void ImportProfile_Click(object sender, RoutedEventArgs e) => ImportProfileRequested?.Invoke(sender, e);
    private void MoveServer_Click(object sender, RoutedEventArgs e) => MoveRequested?.Invoke(sender, e);
    private void SaveManagementProfile_Click(object sender, RoutedEventArgs e) => SaveProfileRequested?.Invoke(sender, e);
    private void TestJava_Click(object sender, RoutedEventArgs e) => TestJavaRequested?.Invoke(sender, e);
    private void VerifyServer_Click(object sender, RoutedEventArgs e) => VerifyRequested?.Invoke(sender, e);
}
