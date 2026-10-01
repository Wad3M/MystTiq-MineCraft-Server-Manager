using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using ASimpleMinecraftServer.UI.Pages;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ASimpleMinecraftServer;

public partial class PrototypeWindow : Window
{
    private const string ThemeFileName = "mystui.theme";
    private readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"]="dashboard", ["Create Server"]="create", ["Console"]="console", ["Worlds"]="worlds", ["Players"]="players_nav",
        ["Add-ons"]="plugins_nav", ["Backups"]="autobackups", ["Health"]="health", ["Settings"]="settings_nav", ["Help"]="help"
    };

    // Pages that became tabs of another page. Navigating to one opens its parent on that tab,
    // so existing Navigate("Datapacks") calls keep working.
    private static readonly Dictionary<string, (string Page, string Tab)> PageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plugin Packs"] = ("Add-ons", "Plugin Packs"), ["Datapacks"] = ("Add-ons", "Datapacks"), ["Resource Packs"] = ("Add-ons", "Resource Pack"),
        ["Performance"] = ("Health", "Performance"), ["Log Analyzer"] = ("Health", "Logs"), ["Startup Analyzer"] = ("Health", "Startup"), ["Optimization"] = ("Health", "Optimization"),
        ["Auto Backups"] = ("Backups", "Backups"), ["Scheduler"] = ("Backups", "Scheduled Tasks"),
        ["Templates"] = ("Settings", "Presets"), ["About"] = ("Help", "Help")
    };
    private readonly Dictionary<string, string> _selectedTabs = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeTab;
    private TabItem? _activeTabItem;

    // Maps UI icon names to files in Assets/Icons (MystCraft icon pack, see docs/icons).
    private static readonly Dictionary<string, string> IconFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"]="dashboard", ["create"]="installer", ["console"]="console", ["worlds"]="worlds", ["world_card"]="worlds",
        ["players"]="players", ["players_nav"]="players", ["plugins_nav"]="plugins", ["plugin_card"]="plugins", ["manageplugins"]="plugins",
        ["installplugins"]="install_plugin", ["datapacks"]="datapacks", ["resourcepacks"]="resourcepacks", ["templates"]="workshop", ["pluginpacks"]="modpack",
        ["migration"]="portal", ["performance"]="performance", ["health"]="health", ["loganalyzer"]="search_logs", ["startup"]="boot",
        ["optimization"]="speed", ["scheduler_nav"]="scheduler", ["tasks"]="tasks", ["autobackups"]="schedule_backup", ["settings_nav"]="settings",
        ["settings_top"]="settings", ["help"]="help", ["about"]="about", ["start"]="start", ["stop"]="stop", ["restart"]="restart",
        ["kill"]="force_stop", ["backup"]="backup", ["createbackup"]="backup", ["filemanager"]="files", ["server"]="server", ["serverinfo"]="info",
        ["page_server"]="server", ["openconsole"]="terminal", ["createworld"]="new_world", ["tps"]="tps", ["cpu"]="cpu", ["memory"]="ram",
        ["disk"]="disk", ["mspt"]="mspt", ["clock"]="time", ["signal"]="network", ["network"]="network", ["log"]="logs", ["edit"]="edit", ["viewall"]="search", ["search"]="search"
    };
    private static readonly Dictionary<string, BitmapImage> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private NativeBackendController? _backend;
    private readonly Dictionary<string, Button> _toolbarButtons = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _statusTimer;
    private bool _sidebarCollapsed;
    // The dashboard rebuilds every second, so connection details live here between rebuilds.
    private IReadOnlyList<string> _lanAddresses = [];
    private DateTimeOffset _lanAddressesAt = DateTimeOffset.MinValue;
    private string? _publicAddress;
    private bool _publicAddressLoading;
    // Sidebar pieces the collapse toggle switches between "icon + label" and "icon only".
    private readonly List<(Button Button, FrameworkElement Icon, TextBlock Label)> _navItems = new();
    private readonly List<(Expander Section, string Title)> _navSections = new();
    private bool _allowClose;
    private bool _updatingServerPicker;
    private string _currentPage = "Dashboard";
    private DateTimeOffset? _lastCpuSampleAt;
    private TimeSpan _lastCpuTime;
    private double _lastCpuPercent;
    private TextBox? _consoleOutputBox;
    private ListBox? _playersList;
    private DateTimeOffset _lastMaintenanceCheck = DateTimeOffset.MinValue;
    private bool _maintenanceBusy;

    /// <summary>App version from the project file (the Version property in the .csproj), e.g. "0.2.0".</summary>
    private static readonly string AppVersion = typeof(PrototypeWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public PrototypeWindow()
    {
        InitializeComponent();
        Title = $"MystTiq MineCraft Server Manager v{AppVersion}";
        BannerVersionText.Text = $"v{AppVersion}";
        PrototypeStatus.Text = $"v{AppVersion} — Ready";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            App.WriteStartupLog($"MystTiq v{AppVersion} shell loaded; initializing native backend services.");
            BuildToolbar();
            BuildNavigation();
            RestoreTheme();

            _backend = new NativeBackendController();
            _backend.StateChanged += Backend_StateChanged;
            _backend.ConsoleLineReceived += Backend_ConsoleLineReceived;
            _backend.PlayersChanged += Backend_PlayersChanged;

            _updatingServerPicker = true;
            ServerPicker.ItemsSource = _backend.Servers;
            ServerPicker.SelectedItem = _backend.SelectedServer;
            _updatingServerPicker = false;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();

            Navigate("Dashboard");
            RefreshShellStatus();
            App.WriteStartupLog($"Native backend initialized. Profiles: {_backend.Servers.Count}.");
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Native backend initialization failed", ex);
            ShowStartupFailure(ex);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose || _backend is null)
        {
            base.OnClosing(e);
            return;
        }

        var running = _backend.RunningServers;
        if (running.Count == 0)
        {
            CleanupBackend();
            _allowClose = true;
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        var runningNames = string.Join(", ", running.Select(p => p.Name));
        var answer = MessageBox.Show(
            running.Count == 1
                ? $"{runningNames} is still running.\n\nStop it safely and exit MystTiq?"
                : $"{running.Count} servers are still running: {runningNames}.\n\nStop them all safely and exit MystTiq?",
            "Servers are running", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.No)
        {
            MessageBox.Show("MystTiq cannot exit while it owns running Java processes. Stop the servers first, or use Kill if absolutely necessary.", "MystTiq", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            PrototypeStatus.Text = running.Count == 1 ? "Stopping server before exit…" : $"Stopping {running.Count} servers before exit…";
            var stuck = await _backend.StopAllGracefullyAsync(TimeSpan.FromSeconds(20));
            if (stuck.Count > 0)
            {
                var force = MessageBox.Show($"These servers did not stop within 20 seconds: {string.Join(", ", stuck.Select(p => p.Name))}.\n\nForce kill them? Unsaved world data could be lost.", "Server did not stop", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (force != MessageBoxResult.Yes) return;
                foreach (var profile in stuck) await _backend.ForceKillAsync(profile);
            }
            CleanupBackend();
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError("Could not close MystTiq safely", ex);
        }
    }

    private void CleanupBackend()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        if (_backend is null) return;
        _backend.StateChanged -= Backend_StateChanged;
        _backend.ConsoleLineReceived -= Backend_ConsoleLineReceived;
        _backend.PlayersChanged -= Backend_PlayersChanged;
        _backend.Dispose();
        _backend = null;
    }

    private void ShowStartupFailure(Exception ex)
    {
        PrototypeStatus.Text = "Native backend initialization failed — see MystTiq_startup.log";
        ServerStatusText.Text = "Startup Error";
        ServerDetailText.Text = ex.Message;
        PageTitle.Text = "Startup Error";
        PageSubtitle.Text = "The shell stayed open so the failure can be diagnosed.";
        PageContent.Content = MessagePage("MystTiq could not initialize its native backend services.", ex.Message + "\n\nSee MystTiq_startup.log beside the executable for full details.");
    }

    private void BuildToolbar()
    {
        string[] actions = ["Start", "Stop", "Restart", "Kill", "Backup", "Console", "File Manager", "Settings"];
        foreach (var action in actions)
        {
            var button = new Button { Style = (Style)FindResource("ToolbarButton"), Tag = action };
            button.Click += ToolbarAction_Click;
            _toolbarButtons[action] = button;
            var stack = new StackPanel();
            stack.Children.Add(CreateIcon(action.Replace(" ", string.Empty).ToLowerInvariant() switch { "filemanager" => "filemanager", "settings" => "settings_top", _ => action.ToLowerInvariant() }, 22));
            stack.Children.Add(new TextBlock { Text = action, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            button.Content = stack;
            ToolbarHost.Children.Add(button);
        }
    }

    private void BuildNavigation()
    {
        AddSection("SERVER", ["Players", "Worlds", "Add-ons"]);
        AddSection("TOOLS", ["Backups", "Health"]);
        AddSection("APP", ["Settings", "Help"]);
    }

    private void AddSection(string title, IEnumerable<string> pages)
    {
        var panel = new StackPanel();
        foreach (var page in pages)
        {
            var button = new Button { Style = (Style)FindResource("NavButton"), Tag = page, ToolTip = page };
            button.Click += Nav_Click;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = CreateIcon(_icons[page], 27, new Thickness(0, 0, 10, 0));
            var label = new TextBlock { Text = page, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
            row.Children.Add(label);
            button.Content = row;
            panel.Children.Add(button);
            _navItems.Add((button, icon, label));
        }
        var expander = new Expander
        {
            Header = title, IsExpanded = true, Content = panel,
            Foreground = (Brush)FindResource("AccentBrush"), FontSize = 9, Margin = new Thickness(2, 4, 0, 0)
        };
        NavHost.Children.Add(expander);
        _navSections.Add((expander, title));
    }

    /// <summary>
    /// Several related tools on one page. Only the selected tab is built (so opening Health doesn't
    /// run every analyzer), and each page remembers its last tab.
    /// </summary>
    private TabControl Tabs(string page, params (string Name, Func<UIElement> Build)[] tabs)
    {
        var control = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        foreach (var (name, _) in tabs) control.Items.Add(new TabItem { Header = name });
        control.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, control) || control.SelectedItem is not TabItem item) return;
            var name = (string)item.Header;
            _selectedTabs[page] = name;
            _activeTab = name; _activeTabItem = item;
            try { item.Content = tabs.First(t => t.Name == name).Build(); }
            catch (Exception ex) { App.WriteStartupLog($"Failed to build tab: {page}/{name}", ex); item.Content = MessagePage(name, ex.Message); }
        };
        var wanted = _selectedTabs.TryGetValue(page, out var saved) ? Array.FindIndex(tabs, t => t.Name == saved) : 0;
        control.SelectedIndex = Math.Max(0, wanted);
        return control;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) Navigate(page);
    }

    private void Navigate(string page)
    {
        if (PageAliases.TryGetValue(page, out var alias)) { _selectedTabs[alias.Page] = alias.Tab; page = alias.Page; }
        _activeTab = null; _activeTabItem = null;
        _currentPage = page;
        PageTitle.Text = page;
        PageSubtitle.Text = SubtitleFor(page);
        PageIcon.Source = IconSource(_icons.TryGetValue(page, out var icon) ? icon : "server");
        UpdateNavigationSelection(page);

        try
        {
            PageContent.Content = page switch
            {
                "Dashboard" => BuildDashboard(),
                "Create Server" => BuildCreateServerPage(),
                "Console" => BuildConsolePage(),
                "Worlds" => BuildWorldsPage(),
                "Players" => BuildPlayersPage(),
                "Add-ons" => Tabs("Add-ons",
                    ("Add-ons", BuildAddonsPage),
                    ("Plugin Packs", BuildPluginPacksPage),
                    ("Datapacks", BuildDatapacksPage),
                    ("Resource Pack", BuildResourcePacksPage)),
                "Backups" => Tabs("Backups",
                    ("Backups", BuildBackupsPage),
                    ("Scheduled Tasks", BuildSchedulerPage)),
                "Health" => Tabs("Health",
                    ("Health", BuildHealthPage),
                    ("Performance", BuildPerformancePage),
                    ("Logs", BuildLogAnalyzerPage),
                    ("Startup", BuildStartupAnalyzerPage),
                    ("Optimization", BuildOptimizationPage)),
                "Settings" => Tabs("Settings",
                    ("Server", BuildSettingsPage),
                    ("Presets", BuildTemplatesPage)),
                "Help" => BuildHelpPage(),
                _ => MessagePage(page, "This page is not available.")
            };
            PrototypeStatus.Text = $"v{AppVersion} — {page}";
        }
        catch (Exception ex)
        {
            App.WriteStartupLog($"Failed to build page: {page}", ex);
            PageContent.Content = MessagePage(page, ex.Message);
            PrototypeStatus.Text = $"{page} could not be loaded";
        }

        RefreshShellStatus();
    }

    private UIElement BuildDashboard()
    {
        if (_backend is null) return MessagePage("Dashboard", "Backend is not initialized.");
        var root = new StackPanel();

        var summary = Card(); summary.Padding = new Thickness(16); summary.Margin = new Thickness(0, 0, 0, 8);
        var summaryGrid = new Grid();
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var summaryText = new StackPanel();
        summaryText.Children.Add(new TextBlock { Text = _backend.SelectedServer?.Name ?? "No server selected", FontSize = 24, FontWeight = FontWeights.SemiBold });
        summaryText.Children.Add(new TextBlock { Text = SelectedServerSummary(), Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(0, 4, 0, 0) });
        summaryGrid.Children.Add(summaryText);
        var state = new TextBlock { Text = RunningStateText(), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentBrightBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(state, 1); summaryGrid.Children.Add(state);
        summary.Child = summaryGrid; root.Children.Add(summary);

        var metrics = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 8) };
        var maxPlayers = ReadServerPropertyInt("max-players", 20);
        metrics.Children.Add(Metric("players", "PLAYERS ONLINE", $"{_backend.OnlinePlayers.Count} / {maxPlayers}", _backend.IsRunning ? "Live" : "Stopped", maxPlayers <= 0 ? 0 : _backend.OnlinePlayers.Count * 100d / maxPlayers));
        metrics.Children.Add(Metric("tps", "TPS", "—", "Use Spark/Paper metrics", 0));
        metrics.Children.Add(Metric("mspt", "MSPT", "—", "Use Spark/Paper metrics", 0));
        metrics.Children.Add(Metric("cpu", "CPU USAGE", _backend.IsRunning ? $"{_lastCpuPercent:N1}%" : "—", "Managed Java process", Math.Clamp(_lastCpuPercent, 0, 100)));
        var memoryMb = GetMemoryMb();
        var allocatedMb = (_backend.SelectedServer?.MemoryGb ?? 0) * 1024d;
        metrics.Children.Add(Metric("memory", "MEMORY USAGE", _backend.IsRunning ? $"{memoryMb:N0} MB" : "—", allocatedMb > 0 ? $"Configured {allocatedMb / 1024:N0} GB" : "", allocatedMb > 0 ? Math.Min(100, memoryMb * 100 / allocatedMb) : 0));
        metrics.Children.Add(Metric("clock", "UPTIME", _backend.IsRunning ? FormatDuration(_backend.Uptime) : "—", _backend.IsRecoveredProcess ? "Recovered process" : "Managed by MystTiq", _backend.IsRunning ? 100 : 0));
        root.Children.Add(metrics);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logCard = Card(); logCard.Padding = new Thickness(12); logCard.Margin = new Thickness(0, 0, 8, 0);
        var logStack = new StackPanel(); logStack.Children.Add(TitleRow("log", "Recent Console"));
        foreach (var line in _backend.ConsoleSnapshot().TakeLast(9)) logStack.Children.Add(new TextBlock { Text = line, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        if (_backend.ConsoleSnapshot().Count == 0) logStack.Children.Add(Muted("No server output yet."));
        logCard.Child = logStack; grid.Children.Add(logCard);

        var infoCard = Card(); infoCard.Padding = new Thickness(12); infoCard.Margin = new Thickness(0, 0, 8, 0);
        var info = new StackPanel(); info.Children.Add(TitleRow("serverinfo", "Server Information"));
        if (_backend.SelectedServer is { } p)
        {
            info.Children.Add(InfoLine("Server", p.Name));
            info.Children.Add(InfoLine("Type", p.Type));
            info.Children.Add(InfoLine("Version", string.IsNullOrWhiteSpace(p.Version) ? "Unknown" : p.Version));
            info.Children.Add(InfoLine("Port", p.Port.ToString()));
            info.Children.Add(InfoLine("Memory", $"{p.MemoryGb} GB"));
            info.Children.Add(InfoLine("Folder", p.Folder));
            info.Children.Add(InfoLine("JAR", p.Jar));
        }
        else info.Children.Add(Muted("Select or create a server profile."));
        infoCard.Child = info; Grid.SetColumn(infoCard, 1); grid.Children.Add(infoCard);

        var protectionCard = Card(); protectionCard.Padding = new Thickness(12);
        var protection = new StackPanel(); protection.Children.Add(TitleRow("backup", "Protection & Content"));
        if (_backend.SelectedServer is { } selected)
        {
            var backups = Safe(() => _backend.Backups.List(selected).Count, 0);
            var worlds = Safe(() => _backend.Worlds.Scan(selected).Count, 0);
            var plugins = Safe(() => _backend.Plugins.List(selected).Count, 0);
            protection.Children.Add(InfoLine("Backups", backups.ToString()));
            protection.Children.Add(InfoLine("Worlds", worlds.ToString()));
            protection.Children.Add(InfoLine("Add-ons", plugins.ToString()));
            protection.Children.Add(InfoLine("Auto backup", selected.AutomaticBackupsEnabled ? "Enabled" : "Disabled"));
        }
        else protection.Children.Add(Muted("No server selected."));
        protectionCard.Child = protection; Grid.SetColumn(protectionCard, 2); grid.Children.Add(protectionCard);
        root.Children.Add(grid);
        if (_backend.SelectedServer is { } connectTo) root.Children.Add(BuildConnectCard(connectTo));
        root.Children.Add(BuildAllServersCard());
        return root;
    }

    /// <summary>
    /// The addresses players type into Minecraft to join the selected server: this PC,
    /// the home network, and the internet (public IP looked up only when asked).
    /// </summary>
    private UIElement BuildConnectCard(ServerProfile profile)
    {
        if (DateTimeOffset.Now - _lanAddressesAt > TimeSpan.FromSeconds(30))
        {
            _lanAddresses = NetworkAddressService.GetLanAddresses();
            _lanAddressesAt = DateTimeOffset.Now;
        }
        var port = profile.Port;
        var lan = _lanAddresses.FirstOrDefault();

        var card = Card(); card.Padding = new Thickness(12); card.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel(); card.Child = stack;
        stack.Children.Add(TitleRow("network", $"How to Connect to {profile.Name}"));

        var rows = new Grid();
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        void AddRow(string label, string? address, UIElement? action, string note)
        {
            var row = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(name, row); rows.Children.Add(name);
            var value = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (address is not null)
            {
                value.Children.Add(new TextBox { Text = address, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), MinWidth = 190, Margin = new Thickness(0, 2, 6, 2), VerticalContentAlignment = VerticalAlignment.Center });
                value.Children.Add(ActionButton("Copy", (_, _) => { try { Clipboard.SetText(address); PrototypeStatus.Text = $"Copied {address}"; } catch { } }));
            }
            if (action is not null) value.Children.Add(action);
            Grid.SetRow(value, row); Grid.SetColumn(value, 1); rows.Children.Add(value);
            var hint = Muted(note); hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetRow(hint, row); Grid.SetColumn(hint, 2); rows.Children.Add(hint);
        }

        AddRow("This PC", $"localhost:{port}", null, "Play on the same computer that runs the server.");
        AddRow("Same network (LAN)", lan is null ? null : $"{lan}:{port}", null,
            lan is null ? "No local network connection found." : "Friends on your Wi-Fi or home network.");

        UIElement publicAction;
        if (_publicAddress is not null) publicAction = new TextBlock();
        else
        {
            var show = ActionButton(_publicAddressLoading ? "Looking up…" : "Show public address", async (_, _) =>
            {
                if (_backend is null || _publicAddressLoading) return;
                _publicAddressLoading = true; RefreshDashboard();
                try { _publicAddress = await _backend.Network.GetPublicAddressAsync(); }
                catch (Exception ex) { PrototypeStatus.Text = $"Couldn't look up the public address: {ex.Message}"; }
                finally { _publicAddressLoading = false; RefreshDashboard(); }
            });
            show.IsEnabled = !_publicAddressLoading;
            show.ToolTip = "Asks api.ipify.org for this network's public IP. Only happens when you click.";
            publicAction = show;
        }
        AddRow("Internet", _publicAddress is null ? null : $"{_publicAddress}:{port}", publicAction,
            lan is null ? $"Forward TCP port {port} on your router to this PC." : $"Needs TCP port {port} forwarded on your router to {lan}.");
        stack.Children.Add(rows);

        var tip = Muted($"In Minecraft: Multiplayer → Add Server, then paste the address. Port {port} must be different for every server you run at the same time. Windows Firewall may ask to allow Java the first time a server starts; allow it on private networks.");
        tip.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(tip);
        return card;
    }

    private void RefreshDashboard()
    {
        if (_currentPage == "Dashboard") PageContent.Content = BuildDashboard();
    }

    /// <summary>Every server profile with its port and state, and a Start/Stop button for each.</summary>
    private UIElement BuildAllServersCard()
    {
        var card = Card(); card.Padding = new Thickness(12); card.Margin = new Thickness(0, 8, 0, 0);
        var stack = new StackPanel(); stack.Children.Add(TitleRow("server", "All Servers"));
        if (_backend is null || _backend.Servers.Count == 0)
        {
            stack.Children.Add(Muted("No servers yet. Use Create Server to add one."));
            card.Child = stack; return card;
        }

        foreach (var profile in _backend.Servers)
        {
            var running = _backend.IsServerRunning(profile);
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = (ReferenceEquals(profile, _backend.SelectedServer) ? "▶ " : string.Empty) + profile.Name, FontWeight = FontWeights.SemiBold };
            text.Children.Add(name);
            text.Children.Add(Muted($"{profile.Type} {profile.Version}   •   Port {profile.Port}   •   {profile.MemoryGb} GB   •   {(running ? profile.RuntimeState : "Stopped")}"));
            row.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var target = profile;
            if (!ReferenceEquals(profile, _backend.SelectedServer))
                buttons.Children.Add(ActionButton("Select", (_, _) => ServerPicker.SelectedItem = target));
            buttons.Children.Add(running
                ? ActionButton("Stop", async (_, _) => await RunServerAction("Stop", target, async b =>
                    {
                        if (!await b.StopGracefullyAsync(target, TimeSpan.FromSeconds(20)))
                            MessageBox.Show($"{target.Name} did not stop within 20 seconds. Select it and use Kill only if you are certain it is stuck.", "Stop timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }))
                : ActionButton("Start", async (_, _) => { if (await ConfirmJavaAsync(target)) await RunServerAction("Start", target, b => b.StartAsync(target)); }));
            Grid.SetColumn(buttons, 1); row.Children.Add(buttons);
            stack.Children.Add(row);
        }
        card.Child = stack; return card;
    }

    /// <summary>
    /// Checks the server's Java before starting. Missing Java blocks the start; an older Java than
    /// the Minecraft version needs asks first, since the server would almost certainly fail.
    /// </summary>
    private async Task<bool> ConfirmJavaAsync(ServerProfile profile)
    {
        var result = await ASimpleMinecraftServer.Services.JavaDetector.ValidateAsync(profile.JavaPath, profile.Version);
        if (result.IsValid) return true;
        if (result.Installation is null)
        {
            MessageBox.Show(this, $"{result.Message}\n\nInstall Java {result.RequiredMajorVersion} (for example from adoptium.net), or set the Java path in this server's Settings.", "Java not found", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        // Versions MystTiq cannot read (custom or imported servers) are only checked for a working Java.
        if (!System.Text.RegularExpressions.Regex.IsMatch(profile.Version ?? string.Empty, @"^\d+\.\d+")) return true;
        return MessageBox.Show(this, $"{result.Message}\n\nThe server will most likely fail to start. Start anyway?", "Java version", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private async Task RunServerAction(string action, ServerProfile profile, Func<NativeBackendController, Task> work)
    {
        if (_backend is null) return;
        try { await work(_backend); }
        catch (Exception ex) { ShowError($"{action} {profile.Name} failed", ex); }
        RefreshShellStatus();
        if (_currentPage == "Dashboard") PageContent.Content = BuildDashboard();
    }

    private UIElement BuildCreateServerPage()
    {
        if (_backend is null) return new TextBlock { Text = "Backend is not initialized." };

        var rootPanel = new StackPanel();
        var import = Card(); import.Padding = new Thickness(16); import.Margin = new Thickness(0, 0, 0, 8);
        var importStack = new StackPanel(); importStack.Children.Add(TitleRow("create", "Add an Existing Minecraft Server"));
        importStack.Children.Add(new TextBlock { Text = "Select an existing server folder. MystTiq will detect its server JAR and add it to the server list without changing the server files.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 10) });
        importStack.Children.Add(ActionButton("Browse Existing Server…", (_, _) => ImportExistingServer()));
        import.Child = importStack; rootPanel.Children.Add(import);

        var create = Card(); create.Padding = new Thickness(16);
        var outer = new StackPanel();
        outer.Children.Add(TitleRow("server", "Install a New Minecraft Server"));
        outer.Children.Add(new TextBlock
        {
            Text = "Choose a server implementation and Minecraft version. MystTiq will download the correct server JAR, create the folder and server.properties, and add the new server profile.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var form = new Grid();
        for (var i = 0; i < 10; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        form.ColumnDefinitions.Add(new ColumnDefinition());

        var name = new TextBox { Text = "Minecraft Server", Margin = new Thickness(0, 3, 0, 3) };
        var installRoot = new TextBox { Text = @"C:\GameServers\Minecraft", Margin = new Thickness(0, 3, 0, 3) };
        var folder = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 3, 0, 3) };
        var type = new ComboBox { ItemsSource = _backend.SupportedServerTypes, SelectedItem = "Paper", Margin = new Thickness(0, 3, 0, 3) };
        var version = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Margin = new Thickness(0, 3, 0, 3), MinWidth = 180 };
        var memory = new TextBox { Text = "4", Margin = new Thickness(0, 3, 0, 3) };
        var port = new TextBox { Text = _backend.SuggestFreePort().ToString(), Margin = new Thickness(0, 3, 0, 3), ToolTip = "Each server needs its own port. The next free one is filled in for you." };
        var customJar = new TextBox { Margin = new Thickness(0, 3, 0, 3), IsEnabled = false };
        var java = new TextBox { Text = "java", Margin = new Thickness(0, 3, 0, 3) };
        var provider = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(0, 6, 0, 3) };

        AddFormRow(form, 0, "Server name", name);
        AddFormRow(form, 1, "Install root", installRoot);
        AddFormRow(form, 2, "Server folder", folder);
        AddFormRow(form, 3, "Server type", type);
        AddFormRow(form, 4, "Minecraft version", version);
        AddFormRow(form, 5, "Memory (GB)", memory);
        AddFormRow(form, 6, "Port", port);
        AddFormRow(form, 7, "Custom server JAR", customJar);
        AddFormRow(form, 8, "Java executable", java);
        AddFormRow(form, 9, "Download source", provider);
        outer.Children.Add(form);

        // The Minecraft EULA must be accepted by the user, not on their behalf.
        var eulaLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Minecraft End User License Agreement")) { NavigateUri = new Uri("https://aka.ms/MinecraftEULA") };
        eulaLink.RequestNavigate += (_, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; };
        var eulaText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        eulaText.Inlines.Add("I have read and accept the ");
        eulaText.Inlines.Add(eulaLink);
        var eula = new CheckBox { Content = eulaText, Margin = new Thickness(0, 10, 0, 0) };
        outer.Children.Add(eula);

        var status = new TextBlock
        {
            Text = "Choose a server type and version.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        outer.Children.Add(status);

        static string SafeFolderName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((value ?? string.Empty).Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(cleaned) ? "Minecraft Server" : cleaned;
        }

        void UpdateServerFolder()
        {
            try
            {
                var basePath = string.IsNullOrWhiteSpace(installRoot.Text) ? @"C:\GameServers\Minecraft" : installRoot.Text.Trim();
                folder.Text = Path.Combine(basePath, SafeFolderName(name.Text));
            }
            catch
            {
                folder.Text = string.Empty;
            }
        }

        void UpdateProviderText()
        {
            var selectedType = type.SelectedItem?.ToString() ?? "Paper";
            provider.Text = selectedType switch
            {
                "Vanilla" => "Mojang official server downloads • installed as server.jar",
                "Paper" => "PaperMC Downloads Service • latest stable build when available • installed as server.jar",
                "Purpur" => "PurpurMC Downloads API • latest build for the selected Minecraft version • installed as server.jar",
                "Folia" => "PaperMC Downloads Service (Folia) • installed as server.jar",
                "Fabric" => "Fabric Metadata API executable server launcher • installed as server.jar",
                "Custom JAR" => "Local JAR selected below • copied into the server folder as server.jar",
                _ => "Automatic server download"
            };
        }

        async Task LoadVersionsAsync()
        {
            var selectedType = type.SelectedItem?.ToString() ?? "Paper";
            UpdateProviderText();
            var custom = selectedType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase);
            customJar.IsEnabled = custom;
            version.IsEnabled = !custom;
            if (custom)
            {
                version.ItemsSource = null;
                version.Text = string.Empty;
                status.Text = "Choose the custom server JAR you want MystTiq to copy into the new server folder.";
                return;
            }

            try
            {
                version.IsEnabled = false;
                status.Text = $"Loading available {selectedType} Minecraft versions…";
                var versions = await _backend.GetAvailableVersionsAsync(selectedType);
                version.ItemsSource = versions;
                version.IsEnabled = true;
                if (versions.Count > 0)
                {
                    version.SelectedIndex = 0;
                    status.Text = $"{versions.Count} {selectedType} Minecraft version(s) available. The selected build will be downloaded automatically.";
                }
                else
                {
                    status.Text = $"No downloadable {selectedType} versions were returned by the provider.";
                }
            }
            catch (Exception ex)
            {
                version.IsEnabled = true;
                status.Text = $"Could not load {selectedType} versions: {ex.Message}. You may type a version manually and try the install.";
            }
        }

        name.TextChanged += (_, _) => UpdateServerFolder();
        installRoot.TextChanged += (_, _) => UpdateServerFolder();
        type.SelectionChanged += async (_, _) => await LoadVersionsAsync();
        UpdateServerFolder();
        UpdateProviderText();

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(ActionButton("Browse Install Root…", (_, _) =>
        {
            var d = PickFolder(installRoot.Text);
            if (d is not null) installRoot.Text = d;
        }));
        var customBrowse = ActionButton("Browse Custom JAR…", (_, _) =>
        {
            var f = PickFile("Java archives (*.jar)|*.jar|All files (*.*)|*.*", installRoot.Text);
            if (f is not null) customJar.Text = f;
        });
        actions.Children.Add(customBrowse);
        actions.Children.Add(ActionButton("Browse Java…", (_, _) =>
        {
            var f = PickFile("Java executable (java.exe)|java.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*");
            if (f is not null) java.Text = f;
        }));

        Button? installButton = null;
        installButton = ActionButton("Install Server", async (_, _) =>
        {
            if (_backend is null || installButton is null) return;
            var selectedType = type.SelectedItem?.ToString() ?? "Paper";
            var selectedVersion = version.SelectedItem?.ToString() ?? version.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(memory.Text, out var gb)) gb = 4;
            if (!int.TryParse(port.Text, out var serverPort) || serverPort is < 1 or > 65535)
            {
                status.Text = "Enter a port between 1 and 65535.";
                return;
            }
            if (eula.IsChecked != true)
            {
                status.Text = "Accept the Minecraft EULA to install a server.";
                return;
            }

            try
            {
                installButton.IsEnabled = false;
                status.Text = selectedType.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase)
                    ? $"Installing custom server into {folder.Text}…"
                    : $"Downloading and installing {selectedType} for Minecraft {selectedVersion}…";

                var profile = await _backend.InstallServerAsync(
                    name.Text,
                    folder.Text,
                    selectedType,
                    selectedVersion,
                    gb,
                    java.Text,
                    serverPort,
                    eulaAccepted: true,
                    customJarPath: customJar.Text);

                ServerPicker.SelectedItem = profile;
                status.Text = $"Installed {profile.Type} {profile.Version} successfully in {profile.Folder}.";
                MessageBox.Show($"Server installed successfully.\n\n{profile.Name}\n{profile.Type} {profile.Version}\n{profile.Folder}", "MystTiq", MessageBoxButton.OK, MessageBoxImage.Information);
                Navigate("Dashboard");
            }
            catch (Exception ex)
            {
                status.Text = $"Install failed: {ex.Message}";
                App.WriteStartupLog("Server installation failed", ex);
                MessageBox.Show(ex.Message, "Server installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                installButton.IsEnabled = eula.IsChecked == true;
            }
        }, "GreenActionButton");
        installButton.IsEnabled = false;
        eula.Checked += (_, _) => installButton.IsEnabled = true;
        eula.Unchecked += (_, _) => installButton.IsEnabled = false;
        actions.Children.Add(installButton);
        outer.Children.Add(actions);

        create.Child = outer;
        rootPanel.Children.Add(create);
        rootPanel.Loaded += async (_, _) => await LoadVersionsAsync();
        return rootPanel;
    }

    private UIElement BuildConsolePage()
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _consoleOutputBox = new TextBox
        {
            Text = _backend is null ? string.Empty : string.Join(Environment.NewLine, _backend.ConsoleSnapshot()),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"), FontSize = 12, MinHeight = 500
        };
        root.Children.Add(_consoleOutputBox);
        var commandGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) }; commandGrid.ColumnDefinitions.Add(new ColumnDefinition()); commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var command = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        command.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { SendConsoleCommand(command); e.Handled = true; } };
        commandGrid.Children.Add(command);
        var send = ActionButton("Send", (_, _) => SendConsoleCommand(command), "GreenActionButton"); Grid.SetColumn(send, 1); commandGrid.Children.Add(send);
        Grid.SetRow(commandGrid, 1); root.Children.Add(commandGrid);
        Dispatcher.BeginInvoke(() => { _consoleOutputBox?.ScrollToEnd(); });
        return root;
    }

    private UIElement BuildWorldsPage()
    {
        if (!TrySelected(out var profile, out var unavailable)) return unavailable;
        var stack = new StackPanel(); var data = _backend!.Worlds.Scan(profile);
        var grid = DataGridFor(data, 360); stack.Children.Add(grid);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Refresh", (_, _) => Navigate("Worlds")));
        buttons.Children.Add(ActionButton("Open Folder", (_, _) => { if (grid.SelectedItem is WorldRecord w) OpenFolder(w.FolderPath); }));
        buttons.Children.Add(ActionButton("Rename", (_, _) => { if (grid.SelectedItem is WorldRecord w) { var n = Prompt("Rename World", "New world folder name:", w.Name); if (!string.IsNullOrWhiteSpace(n)) { _backend.Worlds.Rename(profile, w, n); Navigate("Worlds"); } } }));
        buttons.Children.Add(ActionButton("Backup World", async (_, _) => { if (grid.SelectedItem is WorldRecord w) { var path = Path.Combine(_backend.BackupRoot, profile.Name, $"{w.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await _backend.Worlds.CreateArchiveAsync(w, path); MessageBox.Show($"World backup created:\n{path}", "World Backup"); } }));
        buttons.Children.Add(ActionButton("Import ZIP", (_, _) => { var f = PickFile("ZIP archives (*.zip)|*.zip"); if (f is null) return; var n = Prompt("Import World", "World folder name:", Path.GetFileNameWithoutExtension(f)); if (!string.IsNullOrWhiteSpace(n)) { _backend.Worlds.ImportArchive(profile, f, n); Navigate("Worlds"); } }));
        buttons.Children.Add(ActionButton("Delete", (_, _) => { if (grid.SelectedItem is WorldRecord w && Confirm($"Delete world '{w.Name}' permanently?")) { _backend.Worlds.Delete(w); Navigate("Worlds"); } }, "DangerButton"));
        stack.Children.Add(buttons); return stack;
    }

    private UIElement BuildPlayersPage()
    {
        if (_backend is null) return MessagePage("Players", "Backend is not initialized.");
        var stack = new StackPanel();
        var info = Card(); info.Padding = new Thickness(12); info.Margin = new Thickness(0, 0, 0, 8); info.Child = new TextBlock { Text = _backend.IsRecoveredProcess ? "Recovered server: player commands are unavailable until MystTiq launches the server itself." : "Live player list. Refresh sends Minecraft's list command.", TextWrapping = TextWrapping.Wrap }; stack.Children.Add(info);
        _playersList = new ListBox { ItemsSource = _backend.OnlinePlayers.ToList(), MinHeight = 300 };
        stack.Children.Add(_playersList);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Refresh", (_, _) => _backend.RequestPlayers()));
        buttons.Children.Add(ActionButton("Make Admin", (_, _) => PlayerCommand("op")));
        buttons.Children.Add(ActionButton("Kick", (_, _) => PlayerCommand("kick"), "WarningButton"));
        buttons.Children.Add(ActionButton("Ban", (_, _) => PlayerCommand("ban"), "DangerButton"));
        stack.Children.Add(buttons);
        _backend.RequestPlayers();
        return stack;
    }

    /// <summary>
    /// One page for server add-ons: plugins on Paper/Purpur/Folia, mods on Fabric.
    /// Shows what's installed and a Modrinth search filtered to what this server can run.
    /// </summary>
    private UIElement BuildAddonsPage()
    {
        if (!TrySelected(out var profile, out var unavailable)) return unavailable;
        var kind = AddonKinds.For(profile);
        if (kind == AddonKind.None)
            return MessagePage("Add-ons", $"{profile.Type} servers can't load plugins or mods.\n\nTo use add-ons, create a Paper, Purpur, or Folia server (plugins) or a Fabric server (mods). If this server already is one of those, set its type in Settings.");

        var noun = AddonKinds.Noun(kind);
        var nouns = noun + "s";
        var root = new StackPanel();

        // Installed add-ons
        var installedCard = Card(); installedCard.Padding = new Thickness(12); installedCard.Margin = new Thickness(0, 0, 0, 8);
        var installed = new StackPanel(); installedCard.Child = installed;
        installed.Children.Add(TitleRow("plugins_nav", $"Installed {nouns} ({AddonKinds.FolderName(kind)} folder)"));
        var grid = new DataGrid { ItemsSource = _backend!.Plugins.List(profile), AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 160, CanUserAddRows = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding(nameof(PluginRecord.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new System.Windows.Data.Binding(nameof(PluginRecord.Version)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(PluginRecord.StatusText)), Width = DataGridLength.Auto });
        grid.Columns.Add(new DataGridTextColumn { Header = "Health", Binding = new System.Windows.Data.Binding(nameof(PluginRecord.HealthText)), Width = DataGridLength.Auto });
        grid.Columns.Add(new DataGridTextColumn { Header = "Needs", Binding = new System.Windows.Data.Binding(nameof(PluginRecord.Dependencies)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        installed.Children.Add(grid);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Install from File…", (_, _) => { var file = PickFile($"{char.ToUpper(noun[0]) + noun[1..]} JAR (*.jar)|*.jar"); if (file is null) return; try { _backend.Plugins.Install(profile, file, Confirm($"Overwrite an existing {noun} with the same filename if necessary?")); } catch (Exception ex) { ShowError($"Could not install {noun}", ex); } Navigate("Add-ons"); }));
        buttons.Children.Add(ActionButton("Enable / Disable", (_, _) => { if (grid.SelectedItem is PluginRecord p) { _backend.Plugins.SetEnabled(p, !p.IsEnabled); Navigate("Add-ons"); } }));
        buttons.Children.Add(ActionButton("Delete", (_, _) => { if (grid.SelectedItem is PluginRecord p && Confirm($"Delete {noun} '{p.Name}'?")) { _backend.Plugins.Delete(p); Navigate("Add-ons"); } }, "DangerButton"));
        buttons.Children.Add(ActionButton("Open Folder", (_, _) => { Directory.CreateDirectory(_backend.Plugins.GetPluginFolder(profile)); OpenFolder(_backend.Plugins.GetPluginFolder(profile)); }));
        installed.Children.Add(buttons);
        installed.Children.Add(Muted($"Changes take effect the next time the server starts."));
        root.Children.Add(installedCard);

        // Modrinth search
        var browseCard = Card(); browseCard.Padding = new Thickness(12);
        var browse = new StackPanel(); browseCard.Child = browse;
        browse.Children.Add(TitleRow("search", $"Find {nouns} on Modrinth"));
        browse.Children.Add(Muted($"Only server-side {nouns} for {profile.Type} {profile.Version} are shown. Every download is checked against Modrinth's SHA-512 hash."));
        if (kind == AddonKind.Mod)
        {
            var fabricApiInstalled = _backend.Plugins.List(profile).Any(p => p.FileName.StartsWith("fabric-api", StringComparison.OrdinalIgnoreCase));
            if (!fabricApiInstalled)
            {
                var hint = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                hint.Children.Add(new TextBlock { Text = "Most Fabric mods need Fabric API. ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
                hint.Children.Add(ActionButton("Install Fabric API", async (_, _) => await InstallAddonAsync(() => _backend.InstallAddonByIdAsync("fabric-api"), "Fabric API"), "GreenActionButton"));
                browse.Children.Add(hint);
            }
        }
        var searchRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var searchButton = ActionButton("Search", (_, _) => { }, "GreenActionButton");
        DockPanel.SetDock(searchButton, Dock.Right); searchRow.Children.Add(searchButton);
        var query = new TextBox { Margin = new Thickness(0, 0, 6, 6), ToolTip = $"Search by name, e.g. {(kind == AddonKind.Mod ? "lithium" : "luckperms")}" };
        searchRow.Children.Add(query);
        browse.Children.Add(searchRow);
        var results = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = 200, CanUserAddRows = false };
        results.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding(nameof(PluginCatalogItem.Name)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        results.Columns.Add(new DataGridTextColumn { Header = "Author", Binding = new System.Windows.Data.Binding(nameof(PluginCatalogItem.Author)), Width = DataGridLength.Auto });
        results.Columns.Add(new DataGridTextColumn { Header = "Downloads", Binding = new System.Windows.Data.Binding(nameof(PluginCatalogItem.DownloadsText)), Width = DataGridLength.Auto });
        results.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new System.Windows.Data.Binding(nameof(PluginCatalogItem.Description)), Width = new DataGridLength(2.5, DataGridLengthUnitType.Star) });
        browse.Children.Add(results);
        var status = Muted("Type a name and press Search. Leave it empty to see the most popular.");
        status.Margin = new Thickness(0, 6, 0, 0);
        browse.Children.Add(status);

        async Task RunSearchAsync()
        {
            try
            {
                searchButton.IsEnabled = false; status.Text = "Searching Modrinth…";
                var found = await _backend.SearchAddonsAsync(query.Text.Trim());
                results.ItemsSource = found;
                status.Text = found.Count == 0 ? $"No compatible {nouns} found." : $"{found.Count} result(s). Select one and press Install.";
            }
            catch (Exception ex) { status.Text = $"Search failed: {ex.Message}"; }
            finally { searchButton.IsEnabled = true; }
        }
        searchButton.Click += async (_, _) => await RunSearchAsync();
        query.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await RunSearchAsync(); };

        var install = ActionButton($"Install Selected {char.ToUpper(noun[0]) + noun[1..]}", async (_, _) =>
        {
            if (results.SelectedItem is not PluginCatalogItem item) { status.Text = $"Select a {noun} in the results first."; return; }
            await InstallAddonAsync(() => _backend.InstallAddonAsync(item), item.Name);
        }, "GreenActionButton");
        install.Margin = new Thickness(0, 8, 6, 0);
        browse.Children.Add(install);
        root.Children.Add(browseCard);
        return root;
    }

    private async Task InstallAddonAsync(Func<Task<PluginRecord>> install, string name)
    {
        try
        {
            PrototypeStatus.Text = $"Installing {name}…";
            var record = await install();
            var needs = record.Dependencies is { Length: > 0 } d && d != "—" ? $"\n\nIt needs: {d}. Install those too if they aren't already." : string.Empty;
            MessageBox.Show(this, $"Installed {record.Name} {record.Version}.{needs}\n\nRestart the server to load it.", "Add-on installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError($"Could not install {name}", ex); }
        Navigate("Add-ons");
    }

    private UIElement BuildDatapacksPage()
    {
        if (!TryGetPrimaryWorld(out var worldFolder, out var unavailable)) return unavailable;
        var stack = new StackPanel(); var packs = _backend!.Datapacks.Scan(worldFolder); var grid = DataGridFor(packs, 360); stack.Children.Add(grid);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Install Datapack…", (_, _) => { var f = PickFile("Datapack ZIP (*.zip)|*.zip"); if (f is null) return; _backend.Datapacks.Install(f, worldFolder); Navigate("Datapacks"); }, "GreenActionButton"));
        buttons.Children.Add(ActionButton("Enable / Disable", (_, _) => { if (grid.SelectedItem is DatapackRecord d) { _backend.Datapacks.SetEnabled(d, !d.Enabled); Navigate("Datapacks"); } }));
        buttons.Children.Add(ActionButton("Delete", (_, _) => { if (grid.SelectedItem is DatapackRecord d && Confirm($"Delete datapack '{d.Name}'?")) { _backend.Datapacks.Remove(d); Navigate("Datapacks"); } }, "DangerButton"));
        buttons.Children.Add(ActionButton("Open Datapacks Folder", (_, _) => OpenFolder(Path.Combine(worldFolder, "datapacks"))));
        stack.Children.Add(buttons); return stack;
    }

    private UIElement BuildResourcePacksPage()
    {
        if (!TrySelected(out var profile, out var unavailable)) return unavailable;
        var card = Card(); card.Padding = new Thickness(16); var stack = new StackPanel(); stack.Children.Add(TitleRow("resourcepacks", "Server Resource Pack"));
        var url = new TextBox { Text = ReadServerProperty("resource-pack"), Margin = new Thickness(0, 4, 0, 8), ToolTip = "Direct HTTP/HTTPS URL to the resource-pack ZIP" };
        var sha = new TextBox { Text = ReadServerProperty("resource-pack-sha1"), Margin = new Thickness(0, 4, 0, 8), ToolTip = "SHA-1 hash" };
        var required = new CheckBox { Content = "Require resource pack", IsChecked = string.Equals(ReadServerProperty("require-resource-pack"), "true", StringComparison.OrdinalIgnoreCase), Margin = new Thickness(0, 4, 0, 10) };
        stack.Children.Add(new TextBlock { Text = "Download URL" }); stack.Children.Add(url); stack.Children.Add(new TextBlock { Text = "SHA-1" }); stack.Children.Add(sha); stack.Children.Add(required);
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Validate Local ZIP…", (_, _) => { var f = PickFile("Resource-pack ZIP (*.zip)|*.zip"); if (f is null) return; var result = _backend!.ResourcePacks.Validate(f); sha.Text = result.Sha1; MessageBox.Show($"Valid resource pack.\nSHA-1: {result.Sha1}\nSize: {FormatBytes(result.SizeBytes)}", "Resource Pack"); }));
        buttons.Children.Add(ActionButton("Apply", (_, _) => { var errors = _backend!.ResourcePacks.ValidateDownloadSettings(url.Text, sha.Text); if (errors.Count > 0) { MessageBox.Show(string.Join(Environment.NewLine, errors), "Resource Pack", MessageBoxButton.OK, MessageBoxImage.Warning); return; } _backend.ResourcePacks.ApplyToProperties(Path.Combine(profile.Folder, "server.properties"), url.Text.Trim(), sha.Text.Trim(), required.IsChecked == true); MessageBox.Show("Resource-pack settings saved.", "Resource Pack"); }, "GreenActionButton"));
        buttons.Children.Add(ActionButton("Clear", (_, _) => { _backend!.ResourcePacks.ClearFromProperties(Path.Combine(profile.Folder, "server.properties")); Navigate("Resource Packs"); }, "WarningButton"));
        stack.Children.Add(buttons); card.Child = stack; return card;
    }

    private UIElement BuildTemplatesPage()
    {
        if (_backend is null) return MessagePage("Templates", "Backend is not initialized.");
        var templates = _backend.Templates.GetBuiltInTemplates(); var root = new StackPanel(); var grid = DataGridFor(templates, 300); root.Children.Add(grid);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 150, Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily("Consolas") }; root.Children.Add(preview);
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is ServerTemplate t) preview.Text = _backend.Templates.Preview(t); };
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Apply to Selected Server", (_, _) => { if (grid.SelectedItem is not ServerTemplate t) return; var profile = _backend.RequireSelected(); var errors = _backend.Templates.Validate(t); if (errors.Count > 0) { MessageBox.Show(string.Join(Environment.NewLine, errors)); return; } if (Confirm($"Apply template '{t.Name}' to {profile.Name}? This updates matching server.properties values.")) { _backend.Templates.Apply(t, profile.Folder); MessageBox.Show("Template applied."); } }, "GreenActionButton"));
        root.Children.Add(buttons); return root;
    }

    private UIElement BuildPluginPacksPage()
    {
        if (_backend is null) return MessagePage("Plugin Packs", "Backend is not initialized.");
        var packs = _backend.PluginPacks.GetBuiltInPacks(); var root = new StackPanel(); var grid = DataGridFor(packs, 280); root.Children.Add(grid);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 150, Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily("Consolas") }; root.Children.Add(preview);
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is PluginPack p) preview.Text = _backend.PluginPacks.Preview(p); };
        var note = Card(); note.Padding = new Thickness(12); note.Margin = new Thickness(0, 8, 0, 0); note.Child = new TextBlock { Text = "Plugin Packs are curated Modrinth project groups. MystTiq resolves a compatible release for the selected Minecraft version and installs each JAR into the server's plugins folder.", TextWrapping = TextWrapping.Wrap }; root.Children.Add(note);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(ActionButton("Install Selected Pack", async (_, _) => { if (grid.SelectedItem is not PluginPack pack) { MessageBox.Show("Select a plugin pack first."); return; } try { var result = await _backend.InstallPluginPackAsync(pack); var message = $"Installed {result.Installed.Count} plugin(s):\n" + string.Join("\n", result.Installed); if (result.Skipped.Count > 0) message += $"\n\nSkipped {result.Skipped.Count}:\n" + string.Join("\n", result.Skipped); MessageBox.Show(message, "Plugin Pack"); Navigate("Add-ons"); } catch (Exception ex) { ShowError("Plugin pack installation failed", ex); } }, "GreenActionButton"));
        actions.Children.Add(ActionButton("Open Add-ons", (_, _) => Navigate("Add-ons")));
        root.Children.Add(actions);
        return root;
    }

    private UIElement BuildPerformancePage()
    {
        if (_backend is null) return MessagePage("Performance", "Backend is not initialized.");
        var root = new StackPanel(); var metrics = new UniformGrid { Columns = 4 };
        var memory = GetMemoryMb();
        metrics.Children.Add(Metric("cpu", "CPU", _backend.IsRunning ? $"{_lastCpuPercent:N1}%" : "—", "Managed process", _lastCpuPercent));
        metrics.Children.Add(Metric("memory", "WORKING SET", _backend.IsRunning ? $"{memory:N0} MB" : "—", "Java memory in RAM", 0));
        metrics.Children.Add(Metric("clock", "UPTIME", _backend.IsRunning ? FormatDuration(_backend.Uptime) : "—", _backend.IsRecoveredProcess ? "Recovered" : "Managed", _backend.IsRunning ? 100 : 0));
        metrics.Children.Add(Metric("plugins_nav", "PLUGINS", _backend.SelectedServer is null ? "—" : Safe(() => _backend.Plugins.List(_backend.SelectedServer).Count, 0).ToString(), _backend.SelectedServer?.Type ?? "", 0));
        root.Children.Add(metrics);
        var info = Card(); info.Padding = new Thickness(14); info.Margin = new Thickness(0, 8, 0, 0); info.Child = new TextBlock { Text = "MystTiq reports process CPU, working set, uptime, and local configuration. Authoritative TPS/MSPT requires server-side telemetry such as Spark or Paper timings; MystTiq does not invent those values.", TextWrapping = TextWrapping.Wrap }; root.Children.Add(info);
        return root;
    }

    private UIElement BuildHealthPage()
    {
        if (!TrySelected(out _, out var unavailable)) return unavailable;
        var root = new StackPanel(); var status = new TextBlock { Text = "Run the analyzer to score installation, Java, EULA, memory, backups, and plugin platform.", Margin = new Thickness(0, 0, 0, 8) }; root.Children.Add(status);
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, MinHeight = 350 }; root.Children.Add(grid);
        root.Children.Add(ActionButton("Analyze Server", async (_, _) => { try { status.Text = "Analyzing…"; var report = await _backend!.AnalyzeHealthAsync(); grid.ItemsSource = report.Checks; status.Text = $"Health score: {report.Score}/100 — {report.Grade}"; } catch (Exception ex) { ShowError("Health analysis failed", ex); } }, "GreenActionButton"));
        return root;
    }

    private UIElement BuildLogAnalyzerPage()
    {
        if (_backend is null) return MessagePage("Log Analyzer", "Backend is not initialized.");
        var view = new LogAnalyzerView(); view.Bind(_backend.SelectedServer); return view;
    }

    private UIElement BuildStartupAnalyzerPage()
    {
        if (_backend is null) return MessagePage("Startup Analyzer", "Backend is not initialized.");
        var view = new StartupAnalyzerView(); view.Bind(_backend.SelectedServer); return view;
    }

    private UIElement BuildOptimizationPage()
    {
        if (_backend is null) return MessagePage("Optimization", "Backend is not initialized.");
        var view = new OptimizationView(); view.Bind(_backend.SelectedServer); return view;
    }

    private UIElement BuildSchedulerPage()
    {
        if (_backend is null) return MessagePage("Scheduler", "Backend is not initialized.");
        var root = new StackPanel(); var tasks = new ObservableCollection<ScheduledTaskRecord>(_backend.ScheduledTasks.Load()); var grid = DataGridFor(tasks, 360); root.Children.Add(grid);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Add Task", (_, _) => { var server = _backend.SelectedServer; if (server is null) { MessageBox.Show("Select a server first."); return; } var task = new ScheduledTaskRecord { ServerFolder = server.Folder, ServerName = server.Name, Name = "Scheduled broadcast", Action = "Broadcast", Payload = "Server message", IntervalMinutes = 60 }; tasks.Add(task); _backend.ScheduledTasks.Save(tasks); }));
        buttons.Children.Add(ActionButton("Enable / Disable", (_, _) => { if (grid.SelectedItem is ScheduledTaskRecord t) { t.Enabled = !t.Enabled; _backend.ScheduledTasks.Save(tasks); grid.Items.Refresh(); } }));
        buttons.Children.Add(ActionButton("Delete", (_, _) => { if (grid.SelectedItem is ScheduledTaskRecord t && Confirm($"Delete scheduled task '{t.Name}'?")) { tasks.Remove(t); _backend.ScheduledTasks.Save(tasks); } }, "DangerButton"));
        buttons.Children.Add(ActionButton("Run Now", (_, _) => { if (grid.SelectedItem is not ScheduledTaskRecord t) return; RunScheduledTaskNow(t); _backend.ScheduledTasks.Save(tasks); grid.Items.Refresh(); }));
        root.Children.Add(buttons); return root;
    }

    private UIElement BuildBackupsPage()
    {
        if (!TrySelected(out var profile, out var unavailable)) return unavailable;
        var root = new StackPanel(); var backups = new ObservableCollection<BackupRecord>(_backend!.Backups.List(profile)); var grid = DataGridFor(backups, 360); root.Children.Add(grid);
        var settings = Card(); settings.Padding = new Thickness(12); settings.Margin = new Thickness(0, 8, 0, 0);
        var settingsRow = new WrapPanel(); var auto = new CheckBox { Content = "Automatic backups", IsChecked = profile.AutomaticBackupsEnabled, Margin = new Thickness(0, 6, 15, 0) }; var interval = new TextBox { Text = profile.BackupIntervalMinutes.ToString(), Width = 70, Margin = new Thickness(0, 0, 8, 0) }; var retention = new TextBox { Text = profile.BackupRetentionCount.ToString(), Width = 70, Margin = new Thickness(0, 0, 8, 0) }; settingsRow.Children.Add(auto); settingsRow.Children.Add(new TextBlock { Text = "Interval (min)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }); settingsRow.Children.Add(interval); settingsRow.Children.Add(new TextBlock { Text = "Keep", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) }); settingsRow.Children.Add(retention); settingsRow.Children.Add(ActionButton("Save", (_, _) => { profile.AutomaticBackupsEnabled = auto.IsChecked == true; if (int.TryParse(interval.Text, out var i)) profile.BackupIntervalMinutes = Math.Max(5, i); if (int.TryParse(retention.Text, out var r)) profile.BackupRetentionCount = Math.Max(1, r); _backend.SaveProfiles(); })); settings.Child = settingsRow; root.Children.Add(settings);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ActionButton("Backup Now", async (_, _) => { try { await _backend.CreateBackupAsync(); Navigate("Auto Backups"); } catch (Exception ex) { ShowError("Backup failed", ex); } }, "GreenActionButton"));
        buttons.Children.Add(ActionButton("Restore", async (_, _) => { if (grid.SelectedItem is not BackupRecord b) return; if (_backend.IsServerRunning(profile)) { MessageBox.Show("Stop the selected server before restoring a backup."); return; } if (!Confirm($"Restore '{b.Name}'? Existing server files will be replaced.")) return; await _backend.Backups.RestoreAsync(profile, b); MessageBox.Show("Backup restored."); }));
        buttons.Children.Add(ActionButton("Delete", (_, _) => { if (grid.SelectedItem is BackupRecord b && Confirm($"Delete backup '{b.Name}'?")) { _backend.Backups.Delete(b); backups.Remove(b); } }, "DangerButton"));
        buttons.Children.Add(ActionButton("Open Backup Folder", (_, _) => OpenFolder(_backend.Backups.GetServerBackupFolder(profile))));
        root.Children.Add(buttons); return root;
    }

    private UIElement BuildSettingsPage()
    {
        if (!TrySelected(out var profile, out var unavailable)) return unavailable;
        var root = new StackPanel();
        var card = Card(); card.Padding = new Thickness(16); var stack = new StackPanel(); stack.Children.Add(TitleRow("settings_nav", "Server Profile"));
        var name = new TextBox { Text = profile.Name, Margin = new Thickness(0, 3, 0, 8) }; var memory = new TextBox { Text = profile.MemoryGb.ToString(), Margin = new Thickness(0, 3, 0, 8) }; var java = new TextBox { Text = profile.JavaPath, Margin = new Thickness(0, 3, 0, 8) }; var jar = new TextBox { Text = profile.Jar, Margin = new Thickness(0, 3, 0, 8) };
        // Type and version drive add-on compatibility and the Java check; imported servers only have a guess.
        var serverType = new ComboBox { IsEditable = true, ItemsSource = new[] { "Vanilla", "Paper", "Purpur", "Folia", "Fabric", "Custom JAR" }, Text = profile.Type, Margin = new Thickness(0, 3, 0, 8) };
        var mcVersion = new TextBox { Text = profile.Version.Equals("Detected", StringComparison.OrdinalIgnoreCase) ? string.Empty : profile.Version, Margin = new Thickness(0, 3, 0, 8), ToolTip = "The Minecraft version this server runs, e.g. 1.21.4" };
        stack.Children.Add(new TextBlock { Text = "Name" }); stack.Children.Add(name);
        stack.Children.Add(new TextBlock { Text = "Server type" }); stack.Children.Add(serverType);
        stack.Children.Add(new TextBlock { Text = "Minecraft version" }); stack.Children.Add(mcVersion); stack.Children.Add(new TextBlock { Text = "Memory (GB)" }); stack.Children.Add(memory); stack.Children.Add(new TextBlock { Text = "Java executable" }); stack.Children.Add(java); stack.Children.Add(new TextBlock { Text = "Server JAR" }); stack.Children.Add(jar);
        var buttons = new WrapPanel(); buttons.Children.Add(ActionButton("Browse Java…", (_, _) => { var f = PickFile("Java executable (java.exe)|java.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*"); if (f is not null) java.Text = f; })); buttons.Children.Add(ActionButton("Save Profile", (_, _) => { if (!int.TryParse(memory.Text, out var gb)) gb = profile.MemoryGb; _backend!.UpdateSelectedProfile(name.Text, gb, java.Text, jar.Text, serverType.Text, mcVersion.Text); Navigate("Settings"); }, "GreenActionButton")); buttons.Children.Add(ActionButton("Open Server Folder", (_, _) => OpenFolder(profile.Folder))); stack.Children.Add(buttons); card.Child = stack; root.Children.Add(card);
        var config = new ConfigurationView(); config.ResetForSelection(profile); config.ConfigurationSaved += (_, _) => _backend!.SaveProfiles(); root.Children.Add(config);
        return root;
    }

    private UIElement BuildHelpPage()
    {
        var root = new StackPanel();
        root.Children.Add(TextCard("about", $"MystTiq Minecraft Server Manager v{AppVersion}", ["A simple Windows app for creating and running several Minecraft servers side by side.", "Source, releases, and release notes: github.com/Wad3M/MystTiq-MineCraft-Server-Manager"]));
        root.Children.Add(TextCard("help", "Getting Started", ["1. Create or import a server profile.", "2. Select the server in the header.", "3. Verify Java and the configured server JAR in Settings.", "4. Start the server and use Console for live output.", "5. Configure automatic backups before major changes."]));
        root.Children.Add(TextCard("health", "Troubleshooting", ["Startup problems: open Health, then the Logs and Startup tabs.", "Java errors: verify the Java executable and Minecraft version requirements.", "Recovered process: MystTiq can monitor/kill it but cannot reconnect stdin/stdout.", "TPS/MSPT: use Spark or Paper timings; MystTiq intentionally does not fabricate these metrics."]));
        return root;
    }

    private async void ToolbarAction_Click(object sender, RoutedEventArgs e)
    {
        if (_backend is null || sender is not Button button) return;
        var action = button.Tag?.ToString() ?? string.Empty;
        try
        {
            switch (action)
            {
                case "Start": if (_backend.SelectedServer is { } toStart && await ConfirmJavaAsync(toStart)) await _backend.StartAsync(); break;
                case "Stop":
                    if (!await _backend.StopGracefullyAsync(TimeSpan.FromSeconds(20)))
                        MessageBox.Show("The server did not stop within 20 seconds. Use Kill only if you are certain the server is stuck.", "Stop timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                case "Restart": await _backend.RestartAsync(TimeSpan.FromSeconds(20)); break;
                case "Kill":
                    if (Confirm("Force kill the Minecraft server?\n\nUnsaved world data may be lost.")) await _backend.ForceKillAsync();
                    break;
                case "Backup": await _backend.CreateBackupAsync(); break;
                case "Console": Navigate("Console"); break;
                case "File Manager": if (_backend.SelectedServer is { } p) OpenFolder(p.Folder); break;
                case "Settings": Navigate("Settings"); break;
            }
        }
        catch (TimeoutException ex) { MessageBox.Show(ex.Message, action, MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception ex) { ShowError($"{action} failed", ex); }
        RefreshShellStatus();
        if (_currentPage == "Dashboard") PageContent.Content = BuildDashboard();
    }

    private void ServerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingServerPicker || _backend is null) return;
        try
        {
            _backend.SelectServer(ServerPicker.SelectedItem as ServerProfile);
            // CPU is sampled per process; start a fresh sample for the newly selected server.
            _lastCpuSampleAt = null; _lastCpuTime = TimeSpan.Zero; _lastCpuPercent = 0;
            Navigate(_currentPage);
        }
        catch (Exception ex) { ShowError("Could not select server", ex); }
    }

    private void Backend_StateChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        RefreshShellStatus();
        if (_currentPage == "Dashboard") PageContent.Content = BuildDashboard();
    });

    private void Backend_ConsoleLineReceived(object? sender, string line) => Dispatcher.BeginInvoke(() =>
    {
        if (_consoleOutputBox is not null)
        {
            _consoleOutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            _consoleOutputBox.ScrollToEnd();
        }
    });

    private void Backend_PlayersChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_playersList is not null) _playersList.ItemsSource = _backend?.OnlinePlayers.ToList() ?? new List<string>();
        if (_currentPage == "Dashboard") PageContent.Content = BuildDashboard();
    });

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        UpdateCpuSample();
        RefreshShellStatus();
        // Rebuilding the page replaces its buttons; skip a tick while a click is in progress
        // so the press and release land on the same button.
        var clicking = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
        if (!clicking && _currentPage == "Dashboard") PageContent.Content = BuildDashboard();
        else if (!clicking && _activeTab == "Performance" && _activeTabItem is not null) _activeTabItem.Content = BuildPerformancePage();

        if (!_maintenanceBusy && _backend is not null && DateTimeOffset.Now - _lastMaintenanceCheck >= TimeSpan.FromMinutes(1))
        {
            _maintenanceBusy = true;
            _lastMaintenanceCheck = DateTimeOffset.Now;
            try { await _backend.RunDueMaintenanceAsync(); }
            catch (Exception ex) { App.WriteStartupLog("Background maintenance failed", ex); }
            finally { _maintenanceBusy = false; }
        }
    }

    private void UpdateCpuSample()
    {
        if (_backend is null || !_backend.TryGetPerformanceSnapshot(out var cpu, out _))
        {
            _lastCpuSampleAt = null; _lastCpuTime = TimeSpan.Zero; _lastCpuPercent = 0; return;
        }
        var now = DateTimeOffset.Now;
        if (_lastCpuSampleAt is { } previous)
        {
            var wallMs = (now - previous).TotalMilliseconds;
            var cpuMs = (cpu - _lastCpuTime).TotalMilliseconds;
            if (wallMs > 0) _lastCpuPercent = Math.Clamp(cpuMs / (wallMs * Environment.ProcessorCount) * 100d, 0, 100);
        }
        _lastCpuSampleAt = now; _lastCpuTime = cpu;
    }

    private void RefreshShellStatus()
    {
        if (_backend is null) return;
        var runningCount = _backend.RunningServers.Count;
        ServerStatusText.Text = _backend.IsRunning ? (_backend.IsRecoveredProcess ? "Recovered" : "Running") : "Stopped";
        ServerDetailText.Text = _backend.SelectedServer is null ? "No server selected" : $"Selected: {_backend.SelectedServer.Name}";
        ActiveServerFooterText.Text = _backend.SelectedServer is null ? "Selected Server: None" : $"Selected Server: {_backend.SelectedServer.Name}";
        ServerVersionFooterText.Text = runningCount > 0
            ? $"{SelectedServerSummary()}   •   {runningCount} of {_backend.Servers.Count} servers running"
            : SelectedServerSummary();

        if (_toolbarButtons.TryGetValue("Start", out var start)) start.IsEnabled = _backend.SelectedServer is not null && !_backend.IsRunning;
        if (_toolbarButtons.TryGetValue("Stop", out var stop)) stop.IsEnabled = _backend.IsRunning && _backend.CanSendCommands;
        if (_toolbarButtons.TryGetValue("Restart", out var restart)) restart.IsEnabled = _backend.IsRunning && _backend.CanSendCommands;
        if (_toolbarButtons.TryGetValue("Kill", out var kill)) kill.IsEnabled = _backend.IsRunning;
        if (_toolbarButtons.TryGetValue("Backup", out var backup)) backup.IsEnabled = _backend.SelectedServer is not null;
        if (_toolbarButtons.TryGetValue("File Manager", out var folder)) folder.IsEnabled = _backend.SelectedServer is not null;

        _updatingServerPicker = true;
        if (!ReferenceEquals(ServerPicker.SelectedItem, _backend.SelectedServer)) ServerPicker.SelectedItem = _backend.SelectedServer;
        _updatingServerPicker = false;
    }

    private string SelectedServerSummary()
    {
        if (_backend?.SelectedServer is not { } p) return "Server details unavailable";
        return $"{p.Type} {p.Version}   •   Port {p.Port}   •   {p.MemoryGb} GB";
    }

    private string RunningStateText()
    {
        if (_backend is null || !_backend.IsRunning) return "STOPPED";
        return _backend.IsRecoveredProcess ? "RECOVERED / LIMITED CONTROL" : "RUNNING";
    }

    private bool TrySelected(out ServerProfile profile, out UIElement unavailable)
    {
        if (_backend?.SelectedServer is { } selected) { profile = selected; unavailable = null!; return true; }
        profile = null!; unavailable = MessagePage(PageTitle.Text, "Select or create a server first."); return false;
    }

    private bool TryGetPrimaryWorld(out string worldFolder, out UIElement unavailable)
    {
        if (!TrySelected(out var profile, out unavailable)) { worldFolder = string.Empty; return false; }
        var worlds = _backend!.Worlds.Scan(profile);
        var primary = worlds.FirstOrDefault(w => w.IsPrimary) ?? worlds.FirstOrDefault();
        if (primary is null) { worldFolder = string.Empty; unavailable = MessagePage("Datapacks", "No Minecraft world containing level.dat was found for the selected server."); return false; }
        worldFolder = primary.FolderPath; return true;
    }

    private void ImportExistingServer()
    {
        if (_backend is null) return;
        var folder = PickFolder(@"C:\GameServers"); if (folder is null) return;
        try { var profile = _backend.AddExistingServer(folder); ServerPicker.SelectedItem = profile; Navigate("Dashboard"); }
        catch (Exception ex) { ShowError("Could not import server", ex); }
    }

    private void SendConsoleCommand(TextBox command)
    {
        if (_backend is null || string.IsNullOrWhiteSpace(command.Text)) return;
        try { _backend.SendCommand(command.Text); command.Clear(); }
        catch (Exception ex) { ShowError("Command failed", ex); }
    }

    private void PlayerCommand(string command)
    {
        if (_backend is null || _playersList?.SelectedItem is not string player) { MessageBox.Show("Select a player first."); return; }
        try
        {
            if (command is "kick" or "ban" && !Confirm($"{command.ToUpperInvariant()} {player}?")) return;
            _backend.SendCommand($"{command} {player}");
            _backend.RequestPlayers();
        }
        catch (Exception ex) { ShowError("Player action failed", ex); }
    }

    private void RunScheduledTaskNow(ScheduledTaskRecord task)
    {
        if (_backend is null) return;
        try
        {
            switch (task.Action.ToLowerInvariant())
            {
                case "broadcast": _backend.SendCommand("say " + task.Payload); break;
                case "command": _backend.SendCommand(task.Payload); break;
                case "backup": _ = _backend.CreateBackupAsync(); break;
                case "stop": _ = _backend.StopGracefullyAsync(TimeSpan.FromSeconds(20)); break;
                case "start": _ = _backend.StartAsync(); break;
                case "restart": _ = _backend.RestartAsync(TimeSpan.FromSeconds(20)); break;
                default: throw new InvalidOperationException($"Unsupported task action: {task.Action}");
            }
            task.LastRunAt = DateTimeOffset.Now; task.LastResult = "Run requested";
        }
        catch (Exception ex) { task.LastRunAt = DateTimeOffset.Now; task.LastResult = ex.Message; }
    }

    private string ReadServerProperty(string key)
    {
        if (_backend?.SelectedServer is not { } p) return string.Empty;
        var path = Path.Combine(p.Folder, "server.properties");
        if (!File.Exists(path)) return string.Empty;
        try { return File.ReadLines(path).Select(x => x.Split('=', 2)).FirstOrDefault(x => x.Length == 2 && x[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))?.ElementAtOrDefault(1)?.Trim() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private int ReadServerPropertyInt(string key, int fallback) => int.TryParse(ReadServerProperty(key), out var value) ? value : fallback;

    private double GetMemoryMb()
    {
        if (_backend is null || !_backend.TryGetPerformanceSnapshot(out _, out var bytes)) return 0;
        return bytes / 1024d / 1024d;
    }

    private static T Safe<T>(Func<T> action, T fallback) { try { return action(); } catch { return fallback; } }

    private static string FormatDuration(TimeSpan value) => value.TotalDays >= 1 ? $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m" : value.TotalHours >= 1 ? $"{value.Hours}h {value.Minutes}m {value.Seconds}s" : $"{value.Minutes}m {value.Seconds}s";
    private static string FormatBytes(long bytes) { string[] u = ["B", "KB", "MB", "GB", "TB"]; double v = bytes; var i = 0; while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; } return $"{v:0.##} {u[i]}"; }

    private static string SubtitleFor(string page) => page switch
    {
        "Dashboard" => "All your servers at a glance",
        "Create Server" => "Install a new server or add an existing one",
        "Console" => "Live output and commands for the selected server",
        "Players" => "Who's online, with op, kick, and ban",
        "Worlds" => "Import, rename, archive, and delete worlds",
        "Add-ons" => "Plugins or mods, plugin packs, datapacks, and the resource pack",
        "Backups" => "Manual and automatic backups, and scheduled tasks",
        "Health" => "Health checks, performance, logs, startup, and optimization",
        "Settings" => "Server profile, server.properties, and presets",
        "Help" => "Getting started and troubleshooting",
        _ => page
    };

    private Border Metric(string icon, string title, string value, string subtitle, double progress)
    {
        var card = Card(); card.Margin = new Thickness(0, 0, 6, 0); card.Padding = new Thickness(10);
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.SemiBold });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 3) }; row.Children.Add(CreateIcon(icon, 30, new Thickness(0, 0, 8, 0))); row.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); stack.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(subtitle)) stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 9, Foreground = (Brush)FindResource("AccentBrightBrush") });
        stack.Children.Add(new ProgressBar { Value = Math.Clamp(progress, 0, 100), Height = 7, Margin = new Thickness(0, 5, 0, 0) }); card.Child = stack; return card;
    }

    private Border TextCard(string icon, string title, IEnumerable<string> lines)
    {
        var card = Card(); card.Margin = new Thickness(0, 0, 7, 8); card.Padding = new Thickness(10); var stack = new StackPanel(); stack.Children.Add(TitleRow(icon, title)); foreach (var line in lines) stack.Children.Add(new TextBlock { Text = line, FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap }); card.Child = stack; return card;
    }

    private UIElement TitleRow(string icon, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) }; row.Children.Add(CreateIcon(icon, 18, new Thickness(0, 0, 6, 0))); row.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }); return row;
    }

    private TextBlock InfoLine(string label, string value) => new() { Text = $"{label}:  {value}", FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value };
    private TextBlock Muted(string text) { var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; t.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush"); return t; }
    private Border Card() => new() { Style = (Style)FindResource("Card") };

    private FrameworkElement CreateIcon(string name, double size, Thickness? margin = null)
    {
        var icon = new Image { Source = IconSource(name), Width = size + 4, Height = size + 4, Margin = margin ?? new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true, UseLayoutRounding = true };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        return icon;
    }

    /// <summary>Loads (once) the icon image for a UI icon name. Unknown names fall back to the server icon.</summary>
    private static BitmapImage IconSource(string name)
    {
        var file = IconFiles.TryGetValue(name, out var mapped) ? mapped : "server";
        if (IconCache.TryGetValue(file, out var cached)) return cached;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Icons/{file}.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        IconCache[file] = image;
        return image;
    }

    private Button ActionButton(string text, RoutedEventHandler handler, string style = "SmallButton")
    {
        Style buttonStyle;
        try { buttonStyle = (Style)FindResource(style); } catch { buttonStyle = (Style)FindResource("SmallButton"); }
        var button = new Button { Content = text, Style = buttonStyle, Margin = new Thickness(0, 0, 6, 6) }; button.Click += handler; return button;
    }

    private DataGrid DataGridFor(object items, double minHeight)
    {
        return new DataGrid { ItemsSource = items as System.Collections.IEnumerable, AutoGenerateColumns = true, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, MinHeight = minHeight, CanUserAddRows = false };
    }

    private UIElement MessagePage(string title, string message)
    {
        var card = Card(); card.Padding = new Thickness(24); var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }); stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 }); card.Child = stack; return card;
    }

    private static void AddFormRow(Grid grid, int row, string label, FrameworkElement control)
    {
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) }; Grid.SetRow(l, row); grid.Children.Add(l); Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
    }

    private string? PickFolder(string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = "Select Folder", Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private string? PickFile(string filter, string? initialFolder = null)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OpenFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Could not open folder", ex); }
    }

    private bool Confirm(string message) => MessageBox.Show(this, message, "MystTiq", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private string? Prompt(string title, string label, string initial)
    {
        var window = new Window { Title = title, Owner = this, Width = 430, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = (Brush)FindResource("AppBackground"), Foreground = (Brush)FindResource("TextBrush") };
        var root = new StackPanel { Margin = new Thickness(16) }; root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) }); var box = new TextBox { Text = initial }; root.Children.Add(box); var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true }; var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true }; string? result = null; ok.Click += (_, _) => { result = box.Text; window.DialogResult = true; }; buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons); window.Content = root; window.ShowDialog(); return result;
    }

    private void ShowError(string title, Exception ex)
    {
        App.WriteStartupLog(title, ex);
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UpdateNavigationSelection(string page) => UpdateNavigationSelectionRecursive(this, page);
    private static void UpdateNavigationSelectionRecursive(DependencyObject root, string page)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && button.Tag is string tag)
            {
                if (string.Equals(tag, page, StringComparison.OrdinalIgnoreCase)) button.SetResourceReference(Control.BackgroundProperty, "ButtonHoverBrush");
                else button.ClearValue(Control.BackgroundProperty);
            }
            UpdateNavigationSelectionRecursive(child, page);
        }
    }

    /// <summary>
    /// Collapses the sidebar to an icon rail: every page keeps its icon (centred, with the
    /// page name as a tooltip) and only the labels and section titles are hidden.
    /// </summary>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        var collapsed = _sidebarCollapsed;
        var labelVisibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        var alignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;

        // 36px icon + button padding/margins + 24px toggle strip.
        SidebarColumn.Width = new GridLength(collapsed ? 96 : 225);

        foreach (var (text, icon) in new[] { (DashboardNavText, DashboardNavIcon), (CreateNavText, CreateNavIcon), (ConsoleNavText, ConsoleNavIcon) })
        {
            text.Visibility = labelVisibility;
            icon.Margin = collapsed ? new Thickness(0) : new Thickness(0, 0, 8, 0);
            if (icon.Parent is FrameworkElement row && row.Parent is Button button)
            {
                button.HorizontalContentAlignment = alignment;
                button.ToolTip = text.Text;
            }
        }

        foreach (var (button, icon, label) in _navItems)
        {
            label.Visibility = labelVisibility;
            icon.Margin = collapsed ? new Thickness(0) : new Thickness(0, 0, 10, 0);
            button.HorizontalContentAlignment = alignment;
        }

        // Section titles don't fit in the rail; keep the sections open so every icon stays reachable.
        foreach (var (section, title) in _navSections)
        {
            section.Header = collapsed ? null : title;
            if (collapsed) section.IsExpanded = true;
        }

        SidebarToggleArrow.Data = Geometry.Parse(collapsed ? "M0,0 L10,9 L0,18 Z" : "M10,0 L0,9 L10,18 Z");
        SidebarToggleButton.ToolTip = collapsed ? "Expand sidebar" : "Collapse sidebar";
    }

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemePicker.SelectedItem is not ComboBoxItem item) return;
        ApplyTheme(item.Tag?.ToString() ?? "Minecraft", true);
    }

    private void ApplyTheme(string theme, bool save)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var themeFiles = new[] { "Minecraft.xaml", "ModernDark.xaml", "Light.xaml" };
        var old = dictionaries.FirstOrDefault(d => d.Source is not null && themeFiles.Any(file => d.Source.OriginalString.EndsWith($"Themes/{file}", StringComparison.OrdinalIgnoreCase)));
        if (old is not null) dictionaries.Remove(old);
        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) });
        PrototypeStatus.Text = $"Theme changed to {theme}";
        if (save) { try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, ThemeFileName), theme); } catch { } }
    }

    private void RestoreTheme()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ThemeFileName); if (!File.Exists(path)) return;
            var saved = File.ReadAllText(path).Trim();
            foreach (ComboBoxItem item in ThemePicker.Items) if (string.Equals(item.Tag?.ToString(), saved, StringComparison.OrdinalIgnoreCase)) { ThemePicker.SelectedItem = item; ApplyTheme(saved, false); break; }
        }
        catch { }
    }
}
