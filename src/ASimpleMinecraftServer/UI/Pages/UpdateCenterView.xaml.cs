using ASimpleMinecraftServer.Models;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ASimpleMinecraftServer.UI.Pages;

public partial class UpdateCenterView : UserControl
{
    private ServerProfile? _profile;
    private ServerUpdateInfo? _update;
    public event EventHandler? CheckRequested;
    public event EventHandler? InstallRequested;
    public event EventHandler? LoadVersionsRequested;
    public event EventHandler<string>? ChangeVersionRequested;

    public UpdateCenterView() => InitializeComponent();

    public void Bind(ServerProfile? profile, ServerUpdateInfo? update = null)
    {
        _profile = profile;
        _update = update;
        ServerNameText.Text = profile?.Name ?? "No server selected";
        ServerTypeText.Text = profile is null ? string.Empty : $"{profile.Type} • {profile.Version}";
        MinecraftVersionText.Text = update?.MinecraftVersion ?? profile?.Version ?? "—";
        InstalledBuildText.Text = update?.InstalledBuild ?? "—";
        LatestBuildText.Text = update?.LatestBuild ?? "—";
        SummaryText.Text = update?.Summary ?? "No update check has been performed.";
        StatusText.Text = profile is null ? "Select a server first." : update is null ? "Ready to check." : update.IsUpdateAvailable ? "Update available" : "No update available";
        InstallButton.IsEnabled = update?.CanInstall == true;
        ChangeVersionButton.IsEnabled = profile is not null && TargetVersionCombo.SelectedItem is string;
    }

    public void SetBusy(bool busy, string message = "")
    {
        IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message)) ProgressText.Text = message;
        if (!busy) IsEnabled = true;
    }

    public void SetProgress(double value, string message)
    {
        UpdateProgress.Value = Math.Clamp(value, 0, 100);
        ProgressText.Text = message;
    }

    private void Check_Click(object sender, RoutedEventArgs e) => CheckRequested?.Invoke(this, EventArgs.Empty);
    public void SetAvailableVersions(IEnumerable<string> versions)
    {
        var items = versions.ToList();
        TargetVersionCombo.ItemsSource = items;
        TargetVersionCombo.SelectedItem = items.FirstOrDefault(v => string.Equals(v, _profile?.Version, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        ChangeVersionButton.IsEnabled = _profile is not null && TargetVersionCombo.SelectedItem is string;
        VersionChangeStatusText.Text = items.Count == 0 ? "No compatible releases were found." : $"{items.Count} releases loaded. Select a target version.";
    }

    public void SetVersionChangeStatus(string message) => VersionChangeStatusText.Text = message;

    private void Install_Click(object sender, RoutedEventArgs e) => InstallRequested?.Invoke(this, EventArgs.Empty);
    private void LoadVersions_Click(object sender, RoutedEventArgs e) => LoadVersionsRequested?.Invoke(this, EventArgs.Empty);
    private void ChangeVersion_Click(object sender, RoutedEventArgs e)
    {
        if (TargetVersionCombo.SelectedItem is string version) ChangeVersionRequested?.Invoke(this, version);
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is not null && Directory.Exists(_profile.Folder))
            Process.Start(new ProcessStartInfo("explorer.exe", _profile.Folder) { UseShellExecute = true });
    }
}
