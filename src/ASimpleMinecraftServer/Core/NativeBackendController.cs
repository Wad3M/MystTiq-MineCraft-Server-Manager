using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

public sealed class NativeBackendController : IDisposable
{
    private readonly ProfileStore _profileStore;
    private readonly RuntimeSessionStore _runtimeSessions;
    private readonly ServerHealthAnalyzer _healthAnalyzer = new();
    private readonly ModrinthPluginCatalogService _pluginCatalog = new();
    private readonly DownloadService _downloads = new();
    private readonly VersionManager _versions;
    private readonly ServerInstaller _serverInstaller;
    private readonly object _consoleLock = new();
    private readonly List<string> _consoleLines = new();
    private DateTimeOffset? _startedAt;
    private ServerProfile? _selectedServer;
    private ServerProfile? _runningServer;

    private readonly List<string> _onlinePlayers = new();
    public ObservableCollection<ServerProfile> Servers { get; }
    public IReadOnlyList<string> OnlinePlayers { get { lock (_onlinePlayers) return _onlinePlayers.ToArray(); } }
    public ServerLauncher Launcher { get; } = new();
    public BackupService Backups { get; }
    public WorldService Worlds { get; } = new();
    public PluginService Plugins { get; } = new();
    public DatapackService Datapacks { get; } = new();
    public ResourcePackService ResourcePacks { get; } = new();
    public ServerTemplateService Templates { get; } = new();
    public PluginPackService PluginPacks { get; } = new();
    public MigrationService Migration { get; } = new();
    public ScheduledTaskStore ScheduledTasks { get; } = new();

    public ServerProfile? SelectedServer => _selectedServer;
    public ServerProfile? RunningServer => _runningServer;
    public bool IsRunning => Launcher.IsRunning;
    public bool IsRecoveredProcess => Launcher.IsAttachedProcess;
    public bool CanSendCommands => Launcher.CanSendCommands;
    public DateTimeOffset? StartedAt => _startedAt;
    public TimeSpan Uptime => _startedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - _startedAt.Value;
    public string DataDirectory { get; }
    public string BackupRoot => Backups.BackupRoot;
    public IReadOnlyList<string> SupportedServerTypes => VersionManager.SupportedServerTypes;

    public event EventHandler? StateChanged;
    public event EventHandler<string>? ConsoleLineReceived;
    public event EventHandler? PlayersChanged;

    public NativeBackendController()
    {
        _versions = new VersionManager(_downloads);
        _serverInstaller = new ServerInstaller(_downloads, _versions);

        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASimpleMinecraftServer");
        Directory.CreateDirectory(DataDirectory);
        _profileStore = new ProfileStore(DataDirectory);
        _runtimeSessions = new RuntimeSessionStore(DataDirectory);
        Servers = _profileStore.Load();
        foreach (var server in Servers) server.RuntimeState = "Stopped";

        var preferredRoot = @"C:\GameServers\_Backups";
        string backupRoot;
        try { Directory.CreateDirectory(preferredRoot); backupRoot = preferredRoot; }
        catch { backupRoot = Path.Combine(DataDirectory, "Backups"); }
        Backups = new BackupService(backupRoot);

        Launcher.OutputReceived += Launcher_OutputReceived;
        Launcher.Exited += Launcher_Exited;

        RecoverPreviousSession();
        _selectedServer = _runningServer ?? Servers.FirstOrDefault();
    }

    private void RecoverPreviousSession()
    {
        var session = _runtimeSessions.Load();
        if (session is null) return;
        var profile = Servers.FirstOrDefault(s => PathsEqual(s.Folder, session.ServerFolder));
        if (profile is null || !Launcher.TryAttach(session.ProcessId))
        {
            _runtimeSessions.Clear();
            return;
        }
        _runningServer = profile;
        _startedAt = session.StartedAt;
        profile.RuntimeState = "Recovered";
        AddConsole("[Manager] Reconnected to the existing Java server process. Console input is unavailable for recovered processes.");
    }

    public void SelectServer(ServerProfile? profile)
    {
        if (profile is not null && !Servers.Contains(profile)) throw new InvalidOperationException("The selected server profile is not loaded.");
        _selectedServer = profile;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveProfiles() => _profileStore.Save(Servers);


    public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(string serverType, CancellationToken cancellationToken = default) =>
        _versions.GetVersionsAsync(serverType, cancellationToken);

    public async Task<ServerProfile> InstallServerAsync(
        string name,
        string folder,
        string type,
        string version,
        int memoryGb,
        string javaPath,
        string? customJarPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a server name.");
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose an install folder.");
        if (Servers.Any(server => string.Equals(server.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A MystMC server profile with this name already exists.");
        if (Servers.Any(server => PathsEqual(server.Folder, folder)))
            throw new InvalidOperationException("A MystMC server profile already uses this folder.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("The generated server folder already exists and is not empty. Choose a different server name/install root, or use Add Existing Server.");

        var request = new ServerInstallRequest(
            name.Trim(),
            folder.Trim(),
            string.IsNullOrWhiteSpace(type) ? "Vanilla" : type.Trim(),
            version?.Trim() ?? string.Empty,
            Math.Max(1, memoryGb),
            customJarPath);

        AddConsole($"[Manager] Installing {request.Type} for Minecraft {request.Version} into {request.Folder}...");
        var profile = await _serverInstaller.InstallAsync(request, cancellationToken);
        profile.JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim();
        profile.RefreshDerivedProperties();
        Servers.Add(profile);
        SaveProfiles();
        SelectServer(profile);
        AddConsole($"[Manager] Installed {profile.Type} {profile.Version} as '{profile.Name}'.");
        return profile;
    }

    public ServerProfile AddExistingServer(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The selected server folder does not exist.");
        var existing = Servers.FirstOrDefault(s => PathsEqual(s.Folder, folder));
        if (existing is not null) { SelectServer(existing); return existing; }

        var jar = Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).FirstOrDefault() ?? "server.jar";
        var profile = new ServerProfile
        {
            Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Folder = Path.GetFullPath(folder),
            Jar = jar ?? "server.jar",
            Version = DetectVersion(folder),
            Type = DetectServerType(folder, jar ?? string.Empty),
            MemoryGb = 4,
            JavaPath = "java"
        };
        Servers.Add(profile);
        SaveProfiles();
        SelectServer(profile);
        return profile;
    }

    public ServerProfile CreateProfile(string name, string folder, string type, string version, int memoryGb, string jar, string javaPath)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Server name is required.");
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Server folder is required.");
        Directory.CreateDirectory(folder);
        var profile = new ServerProfile
        {
            Name = name.Trim(), Folder = Path.GetFullPath(folder), Type = string.IsNullOrWhiteSpace(type) ? "Vanilla" : type.Trim(),
            Version = version?.Trim() ?? string.Empty, MemoryGb = Math.Max(1, memoryGb),
            Jar = string.IsNullOrWhiteSpace(jar) ? "server.jar" : jar.Trim(), JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim()
        };
        Servers.Add(profile);
        SaveProfiles();
        SelectServer(profile);
        return profile;
    }

    public void UpdateSelectedProfile(string name, int memoryGb, string javaPath, string jar)
    {
        var profile = RequireSelected();
        profile.Name = string.IsNullOrWhiteSpace(name) ? profile.Name : name.Trim();
        profile.MemoryGb = Math.Max(1, memoryGb);
        profile.JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim();
        profile.Jar = string.IsNullOrWhiteSpace(jar) ? profile.Jar : jar.Trim();
        profile.RefreshDerivedProperties();
        SaveProfiles();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = RequireSelected();
        if (Launcher.IsRunning) throw new InvalidOperationException($"{_runningServer?.Name ?? "A server"} is already running.");
        ValidateStart(profile);
        Launcher.Start(profile);
        _runningServer = profile;
        _startedAt = DateTimeOffset.Now;
        profile.RuntimeState = "Running";
        if (Launcher.ProcessId is int pid) _runtimeSessions.Save(new RuntimeSession(pid, profile.Folder, _startedAt.Value));
        AddConsole($"[Manager] Started {profile.Name}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public async Task<bool> StopGracefullyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!Launcher.IsRunning) return true;
        AddConsole("[Manager] Sending graceful stop command...");
        var stopped = await Launcher.TryStopGracefullyAsync(timeout, cancellationToken);
        if (!stopped) AddConsole("[Manager] The server did not stop before the timeout.");
        return stopped;
    }

    public async Task ForceKillAsync(CancellationToken cancellationToken = default)
    {
        if (!Launcher.IsRunning) return;
        AddConsole("[Manager] Force-terminating the Java process.");
        await Launcher.ForceKillAsync(cancellationToken);
    }

    public async Task RestartAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var profile = _runningServer ?? RequireSelected();
        if (Launcher.IsRunning)
        {
            var stopped = await StopGracefullyAsync(timeout, cancellationToken);
            if (!stopped) throw new TimeoutException("The server did not stop gracefully. Force kill it before restarting.");
        }
        SelectServer(profile);
        await StartAsync(cancellationToken);
    }

    public void SendCommand(string command)
    {
        if (!Launcher.CanSendCommands) throw new InvalidOperationException(Launcher.IsAttachedProcess
            ? "Commands are unavailable because MystMC recovered an already-running Java process."
            : "The server is not running.");
        if (string.IsNullOrWhiteSpace(command)) return;
        AddConsole("> " + command.Trim());
        Launcher.SendCommand(command.Trim());
    }

    public void RequestPlayers()
    {
        if (Launcher.CanSendCommands) Launcher.SendCommand("list");
    }

    public async Task<BackupRecord> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var profile = RequireSelected();
        if (ReferenceEquals(profile, _runningServer) && Launcher.IsRunning && !Launcher.CanSendCommands)
            throw new InvalidOperationException("A safe live backup is unavailable for a recovered server process. Stop it first.");
        if (ReferenceEquals(profile, _runningServer) && Launcher.CanSendCommands)
        {
            Launcher.SendCommand("save-all flush");
            await Task.Delay(1200, cancellationToken);
        }
        var backup = await Backups.CreateAsync(profile, "Manual", cancellationToken: cancellationToken);
        AddConsole($"[Manager] Backup created: {backup.Name}");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return backup;
    }

    public IReadOnlyList<string> ConsoleSnapshot()
    {
        lock (_consoleLock) return _consoleLines.ToArray();
    }

    public bool TryGetPerformanceSnapshot(out TimeSpan cpuTime, out long memoryBytes) => Launcher.TryGetPerformanceSnapshot(out cpuTime, out memoryBytes);

    public Task<ServerHealthReport> AnalyzeHealthAsync(CancellationToken cancellationToken = default) =>
        _healthAnalyzer.AnalyzeAsync(RequireSelected(), BackupRoot, cancellationToken);

    public async Task<IReadOnlyList<string>> InstallPluginPackAsync(PluginPack pack, CancellationToken cancellationToken = default)
    {
        var profile = RequireSelected();
        if (!profile.Type.Equals("Paper", StringComparison.OrdinalIgnoreCase)
            && !profile.Type.Equals("Purpur", StringComparison.OrdinalIgnoreCase)
            && !profile.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plugin packs require a Bukkit-compatible server such as Paper, Purpur, or Folia.");
        if (string.IsNullOrWhiteSpace(profile.Version))
            throw new InvalidOperationException("Set the Minecraft version in the server profile before installing a plugin pack.");

        var installed = new List<string>();
        foreach (var projectId in PluginPacks.GetProjectIds(pack))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await _pluginCatalog.SearchAsync(projectId, profile.Version, cancellationToken);
            var item = results.FirstOrDefault(x => x.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) || x.Slug.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                       ?? results.FirstOrDefault();
            if (item is null) throw new InvalidOperationException($"Could not find '{projectId}' on Modrinth for Minecraft {profile.Version}.");
            await _pluginCatalog.ResolveInstallAsync(item, profile.Version, cancellationToken);
            if (!item.CanInstall || string.IsNullOrWhiteSpace(item.FileName)) throw new InvalidOperationException($"No compatible downloadable build was found for {item.Name}.");
            var temp = Path.Combine(Path.GetTempPath(), "MystMC-" + Guid.NewGuid().ToString("N") + ".jar");
            try
            {
                await _pluginCatalog.DownloadAsync(item, temp, cancellationToken);
                Plugins.InstallDownloaded(profile, temp, item.FileName, overwrite: true);
                installed.Add(item.Name);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return installed;
    }

    public async Task RunDueMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;

        foreach (var profile in Servers.Where(p => p.AutomaticBackupsEnabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interval = TimeSpan.FromMinutes(Math.Max(5, profile.BackupIntervalMinutes));
            if (profile.LastAutomaticBackupAt is { } last && now - last < interval) continue;
            if (ReferenceEquals(profile, _runningServer) && Launcher.IsRunning && !Launcher.CanSendCommands) continue;
            try
            {
                if (ReferenceEquals(profile, _runningServer) && Launcher.CanSendCommands)
                {
                    Launcher.SendCommand("save-all flush");
                    await Task.Delay(1000, cancellationToken);
                }
                await Backups.CreateAsync(profile, "Automatic", cancellationToken: cancellationToken);
                profile.LastAutomaticBackupAt = now;
                Backups.ApplyAutomaticRetention(profile, Math.Max(1, profile.BackupRetentionCount));
                SaveProfiles();
                AddConsole($"[Manager] Automatic backup completed for {profile.Name}.");
            }
            catch (Exception ex)
            {
                AddConsole($"[Manager] Automatic backup failed for {profile.Name}: {ex.Message}");
            }
        }

        var tasks = ScheduledTasks.Load().ToList();
        var changed = false;
        foreach (var task in tasks.Where(t => t.Enabled && t.NextRunAt <= now))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Servers.FirstOrDefault(s => PathsEqual(s.Folder, task.ServerFolder));
            try
            {
                if (target is null) throw new InvalidOperationException("Scheduled task server profile no longer exists.");
                switch (task.Action.ToLowerInvariant())
                {
                    case "broadcast":
                        if (!ReferenceEquals(target, _runningServer) || !Launcher.CanSendCommands) throw new InvalidOperationException("Target server is not running with command control.");
                        Launcher.SendCommand("say " + task.Payload);
                        break;
                    case "command":
                        if (!ReferenceEquals(target, _runningServer) || !Launcher.CanSendCommands) throw new InvalidOperationException("Target server is not running with command control.");
                        Launcher.SendCommand(task.Payload);
                        break;
                    case "backup":
                        if (ReferenceEquals(target, _runningServer) && Launcher.IsRunning && !Launcher.CanSendCommands) throw new InvalidOperationException("Recovered server cannot be safely backed up live.");
                        if (ReferenceEquals(target, _runningServer) && Launcher.CanSendCommands) { Launcher.SendCommand("save-all flush"); await Task.Delay(1000, cancellationToken); }
                        await Backups.CreateAsync(target, "Automatic", cancellationToken: cancellationToken);
                        break;
                    case "start":
                        if (Launcher.IsRunning) throw new InvalidOperationException("Another server is already running.");
                        SelectServer(target);
                        await StartAsync(cancellationToken);
                        break;
                    case "stop":
                        if (!ReferenceEquals(target, _runningServer)) throw new InvalidOperationException("Target server is not running.");
                        if (!await StopGracefullyAsync(TimeSpan.FromSeconds(20), cancellationToken)) throw new TimeoutException("Server did not stop within 20 seconds.");
                        break;
                    case "restart":
                        if (!ReferenceEquals(target, _runningServer)) throw new InvalidOperationException("Target server is not running.");
                        await RestartAsync(TimeSpan.FromSeconds(20), cancellationToken);
                        break;
                    default: throw new InvalidOperationException($"Unsupported scheduled action '{task.Action}'.");
                }
                task.LastResult = "Completed";
            }
            catch (Exception ex)
            {
                task.LastResult = ex.Message;
                AddConsole($"[Manager] Scheduled task '{task.Name}' failed: {ex.Message}");
            }
            task.LastRunAt = now;
            changed = true;
        }
        if (changed) ScheduledTasks.Save(tasks);
    }

    public ServerProfile RequireSelected() => _selectedServer ?? throw new InvalidOperationException("Select a server first.");

    private void Launcher_OutputReceived(object? sender, string line)
    {
        AddConsole(line);
        ParsePlayerLine(line);
    }

    private void Launcher_Exited(object? sender, ServerProcessExitedEventArgs e)
    {
        _runtimeSessions.Clear();
        var profile = _runningServer;
        if (profile is not null) profile.RuntimeState = e.StopWasRequested ? "Stopped" : "Failed";
        var text = e.WasForceKilled ? "force killed" : e.StopWasRequested ? "stopped" : "exited unexpectedly";
        AddConsole($"[Manager] Server {text}{(e.ExitCode is int code ? $" (exit code {code})" : string.Empty)}.");
        _runningServer = null;
        _startedAt = null;
        lock (_onlinePlayers) _onlinePlayers.Clear();
        PlayersChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddConsole(string line)
    {
        lock (_consoleLock)
        {
            _consoleLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_consoleLines.Count > 5000) _consoleLines.RemoveRange(0, _consoleLines.Count - 5000);
        }
        ConsoleLineReceived?.Invoke(this, line);
    }

    private void ParsePlayerLine(string line)
    {
        var list = Regex.Match(line, @"There are \d+ of a max of \d+ players online:?\s*(.*)$", RegexOptions.IgnoreCase);
        if (list.Success)
        {
            var names = list.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lock (_onlinePlayers)
            {
                _onlinePlayers.Clear();
                foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n))) _onlinePlayers.Add(name);
            }
            PlayersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var joined = Regex.Match(line, @":\s*([^\s]+) joined the game", RegexOptions.IgnoreCase);
        if (joined.Success)
        {
            lock (_onlinePlayers) if (!_onlinePlayers.Contains(joined.Groups[1].Value)) _onlinePlayers.Add(joined.Groups[1].Value);
            PlayersChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var left = Regex.Match(line, @":\s*([^\s]+) left the game", RegexOptions.IgnoreCase);
        if (left.Success)
        {
            lock (_onlinePlayers) _onlinePlayers.Remove(left.Groups[1].Value);
            PlayersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void ValidateStart(ServerProfile profile)
    {
        if (!Directory.Exists(profile.Folder)) throw new DirectoryNotFoundException("The server folder does not exist.");
        var jar = Path.Combine(profile.Folder, profile.Jar);
        if (!File.Exists(jar)) throw new FileNotFoundException("The configured server JAR was not found.", jar);
        if (!string.Equals(profile.JavaPath, "java", StringComparison.OrdinalIgnoreCase) && !File.Exists(profile.JavaPath))
            throw new FileNotFoundException("The configured Java executable was not found.", profile.JavaPath);
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'), Path.GetFullPath(b).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static string DetectVersion(string folder)
    {
        var props = Path.Combine(folder, "version_history.json");
        return File.Exists(props) ? "Detected" : string.Empty;
    }

    private static string DetectServerType(string folder, string jar)
    {
        var name = jar.ToLowerInvariant();
        if (name.Contains("folia")) return "Folia";
        if (name.Contains("paper")) return "Paper";
        if (name.Contains("purpur")) return "Purpur";
        if (name.Contains("fabric")) return "Fabric";
        if (Directory.Exists(Path.Combine(folder, "plugins"))) return "Paper/Spigot";
        return "Vanilla";
    }

    public void Dispose()
    {
        Launcher.OutputReceived -= Launcher_OutputReceived;
        Launcher.Exited -= Launcher_Exited;
        Launcher.Dispose();
        _pluginCatalog.Dispose();
        _downloads.Dispose();
        GC.SuppressFinalize(this);
    }
}
