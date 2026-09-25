using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using ASimpleMinecraftServer.UI.Dialogs;
using ASimpleMinecraftServer.UI.Commands;
using ASimpleMinecraftServer.UI.Navigation;
using ASimpleMinecraftServer.Services.State;
using ASimpleMinecraftServer.Services.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ASimpleMinecraftServer;

public partial class MainWindow : Window
{
    private readonly DownloadService _downloads = new();
    private readonly VersionManager _versions;
    private readonly ServerInstaller _installer;
    private readonly ServerLauncher _launcher = new();
    private readonly ProfileStore _profileStore;
    private readonly RuntimeSessionStore _runtimeSessionStore;
    private readonly BackupService _backupService;
    private readonly ObservableCollection<BackupRecord> _backups = new();
    private readonly DispatcherTimer _automaticBackupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _scheduledTaskTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly ScheduledTaskStore _scheduledTaskStore = new();
    private readonly ObservableCollection<ScheduledTaskRecord> _scheduledTasks = new();
    private bool _scheduledTaskBusy;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _crashRestartHistory = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingCrashRecoverySettings;
    private readonly NotificationStore _notificationStore = new();
    private readonly ObservableCollection<NotificationRecord> _notifications = new();
    private readonly ServerHealthAnalyzer _healthAnalyzer = new();
    private readonly ObservableCollection<HealthCheckItem> _healthChecks = new();
    private bool _backupBusy;
    private bool _loadingBackupSettings;
    private readonly ObservableCollection<ServerProfile> _servers;
    private ServerProfile? _active;
    private ServerProfile? _runningProfile;
    private CancellationTokenSource? _versionLoadCancellation;
    private CancellationTokenSource? _buildCancellation;
    private const string DefaultServersRoot = @"C:\GameServers";
    private bool _updatingInstallFolder;
    private readonly DispatcherTimer _uptimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _playerRefreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly ObservableCollection<string> _onlinePlayers = new();
    private DateTimeOffset? _serverStartedAt;
    private string _runtimeState = "Stopped";
    private readonly StringBuilder _consoleHistory = new();
    private readonly ConsoleService _consoleService = new();
    private readonly List<string> _commandHistory = new();
    private int _commandHistoryIndex;
    private bool _consoleDisplayReady;
    private int _createStep = 1;
    private TimeSpan _lastProcessorTime;
    private DateTimeOffset? _lastPerformanceSampleAt;
    private DateTimeOffset _lastDiskRefreshAt = DateTimeOffset.MinValue;
    private bool _diskRefreshInProgress;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memoryHistory = new();
    private readonly CommandRouter _commandRouter = new();
    private readonly ObservableCollection<AppCommand> _filteredPaletteCommands = new();
    private NavigationService? _navigation;
    private readonly PluginService _pluginService = new();
    private readonly ObservableCollection<PluginRecord> _plugins = new();
    private readonly List<PluginRecord> _allPlugins = new();
    private readonly ModrinthPluginCatalogService _pluginCatalogService = new();
    private readonly ObservableCollection<PluginCatalogItem> _pluginCatalogResults = new();
    private CancellationTokenSource? _pluginCatalogCancellation;
    private readonly WorldService _worldService = new();
    private readonly ServerPreflightService _serverPreflightService = new();
    private readonly ServerOperationGuard _serverOperationGuard = new();
    private readonly DialogService _dialogs;
    private readonly ApplicationState _applicationState = new();
    private readonly StructuredLogger _structuredLogger;
    private readonly ServerUpdateService _serverUpdateService;
    private ServerUpdateInfo? _currentServerUpdate;
    private CancellationTokenSource? _updateCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ObservableCollection<WorldRecord> _worlds = new();
    private bool _commandPaletteOpen;


    // Phase 8: extracted pages now own their controls directly. MainWindow
    // keeps orchestration logic and talks to each page through its public API.

    public MainWindow()
    {
        InitializeComponent();
        WireExtractedPageEvents();
        _dialogs = new DialogService(this);
        var displayVersion = GetDisplayVersion();
        SidebarVersionText.Text = $"v{displayVersion}";
        AboutVersionText.Text = $"v{displayVersion}";
        _versions = new VersionManager(_downloads);
        _serverUpdateService = new ServerUpdateService(_downloads);
        _installer = new ServerInstaller(_downloads, _versions);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASimpleMinecraftServer");
        _structuredLogger = new StructuredLogger(Path.Combine(dataDirectory, "Logs"));
        _profileStore = new ProfileStore(dataDirectory);
        _runtimeSessionStore = new RuntimeSessionStore(dataDirectory);
        _backupService = new BackupService(Path.Combine(DefaultServersRoot, "_Backups"));
        _servers = _profileStore.Load();
        foreach (var profile in _servers) profile.RuntimeState = "Stopped";
        _applicationState.ReplaceServers(_servers);
        _structuredLogger.Information("Application starting", new { Version = displayVersion, ProfileCount = _servers.Count });
        DashboardPage.BindServers(_servers);
        Directory.CreateDirectory(DefaultServersRoot);
        InstallFolderBox.Text = Path.Combine(DefaultServersRoot, SanitizeFolderName(ServerNameBox.Text));
        UpdateCreateForm();
        UpdateCreateWizard();
        _uptimeTimer.Tick += (_, _) => UpdateLiveServerMetrics();
        _playerRefreshTimer.Tick += (_, _) => RequestPlayerList(showStatus: false);
        OnlinePlayersList.ItemsSource = _onlinePlayers;
        _launcher.OutputReceived += (_, line) => Dispatcher.Invoke(() => HandleServerOutput(line));
        _launcher.Exited += (_, args) => Dispatcher.Invoke(() =>
        {
            _runtimeSessionStore.Clear();
            var profile = _runningProfile;
            var crashed = !args.StopWasRequested;
            var state = crashed ? "Failed" : "Stopped";
            StatusText.Text = state;
            ActionStatusText.Text = state;
            if (profile is not null) profile.RuntimeState = state;
            _runtimeState = state;
            _applicationState.RuntimeState = state;
            _serverStartedAt = null;
            ResetPerformanceSampling();
            _uptimeTimer.Stop();
            _playerRefreshTimer.Stop();
            _onlinePlayers.Clear();
            UpdatePlayerPanel();
            _runningProfile = null;
            _applicationState.RunningServer = null;
            var exitText = args.ExitCode is null ? string.Empty : $" (exit code {args.ExitCode})";
            if (crashed)
            {
                AppendLog($"[Manager] Server stopped unexpectedly{exitText}. Review the final console lines for the cause.");
                AddNotification("Crash", "Server stopped unexpectedly", $"The Java process exited{exitText}.", profile?.Name);
            }
            else if (args.WasForceKilled)
            {
                AppendLog("[Manager] Server did not stop within the timeout and was forcefully terminated.");
            }
            else
            {
                AppendLog($"[Manager] Server process exited normally{exitText}.");
            }
            UpdateDetails();
            if (crashed && profile is not null) _ = HandleCrashRecoveryAsync(profile, args.ExitCode);
        });
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
                BackupsPage.BackupsGrid.ItemsSource = _backups;
        PluginManagerPage.PluginsGrid.ItemsSource = _plugins;
        PluginManagerPage.CatalogGrid.ItemsSource = _pluginCatalogResults;
        WorldManagerPage.WorldsGrid.ItemsSource = _worlds;
        _automaticBackupTimer.Tick += AutomaticBackupTimer_Tick;
        _automaticBackupTimer.Start();
        foreach (var task in _scheduledTaskStore.Load()) _scheduledTasks.Add(task);
        foreach (var notification in _notificationStore.Load().OrderByDescending(item => item.CreatedAt)) _notifications.Add(notification);
        NotificationCenterPage.NotificationsGrid.ItemsSource = _notifications;
        HealthAnalyzerPage.ChecksGrid.ItemsSource = _healthChecks;
        ScheduledTasksPage.TasksGrid.ItemsSource = _scheduledTasks;
        _scheduledTaskTimer.Tick += ScheduledTaskTimer_Tick;
        _scheduledTaskTimer.Start();
        UpdateDetails();
        CommandPaletteList.ItemsSource = _filteredPaletteCommands;
        ConfigureNavigation();
        BuildCommandPalette();
        if (_servers.Count == 0) Navigate(AppPage.FirstRunWizard);
        _consoleDisplayReady = true;
    }


    private void WireExtractedPageEvents()
    {
        DashboardPage.ServerSelectionChanged += ServerList_SelectionChanged;
        DashboardPage.CreateServerRequested += CreateServer_Click;
        DashboardPage.BrowseExistingRequested += BrowseExisting_Click;
        ConfigurationPage.ConfigurationSaved += (_, profile) => { SaveProfiles(); AppendLog($"[Manager] Saved server configuration for {profile.Name}."); UpdateDetails(); };

        ConsolePage.SearchKeyDownRequested += ConsoleSearchBox_KeyDown;
        ConsolePage.FindPreviousRequested += FindPreviousConsoleText_Click;
        ConsolePage.FindNextRequested += FindConsoleText_Click;
        ConsolePage.FilterChangedRequested += ConsoleFilterCombo_SelectionChanged;
        ConsolePage.SaveLogRequested += SaveConsoleLog_Click;
        ConsolePage.PauseRequested += PauseConsole_Click;
        ConsolePage.JumpLatestRequested += JumpLatest_Click;
        ConsolePage.DisplayOptionChangedRequested += ConsoleDisplayOption_Changed;
        ConsolePage.ClearRequested += ClearConsole_Click;
        ConsolePage.CommandKeyDownRequested += CommandBox_KeyDown;
        ConsolePage.SendCommandRequested += SendCommand_Click;

        BackupsPage.CreateRequested += CreateBackup_Click;
        BackupsPage.SettingsChangedRequested += BackupSettings_Changed;
        BackupsPage.OpenFolderRequested += OpenBackupFolder_Click;
        BackupsPage.RefreshRequested += RefreshBackups_Click;
        BackupsPage.RestoreRequested += RestoreBackup_Click;
        BackupsPage.DeleteRequested += DeleteBackup_Click;

        ScheduledTasksPage.AddRequested += ScheduledTask_AddRequested;
        ScheduledTasksPage.DeleteRequested += ScheduledTask_DeleteRequested;
        ScheduledTasksPage.ToggleRequested += ScheduledTask_ToggleRequested;
        ScheduledTasksPage.RunNowRequested += ScheduledTask_RunNowRequested;

        NotificationCenterPage.MarkAllRequested += Notification_MarkAll;
        NotificationCenterPage.MarkSelectedRequested += Notification_MarkSelected;
        NotificationCenterPage.ClearRequested += Notification_Clear;
        HealthAnalyzerPage.AnalyzeRequested += async (_, _) => await AnalyzeServerHealthAsync();
        FirstRunWizardPage.CreateServerRequested += (_, _) => Navigate(AppPage.CreateServer);

        PluginManagerPage.InstallRequested += InstallPlugin_Click;
        PluginManagerPage.OpenFolderRequested += OpenPluginFolder_Click;
        PluginManagerPage.RefreshRequested += RefreshPlugins_Click;
        PluginManagerPage.EnableRequested += EnablePlugin_Click;
        PluginManagerPage.DisableRequested += DisablePlugin_Click;
        PluginManagerPage.DeleteRequested += DeletePlugin_Click;
        PluginManagerPage.ClearSearchRequested += ClearPluginSearch_Click;
        PluginManagerPage.SelectionChangedRequested += PluginsGrid_SelectionChanged;
        PluginManagerPage.SearchChangedRequested += PluginSearchBox_TextChanged;
        PluginManagerPage.DragOverRequested += PluginArea_DragOver;
        PluginManagerPage.DropRequested += PluginArea_Drop;
        PluginManagerPage.CatalogSearchRequested += SearchPluginCatalog_Click;
        PluginManagerPage.CatalogInstallRequested += InstallCatalogPlugin_Click;
        PluginManagerPage.CheckUpdatesRequested += CheckPluginUpdates_Click;
        PluginManagerPage.UpdateSelectedRequested += UpdateSelectedPlugins_Click;
        PluginManagerPage.UpdateAllRequested += UpdateAllPlugins_Click;

        WorldManagerPage.BackupRequested += BackupWorld_Click;
        WorldManagerPage.DeleteRequested += DeleteWorld_Click;
        WorldManagerPage.ImportRequested += ImportWorld_Click;
        WorldManagerPage.OpenFolderRequested += OpenWorldFolder_Click;
        WorldManagerPage.RefreshRequested += RefreshWorlds_Click;
        WorldManagerPage.RenameRequested += RenameWorld_Click;
        WorldManagerPage.VerifyRequested += VerifyWorld_Click;
        WorldManagerPage.SelectionChangedRequested += WorldsGrid_SelectionChanged;

        ServerManagementPage.AutoDetectJavaRequested += AutoDetectJava_Click;
        ServerManagementPage.BrowseJavaRequested += BrowseJava_Click;
        ServerManagementPage.DuplicateRequested += DuplicateServer_Click;
        ServerManagementPage.ExportProfileRequested += ExportProfile_Click;
        ServerManagementPage.ExportServerZipRequested += ExportServerZip_Click;
        ServerManagementPage.ImportProfileRequested += ImportProfile_Click;
        ServerManagementPage.MoveRequested += MoveServer_Click;
        ServerManagementPage.SaveProfileRequested += SaveManagementProfile_Click;
        ServerManagementPage.TestJavaRequested += TestJava_Click;
        ServerManagementPage.VerifyRequested += VerifyServer_Click;
        UpdateCenterPage.CheckRequested += async (_, _) => await CheckServerUpdateAsync();
        UpdateCenterPage.InstallRequested += async (_, _) => await InstallServerUpdateAsync();
        UpdateCenterPage.LoadVersionsRequested += async (_, _) => await LoadMinecraftVersionsAsync();
        UpdateCenterPage.ChangeVersionRequested += async (_, version) => await ChangeMinecraftVersionAsync(version);
    }

    private void ConfigureNavigation()
    {
        _navigation = new NavigationService(MainTabs, PageTitle, DashboardNavButton, CreateServerNavButton, ConsoleNavButton, ConfigurationNavButton, BackupsNavButton, PluginsNavButton, WorldsNavButton, ManagementNavButton, UpdatesNavButton, ScheduledTasksNavButton, NotificationsNavButton, HealthAnalyzerNavButton, StartupAnalyzerNavButton, LogAnalyzerNavButton, PluginCompatibilityNavButton, PerformanceDashboardNavButton, OptimizationNavButton, FirstRunWizardNavButton, AboutNavButton);
        _navigation.RegisterActivation(AppPage.CreateServer, () => { _createStep = 1; UpdateCreateWizard(); });
        _navigation.RegisterActivation(AppPage.Configuration, () => ConfigurationPage.ResetForSelection(_active));
        _navigation.RegisterActivation(AppPage.Backups, LoadBackups);
        _navigation.RegisterActivation(AppPage.Plugins, LoadPlugins);
        _navigation.RegisterActivation(AppPage.Worlds, LoadWorlds);
        _navigation.RegisterActivation(AppPage.Management, LoadManagementPage);
        _navigation.RegisterActivation(AppPage.Updates, () => UpdateCenterPage.Bind(_active, _currentServerUpdate));
        _navigation.RegisterActivation(AppPage.ScheduledTasks, LoadScheduledTasksPage);
        _navigation.RegisterActivation(AppPage.Notifications, LoadNotificationCenter);
        _navigation.RegisterActivation(AppPage.HealthAnalyzer, LoadHealthAnalyzerPage);
        _navigation.RegisterActivation(AppPage.StartupAnalyzer, () => StartupAnalyzerPage.Bind(_active));
        _navigation.RegisterActivation(AppPage.LogAnalyzer, () => LogAnalyzerPage.Bind(_active));
        _navigation.RegisterActivation(AppPage.PluginCompatibility, () => PluginCompatibilityPage.Bind(_active));
        _navigation.RegisterActivation(AppPage.PerformanceDashboard, () => PerformanceDashboardPage.Bind(_active));
        _navigation.RegisterActivation(AppPage.Optimization, () => OptimizationPage.Bind(_active));
        _navigation.RegisterActivation(AppPage.FirstRunWizard, () => { });
        _navigation.SynchronizeFromTabSelection();
    }

    private void Navigate(AppPage page) => _navigation?.Navigate(page);

    private void BuildCommandPalette()
    {
        _commandRouter.Commands.Clear();
        void Add(string id, string icon, string title, string description, string shortcut, KeyGesture? gesture, Func<bool> canExecute, Func<string> reason, Func<Task> execute) =>
            _commandRouter.Register(new AppCommand(id, icon, title, description, shortcut, gesture, canExecute, reason, execute));
        Task Done(Action action) { action(); return Task.CompletedTask; }

        Add("dashboard", "⌂", "Open Dashboard", "View servers, performance, and quick actions", "Ctrl+1", new KeyGesture(Key.D1, ModifierKeys.Control), () => true, () => "", () => Done(() => Navigate(AppPage.Dashboard)));
        Add("create", "+", "Create Server", "Open the guided server creation wizard", "Ctrl+2", new KeyGesture(Key.D2, ModifierKeys.Control), () => true, () => "", () => Done(() => Navigate(AppPage.CreateServer)));
        Add("console", ">_", "Open Console", "View live output and send server commands", "Ctrl+3", new KeyGesture(Key.D3, ModifierKeys.Control), () => true, () => "", () => Done(() => Navigate(AppPage.Console)));
        Add("configuration", "⚙", "Server Configuration", "Edit server.properties in Simple or Advanced mode", "Ctrl+4", new KeyGesture(Key.D4, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Configuration)));
        Add("backups", "▣", "Open Backups", "Create, restore, and manage server backups", "Ctrl+5", new KeyGesture(Key.D5, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Backups)));
        Add("plugins", "🧩", "Open Plugin Manager", "Install, enable, disable, and remove local plugin JARs", "Ctrl+6", new KeyGesture(Key.D6, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Plugins)));
        Add("worlds", "🌎", "Open World Manager", "Inspect, back up, import, rename, and remove worlds", "Ctrl+7", new KeyGesture(Key.D7, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Worlds)));
        Add("management", "◆", "Server Management", "Verify, rename, duplicate, move, import, or export", "Ctrl+8", new KeyGesture(Key.D8, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Management)));
        Add("updates", "↻", "Open Update Center", "Check for and safely install server software updates", "Ctrl+9", new KeyGesture(Key.D9, ModifierKeys.Control), () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Updates)));
        Add("tasks", "◷", "Open Scheduled Tasks", "Schedule restarts, commands, broadcasts, starts, stops, and update checks", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.ScheduledTasks)));
        Add("notifications", "!", "Open Notification Center", "Review server starts, stops, crashes, backups, and warnings", "", null, () => true, () => "", () => Done(() => Navigate(AppPage.Notifications)));
        Add("health", "♥", "Open Health Analyzer", "Score the selected server and identify configuration risks", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.HealthAnalyzer)));
        Add("startup", "▶", "Open Startup Analyzer", "Inspect startup timing and slow stages", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.StartupAnalyzer)));
        Add("loganalyzer", "≡", "Open Log Analyzer", "Scan latest.log for common errors and suggested fixes", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.LogAnalyzer)));
        Add("plugincompat", "✓", "Plugin Compatibility", "Check installed plugin metadata and server support", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.PluginCompatibility)));
        Add("performance", "▥", "Performance Dashboard", "View Java memory, CPU time, threads, plugins, and load settings", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.PerformanceDashboard)));
        Add("optimization", "⚡", "Optimization Recommendations", "Generate conservative performance recommendations", "", null, () => _active is not null, () => "Select a server first", () => Done(() => Navigate(AppPage.Optimization)));
        Add("firstrun", "1", "First Run Wizard", "Check the environment and create your first server", "", null, () => true, () => "", () => Done(() => Navigate(AppPage.FirstRunWizard)));
        Add("about", "i", "Open About", "View application version and information", "", null, () => true, () => "", () => Done(() => Navigate(AppPage.About)));
        Add("start", "▶", "Start Server", "Start the selected Minecraft server", "F5", new KeyGesture(Key.F5), () => _active is not null && !_launcher.IsRunning, () => _active is null ? "Select a server first" : "A server is already running", StartServerAsync);
        Add("restart", "↻", "Restart Server", "Gracefully stop and relaunch the running server", "Ctrl+F5", new KeyGesture(Key.F5, ModifierKeys.Control), () => _launcher.IsRunning, () => "No server is running", () => Done(() => Restart_Click(this, new RoutedEventArgs())));
        Add("stop", "■", "Stop Server", "Gracefully stop the running Minecraft server", "Shift+F5", new KeyGesture(Key.F5, ModifierKeys.Shift), () => _launcher.IsRunning, () => "No server is running", () => Done(() => Stop_Click(this, new RoutedEventArgs())));
        Add("backup", "▰", "Create Backup", "Create a manual backup of the selected server", "Ctrl+B", new KeyGesture(Key.B, ModifierKeys.Control), () => _active is not null && !_backupBusy, () => _active is null ? "Select a server first" : "A backup operation is already running", async () => { await CreateBackupAsync("Manual", showMessages: true); });
        Add("folder", "▱", "Open Server Folder", "Open the selected server installation in File Explorer", "", null, () => _active is not null, () => "Select a server first", () => Done(() => OpenFolder_Click(this, new RoutedEventArgs())));
        Add("verify", "✓", "Verify Server Installation", "Run the complete server health check", "", null, () => _active is not null, () => "Select a server first", () => Done(() => { Navigate(AppPage.Management); VerifyServerInternal(showMessage: true); }));
        Add("saveconsole", "⇩", "Export Console Log", "Save the complete console history to a log file", "", null, () => _consoleHistory.Length > 0, () => "Console history is empty", () => Done(() => SaveConsoleLog_Click(this, new RoutedEventArgs())));
        RefreshCommandPalette();
    }

    private void RefreshCommandPalette()
    {
        _commandRouter.RefreshAvailability();
        var query = CommandPaletteSearchBox?.Text.Trim() ?? string.Empty;
        _filteredPaletteCommands.Clear();
        foreach (var command in _commandRouter.Commands.Where(command => string.IsNullOrWhiteSpace(query) || command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || command.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            _filteredPaletteCommands.Add(command);
        CommandPaletteList.SelectedIndex = _filteredPaletteCommands.Count == 0 ? -1 : Math.Max(0, _filteredPaletteCommands.ToList().FindIndex(item => item.IsEnabled));
        CommandPaletteHintText.Text = _filteredPaletteCommands.Count == 0 ? "No matching commands" : "↑ ↓ Navigate    Enter Run    Esc Close";
    }

    private void OpenCommandPalette()
    {
        RefreshCommandPalette();
        CommandPaletteSearchBox.Text = string.Empty;
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        _commandPaletteOpen = true;
        Dispatcher.BeginInvoke(() => CommandPaletteSearchBox.Focus(), DispatcherPriority.Input);
    }

    private void CloseCommandPalette() { CommandPaletteOverlay.Visibility = Visibility.Collapsed; _commandPaletteOpen = false; }

    private async Task ExecutePaletteCommandAsync(AppCommand? command)
    {
        var result = await _commandRouter.ExecuteAsync(command);
        if (!result.Executed) { CommandPaletteHintText.Text = result.Message; return; }
        CloseCommandPalette();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_commandPaletteOpen) CloseCommandPalette(); else OpenCommandPalette();
            e.Handled = true; return;
        }
        if (_commandPaletteOpen && e.Key == Key.Escape) { CloseCommandPalette(); e.Handled = true; return; }
        if (_commandPaletteOpen) return;

        var command = _commandRouter.FindShortcut(e.Key, Keyboard.Modifiers);
        if (command is null) return;
        var result = await _commandRouter.ExecuteAsync(command);
        if (!result.Executed) ShowCommandUnavailable(result.Message);
        e.Handled = true;
    }

    private void ShowCommandUnavailable(string message)
    {
        ActionStatusText.Text = message;
        AppendLog("[Manager] " + message);
    }

    private void CommandPaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCommandPalette();
    private async void CommandPaletteSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && CommandPaletteList.Items.Count > 0) { CommandPaletteList.SelectedIndex = Math.Min(CommandPaletteList.Items.Count - 1, CommandPaletteList.SelectedIndex + 1); CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Up && CommandPaletteList.Items.Count > 0) { CommandPaletteList.SelectedIndex = Math.Max(0, CommandPaletteList.SelectedIndex - 1); CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Enter) { await ExecutePaletteCommandAsync(CommandPaletteList.SelectedItem as AppCommand); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseCommandPalette(); e.Handled = true; }
    }
    private async void CommandPaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await ExecutePaletteCommandAsync(CommandPaletteList.SelectedItem as AppCommand);
    private void CommandPaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e) { if (ReferenceEquals(e.OriginalSource, CommandPaletteOverlay)) CloseCommandPalette(); }
    private void CommandPalettePanel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private string SelectedServerType => (ServerTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Vanilla";
    private string SelectedVersion => VersionCombo.Text.Trim();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            RestoreRuntimeSession();
            await LoadVersionsAsync();
            if (_active is null)
            {
                var java = await JavaDetector.DetectAsync(_lifetimeCancellation.Token);
                var result = java is null
                    ? new JavaValidationResult(false, null, 0, "Java was not detected in Windows PATH.")
                    : new JavaValidationResult(true, java, 0, java.Summary + " detected in Windows PATH.");
                UpdateJavaStatus(result);
                if (!result.IsValid) AppendLog("[Manager] " + result.Message);
            }
            else
            {
                var javaResult = await JavaDetector.ValidateAsync(_active.JavaPath, _active.Version, _lifetimeCancellation.Token);
                UpdateJavaStatus(javaResult);
                if (!javaResult.IsValid) AppendLog("[Manager] " + javaResult.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Functional MainWindow Loaded initialization failed", ex);
            try { AppendLog("[Manager] Startup initialization warning: " + ex.Message); } catch { }
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_launcher.IsRunning) { DisposeServices(); return; }
        if (MessageBox.Show("A server is running. Stop it and exit?", "Server running", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        e.Cancel = true;
        IsEnabled = false;
        await _launcher.StopAsync(TimeSpan.FromSeconds(12));
        DisposeServices();
        Closing -= MainWindow_Closing;
        Close();
    }

    private void DisposeServices()
    {
        _lifetimeCancellation.Cancel();
        _versionLoadCancellation?.Cancel();
        _versionLoadCancellation?.Dispose();
        _buildCancellation?.Cancel();
        _buildCancellation?.Dispose();
        _uptimeTimer.Stop();
        _playerRefreshTimer.Stop();
        _launcher.Dispose();
        _downloads.Dispose();
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _pluginCatalogCancellation?.Cancel();
        _pluginCatalogCancellation?.Dispose();
        _pluginCatalogService.Dispose();
        _structuredLogger.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async Task LoadVersionsAsync()
    {
        _versionLoadCancellation?.Cancel();
        _versionLoadCancellation?.Dispose();
        _versionLoadCancellation = new CancellationTokenSource();
        try
        {
            StatusText.Text = "Loading versions…";
            VersionCombo.Items.Clear();
            foreach (var version in await _versions.GetVersionsAsync(SelectedServerType, _versionLoadCancellation.Token)) VersionCombo.Items.Add(version);
            if (VersionCombo.Items.Count > 0) VersionCombo.SelectedIndex = 0;
            StatusText.Text = "Ready";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog("[Manager] Could not load versions: " + ex.Message);
            StatusText.Text = "Version lookup failed";
        }
    }

    private async void ServerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomJarPanel is not null)
        {
            var custom = SelectedServerType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase);
            CustomJarPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            ServerTypeHelpText.Text = SelectedServerType switch
            {
                "Paper" => "Recommended for most servers. Fast and supports Bukkit/Spigot/Paper plugins.",
                "Purpur" => "Paper-compatible with additional gameplay and performance configuration.",
                "Vanilla" => "Official Mojang server with the standard experience and no plugin support.",
                _ => "Use a server JAR you already downloaded. Version lookup is not required."
            };
        }

        UpdateCreateForm();
        if (IsLoaded) await LoadVersionsAsync();
    }

    private void CreateForm_Changed(object sender, RoutedEventArgs e) => UpdateCreateForm();

    private void UpdateCreateForm()
    {
        if (BuildReviewText is null || BuildServerButton is null) return;

        var name = ServerNameBox?.Text.Trim() ?? string.Empty;
        var folder = InstallFolderBox?.Text.Trim() ?? string.Empty;
        var version = SelectedServerType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase)
            ? "Custom JAR"
            : (string.IsNullOrWhiteSpace(SelectedVersion) ? "Select a version" : SelectedVersion);
        var portText = ServerPortBox?.Text.Trim() ?? "25565";
        var playerText = MaxPlayersBox?.Text.Trim() ?? "20";
        var difficulty = (DifficultyCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";

        BuildReviewText.Text = $"{(string.IsNullOrWhiteSpace(name) ? "Unnamed server" : name)} • {SelectedServerType} {version} • {ParseMemory()} GB RAM{Environment.NewLine}" +
                               $"Port {portText} • {playerText} players • {difficulty} • {(OnlineModeCheckBox?.IsChecked == true ? "Online mode" : "Offline mode")}{Environment.NewLine}" +
                               $"Install folder: {(string.IsNullOrWhiteSpace(folder) ? "Not selected" : folder)}";

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name)) errors.Add("Enter a server name.");
        if (string.IsNullOrWhiteSpace(folder)) errors.Add("Choose an install folder.");
        if (!SelectedServerType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(SelectedVersion)) errors.Add("Choose a Minecraft version.");
        if (SelectedServerType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase) && !File.Exists(CustomJarBox?.Text.Trim())) errors.Add("Choose a valid custom server JAR.");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) errors.Add("Server port must be between 1 and 65535.");
        if (!int.TryParse(playerText, out var maxPlayers) || maxPlayers is < 1 or > 1000) errors.Add("Maximum players must be between 1 and 1000.");
        if (EulaCheckBox?.IsChecked != true) errors.Add("Accept the Minecraft EULA before creating the server.");

        BuildValidationText.Text = string.Join(Environment.NewLine, errors);
        BuildServerButton.IsEnabled = errors.Count == 0 && _buildCancellation is null;
    }


    private void CreateStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var step))
        {
            _createStep = Math.Clamp(step, 1, 5);
            UpdateCreateWizard();
        }
    }

    private void CreatePrevious_Click(object sender, RoutedEventArgs e)
    {
        _createStep = Math.Max(1, _createStep - 1);
        UpdateCreateWizard();
    }

    private void CreateNext_Click(object sender, RoutedEventArgs e)
    {
        _createStep = Math.Min(5, _createStep + 1);
        UpdateCreateWizard();
    }

    private void UpdateCreateWizard()
    {
        if (CreateStep1Panel is null) return;

        var panels = new[] { CreateStep1Panel, CreateStep2Panel, CreateStep3Panel, CreateStep4Panel, CreateStep5Panel };
        var buttons = new[] { CreateStep1Button, CreateStep2Button, CreateStep3Button, CreateStep4Button, CreateStep5Button };
        for (var index = 0; index < panels.Length; index++)
        {
            panels[index].Visibility = index + 1 == _createStep ? Visibility.Visible : Visibility.Collapsed;
            buttons[index].Style = (Style)FindResource(index + 1 == _createStep ? "PrimaryButton" : "SecondaryButton");
        }

        CreatePreviousButton.IsEnabled = _createStep > 1;
        CreateNextButton.Visibility = _createStep < 5 ? Visibility.Visible : Visibility.Hidden;
        CreateStepStatusText.Text = $"Step {_createStep} of 5";
        UpdateCreateForm();
    }

    private async void BuildServer_Click(object sender, RoutedEventArgs e)
    {
        UpdateCreateForm();
        if (!BuildServerButton.IsEnabled) return;

        _buildCancellation?.Dispose();
        _buildCancellation = new CancellationTokenSource();
        try
        {
            BuildServerButton.IsEnabled = false;
            CancelBuildButton.IsEnabled = true;
            BuildProgress.IsIndeterminate = true;
            BuildMessage.Text = "Preparing installation…";
            StatusText.Text = "Building";
            var serverName = ServerNameBox.Text.Trim();

            if (_servers.Any(item => string.Equals(item.Name.Trim(), serverName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A server named '{serverName}' already exists in the manager.");

            var installFolder = ResolveInstallFolder(serverName);
            if (Directory.Exists(installFolder) && Directory.EnumerateFileSystemEntries(installFolder).Any())
                throw new InvalidOperationException($"A server folder already exists at:{Environment.NewLine}{installFolder}{Environment.NewLine}{Environment.NewLine}Choose another server name or install folder.");

            var difficulty = (DifficultyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            var request = new ServerInstallRequest(
                serverName,
                installFolder,
                SelectedServerType,
                SelectedVersion,
                ParseMemory(),
                CustomJarBox.Text.Trim(),
                int.Parse(ServerPortBox.Text.Trim()),
                int.Parse(MaxPlayersBox.Text.Trim()),
                difficulty,
                OnlineModeCheckBox.IsChecked == true);

            BuildMessage.Text = $"Downloading and installing {SelectedServerType}…";
            var profile = await _installer.InstallAsync(request, _buildCancellation.Token);
            AddOrReplace(profile);
            SaveProfiles();
            Select(profile);
            AppendLog($"[Manager] Built {profile.Type} {profile.Version} at {profile.Folder}");
            BuildMessage.Text = "Server created successfully. Select Start when you are ready.";
            StatusText.Text = "Ready";
        }
        catch (OperationCanceledException)
        {
            BuildMessage.Text = "Installation cancelled. Incomplete files may remain in the selected folder.";
            StatusText.Text = "Cancelled";
            AppendLog("[Manager] Server installation cancelled.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog("[Manager] Build failed: " + ex);
            BuildMessage.Text = "Build failed. Review the message and try again.";
            StatusText.Text = "Error";
        }
        finally
        {
            BuildProgress.IsIndeterminate = false;
            CancelBuildButton.IsEnabled = false;
            _buildCancellation?.Dispose();
            _buildCancellation = null;
            UpdateCreateForm();
        }
    }

    private void CancelBuild_Click(object sender, RoutedEventArgs e)
    {
        CancelBuildButton.IsEnabled = false;
        BuildMessage.Text = "Cancelling installation…";
        _buildCancellation?.Cancel();
    }


    private string ResolveInstallFolder(string serverName)
    {
        var entered = InstallFolderBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(entered) || string.Equals(entered, DefaultServersRoot, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(DefaultServersRoot, SanitizeFolderName(serverName));
        return Path.GetFullPath(entered);
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Minecraft Server" : cleaned;
    }

    private void ServerNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingInstallFolder || InstallFolderBox is null) return;
        var current = InstallFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(current) || current.StartsWith(DefaultServersRoot, StringComparison.OrdinalIgnoreCase))
        {
            _updatingInstallFolder = true;
            InstallFolderBox.Text = Path.Combine(DefaultServersRoot, SanitizeFolderName(ServerNameBox.Text));
            _updatingInstallFolder = false;
        }
        UpdateCreateForm();
    }

    private int ParseMemory() => int.TryParse(System.Text.RegularExpressions.Regex.Match((MemoryCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "4", @"\d+").Value, out var memory) ? memory : 4;

    private void AddOrReplace(ServerProfile profile)
    {
        var existing = _servers.FirstOrDefault(item => string.Equals(item.Folder, profile.Folder, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _servers.Remove(existing);
        _servers.Add(profile);
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartServerAsync();

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        var profile = _runningProfile;
        if (profile is null || !_launcher.IsRunning) return;
        SetRuntimeState("Stopping…", profile);
        SetServerActionState(isBusy: true);
        AppendLog("[Manager] Restart requested. Stopping the server first…");
        await _launcher.StopAsync(TimeSpan.FromSeconds(20));
        if (!_launcher.IsRunning)
        {
            _active = profile;
            await Task.Delay(750);
            await StartServerAsync();
        }
        else
        {
            MessageBox.Show("The server could not be stopped, so restart was cancelled.", "Restart cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateDetails();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!_launcher.IsRunning) return;
        var wasAttached = _launcher.IsAttachedProcess;
        SetRuntimeState("Stopping…");
        SetServerActionState(isBusy: true);
        AppendLog(wasAttached
            ? "[Manager] Stopping a recovered server process. Console input cannot be reattached, so it will be terminated if it remains active."
            : "[Manager] Sending the graceful stop command…");
        await _launcher.StopAsync(TimeSpan.FromSeconds(20));
        if (_launcher.IsRunning)
        {
            SetRuntimeState("Failed");
            MessageBox.Show("The server process is still running after the stop attempt.", "Stop failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateDetails();
    }

    private async Task StartServerAsync()
    {
        try
        {
            if (_active is null) throw new InvalidOperationException("Select a server first.");
            if (_launcher.IsRunning) throw new InvalidOperationException($"{_runningProfile?.Name ?? "Another server"} is already running. Stop it before starting another server.");
            var preflight = _serverPreflightService.Validate(_active);
            if (!preflight.IsValid) throw new InvalidOperationException(preflight.Message);

            var javaValidation = await JavaDetector.ValidateAsync(_active.JavaPath, _active.Version, _lifetimeCancellation.Token);
            UpdateJavaStatus(javaValidation);
            if (!javaValidation.IsValid)
            {
                AppendLog("[Manager] Java validation failed: " + javaValidation.Message);
                var prompt = javaValidation.Message + Environment.NewLine + Environment.NewLine +
                    $"Required: Java {javaValidation.RequiredMajorVersion} or newer." + Environment.NewLine +
                    "Open Server Management to browse for java.exe or use Auto Detect Java.";
                MessageBox.Show(prompt, "Java configuration required", MessageBoxButton.OK, MessageBoxImage.Error);
                SelectMainTab(7);
                LoadManagementPage();
                return;
            }

            if (javaValidation.Installation is not null &&
                !string.Equals(_active.JavaPath, javaValidation.Installation.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                _active.JavaPath = javaValidation.Installation.ExecutablePath;
                SaveProfiles();
            }

            SetRuntimeState("Starting…", _active);
            AppendLog($"[Manager] Launching {_active.Name} with {javaValidation.Installation?.Summary} from {_active.Folder}…");
            _launcher.Start(_active);
            _onlinePlayers.Clear();
            _playerRefreshTimer.Start();
            _runningProfile = _active;
            _serverStartedAt = DateTimeOffset.Now;
            if (_launcher.ProcessId is int processId)
                _runtimeSessionStore.Save(new RuntimeSession(processId, _active.Folder, _serverStartedAt.Value));
            _uptimeTimer.Start();
            SetRuntimeState("Running", _active);
            StatusText.Text = "Running";
            AppendLog($"[Manager] Java process started with PID {_launcher.ProcessId}.");
            AddNotification("Server", "Server started", $"Java process started with PID {_launcher.ProcessId}.", _active.Name);
            UpdateDetails();
        }
        catch (Exception ex)
        {
            SetRuntimeState("Failed", _active);
            AppendLog("[Manager] Start failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Could not start server", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateJavaStatus(JavaValidationResult result)
    {
        var text = result.Installation?.Summary ?? "Not detected";
        ActionJavaText.Text = text;
        VisibleJavaText.Text = text;
        ActionJavaText.ToolTip = result.Installation?.VersionText ?? result.Message;
        VisibleJavaText.ToolTip = ActionJavaText.ToolTip;
        if (ServerManagementPage.ManagementJavaStatusText is not null)
        {
            ServerManagementPage.ManagementJavaStatusText.Text = result.Message;
            ServerManagementPage.ManagementJavaStatusText.ToolTip = result.Installation?.VersionText;
        }
    }

    private void RestoreRuntimeSession()
    {
        var session = _runtimeSessionStore.Load();
        if (session is null) return;
        var profile = _servers.FirstOrDefault(item => string.Equals(item.Folder, session.ServerFolder, StringComparison.OrdinalIgnoreCase));
        if (profile is null || !_launcher.TryAttach(session.ProcessId))
        {
            _runtimeSessionStore.Clear();
            return;
        }

        _active = profile;
        _runningProfile = profile;
        _serverStartedAt = session.StartedAt;
        profile.RuntimeState = "Running";
        _runtimeState = "Running";
        _uptimeTimer.Start();
        Select(profile);
        AppendLog($"[Manager] Recovered running server process PID {session.ProcessId}. Live metrics are available; restart it to restore console input/output.");
        UpdateDetails();
    }

    private void SendCommand_Click(object sender, RoutedEventArgs e) => SendCommand();
    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendCommand(); e.Handled = true; return; }
        if (e.Key == Key.Up) { BrowseCommandHistory(-1); e.Handled = true; return; }
        if (e.Key == Key.Down) { BrowseCommandHistory(1); e.Handled = true; }
    }

    private void BrowseCommandHistory(int direction)
    {
        if (_commandHistory.Count == 0) return;
        _commandHistoryIndex = Math.Clamp(_commandHistoryIndex + direction, 0, _commandHistory.Count);
        ConsolePage.CommandBox.Text = _commandHistoryIndex == _commandHistory.Count ? string.Empty : _commandHistory[_commandHistoryIndex];
        ConsolePage.CommandBox.CaretIndex = ConsolePage.CommandBox.Text.Length;
    }

    private void SendCommand()
    {
        var command = ConsolePage.CommandBox.Text.Trim();
        if (!_launcher.IsRunning || command.Length == 0) return;
        if (!_launcher.CanSendCommands)
        {
            MessageBox.Show("This server process was recovered after the manager reopened. Windows cannot reconnect to its original console input. Restart the server from the manager to restore command support.", "Console unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _launcher.SendCommand(command);
        if (_commandHistory.Count == 0 || !string.Equals(_commandHistory[^1], command, StringComparison.Ordinal)) _commandHistory.Add(command);
        _commandHistoryIndex = _commandHistory.Count;
        AppendLog("> " + command);
        ConsolePage.CommandBox.Clear();
    }

    private void AppendLog(string text) => Dispatcher.Invoke(() =>
    {
        _structuredLogger.WriteFromConsoleText(text);
        var entry = _consoleService.Add(text);
        var line = entry.Format(ConsolePage.ConsoleTimestampsCheckBox?.IsChecked != false);
        _consoleHistory.AppendLine(line);
        if (_consoleService.IsPaused)
        {
            ConsolePage.ConsoleLiveText.Text = $"PAUSED • {_consoleService.BufferedCount} BUFFERED";
            ConsolePage.ConsoleLiveDot.Fill = System.Windows.Media.Brushes.Gold;
            ConsolePage.JumpLatestButton.Visibility = Visibility.Visible;
            return;
        }
        if (CurrentConsoleFilter() == ConsoleLevel.All || CurrentConsoleFilter() == entry.Level)
            ConsolePage.ConsoleBox.AppendText(line + Environment.NewLine);
        if (ConsolePage.ConsoleAutoScrollCheckBox?.IsChecked != false) ConsolePage.ConsoleBox.ScrollToEnd();
    });

    private ConsoleLevel CurrentConsoleFilter()
    {
        var text = (ConsolePage.ConsoleFilterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return Enum.TryParse<ConsoleLevel>(text, out var result) ? result : ConsoleLevel.All;
    }

    private void RebuildConsoleDisplay()
    {
        if (!_consoleDisplayReady || ConsolePage.ConsoleBox is null) return;
        ConsolePage.ConsoleBox.Text = _consoleService.BuildText(CurrentConsoleFilter(), ConsolePage.ConsoleTimestampsCheckBox?.IsChecked != false);
        if (ConsolePage.ConsoleAutoScrollCheckBox?.IsChecked != false) ConsolePage.ConsoleBox.ScrollToEnd();
    }

    private void PauseConsole_Click(object sender, RoutedEventArgs e)
    {
        if (_consoleService.IsPaused)
        {
            _consoleService.Resume();
            ConsolePage.PauseConsoleButton.Content = "Pause";
            ConsolePage.ConsoleLiveText.Text = "LIVE OUTPUT";
            ConsolePage.ConsoleLiveDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 212, 90));
            ConsolePage.JumpLatestButton.Visibility = Visibility.Collapsed;
            RebuildConsoleDisplay();
        }
        else
        {
            _consoleService.Pause();
            ConsolePage.PauseConsoleButton.Content = "Resume";
            ConsolePage.ConsoleLiveText.Text = "PAUSED";
            ConsolePage.ConsoleLiveDot.Fill = System.Windows.Media.Brushes.Gold;
            ConsolePage.JumpLatestButton.Visibility = Visibility.Visible;
        }
    }

    private void JumpLatest_Click(object sender, RoutedEventArgs e)
    {
        if (_consoleService.IsPaused) PauseConsole_Click(sender, e);
        ConsolePage.ConsoleBox.ScrollToEnd();
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e)
    {
        _consoleService.Clear();
        _consoleHistory.Clear();
        ConsolePage.ConsoleBox.Clear();
    }

    private void ConsoleDisplayOption_Changed(object sender, RoutedEventArgs e) => RebuildConsoleDisplay();
    private void ConsoleFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildConsoleDisplay();

    private void SaveConsoleLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save console log", Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt", FileName = $"MinecraftConsole_{DateTime.Now:yyyy-MM-dd_HH-mm}.log" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _consoleService.BuildText(ConsoleLevel.All, true)); AppendLog("[Manager] Console log exported to " + dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not save console log", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ConsoleSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindConsoleText();
        e.Handled = true;
    }

    private void FindConsoleText_Click(object sender, RoutedEventArgs e) => FindConsoleText();
    private void FindPreviousConsoleText_Click(object sender, RoutedEventArgs e) => FindConsoleText(previous: true);

    private void FindConsoleText(bool previous = false)
    {
        var query = ConsolePage.ConsoleSearchBox.Text.Trim();
        if (query.Length == 0) return;
        int index;
        if (previous)
        {
            var start = Math.Max(0, ConsolePage.ConsoleBox.SelectionStart - 1);
            index = ConsolePage.ConsoleBox.Text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = ConsolePage.ConsoleBox.Text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var start = Math.Max(0, ConsolePage.ConsoleBox.SelectionStart + ConsolePage.ConsoleBox.SelectionLength);
            index = ConsolePage.ConsoleBox.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = ConsolePage.ConsoleBox.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }
        if (index < 0) return;
        ConsolePage.ConsoleBox.Focus();
        ConsolePage.ConsoleBox.Select(index, query.Length);
        ConsolePage.ConsoleBox.ScrollToLine(ConsolePage.ConsoleBox.GetLineIndexFromCharacterIndex(index));
    }

    private void BrowseExisting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a Minecraft server folder", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var jars = Directory.GetFiles(dialog.FolderName, "*.jar", SearchOption.TopDirectoryOnly);
        if (jars.Length == 0) { MessageBox.Show("No JAR files were found in that folder."); return; }
        var jar = jars.FirstOrDefault(path => Path.GetFileName(path).Equals("server.jar", StringComparison.OrdinalIgnoreCase)) ?? jars[0];
        var profile = new ServerProfile { Name = new DirectoryInfo(dialog.FolderName).Name, Folder = dialog.FolderName, Type = DetectType(jar), Version = "Existing", MemoryGb = 4, Jar = Path.GetFileName(jar) };
        AddOrReplace(profile); SaveProfiles(); Select(profile);
    }

    private static string DetectType(string jar)
    {
        var name = Path.GetFileName(jar).ToLowerInvariant();
        return name.Contains("paper") ? "Paper" : name.Contains("purpur") ? "Purpur" : name.Contains("forge") ? "Forge/Custom" : name.Contains("fabric") ? "Fabric/Custom" : "Vanilla/Custom";
    }

    private void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        if (ReferenceEquals(_active, _runningProfile) && _launcher.IsRunning)
        {
            MessageBox.Show("Stop this server before forgetting it.", "Server is running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var profile = _active;
        var message = $"Forget '{profile.Name}'?{Environment.NewLine}{Environment.NewLine}The server will be removed from this manager, but its files will remain at:{Environment.NewLine}{profile.Folder}";
        if (MessageBox.Show(message, "Forget server", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _servers.Remove(profile);
        _active = null;
        SaveProfiles();
        UpdateDetails();
    }

    private async void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        var profile = _active;
        var warning = $"Permanently delete '{profile.Name}' and everything in this folder?{Environment.NewLine}{Environment.NewLine}{profile.Folder}{Environment.NewLine}{Environment.NewLine}This cannot be undone.";
        if (MessageBox.Show(warning, "Delete Minecraft server", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        try
        {
            if (ReferenceEquals(profile, _runningProfile) && _launcher.IsRunning)
            {
                SetServerActionState(isBusy: true);
                await _launcher.StopAsync(TimeSpan.FromSeconds(12));
                if (_launcher.IsRunning) throw new InvalidOperationException("The running server could not be stopped, so its files were not deleted.");
            }
            if (Directory.Exists(profile.Folder)) Directory.Delete(profile.Folder, recursive: true);
            _servers.Remove(profile);
            _active = null;
            SaveProfiles();
            UpdateDetails();
            AppendLog($"[Manager] Deleted server '{profile.Name}' and its folder.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _active = DashboardPage.Servers.SelectedItem as ServerProfile;
        _applicationState.SelectedServer = _active;
        UpdateDetails();
        if (MainTabs.SelectedIndex == 3) ConfigurationPage.LoadConfiguration(force: true);
        if (MainTabs.SelectedIndex == 4) LoadBackups();
        if (MainTabs.SelectedIndex == 5) LoadPlugins();
        if (MainTabs.SelectedIndex == 6) LoadWorlds();
        if (MainTabs.SelectedIndex == 7) LoadManagementPage();
    }
    private void Select(ServerProfile profile) { DashboardPage.Servers.SelectedItem = profile; DashboardPage.Servers.ScrollIntoView(profile); _active = profile; _applicationState.SelectedServer = profile; UpdateDetails(); }

    private void SetRuntimeState(string state, ServerProfile? profile = null)
    {
        _runtimeState = state;
        _applicationState.RuntimeState = state;
        var target = profile ?? _runningProfile;
        if (target is not null) target.RuntimeState = state;
        ActionStatusText.Text = state;
        UpdateDetails();
    }

    private void UpdateUptimeDisplay()
    {
        if (!_launcher.IsRunning || _serverStartedAt is null)
        {
            UptimeValueText.Text = "—";
            ConsolePage.ConsoleServerText.Text = "Select a server to view and send commands.";
            return;
        }

        var elapsed = DateTimeOffset.Now - _serverStartedAt.Value;
        UptimeValueText.Text = elapsed.TotalDays >= 1
            ? $"{(int)elapsed.TotalDays}d " + elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"hh\:mm\:ss");
    }

    private void UpdateDetails()
    {
        _loadingCrashRecoverySettings = true;
        CrashRecoveryCheckBox.IsChecked = _active?.CrashRecoveryEnabled == true;
        CrashRecoveryLimitCombo.SelectedIndex = _active?.CrashRestartLimit switch { 1 => 0, 2 => 1, 3 => 2, 5 => 3, _ => 2 };
        CrashRecoveryDelayCombo.SelectedIndex = _active?.CrashRestartDelaySeconds switch { 5 => 0, 10 => 1, 30 => 2, 60 => 3, _ => 1 };
        CrashRecoveryStatusText.Text = _active is null ? "Select a server to configure crash recovery." : (_active.CrashRecoveryEnabled ? $"Enabled • up to {_active.CrashRestartLimit} restart(s) in {_active.CrashRestartWindowMinutes} minutes" : "Disabled");
        _loadingCrashRecoverySettings = false;

        var selectedIsRunning = _launcher.IsRunning && ReferenceEquals(_active, _runningProfile);
        var selectedState = selectedIsRunning ? _runtimeState : _active?.RuntimeState ?? "No server selected";
        var runningBrush = (System.Windows.Media.Brush)FindResource("Green");
        var stoppedBrush = (System.Windows.Media.Brush)FindResource("StatusGray");
        var failedBrush = (System.Windows.Media.Brush)FindResource("DangerBorder");
        var stateBrush = selectedState == "Running" ? runningBrush : selectedState == "Failed" ? failedBrush : stoppedBrush;

        HealthStatusText.Text = selectedState;
        HealthStatusText.Foreground = System.Windows.Media.Brushes.White;
        HeaderStatusDot.Fill = System.Windows.Media.Brushes.White;
        RuntimeStatusBadge.Background = selectedState switch
        {
            "Running" => (System.Windows.Media.Brush)FindResource("GreenButton"),
            "Starting" or "Stopping" => (System.Windows.Media.Brush)FindResource("ActionAmber"),
            "Failed" => (System.Windows.Media.Brush)FindResource("ActionRed"),
            _ => (System.Windows.Media.Brush)FindResource("StatusGray")
        };
        SelectedStatusDot.Fill = stateBrush;
        RuntimeStateValueText.Text = selectedState;
        RuntimeStateValueText.Foreground = stateBrush;
        ActionStatusText.Text = selectedState;
        ActionStatusText.Foreground = stateBrush;
        UpdateLiveServerMetrics();
        SetServerActionState();

        if (_active is null)
        {
            SelectedName.Text = "No server selected";
            NoSelectionHintText.Visibility = Visibility.Visible;
            SelectedServerSubtitleText.Visibility = Visibility.Collapsed;
            SelectedServerSubtitleText.Text = string.Empty;
            CpuUsageBar.Value = 0;
            MemoryUsageBar.Value = 0;
            SelectedDetails.Text = "Select or add a server";
            DashboardServerName.Text = "No server selected";
            DashboardServerDetails.Text = "Add or browse for a server using the Dashboard.";
            ActionWorldText.Text = "—";
            ActionWorldText.ToolTip = null;
            ActionMemoryText.Text = "—";
            CpuUsageValueText.Text = "—";
            DiskUsageValueText.Text = "—";
            PortValueText.Text = "—";
            ServerTypeBadgeText.Text = "NO SERVER";
            VisibleVersionText.Text = "—";
            VisibleJarText.Text = "—";
            VisibleLocationText.Text = "—";
            UptimeValueText.Text = "—";
            ConsolePage.ConsoleServerText.Text = "Select a server to view and send commands.";
            return;
        }

        NoSelectionHintText.Visibility = Visibility.Collapsed;
        SelectedServerSubtitleText.Visibility = Visibility.Visible;
        SelectedServerSubtitleText.Text = $"{_active.Type} {_active.Version}  •  Java {_active.MemoryGb} GB allocation  •  {_active.Folder}";
        SelectedName.Text = _active.Name;
        ConsolePage.ConsoleServerText.Text = $"{_active.Name}  •  {_active.Type} {_active.Version}";
        DashboardServerName.Text = _active.Name;
        var port = ReadPort(_active.Folder);
        SelectedDetails.Text = string.Join(
            Environment.NewLine,
            $"{_active.Type}  •  {_active.Version}",
            $"Port: {port}",
            _active.Folder);
        DashboardServerDetails.Text = SelectedDetails.Text;
        ActionWorldText.Text = Path.GetFileName(_active.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ActionWorldText.ToolTip = _active.Folder;
        ActionMemoryText.Text = selectedIsRunning ? "Starting…" : "—";
        CpuUsageValueText.Text = selectedIsRunning ? "Sampling…" : "—";
        PortValueText.Text = port.ToString();
        _ = RefreshDiskUsageAsync();
        ServerTypeBadgeText.Text = _active.Type.ToUpperInvariant();
        VisibleVersionText.Text = _active.Version;
        VisibleJarText.Text = _active.Jar;
        VisibleLocationText.Text = _active.Folder;
        if (!selectedIsRunning) UptimeValueText.Text = "—";
    }


    private void UpdateLiveServerMetrics()
    {
        UpdateUptimeDisplay();

        var selectedIsRunning = _launcher.IsRunning && ReferenceEquals(_active, _runningProfile);
        if (!selectedIsRunning || !_launcher.TryGetPerformanceSnapshot(out var processorTime, out var workingSetBytes))
        {
            CpuUsageValueText.Text = "—";
            ActionMemoryText.Text = "—";
            CpuUsageBar.Value = 0;
            MemoryUsageBar.Value = 0;
            ResetPerformanceSampling();
            return;
        }

        var now = DateTimeOffset.Now;
        if (_lastPerformanceSampleAt is not null)
        {
            var elapsed = now - _lastPerformanceSampleAt.Value;
            var cpuDelta = processorTime - _lastProcessorTime;
            if (elapsed.TotalMilliseconds > 0)
            {
                var percent = cpuDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100d;
                var clampedCpu = Math.Clamp(percent, 0d, 100d);
                CpuUsageValueText.Text = $"{clampedCpu:0.0}%";
                CpuUsageBar.Value = clampedCpu;
                AddPerformanceSample(_cpuHistory, clampedCpu);
            }
        }

        _lastProcessorTime = processorTime;
        _lastPerformanceSampleAt = now;
        ActionMemoryText.Text = FormatBytes(workingSetBytes);
        var configuredBytes = Math.Max(1L, (long)(_active?.MemoryGb ?? 1) * 1024L * 1024L * 1024L);
        MemoryUsageBar.Value = Math.Clamp(workingSetBytes * 100d / configuredBytes, 0d, 100d);
        AddPerformanceSample(_memoryHistory, MemoryUsageBar.Value);
        UpdatePerformanceGraphs();

        if (now - _lastDiskRefreshAt >= TimeSpan.FromSeconds(10))
        {
            _ = RefreshDiskUsageAsync();
        }
    }

    private void ResetPerformanceSampling()
    {
        _lastProcessorTime = TimeSpan.Zero;
        _lastPerformanceSampleAt = null;
        _cpuHistory.Clear();
        _memoryHistory.Clear();
        UpdatePerformanceGraphs();
    }

    private static void AddPerformanceSample(Queue<double> history, double value)
    {
        history.Enqueue(Math.Clamp(value, 0d, 100d));
        while (history.Count > 60) history.Dequeue();
    }

    private void PerformanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePerformanceGraphs();

    private void UpdatePerformanceGraphs()
    {
        if (CpuHistoryCanvas is null || MemoryHistoryCanvas is null) return;
        CpuHistoryLine.Points = BuildGraphPoints(_cpuHistory, CpuHistoryCanvas.ActualWidth, CpuHistoryCanvas.ActualHeight);
        MemoryHistoryLine.Points = BuildGraphPoints(_memoryHistory, MemoryHistoryCanvas.ActualWidth, MemoryHistoryCanvas.ActualHeight);
    }

    private static System.Windows.Media.PointCollection BuildGraphPoints(IEnumerable<double> values, double width, double height)
    {
        var items = values.ToArray();
        var points = new System.Windows.Media.PointCollection();
        if (items.Length == 0 || width <= 0 || height <= 0) return points;
        var step = items.Length <= 1 ? 0d : width / (items.Length - 1);
        for (var index = 0; index < items.Length; index++)
            points.Add(new Point(index * step, height - (items[index] / 100d * height)));
        return points;
    }

    private async Task RefreshDiskUsageAsync()
    {
        if (_diskRefreshInProgress || _active is null || !Directory.Exists(_active.Folder)) return;

        var folder = _active.Folder;
        _diskRefreshInProgress = true;
        try
        {
            var bytes = await Task.Run(() => CalculateDirectorySize(folder));
            if (_active is not null && string.Equals(_active.Folder, folder, StringComparison.OrdinalIgnoreCase))
            {
                DiskUsageValueText.Text = FormatBytes(bytes);
                _lastDiskRefreshAt = DateTimeOffset.Now;
            }
        }
        catch (UnauthorizedAccessException)
        {
            DiskUsageValueText.Text = "Unavailable";
        }
        catch (IOException)
        {
            DiskUsageValueText.Text = "Unavailable";
        }
        finally
        {
            _diskRefreshInProgress = false;
        }
    }

    private static long CalculateDirectorySize(string folder)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var directory in Directory.EnumerateDirectories(current)) pending.Push(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return total;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private void SetServerActionState(bool isBusy = false)
    {
        if (StartServerButton is null) return;

        var hasSelection = _active is not null;
        var selectedIsRunning = _launcher.IsRunning && ReferenceEquals(_active, _runningProfile);
        StartServerButton.IsEnabled = !isBusy && hasSelection && !_launcher.IsRunning;
        RestartServerButton.IsEnabled = !isBusy && selectedIsRunning;
        StopServerButton.IsEnabled = !isBusy && selectedIsRunning;
        ConsoleQuickButton.IsEnabled = hasSelection;
        OpenFolderQuickButton.IsEnabled = hasSelection;
        ForgetServerButton.IsEnabled = !isBusy && hasSelection && !selectedIsRunning;
        DeleteServerButton.IsEnabled = !isBusy && hasSelection;
        UpdatePlayerPanel();
    }

    private void HandleServerOutput(string line)
    {
        AppendLog(line);
        ParsePlayerLine(line);
    }

    private void ParsePlayerLine(string line)
    {
        var listMatch = Regex.Match(line, @"There are (?<count>\d+) of a max of \d+ players online:\s*(?<players>.*)$", RegexOptions.IgnoreCase);
        if (listMatch.Success)
        {
            var players = listMatch.Groups["players"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsValidPlayerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _onlinePlayers.Clear();
            foreach (var player in players) _onlinePlayers.Add(player);
            UpdatePlayerPanel();
            return;
        }

        var joinMatch = Regex.Match(line, @"]:\s*(?<player>[A-Za-z0-9_]{1,16}) joined the game\s*$", RegexOptions.IgnoreCase);
        if (joinMatch.Success)
        {
            AddOnlinePlayer(joinMatch.Groups["player"].Value);
            return;
        }

        var leaveMatch = Regex.Match(line, @"]:\s*(?<player>[A-Za-z0-9_]{1,16}) left the game\s*$", RegexOptions.IgnoreCase);
        if (leaveMatch.Success)
        {
            RemoveOnlinePlayer(leaveMatch.Groups["player"].Value);
        }
    }

    private static bool IsValidPlayerName(string? player) =>
        !string.IsNullOrWhiteSpace(player) && Regex.IsMatch(player, @"^[A-Za-z0-9_]{1,16}$");

    private void AddOnlinePlayer(string player)
    {
        if (!IsValidPlayerName(player) || _onlinePlayers.Any(existing => string.Equals(existing, player, StringComparison.OrdinalIgnoreCase))) return;
        _onlinePlayers.Add(player);
        var sorted = _onlinePlayers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        _onlinePlayers.Clear();
        foreach (var item in sorted) _onlinePlayers.Add(item);
        UpdatePlayerPanel();
    }

    private void RemoveOnlinePlayer(string player)
    {
        var existing = _onlinePlayers.FirstOrDefault(name => string.Equals(name, player, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _onlinePlayers.Remove(existing);
        UpdatePlayerPanel();
    }

    private void RefreshPlayers_Click(object sender, RoutedEventArgs e) => RequestPlayerList(showStatus: true);

    private void RequestPlayerList(bool showStatus)
    {
        if (!_launcher.IsRunning)
        {
            if (showStatus) PlayerActionStatusText.Text = "Start the selected server to refresh players.";
            UpdatePlayerPanel();
            return;
        }
        if (!_launcher.CanSendCommands)
        {
            if (showStatus) PlayerActionStatusText.Text = "Player commands are unavailable for an externally recovered server process.";
            UpdatePlayerPanel();
            return;
        }
        _launcher.SendCommand("list");
        if (showStatus) PlayerActionStatusText.Text = "Refreshing the online player list…";
    }

    private void OnlinePlayersList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePlayerPanel();

    private string? SelectedOnlinePlayer => OnlinePlayersList.SelectedItem as string;

    private void MakeAdmin_Click(object sender, RoutedEventArgs e) => ExecutePlayerCommand("op", "made an administrator", requireConfirmation: true);
    private void KickPlayer_Click(object sender, RoutedEventArgs e) => ExecutePlayerCommand("kick", "kicked", requireConfirmation: true);
    private void BanPlayer_Click(object sender, RoutedEventArgs e) => ExecutePlayerCommand("ban", "banned", requireConfirmation: true);

    private void ExecutePlayerCommand(string command, string completedText, bool requireConfirmation)
    {
        var player = SelectedOnlinePlayer;
        if (!IsValidPlayerName(player) || !_launcher.CanSendCommands)
        {
            PlayerActionStatusText.Text = "Select an online player while the server is running.";
            return;
        }
        if (requireConfirmation && MessageBox.Show($"Run '{command}' for {player}?", "Player Management", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _launcher.SendCommand($"{command} {player}");
        PlayerActionStatusText.Text = $"{player} was {completedText}.";
        AppendLog($"[Manager] Sent player command: {command} {player}");
        if (command is "kick" or "ban") RemoveOnlinePlayer(player!);
        else RequestPlayerList(showStatus: false);
    }

    private void UpdatePlayerPanel()
    {
        if (OnlinePlayersList is null) return;
        var running = _launcher.IsRunning && ReferenceEquals(_active, _runningProfile);
        var canCommand = running && _launcher.CanSendCommands;
        var selected = SelectedOnlinePlayer;
        PlayerCountText.Text = !running ? "Server is offline" : $"{_onlinePlayers.Count} player{(_onlinePlayers.Count == 1 ? string.Empty : "s")} online";
        OnlinePlayersList.Visibility = running && _onlinePlayers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoPlayersText.Text = running ? "No players online" : "Server is offline";
        NoPlayersText.Visibility = running && _onlinePlayers.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshPlayersButton.IsEnabled = canCommand;
        MakeAdminButton.IsEnabled = canCommand && IsValidPlayerName(selected);
        KickPlayerButton.IsEnabled = canCommand && IsValidPlayerName(selected);
        BanPlayerButton.IsEnabled = canCommand && IsValidPlayerName(selected);
        if (!running) PlayerActionStatusText.Text = "Start the selected server to monitor players.";
        else if (!_launcher.CanSendCommands) PlayerActionStatusText.Text = "Player commands are unavailable for an externally recovered server process.";
        else if (_onlinePlayers.Count == 0) PlayerActionStatusText.Text = "No players are currently online.";
        else if (selected is null) PlayerActionStatusText.Text = "Select a player to manage.";
    }

    private static int ReadPort(string folder)
    {
        var file = Path.Combine(folder, "server.properties");
        if (!File.Exists(file)) return 25565;
        var line = File.ReadLines(file).FirstOrDefault(value => value.StartsWith("server-port=", StringComparison.OrdinalIgnoreCase));
        return int.TryParse(line?.Split('=', 2).ElementAtOrDefault(1), out var port) ? port : 25565;
    }

    private void SaveProfiles()
    {
        _profileStore.Save(_servers);
        _structuredLogger.Debug("Profiles saved", new { Count = _servers.Count });
    }
    private sealed record HealthDisplay(string Icon, string Name, string Details);

    private void LoadManagementPage()
    {
        if (_active is null)
        {
            ServerManagementPage.ManagementNameBox.Text = string.Empty;
            ServerManagementPage.ManagementFolderBox.Text = string.Empty;
            ServerManagementPage.ManagementJavaBox.Text = "java";
            ServerManagementPage.ManagementVersionText.Text = "No server selected";
            ServerManagementPage.ManagementStatusText.Text = "Select a server to begin.";
            ServerManagementPage.ServerHealthList.ItemsSource = null;
            return;
        }
        ServerManagementPage.ManagementNameBox.Text = _active.Name;
        ServerManagementPage.ManagementFolderBox.Text = _active.Folder;
        ServerManagementPage.ManagementJavaBox.Text = string.IsNullOrWhiteSpace(_active.JavaPath) ? "java" : _active.JavaPath;
        ServerManagementPage.ManagementVersionText.Text = $"{_active.Type} {_active.Version}  •  {_active.Jar}";
        VerifyServerInternal(showMessage: false);
    }

    private void SaveManagementProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        var name = ServerManagementPage.ManagementNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Enter a server name.", "Server Management", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _active.Name = name;
        _active.JavaPath = string.IsNullOrWhiteSpace(ServerManagementPage.ManagementJavaBox.Text) ? "java" : ServerManagementPage.ManagementJavaBox.Text.Trim();
        _active.RefreshDerivedProperties();
        _profileStore.Save(_servers);
        DashboardPage.Servers.Items.Refresh();
        UpdateDetails();
        ServerManagementPage.ManagementStatusText.Text = "Profile changes saved.";
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Select java.exe", Filter = "Java executable (java.exe)|java.exe|Executables (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) ServerManagementPage.ManagementJavaBox.Text = dialog.FileName;
    }

    private async void TestJava_Click(object sender, RoutedEventArgs e)
    {
        var version = _active?.Version;
        var result = await JavaDetector.ValidateAsync(ServerManagementPage.ManagementJavaBox.Text, version);
        UpdateJavaStatus(result);
        ServerManagementPage.ManagementStatusText.Text = result.Message;
        MessageBox.Show(result.Message + (result.Installation is null ? string.Empty : Environment.NewLine + Environment.NewLine + result.Installation.VersionText),
            result.IsValid ? "Java test passed" : "Java test failed", MessageBoxButton.OK,
            result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void AutoDetectJava_Click(object sender, RoutedEventArgs e)
    {
        var result = await JavaDetector.ValidateAsync("java", _active?.Version);
        UpdateJavaStatus(result);
        if (result.Installation is not null)
        {
            ServerManagementPage.ManagementJavaBox.Text = result.Installation.ExecutablePath;
            ServerManagementPage.ManagementStatusText.Text = result.Message + " Save Profile Changes to keep this selection.";
        }
        else
        {
            ServerManagementPage.ManagementStatusText.Text = "Java was not found in Windows PATH. Use Browse to select java.exe.";
        }
    }

    private bool EnsureStoppedForFileOperation()
    {
        var result = _serverOperationGuard.RequireSelectedAndStopped(
            _active, _runningProfile, _launcher.IsRunning,
            "Stop the selected server before changing its files.");
        if (result.IsAllowed) return true;
        _dialogs.Warning(result.Message, _active is null ? "Server Management" : "Server is running");
        return false;
    }

    private async void DuplicateServer_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureStoppedForFileOperation() || _active is null) return;
        var dialog = new OpenFolderDialog { Title = "Choose the parent folder for the duplicate" };
        if (dialog.ShowDialog(this) != true) return;
        var destination = Path.Combine(dialog.FolderName, SanitizeFolderName(_active.Name + " Copy"));
        if (Directory.Exists(destination)) { MessageBox.Show("The destination folder already exists.", "Duplicate Server", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            ServerManagementPage.ManagementStatusText.Text = "Duplicating server…";
            var progress = new Progress<int>(value => ServerManagementPage.ManagementProgressBar.Value = value);
            await ServerManagementService.CopyDirectoryAsync(_active.Folder, destination, progress);
            var duplicate = new ServerProfile { Name = _active.Name + " Copy", Folder = destination, Type = _active.Type, Version = _active.Version, MemoryGb = _active.MemoryGb, Jar = _active.Jar, JavaPath = _active.JavaPath };
            _servers.Add(duplicate); _profileStore.Save(_servers); DashboardPage.Servers.SelectedItem = duplicate;
            ServerManagementPage.ManagementStatusText.Text = "Server duplicated successfully.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Duplicate failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { ServerManagementPage.ManagementProgressBar.Value = 0; }
    }

    private async void MoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureStoppedForFileOperation() || _active is null) return;
        var dialog = new OpenFolderDialog { Title = "Choose the new parent folder" };
        if (dialog.ShowDialog(this) != true) return;
        var destination = Path.Combine(dialog.FolderName, Path.GetFileName(_active.Folder.TrimEnd(Path.DirectorySeparatorChar)));
        if (Directory.Exists(destination)) { MessageBox.Show("The destination folder already exists.", "Move Server", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var source = _active.Folder;
        try
        {
            ServerManagementPage.ManagementStatusText.Text = "Moving server…";
            var progress = new Progress<int>(value => ServerManagementPage.ManagementProgressBar.Value = value);
            await ServerManagementService.CopyDirectoryAsync(source, destination, progress);
            Directory.Delete(source, true);
            _active.Folder = destination; _active.RefreshDerivedProperties(); _profileStore.Save(_servers); UpdateDetails(); LoadManagementPage();
            ServerManagementPage.ManagementStatusText.Text = "Server moved successfully.";
        }
        catch (Exception ex) { MessageBox.Show($"Move could not be completed. The original folder was preserved when possible.\n\n{ex.Message}", "Move failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { ServerManagementPage.ManagementProgressBar.Value = 0; }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        var dialog = new SaveFileDialog { Filter = "Minecraft server profile (*.mcserverprofile)|*.mcserverprofile|JSON (*.json)|*.json", FileName = SanitizeFolderName(_active.Name) + ".mcserverprofile" };
        if (dialog.ShowDialog(this) != true) return;
        ServerManagementService.ExportProfile(_active, dialog.FileName);
        ServerManagementPage.ManagementStatusText.Text = "Profile exported.";
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Minecraft server profile (*.mcserverprofile;*.json)|*.mcserverprofile;*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profile = ServerManagementService.ImportProfile(dialog.FileName);
            if (!Directory.Exists(profile.Folder))
            {
                var folder = new OpenFolderDialog { Title = "Locate the imported server folder" };
                if (folder.ShowDialog(this) != true) return;
                profile.Folder = folder.FolderName;
            }
            if (_servers.Any(item => string.Equals(item.Folder, profile.Folder, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("That server folder is already registered.", "Import Profile", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            profile.RuntimeState = "Stopped"; _servers.Add(profile); _profileStore.Save(_servers); DashboardPage.Servers.SelectedItem = profile; SelectMainTab(6); LoadManagementPage();
            ServerManagementPage.ManagementStatusText.Text = "Profile imported successfully.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportServerZip_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureStoppedForFileOperation() || _active is null) return;
        var dialog = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip", FileName = SanitizeFolderName(_active.Name) + ".zip" };
        if (dialog.ShowDialog(this) != true) return;
        try { ServerManagementPage.ManagementStatusText.Text = "Creating server archive…"; ServerManagementService.ExportServerZip(_active, dialog.FileName); ServerManagementPage.ManagementStatusText.Text = "Server archive exported."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void VerifyServer_Click(object sender, RoutedEventArgs e) => VerifyServerInternal(showMessage: true);

    private void VerifyServerInternal(bool showMessage)
    {
        if (_active is null) { ServerManagementPage.ServerHealthList.ItemsSource = null; return; }
        var checks = ServerManagementService.Verify(_active);
        ServerManagementPage.ServerHealthList.ItemsSource = checks.Select(check => new HealthDisplay(check.Passed ? "✔" : "⚠", check.Name, check.Details)).ToList();
        var passed = checks.Count(check => check.Passed);
        ServerManagementPage.ManagementStatusText.Text = $"Health check complete: {passed} of {checks.Count} checks passed.";
        if (showMessage) MessageBox.Show(ServerManagementPage.ManagementStatusText.Text, "Server Health", MessageBoxButton.OK, passed == checks.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void BrowseInstall_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Choose the server install folder" }; if (dialog.ShowDialog(this) == true) InstallFolderBox.Text = dialog.FolderName; }
    private void BrowseJar_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Java Archives (*.jar)|*.jar" }; if (dialog.ShowDialog(this) == true) CustomJarBox.Text = dialog.FileName; }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) { var folder = _active?.Folder ?? InstallFolderBox.Text; if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true }); }
    private void Dashboard_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Dashboard);
    private void CreateServer_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.CreateServer);
    private void Console_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Console);
    private void Configuration_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Configuration);
    private void Backups_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Backups);
    private void Plugins_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Plugins);
    private void Worlds_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Worlds);
    private void Management_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Management);
    private void Updates_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Updates);

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainTabs)) _navigation?.SynchronizeFromTabSelection();
    }

    private void SelectMainTab(int index) => Navigate(Enum.IsDefined(typeof(AppPage), index) ? (AppPage)index : AppPage.Dashboard);
    private async Task CheckServerUpdateAsync()
    {
        if (_active is null) { _dialogs.Information("Select a server first.", "Update Center"); return; }
        _updateCancellation?.Cancel();
        _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        UpdateCenterPage.SetBusy(true, "Checking official update sources…");
        UpdateCenterPage.SetProgress(10, "Checking official update sources…");
        try
        {
            _currentServerUpdate = await _serverUpdateService.CheckAsync(_active, _updateCancellation.Token);
            UpdateCenterPage.Bind(_active, _currentServerUpdate);
            UpdateCenterPage.SetProgress(100, _currentServerUpdate.IsUpdateAvailable ? "Update available." : "Update check complete.");
            _structuredLogger.Information("Server update check completed", new { _active.Name, _active.Type, _active.Version, _currentServerUpdate.LatestBuild, _currentServerUpdate.IsUpdateAvailable });
        }
        catch (OperationCanceledException) { UpdateCenterPage.SetProgress(0, "Update check cancelled."); }
        catch (Exception ex)
        {
            UpdateCenterPage.SetProgress(0, "Update check failed.");
            _structuredLogger.Error("Server update check failed", ex);
            _dialogs.Error(ex.Message, "Update Check Failed");
        }
        finally { UpdateCenterPage.SetBusy(false); }
    }

    private async Task LoadMinecraftVersionsAsync()
    {
        if (_active is null) { _dialogs.Information("Select a server first.", "Change Minecraft Version"); return; }
        try
        {
            UpdateCenterPage.SetBusy(true, "Loading Minecraft releases…");
            var versions = await _serverUpdateService.GetAvailableMinecraftVersionsAsync(_active, _lifetimeCancellation.Token);
            UpdateCenterPage.SetAvailableVersions(versions);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message, "Could Not Load Versions"); }
        finally { UpdateCenterPage.SetBusy(false); }
    }

    private async Task ChangeMinecraftVersionAsync(string targetVersion)
    {
        if (_active is null) return;
        if (string.Equals(_active.Version, targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.Information("The server already uses that Minecraft version.", "No Change Needed"); return;
        }
        if (_launcher.IsRunning || string.Equals(_active.RuntimeState, "Running", StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.Information("Stop the server before changing Minecraft versions.", "Stop Server First"); return;
        }
        var downgradeWarning = $"Change {_active.Name} from Minecraft {_active.Version} to {targetVersion}?\n\nA complete backup and previous-JAR copy will be created. Downgrading a world can permanently damage it; restore the backup if Minecraft rejects the world.";
        if (!_dialogs.Confirm(downgradeWarning, "Change Minecraft Version")) return;
        var jarPath = Path.Combine(_active.Folder, _active.Jar);
        if (!File.Exists(jarPath)) { _dialogs.Error("The active server JAR could not be found.", "Version Change Failed"); return; }
        var oldVersion = _active.Version;
        var temp = jarPath + ".version-download";
        var previous = jarPath + $".minecraft-{oldVersion}-previous-{DateTime.Now:yyyyMMdd-HHmmss}.jar";
        try
        {
            UpdateCenterPage.SetBusy(true, "Resolving compatible server build…");
            var package = await _serverUpdateService.ResolveVersionChangeAsync(_active, targetVersion, _lifetimeCancellation.Token);
            if (string.IsNullOrWhiteSpace(package.DownloadUrl)) throw new InvalidOperationException($"No compatible {_active.Type} server build is available for Minecraft {targetVersion}.");
            await _backupService.CreateAsync(_active, $"Safety-VersionChange-{oldVersion}-to-{targetVersion}", new Progress<string>(message => UpdateCenterPage.SetVersionChangeStatus(message)), _lifetimeCancellation.Token);
            File.Copy(jarPath, previous, false);
            await _serverUpdateService.DownloadAndVerifyAsync(package, temp, new Progress<double>(value => UpdateCenterPage.SetVersionChangeStatus($"Downloading Minecraft {targetVersion}… {value:0}%")), _lifetimeCancellation.Token);
            File.Move(temp, jarPath, true);
            _active.Version = targetVersion;
            _active.RefreshDerivedProperties();
            SaveProfiles();
            var markerFolder = Path.Combine(_active.Folder, ".asms"); Directory.CreateDirectory(markerFolder);
            await File.WriteAllTextAsync(Path.Combine(markerFolder, "server-build.txt"), package.LatestBuild, _lifetimeCancellation.Token);
            _currentServerUpdate = null;
            UpdateCenterPage.Bind(_active); UpdateDetails(); LoadManagementPage(); LoadBackups();
            UpdateCenterPage.SetVersionChangeStatus($"Minecraft version changed successfully: {oldVersion} → {targetVersion}.");
            _dialogs.Information($"Minecraft was changed from {oldVersion} to {targetVersion}. Review plugin compatibility before starting the server.", "Version Change Complete");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); if (File.Exists(previous) && (!File.Exists(jarPath) || new FileInfo(jarPath).Length < 1024 * 1024)) File.Copy(previous, jarPath, true); } catch { }
            _active.Version = oldVersion; SaveProfiles();
            UpdateCenterPage.SetVersionChangeStatus("Version change failed. The original profile and JAR were retained.");
            _dialogs.Error(ex.Message, "Version Change Failed");
        }
        finally { UpdateCenterPage.SetBusy(false); }
    }

    private async Task InstallServerUpdateAsync()
    {
        if (_active is null || _currentServerUpdate?.CanInstall != true) return;
        if (_launcher.IsRunning || string.Equals(_active.RuntimeState, "Running", StringComparison.OrdinalIgnoreCase))
        {
            _dialogs.Information("Stop the server before installing an update.", "Stop Server First");
            return;
        }
        if (!_dialogs.Confirm($"Update {_active.Name} from build {_currentServerUpdate.InstalledBuild} to {_currentServerUpdate.LatestBuild}?\n\nA complete safety backup and previous-JAR copy will be created first.", "Install Server Update")) return;

        var jarPath = Path.Combine(_active.Folder, _active.Jar);
        if (!File.Exists(jarPath)) { _dialogs.Error("The active server JAR could not be found.", "Update Failed"); return; }
        _updateCancellation?.Cancel();
        _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var token = _updateCancellation.Token;
        var temp = jarPath + ".download";
        var previous = jarPath + $".previous-{DateTime.Now:yyyyMMdd-HHmmss}.jar";
        try
        {
            UpdateCenterPage.SetBusy(true, "Creating safety backup…");
            UpdateCenterPage.SetProgress(5, "Creating safety backup…");
            await _backupService.CreateAsync(_active, "Safety-ServerUpdate", new Progress<string>(message => UpdateCenterPage.SetProgress(12, message)), token);
            File.Copy(jarPath, previous, overwrite: false);
            var progress = new Progress<double>(value => UpdateCenterPage.SetProgress(15 + value * .70, $"Downloading server JAR… {value:0}%"));
            await _serverUpdateService.DownloadAndVerifyAsync(_currentServerUpdate, temp, progress, token);
            UpdateCenterPage.SetProgress(90, "Installing verified JAR…");
            File.Move(temp, jarPath, true);
            var markerFolder = Path.Combine(_active.Folder, ".asms");
            Directory.CreateDirectory(markerFolder);
            await File.WriteAllTextAsync(Path.Combine(markerFolder, "server-build.txt"), _currentServerUpdate.LatestBuild, token);
            UpdateCenterPage.SetProgress(100, "Update installed successfully. Start the server when ready.");
            _currentServerUpdate = await _serverUpdateService.CheckAsync(_active, token);
            UpdateCenterPage.Bind(_active, _currentServerUpdate);
            LoadBackups();
            _structuredLogger.Information("Server update installed", new { _active.Name, PreviousJar = previous, _currentServerUpdate.LatestBuild });
            _dialogs.Information("The server update was installed successfully. The previous JAR and a complete safety backup were preserved.", "Update Complete");
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (File.Exists(previous) && (!File.Exists(jarPath) || new FileInfo(jarPath).Length < 1024 * 1024)) File.Copy(previous, jarPath, true);
            }
            catch { }
            UpdateCenterPage.SetProgress(0, "Installation failed. The previous JAR was retained.");
            _structuredLogger.Error("Server update installation failed", ex);
            _dialogs.Error(ex.Message, "Update Failed");
        }
        finally { UpdateCenterPage.SetBusy(false); }
    }

    private void ScheduledTasks_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.ScheduledTasks);
    private void Notifications_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Notifications);
    private void HealthAnalyzer_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.HealthAnalyzer);
    private void StartupAnalyzer_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.StartupAnalyzer);
    private void LogAnalyzer_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.LogAnalyzer);
    private void PluginCompatibility_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.PluginCompatibility);
    private void PerformanceDashboard_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.PerformanceDashboard);
    private void Optimization_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.Optimization);
    private void FirstRunWizard_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.FirstRunWizard);
    private void About_Click(object sender, RoutedEventArgs e) => Navigate(AppPage.About);



    private WorldRecord? SelectedWorld => WorldManagerPage.WorldsGrid.SelectedItem as WorldRecord;

    private bool CanModifyWorlds()
    {
        var result = _serverOperationGuard.RequireSelectedAndStopped(
            _active, _runningProfile, _launcher.IsRunning,
            "Stop the selected server before changing world folders.");
        if (result.IsAllowed) return true;
        WorldManagerPage.WorldStatusText.Text = result.Message;
        if (_active is not null) _dialogs.Warning(result.Message, "Server is running");
        return false;
    }

    private void LoadWorlds()
    {
        _worlds.Clear();
        ClearWorldDetails();
        if (_active is null) { WorldManagerPage.WorldServerText.Text = "Select a server to inspect its worlds."; WorldManagerPage.WorldStatusText.Text = string.Empty; return; }
        WorldManagerPage.WorldServerText.Text = $"Worlds for {_active.Name} • {_active.Folder}";
        try
        {
            foreach (var world in _worldService.Scan(_active)) _worlds.Add(world);
            WorldManagerPage.WorldStatusText.Text = _worlds.Count == 0 ? "No Minecraft world folders were detected." : $"Found {_worlds.Count} world folder{(_worlds.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex) { WorldManagerPage.WorldStatusText.Text = "Could not scan worlds: " + ex.Message; }
    }

    private void WorldsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var world = SelectedWorld;
        if (world is null) { ClearWorldDetails(); return; }
        WorldManagerPage.WorldDetailNameText.Text = world.Name + (world.IsPrimary ? " • Primary" : string.Empty);
        WorldManagerPage.WorldDetailDimensionText.Text = world.Dimension;
        WorldManagerPage.WorldDetailFolderText.Text = world.FolderPath;
        WorldManagerPage.WorldDetailStatsText.Text = $"{world.SizeText} • {world.LastModifiedText}";
        WorldManagerPage.WorldDetailHealthText.Text = $"{world.Health}: {world.HealthDetails}";
    }

    private void ClearWorldDetails()
    {
        if (WorldManagerPage.WorldDetailNameText is null) return;
        WorldManagerPage.WorldDetailNameText.Text = WorldManagerPage.WorldDetailDimensionText.Text = WorldManagerPage.WorldDetailFolderText.Text = WorldManagerPage.WorldDetailStatsText.Text = WorldManagerPage.WorldDetailHealthText.Text = "—";
    }

    private void RefreshWorlds_Click(object sender, RoutedEventArgs e) => LoadWorlds();


    private void VerifyWorld_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = SelectedWorld?.FolderPath;
        if (selectedPath is null) { WorldManagerPage.WorldStatusText.Text = "Select a world first."; return; }
        LoadWorlds();
        var refreshed = _worlds.FirstOrDefault(w => string.Equals(w.FolderPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (refreshed is not null)
        {
            WorldManagerPage.WorldsGrid.SelectedItem = refreshed;
            WorldManagerPage.WorldsGrid.ScrollIntoView(refreshed);
            WorldManagerPage.WorldStatusText.Text = $"Verification complete: {refreshed.Health}. {refreshed.HealthDetails}";
        }
    }

    private void OpenWorldFolder_Click(object sender, RoutedEventArgs e)
    {
        var world = SelectedWorld;
        if (world is null) { WorldManagerPage.WorldStatusText.Text = "Select a world first."; return; }
        Process.Start(new ProcessStartInfo("explorer.exe", world.FolderPath) { UseShellExecute = true });
    }

    private async void BackupWorld_Click(object sender, RoutedEventArgs e)
    {
        var world = SelectedWorld;
        if (world is null) { WorldManagerPage.WorldStatusText.Text = "Select a world first."; return; }
        var dialog = new SaveFileDialog { Filter = "ZIP archives (*.zip)|*.zip", FileName = $"{world.Name}_{DateTime.Now:yyyy-MM-dd_HH-mm}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            WorldManagerPage.WorldProgressBar.Visibility = Visibility.Visible; WorldManagerPage.WorldProgressBar.Value = 0;
            WorldManagerPage.WorldStatusText.Text = $"Backing up {world.Name}…";
            var progress = new Progress<int>(value => WorldManagerPage.WorldProgressBar.Value = value);
            await _worldService.CreateArchiveAsync(world, dialog.FileName, progress);
            WorldManagerPage.WorldStatusText.Text = $"World backup created: {dialog.FileName}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "World backup failed", MessageBoxButton.OK, MessageBoxImage.Error); WorldManagerPage.WorldStatusText.Text = "World backup failed."; }
        finally { WorldManagerPage.WorldProgressBar.Visibility = Visibility.Collapsed; }
    }

    private void RenameWorld_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyWorlds() || _active is null) return;
        var world = SelectedWorld; if (world is null) { WorldManagerPage.WorldStatusText.Text = "Select a world first."; return; }
        var newName = _dialogs.PromptText("Rename World", "New world folder name:", world.Name);
        if (newName is null || newName == world.Name) return;
        try { _worldService.Rename(_active, world, newName); LoadWorlds(); WorldManagerPage.WorldStatusText.Text = $"Renamed {world.Name} to {newName}."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Rename failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void DeleteWorld_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyWorlds()) return;
        var world = SelectedWorld; if (world is null) { WorldManagerPage.WorldStatusText.Text = "Select a world first."; return; }
        var warning = world.IsPrimary ? "This is the primary world. Minecraft will generate a new world on the next start." : "This permanently removes the selected world folder.";
        if (MessageBox.Show($"Delete {world.Name}?\n\n{warning}\n\nThis cannot be undone unless you have a backup.", "Delete World", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _worldService.Delete(world); LoadWorlds(); WorldManagerPage.WorldStatusText.Text = $"Deleted {world.Name}."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ImportWorld_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyWorlds() || _active is null) return;
        var dialog = new OpenFileDialog { Filter = "World ZIP archives (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        var suggested = Path.GetFileNameWithoutExtension(dialog.FileName);
        var folderName = _dialogs.PromptText("Import World", "Destination world folder name:", suggested);
        if (folderName is null) return;
        try { _worldService.ImportArchive(_active, dialog.FileName, folderName); LoadWorlds(); WorldManagerPage.WorldStatusText.Text = $"Imported world into {folderName}."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "World import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private PluginRecord? SelectedPlugin => PluginManagerPage.PluginsGrid.SelectedItem as PluginRecord;
    private IReadOnlyList<PluginRecord> SelectedPlugins => PluginManagerPage.PluginsGrid.SelectedItems.Cast<PluginRecord>().ToList();

    private bool CanModifyPlugins()
    {
        var result = _serverOperationGuard.RequireSelectedAndStopped(
            _active, _runningProfile, _launcher.IsRunning,
            "Stop the selected server before changing plugin files.");
        if (result.IsAllowed) return true;
        PluginManagerPage.PluginStatusText.Text = result.Message;
        if (_active is not null) _dialogs.Warning(result.Message, "Server is running");
        return false;
    }

    private void LoadPlugins()
    {
        _allPlugins.Clear();
        _plugins.Clear();
        ClearPluginDetails();
        if (_active is null)
        {
            PluginManagerPage.PluginServerText.Text = "Select a Paper or Purpur server to manage plugins.";
            PluginManagerPage.PluginStatusText.Text = string.Empty;
            return;
        }

        var supportsPlugins = _active.Type.Equals("Paper", StringComparison.OrdinalIgnoreCase) ||
                              _active.Type.Equals("Purpur", StringComparison.OrdinalIgnoreCase) ||
                              _active.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase);
        PluginManagerPage.PluginServerText.Text = supportsPlugins
            ? $"Plugins for {_active.Name} • {_active.Type} {_active.Version}"
            : $"{_active.Name} uses {_active.Type}. Bukkit/Paper plugin compatibility is not guaranteed.";
        PluginManagerPage.CatalogServerText.Text = SupportsPluginCatalog(_active)
            ? $"Browse server-side plugins compatible with {_active.Type} {_active.Version}."
            : $"Plugin browsing is unavailable for {_active.Type} servers.";
        try
        {
            _allPlugins.AddRange(_pluginService.List(_active));
            ApplyPluginFilter();
            var warningCount = _allPlugins.Count(p => p.HealthText != "Ready");
            PluginManagerPage.PluginStatusText.Text = $"{_allPlugins.Count} plugin{(_allPlugins.Count == 1 ? string.Empty : "s")}" +
                                    (warningCount > 0 ? $" • {warningCount} warning{(warningCount == 1 ? string.Empty : "s")}" : string.Empty) +
                                    $" • {_pluginService.GetPluginFolder(_active)}";
        }
        catch (Exception ex)
        {
            PluginManagerPage.PluginStatusText.Text = "Could not scan plugins: " + ex.Message;
        }
    }

    private void ApplyPluginFilter()
    {
        var query = PluginManagerPage.PluginSearchBox?.Text?.Trim() ?? string.Empty;
        _plugins.Clear();
        foreach (var plugin in _allPlugins.Where(p => PluginMatches(p, query))) _plugins.Add(plugin);
    }

    private static bool PluginMatches(PluginRecord plugin, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return new[] { plugin.Name, plugin.Version, plugin.ApiVersion, plugin.FileName, plugin.Authors, plugin.Dependencies, plugin.SoftDependencies, plugin.HealthText }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void PluginSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PluginManagerPage.PluginsGrid is null) return;
        ApplyPluginFilter();
        PluginManagerPage.PluginStatusText.Text = string.IsNullOrWhiteSpace(PluginManagerPage.PluginSearchBox.Text)
            ? $"Showing all {_plugins.Count} plugins."
            : $"Showing {_plugins.Count} of {_allPlugins.Count} plugins matching ‘{PluginManagerPage.PluginSearchBox.Text.Trim()}’.";
    }

    private void ClearPluginSearch_Click(object sender, RoutedEventArgs e)
    {
        PluginManagerPage.PluginSearchBox.Clear();
        PluginManagerPage.PluginSearchBox.Focus();
    }

    private void PluginsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedPlugins;
        if (selected.Count != 1)
        {
            ClearPluginDetails();
            if (selected.Count > 1) PluginManagerPage.PluginDetailNameText.Text = $"{selected.Count} plugins selected";
            return;
        }
        var plugin = selected[0];
        PluginManagerPage.PluginDetailNameText.Text = $"{plugin.Name} {plugin.Version}";
        PluginManagerPage.PluginDetailMainText.Text = plugin.MainClass;
        PluginManagerPage.PluginDetailAuthorsText.Text = plugin.Authors;
        PluginManagerPage.PluginDetailDependenciesText.Text = plugin.Dependencies;
        PluginManagerPage.PluginDetailSoftDependenciesText.Text = plugin.SoftDependencies;
        PluginManagerPage.PluginDetailCompatibilityText.Text = GetPluginCompatibilityText(plugin);
        PluginManagerPage.PluginDetailModifiedText.Text = plugin.LastModifiedText;
    }

    private string GetPluginCompatibilityText(PluginRecord plugin)
    {
        if (!plugin.IsReadableJar) return "The JAR archive could not be read. Re-download this plugin before starting the server.";
        if (!plugin.HasDescriptor) return "No plugin.yml or paper-plugin.yml was found. This may not be a Bukkit/Paper plugin.";
        if (plugin.IsDuplicate) return "Another installed JAR reports the same plugin name. Remove the older or duplicate copy.";
        if (_active is null) return "Select a server to evaluate compatibility.";
        if (!_active.Type.Equals("Paper", StringComparison.OrdinalIgnoreCase) &&
            !_active.Type.Equals("Purpur", StringComparison.OrdinalIgnoreCase) &&
            !_active.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase))
            return $"{_active.Type} does not guarantee Bukkit/Paper plugin support.";
        if (plugin.ApiVersion != "—" && Version.TryParse(plugin.ApiVersion, out var api) && Version.TryParse(_active.Version, out var server) && api > server)
            return $"Plugin API {plugin.ApiVersion} is newer than server {_active.Version}; it may not load.";
        return "No obvious local compatibility problems detected. Runtime compatibility still depends on the plugin author and server build.";
    }

    private void ClearPluginDetails()
    {
        if (PluginManagerPage.PluginDetailNameText is null) return;
        PluginManagerPage.PluginDetailNameText.Text = "—";
        PluginManagerPage.PluginDetailMainText.Text = "—";
        PluginManagerPage.PluginDetailAuthorsText.Text = "—";
        PluginManagerPage.PluginDetailDependenciesText.Text = "—";
        PluginManagerPage.PluginDetailSoftDependenciesText.Text = "—";
        PluginManagerPage.PluginDetailCompatibilityText.Text = "—";
        PluginManagerPage.PluginDetailModifiedText.Text = "—";
    }

    private void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyPlugins() || _active is null) return;
        var dialog = new OpenFileDialog
        {
            Title = "Select Minecraft plugin JAR files",
            Filter = "Minecraft Plugins (*.jar)|*.jar",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true) InstallPluginFiles(dialog.FileNames);
    }

    private void InstallPluginFiles(IEnumerable<string> files)
    {
        if (!CanModifyPlugins() || _active is null) return;
        var installed = 0;
        var failed = 0;
        foreach (var source in files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var destination = Path.Combine(_pluginService.GetPluginFolder(_active), Path.GetFileName(source));
                var overwrite = true;
                if (File.Exists(destination))
                {
                    overwrite = MessageBox.Show($"{Path.GetFileName(source)} is already installed. Replace it?", "Replace plugin", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    if (!overwrite) continue;
                }
                _pluginService.Install(_active, source, overwrite);
                installed++;
            }
            catch (Exception ex)
            {
                failed++;
                MessageBox.Show($"Could not install {Path.GetFileName(source)}:\n\n{ex.Message}", "Plugin installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        LoadPlugins();
        PluginManagerPage.PluginStatusText.Text = installed == 0
            ? failed == 0 ? "No plugin JAR files were selected." : $"No plugins installed • {failed} failed."
            : $"Installed {installed} plugin{(installed == 1 ? string.Empty : "s")}" + (failed > 0 ? $" • {failed} failed" : string.Empty) + ". Restart the server to load them.";
    }

    private void PluginArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    ((string[])e.Data.GetData(DataFormats.FileDrop)).Any(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PluginArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            InstallPluginFiles(files.Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)));
    }

    private void SetSelectedPluginsEnabled(bool enabled)
    {
        if (!CanModifyPlugins()) return;
        var selected = SelectedPlugins;
        if (selected.Count == 0) { PluginManagerPage.PluginStatusText.Text = "Select one or more plugins first."; return; }
        var changed = 0;
        var failed = 0;
        foreach (var plugin in selected.Where(p => p.IsEnabled != enabled))
        {
            try { _pluginService.SetEnabled(plugin, enabled); changed++; }
            catch { failed++; }
        }
        LoadPlugins();
        PluginManagerPage.PluginStatusText.Text = changed == 0
            ? $"The selected plugins were already {(enabled ? "enabled" : "disabled")}."
            : $"{(enabled ? "Enabled" : "Disabled")} {changed} plugin{(changed == 1 ? string.Empty : "s")}" + (failed > 0 ? $" • {failed} failed" : string.Empty) + ". Restart the server to apply the change.";
    }

    private void EnablePlugin_Click(object sender, RoutedEventArgs e) => SetSelectedPluginsEnabled(true);
    private void DisablePlugin_Click(object sender, RoutedEventArgs e) => SetSelectedPluginsEnabled(false);
    private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => LoadPlugins();

    private void OpenPluginFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) { PluginManagerPage.PluginStatusText.Text = "Select a server first."; return; }
        var folder = _pluginService.GetPluginFolder(_active);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyPlugins()) return;
        var selected = SelectedPlugins;
        if (selected.Count == 0) { PluginManagerPage.PluginStatusText.Text = "Select one or more plugins first."; return; }
        var names = selected.Count == 1 ? $"{selected[0].Name} ({selected[0].FileName})" : $"{selected.Count} selected plugin JARs";
        if (MessageBox.Show($"Delete {names}?\n\nThis removes the plugin JAR files but leaves their configuration folders in place.", "Delete plugins", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var deleted = 0;
        var failed = 0;
        foreach (var plugin in selected)
        {
            try { _pluginService.Delete(plugin); deleted++; }
            catch { failed++; }
        }
        LoadPlugins();
        PluginManagerPage.PluginStatusText.Text = $"Deleted {deleted} plugin{(deleted == 1 ? string.Empty : "s")}" + (failed > 0 ? $" • {failed} failed" : string.Empty) + ". Configuration folders were preserved.";
    }

    private async void SearchPluginCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null)
        {
            PluginManagerPage.CatalogStatusText.Text = "Select a server before searching for plugins.";
            return;
        }

        if (!SupportsPluginCatalog(_active))
        {
            PluginManagerPage.CatalogStatusText.Text = $"The plugin catalog currently supports Paper, Purpur, and Folia servers. {_active.Type} is not supported.";
            return;
        }

        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        _pluginCatalogCancellation?.Cancel();
        _pluginCatalogCancellation?.Dispose();
        _pluginCatalogCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var token = _pluginCatalogCancellation.Token;
        var query = PluginManagerPage.CatalogSearchBox.Text.Trim();
        _pluginCatalogResults.Clear();
        PluginManagerPage.CatalogStatusText.Text = $"Searching Modrinth for {_active.Version} plugins...";

        try
        {
            var results = await _pluginCatalogService.SearchAsync(query, _active.Version, token);
            foreach (var item in results)
            {
                await _pluginCatalogService.ResolveInstallAsync(item, _active.Version, token);
                _pluginCatalogResults.Add(item);
            }
            var compatible = _pluginCatalogResults.Count(item => item.CanInstall);
            PluginManagerPage.CatalogStatusText.Text = $"Found {_pluginCatalogResults.Count} result{(_pluginCatalogResults.Count == 1 ? string.Empty : "s")} • {compatible} installable for Minecraft {_active.Version}.";
        }
        catch (OperationCanceledException)
        {
            PluginManagerPage.CatalogStatusText.Text = "Plugin search cancelled.";
        }
        catch (Exception ex)
        {
            PluginManagerPage.CatalogStatusText.Text = "Plugin search failed: " + ex.Message;
            AppendLog("[Manager] Modrinth plugin search failed: " + ex.Message);
        }
    }

    private async void InstallCatalogPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!CanModifyPlugins() || _active is null) return;
        if (PluginManagerPage.CatalogGrid.SelectedItem is not PluginCatalogItem item)
        {
            PluginManagerPage.CatalogStatusText.Text = "Select a plugin from the catalog first.";
            return;
        }
        if (!item.CanInstall || item.DownloadUri is null || string.IsNullOrWhiteSpace(item.FileName))
        {
            PluginManagerPage.CatalogStatusText.Text = "No compatible downloadable file is available for this result.";
            return;
        }

        var pluginFolder = _pluginService.GetPluginFolder(_active);
        Directory.CreateDirectory(pluginFolder);
        var destination = Path.Combine(pluginFolder, item.FileName);
        if (File.Exists(destination) && MessageBox.Show(
            $"{item.FileName} is already installed. Replace it with {item.LatestVersion}?",
            "Replace plugin", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var temporaryPath = destination + ".download";
        PluginManagerPage.CatalogStatusText.Text = $"Downloading {item.Name} {item.LatestVersion}...";
        try
        {
            await _pluginCatalogService.DownloadAsync(item, temporaryPath, _lifetimeCancellation.Token);
            _pluginService.InstallDownloaded(_active, temporaryPath, item.FileName, overwrite: true);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            LoadPlugins();
            PluginManagerPage.CatalogStatusText.Text = $"Installed {item.Name} {item.LatestVersion}. Restart the server to load it.";
            AppendLog($"[Manager] Installed {item.Name} {item.LatestVersion} from Modrinth.");
        }
        catch (OperationCanceledException)
        {
            PluginManagerPage.CatalogStatusText.Text = "Plugin installation cancelled.";
        }
        catch (Exception ex)
        {
            PluginManagerPage.CatalogStatusText.Text = "Plugin installation failed: " + ex.Message;
            AppendLog("[Manager] Modrinth plugin installation failed: " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }


    private async void CheckPluginUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) { PluginManagerPage.PluginStatusText.Text = "Select a server first."; return; }
        if (!SupportsPluginCatalog(_active)) { PluginManagerPage.PluginStatusText.Text = "Plugin updates are available for Paper, Purpur, and Folia servers."; return; }
        if (_launcher.IsRunning) { PluginManagerPage.PluginStatusText.Text = "Stop the server before checking and installing plugin updates."; return; }
        if (_allPlugins.Count == 0) { PluginManagerPage.PluginStatusText.Text = "No installed plugins were found."; return; }

        _pluginCatalogCancellation?.Cancel();
        _pluginCatalogCancellation?.Dispose();
        _pluginCatalogCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var token = _pluginCatalogCancellation.Token;
        PluginManagerPage.PluginStatusText.Text = $"Checking {_allPlugins.Count} installed plugins against Modrinth...";
        var checkedCount = 0;
        try
        {
            foreach (var plugin in _allPlugins)
            {
                token.ThrowIfCancellationRequested();
                await _pluginCatalogService.ResolveInstalledUpdateAsync(plugin, _active.Version, token);
                checkedCount++;
                PluginManagerPage.PluginStatusText.Text = $"Checked {checkedCount} of {_allPlugins.Count} plugins...";
            }
            var updates = _allPlugins.Count(plugin => plugin.HasUpdate);
            var unmatched = _allPlugins.Count(plugin => plugin.UpdateStatus == "Not found on Modrinth");
            PluginManagerPage.PluginStatusText.Text = $"Update check complete • {updates} update{(updates == 1 ? string.Empty : "s")} available" + (unmatched > 0 ? $" • {unmatched} could not be matched" : string.Empty) + ".";
        }
        catch (OperationCanceledException) { PluginManagerPage.PluginStatusText.Text = "Plugin update check cancelled."; }
        catch (Exception ex) { PluginManagerPage.PluginStatusText.Text = "Plugin update check failed: " + ex.Message; AppendLog("[Manager] Plugin update check failed: " + ex.Message); }
    }

    private async void UpdateSelectedPlugins_Click(object sender, RoutedEventArgs e)
    {
        await UpdatePluginsAsync(SelectedPlugins.Where(plugin => plugin.HasUpdate).ToList());
    }

    private async void UpdateAllPlugins_Click(object sender, RoutedEventArgs e)
    {
        await UpdatePluginsAsync(_allPlugins.Where(plugin => plugin.HasUpdate).ToList());
    }

    private async Task UpdatePluginsAsync(IReadOnlyList<PluginRecord> plugins)
    {
        if (!CanModifyPlugins() || _active is null) return;
        if (plugins.Count == 0) { PluginManagerPage.PluginStatusText.Text = "No checked plugin updates are selected. Run Check Updates first."; return; }
        if (MessageBox.Show(
            $"Update {plugins.Count} plugin{(plugins.Count == 1 ? string.Empty : "s")}?\n\n" +
            @"The current JAR files will be retained in plugins\.asms-update-backups. Plugin configuration folders will not be changed.",
            "Update plugins",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var updated = 0;
        var failed = 0;
        foreach (var plugin in plugins)
        {
            var temp = Path.Combine(Path.GetTempPath(), $"asms-plugin-update-{Guid.NewGuid():N}.jar");
            try
            {
                PluginManagerPage.PluginStatusText.Text = $"Updating {plugin.Name} {plugin.Version} → {plugin.LatestVersion}...";
                await _pluginCatalogService.DownloadUpdateAsync(plugin, temp, _lifetimeCancellation.Token);
                _pluginService.ReplaceWithDownloaded(_active, plugin, temp, plugin.UpdateFileName ?? plugin.FileName.Replace(".disabled", string.Empty, StringComparison.OrdinalIgnoreCase));
                updated++;
                AppendLog($"[Manager] Updated plugin {plugin.Name} from {plugin.Version} to {plugin.LatestVersion}.");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { failed++; AppendLog($"[Manager] Could not update {plugin.Name}: {ex.Message}"); }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
        LoadPlugins();
        PluginManagerPage.PluginStatusText.Text = $"Updated {updated} plugin{(updated == 1 ? string.Empty : "s")}" + (failed > 0 ? $" • {failed} failed" : string.Empty) + ". Restart the server to load updated plugins.";
    }

    private static bool SupportsPluginCatalog(ServerProfile profile) =>
        profile.Type.Equals("Paper", StringComparison.OrdinalIgnoreCase) ||
        profile.Type.Equals("Purpur", StringComparison.OrdinalIgnoreCase) ||
        profile.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase);





    private void LoadHealthAnalyzerPage()
    {
        HealthAnalyzerPage.ServerText.Text = _active is null ? "Select a server and run an analysis." : $"Analyze {_active.Name} for common installation, runtime, backup, and plugin risks.";
        HealthAnalyzerPage.AnalyzeButton.IsEnabled = _active is not null;
        if (_active is null) { _healthChecks.Clear(); HealthAnalyzerPage.ScoreText.Text = "—"; HealthAnalyzerPage.GradeText.Text = "Not analyzed"; HealthAnalyzerPage.ScoreBar.Value = 0; }
    }

    private async Task AnalyzeServerHealthAsync()
    {
        if (_active is null) return;
        HealthAnalyzerPage.AnalyzeButton.IsEnabled = false;
        HealthAnalyzerPage.GradeText.Text = "Analyzing…";
        try
        {
            var report = await _healthAnalyzer.AnalyzeAsync(_active, Path.Combine(DefaultServersRoot, "_Backups"), _lifetimeCancellation.Token);
            _healthChecks.Clear(); foreach (var check in report.Checks) _healthChecks.Add(check);
            HealthAnalyzerPage.ScoreText.Text = report.Score.ToString();
            HealthAnalyzerPage.ScoreBar.Value = report.Score;
            HealthAnalyzerPage.GradeText.Text = report.Grade;
            HealthAnalyzerPage.SummaryText.Text = $"Checked {report.CheckedAt.LocalDateTime:g} • {_healthChecks.Count(item => item.Status == "Healthy")} healthy • {_healthChecks.Count(item => item.Status != "Healthy")} need attention";
            AddNotification(report.Score >= 75 ? "Health" : "Warning", $"Health score: {report.Score}", $"{_active.Name} was rated {report.Grade}.", _active.Name);
            AppendLog($"[Health] {_active.Name}: {report.Score}/100 ({report.Grade}).");
        }
        catch (Exception ex) { HealthAnalyzerPage.GradeText.Text = "Analysis failed"; HealthAnalyzerPage.SummaryText.Text = ex.Message; }
        finally { HealthAnalyzerPage.AnalyzeButton.IsEnabled = _active is not null; }
    }

    private void AddNotification(string category, string title, string message, string? serverName = null)
    {
        _notifications.Insert(0, new NotificationRecord { Category = category, Title = title, Message = message, ServerName = serverName ?? string.Empty });
        while (_notifications.Count > 250) _notifications.RemoveAt(_notifications.Count - 1);
        _notificationStore.Save(_notifications);
        if (NotificationCenterPage is not null) LoadNotificationCenter();
    }

    private void LoadNotificationCenter()
    {
        var unread = _notifications.Count(item => !item.IsRead);
        NotificationCenterPage.SummaryText.Text = $"{unread} unread notification{(unread == 1 ? string.Empty : "s")} • {_notifications.Count} total";
        NotificationCenterPage.StatusText.Text = "The newest events appear first. Up to 250 notifications are retained.";
        NotificationCenterPage.NotificationsGrid.Items.Refresh();
    }

    private void Notification_MarkAll(object? sender, EventArgs e) { foreach (var item in _notifications) item.IsRead = true; _notificationStore.Save(_notifications); LoadNotificationCenter(); }
    private void Notification_MarkSelected(object? sender, EventArgs e) { if (NotificationCenterPage.SelectedNotification is { } item) { item.IsRead = true; _notificationStore.Save(_notifications); LoadNotificationCenter(); } }
    private void Notification_Clear(object? sender, EventArgs e)
    {
        if (_notifications.Count == 0) return;
        if (MessageBox.Show("Clear all notifications?", "Notification Center", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _notifications.Clear(); _notificationStore.Save(_notifications); LoadNotificationCenter();
    }

    private void CrashRecoverySettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingCrashRecoverySettings || _active is null) return;
        _active.CrashRecoveryEnabled = CrashRecoveryCheckBox.IsChecked == true;
        _active.CrashRestartLimit = int.TryParse((CrashRecoveryLimitCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var limit) ? limit : 3;
        _active.CrashRestartDelaySeconds = int.TryParse((CrashRecoveryDelayCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var delay) ? delay : 10;
        SaveProfiles(); UpdateDetails();
    }

    private async Task HandleCrashRecoveryAsync(ServerProfile profile, int? exitCode)
    {
        if (!profile.CrashRecoveryEnabled) { AppendLog("[Recovery] Automatic restart is disabled for this server."); return; }
        if (!_crashRestartHistory.TryGetValue(profile.Folder, out var history)) _crashRestartHistory[profile.Folder] = history = new Queue<DateTimeOffset>();
        var cutoff = DateTimeOffset.Now.AddMinutes(-Math.Max(1, profile.CrashRestartWindowMinutes));
        while (history.Count > 0 && history.Peek() < cutoff) history.Dequeue();
        if (history.Count >= Math.Max(1, profile.CrashRestartLimit))
        {
            AppendLog($"[Recovery] Restart limit reached for {profile.Name}. Automatic recovery has stopped to prevent a crash loop.");
            profile.RuntimeState = "Recovery stopped"; UpdateDetails(); return;
        }
        history.Enqueue(DateTimeOffset.Now);
        var attempt = history.Count;
        var exitCodeText = exitCode is null ? string.Empty : $" (exit code {exitCode})";
        AppendLog($"[Recovery] Crash detected{exitCodeText}. Restart attempt {attempt}/{profile.CrashRestartLimit} in {profile.CrashRestartDelaySeconds} seconds...");
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, profile.CrashRestartDelaySeconds)));
        if (_launcher.IsRunning) { AppendLog("[Recovery] Restart cancelled because another server is already running."); return; }
        _active = profile;
        await StartServerAsync();
    }

    private void LoadScheduledTasksPage()
    {
        ScheduledTasksPage.ServerText.Text = _active is null
            ? "Select a server to configure automation."
            : $"Scheduled automation for {_active.Name}. Tasks continue while the manager remains open.";
        foreach (var task in _scheduledTasks) task.RefreshSchedule();
        ScheduledTasksPage.StatusText.Text = _active is null
            ? "No server selected."
            : $"{_scheduledTasks.Count(task => string.Equals(task.ServerFolder, _active.Folder, StringComparison.OrdinalIgnoreCase))} task(s) configured for this server.";
        ScheduledTasksPage.TasksGrid.Items.Filter = item => item is ScheduledTaskRecord task && _active is not null && string.Equals(task.ServerFolder, _active.Folder, StringComparison.OrdinalIgnoreCase);
        ScheduledTasksPage.TasksGrid.Items.Refresh();
    }

    private void SaveScheduledTasks()
    {
        _scheduledTaskStore.Save(_scheduledTasks);
        LoadScheduledTasksPage();
    }

    private void ScheduledTask_AddRequested(object? sender, EventArgs e)
    {
        if (_active is null) { MessageBox.Show("Select a server first.", "Scheduled Tasks", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var name = ScheduledTasksPage.TaskNameBox.Text.Trim();
        var action = ScheduledTasksPage.SelectedAction;
        var payload = ScheduledTasksPage.PayloadBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Enter a task name.", "Scheduled Tasks", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if ((action is "Broadcast" or "Command") && string.IsNullOrWhiteSpace(payload)) { MessageBox.Show("Enter a message or command for this task.", "Scheduled Tasks", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _scheduledTasks.Add(new ScheduledTaskRecord { ServerFolder = _active.Folder, ServerName = _active.Name, Name = name, Action = action, Payload = payload, IntervalMinutes = ScheduledTasksPage.SelectedInterval });
        SaveScheduledTasks();
        AppendLog($"[Scheduler] Added '{name}' every {ScheduledTasksPage.SelectedInterval} minutes.");
    }

    private void ScheduledTask_DeleteRequested(object? sender, EventArgs e)
    {
        var task = ScheduledTasksPage.SelectedTask;
        if (task is null) return;
        if (MessageBox.Show($"Delete scheduled task '{task.Name}'?", "Delete task", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _scheduledTasks.Remove(task); SaveScheduledTasks();
    }

    private void ScheduledTask_ToggleRequested(object? sender, EventArgs e)
    {
        var task = ScheduledTasksPage.SelectedTask;
        if (task is null) return;
        task.Enabled = !task.Enabled; SaveScheduledTasks();
    }

    private async void ScheduledTask_RunNowRequested(object? sender, EventArgs e)
    {
        var task = ScheduledTasksPage.SelectedTask;
        if (task is null) return;
        await ExecuteScheduledTaskAsync(task, manual: true);
    }

    private async void ScheduledTaskTimer_Tick(object? sender, EventArgs e)
    {
        if (_scheduledTaskBusy) return;
        var due = _scheduledTasks.Where(task => task.Enabled && task.NextRunAt <= DateTimeOffset.Now).OrderBy(task => task.NextRunAt).ToList();
        foreach (var task in due) await ExecuteScheduledTaskAsync(task, manual: false);
    }

    private async Task ExecuteScheduledTaskAsync(ScheduledTaskRecord task, bool manual)
    {
        if (_scheduledTaskBusy) return;
        _scheduledTaskBusy = true;
        try
        {
            var profile = _servers.FirstOrDefault(item => string.Equals(item.Folder, task.ServerFolder, StringComparison.OrdinalIgnoreCase));
            if (profile is null) throw new InvalidOperationException("The server profile no longer exists.");
            var runningThisServer = _launcher.IsRunning && _runningProfile is not null && string.Equals(_runningProfile.Folder, profile.Folder, StringComparison.OrdinalIgnoreCase);
            switch (task.Action)
            {
                case "Broadcast":
                    if (!runningThisServer || !_launcher.CanSendCommands) throw new InvalidOperationException("The server must be running under manager control.");
                    _launcher.SendCommand("say " + task.Payload); break;
                case "Command":
                    if (!runningThisServer || !_launcher.CanSendCommands) throw new InvalidOperationException("The server must be running under manager control.");
                    _launcher.SendCommand(task.Payload); break;
                case "Restart":
                    if (!runningThisServer) throw new InvalidOperationException("The selected server is not running.");
                    await _launcher.StopAsync(TimeSpan.FromSeconds(20));
                    _active = profile; await Task.Delay(750); await StartServerAsync(); break;
                case "Stop":
                    if (!runningThisServer) throw new InvalidOperationException("The selected server is not running.");
                    await _launcher.StopAsync(TimeSpan.FromSeconds(20)); break;
                case "Start":
                    if (_launcher.IsRunning) throw new InvalidOperationException("Another server is already running.");
                    _active = profile; await StartServerAsync(); break;
                case "Update Check":
                    _active = profile; await CheckServerUpdateAsync(); break;
                default: throw new InvalidOperationException("Unknown scheduled action.");
            }
            task.LastResult = manual ? "Completed manually" : "Completed";
            AppendLog($"[Scheduler] Completed '{task.Name}' ({task.Action}).");
        }
        catch (Exception ex)
        {
            task.LastResult = "Skipped: " + ex.Message;
            AppendLog($"[Scheduler] '{task.Name}' skipped: {ex.Message}");
        }
        finally
        {
            task.LastRunAt = DateTimeOffset.Now;
            _scheduledTaskBusy = false;
            SaveScheduledTasks();
        }
    }

    private BackupRecord? SelectedBackup => BackupsPage.BackupsGrid.SelectedItem as BackupRecord;

    private void LoadBackups()
    {
        _backups.Clear();
        BackupsPage.BackupServerText.Text = _active is null ? "Select a server to manage its backups." : $"Backups for {_active.Name}";
        if (_active is null) { BackupsPage.BackupStatusText.Text = string.Empty; return; }
        foreach (var backup in _backupService.List(_active)) _backups.Add(backup);
        _loadingBackupSettings = true;
        BackupsPage.AutomaticBackupsCheckBox.IsChecked = _active.AutomaticBackupsEnabled;
        SelectTaggedItem(BackupsPage.BackupIntervalCombo, _active.BackupIntervalMinutes);
        SelectTaggedItem(BackupsPage.BackupRetentionCombo, _active.BackupRetentionCount);
        _loadingBackupSettings = false;
        BackupsPage.BackupStatusText.Text = $"{_backups.Count} backup{(_backups.Count == 1 ? string.Empty : "s")} • {_backupService.GetServerBackupFolder(_active)}";
        UpdateAutomaticBackupScheduleText();
    }

    private static void SelectTaggedItem(ComboBox combo, int value)
    {
        var match = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => int.TryParse(item.Tag?.ToString(), out var tag) && tag == value);
        combo.SelectedItem = match ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void BackupSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingBackupSettings || _active is null || BackupsPage.AutomaticBackupsCheckBox is null) return;
        _active.AutomaticBackupsEnabled = BackupsPage.AutomaticBackupsCheckBox.IsChecked == true;
        if (BackupsPage.BackupIntervalCombo.SelectedItem is ComboBoxItem interval && int.TryParse(interval.Tag?.ToString(), out var minutes)) _active.BackupIntervalMinutes = minutes;
        if (BackupsPage.BackupRetentionCombo.SelectedItem is ComboBoxItem retention && int.TryParse(retention.Tag?.ToString(), out var keep)) _active.BackupRetentionCount = keep;
        SaveProfiles();
        UpdateAutomaticBackupScheduleText();
    }

    private void UpdateAutomaticBackupScheduleText()
    {
        if (_active is null)
        {
            BackupsPage.LastAutomaticBackupText.Text = "Last automatic backup: —";
            BackupsPage.NextAutomaticBackupText.Text = "Next automatic backup: —";
            return;
        }

        BackupsPage.LastAutomaticBackupText.Text = _active.LastAutomaticBackupAt is { } last
            ? $"Last automatic backup: {last.LocalDateTime:g}"
            : "Last automatic backup: Never";

        if (!_active.AutomaticBackupsEnabled)
        {
            BackupsPage.NextAutomaticBackupText.Text = "Next automatic backup: Disabled";
            return;
        }

        var next = GetNextAutomaticBackupAt(_active);
        BackupsPage.NextAutomaticBackupText.Text = next <= DateTimeOffset.Now
            ? "Next automatic backup: Due now"
            : $"Next automatic backup: {next.LocalDateTime:g}";
    }

    private static DateTimeOffset GetNextAutomaticBackupAt(ServerProfile profile)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(5, profile.BackupIntervalMinutes));
        return profile.LastAutomaticBackupAt is { } last ? last + interval : DateTimeOffset.MinValue;
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e) => await CreateBackupAsync("Manual", showMessages: true);

    private async Task<bool> CreateBackupAsync(string type, bool showMessages)
    {
        if (_active is null || _backupBusy) return false;
        _backupBusy = true; BackupsPage.BackupProgressBar.Visibility = Visibility.Visible; BackupsPage.BackupStatusText.Text = "Preparing backup…";
        try
        {
            var selectedIsRunning = _launcher.IsRunning && ReferenceEquals(_active, _runningProfile);
            if (selectedIsRunning)
            {
                if (!_launcher.CanSendCommands)
                {
                    if (showMessages) MessageBox.Show("This recovered server process cannot be flushed safely. Restart it from the manager before creating a live backup.", "Backup unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }
                _launcher.SendCommand("save-all flush");
                await Task.Delay(1800);
            }
            var progress = new Progress<string>(text => BackupsPage.BackupStatusText.Text = text);
            await _backupService.CreateAsync(_active, type, progress);
            if (type == "Automatic")
            {
                _backupService.ApplyAutomaticRetention(_active, _active.BackupRetentionCount);
                _active.LastAutomaticBackupAt = DateTimeOffset.Now;
                SaveProfiles();
            }
            LoadBackups();
            AddNotification("Backup", "Backup completed", $"{type} backup completed successfully.", _active.Name);
            if (showMessages) MessageBox.Show("Backup created successfully.", "Backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) { BackupsPage.BackupStatusText.Text = "Backup failed: " + ex.Message; if (showMessages) MessageBox.Show(ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        finally { _backupBusy = false; BackupsPage.BackupProgressBar.Visibility = Visibility.Collapsed; }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null || SelectedBackup is null || _backupBusy) return;
        if (_launcher.IsRunning && ReferenceEquals(_active, _runningProfile)) { MessageBox.Show("Stop the selected server before restoring a backup.", "Server is running", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show($"Restore {SelectedBackup.Name}?\n\nThe manager will create a safety backup first, then replace the current server files.", "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _backupBusy = true; BackupsPage.BackupProgressBar.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<string>(text => BackupsPage.BackupStatusText.Text = text);
            await _backupService.CreateAsync(_active, "Safety-Before-Restore", progress);
            await _backupService.RestoreAsync(_active, SelectedBackup, progress);
            _active.RefreshDerivedProperties();
            LoadBackups(); UpdateDetails();
            MessageBox.Show("The backup was restored successfully.", "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { BackupsPage.BackupStatusText.Text = "Restore failed: " + ex.Message; MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _backupBusy = false; BackupsPage.BackupProgressBar.Visibility = Visibility.Collapsed; }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup is null || _backupBusy) return;
        if (MessageBox.Show($"Delete {SelectedBackup.Name}?", "Delete backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _backupService.Delete(SelectedBackup); LoadBackups(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RefreshBackups_Click(object sender, RoutedEventArgs e) => LoadBackups();
    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", _backupService.GetServerBackupFolder(_active)) { UseShellExecute = true });
    }

    private async void AutomaticBackupTimer_Tick(object? sender, EventArgs e)
    {
        if (_backupBusy) return;

        var now = DateTimeOffset.Now;
        var dueProfile = _servers
            .Where(profile => profile.AutomaticBackupsEnabled)
            .OrderBy(GetNextAutomaticBackupAt)
            .FirstOrDefault(profile => GetNextAutomaticBackupAt(profile) <= now);

        if (dueProfile is null)
        {
            UpdateAutomaticBackupScheduleText();
            return;
        }

        await CreateAutomaticBackupForProfileAsync(dueProfile);
    }

    private async Task CreateAutomaticBackupForProfileAsync(ServerProfile profile)
    {
        if (_backupBusy || !Directory.Exists(profile.Folder)) return;

        _backupBusy = true;
        var isVisibleProfile = ReferenceEquals(profile, _active);
        if (isVisibleProfile)
        {
            BackupsPage.BackupProgressBar.Visibility = Visibility.Visible;
            BackupsPage.BackupStatusText.Text = "Preparing scheduled backup…";
        }

        try
        {
            if (_launcher.IsRunning && ReferenceEquals(profile, _runningProfile))
            {
                if (!_launcher.CanSendCommands)
                {
                    _structuredLogger.Warning("Scheduled backup skipped because the running server cannot be flushed safely", new { profile.Name });
                    if (isVisibleProfile)
                        BackupsPage.BackupStatusText.Text = "Scheduled backup skipped: restart this server from the manager to restore console control.";
                    return;
                }

                _launcher.SendCommand("save-all flush");
                await Task.Delay(1800, _lifetimeCancellation.Token);
            }

            var progress = isVisibleProfile
                ? new Progress<string>(message => BackupsPage.BackupStatusText.Text = message)
                : null;

            await _backupService.CreateAsync(profile, "Automatic", progress, _lifetimeCancellation.Token);
            _backupService.ApplyAutomaticRetention(profile, profile.BackupRetentionCount);
            profile.LastAutomaticBackupAt = DateTimeOffset.Now;
            SaveProfiles();
            _structuredLogger.Information("Scheduled backup completed", new { profile.Name, profile.BackupRetentionCount });

            if (isVisibleProfile)
            {
                LoadBackups();
                BackupsPage.BackupStatusText.Text = "Scheduled backup completed successfully.";
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _structuredLogger.Error("Scheduled backup failed", ex, new { profile.Name });
            if (isVisibleProfile)
                BackupsPage.BackupStatusText.Text = "Scheduled backup failed: " + ex.Message;
        }
        finally
        {
            _backupBusy = false;
            if (isVisibleProfile)
            {
                BackupsPage.BackupProgressBar.Visibility = Visibility.Collapsed;
                UpdateAutomaticBackupScheduleText();
            }
        }
    }

    private static string GetDisplayVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+', 2)[0];

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.1";
    }

}
