using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class BackupsView : UserControl
{
    public BackupsView() => InitializeComponent();

    public TextBlock BackupServerText => BackupServerTextControl;
    public DataGrid BackupsGrid => BackupsGridControl;
    public CheckBox AutomaticBackupsCheckBox => AutomaticBackupsCheckBoxControl;
    public ComboBox BackupIntervalCombo => BackupIntervalComboControl;
    public ComboBox BackupRetentionCombo => BackupRetentionComboControl;
    public TextBlock BackupStatusText => BackupStatusTextControl;
    public TextBlock LastAutomaticBackupText => LastAutomaticBackupTextControl;
    public TextBlock NextAutomaticBackupText => NextAutomaticBackupTextControl;
    public ProgressBar BackupProgressBar => BackupProgressBarControl;

    public event RoutedEventHandler? CreateRequested;
    public event RoutedEventHandler? SettingsChangedRequested;
    public event RoutedEventHandler? OpenFolderRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? RestoreRequested;
    public event RoutedEventHandler? DeleteRequested;

    private void CreateBackup_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(sender, e);
    private void BackupSettings_Changed(object sender, RoutedEventArgs e) => SettingsChangedRequested?.Invoke(sender, e);
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(sender, e);
    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void RestoreBackup_Click(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(sender, e);
    private void DeleteBackup_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(sender, e);
}
