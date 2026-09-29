using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;

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
    private readonly Dictionary<ServerProfile, ServerInstance> _instances = new();
    private ServerProfile? _selectedServer;

    public ObservableCollection<ServerProfile> Servers { get; }
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

    // The members below describe the *selected* server. Every server has its own
    // ServerInstance, so selecting a different server switches what these report.
    private ServerInstance? Selected => _selectedServer is null ? null : GetInstance(_selectedServer);
    public bool IsRunning => Selected?.IsRunning ?? false;
    public bool IsRecoveredProcess => Selected?.IsRecoveredProcess ?? false;
    public bool CanSendCommands => Selected?.CanSendCommands ?? false;
    public DateTimeOffset? StartedAt => Selected?.StartedAt;
    public TimeSpan Uptime => Selected?.Uptime ?? TimeSpan.Zero;
    public IReadOnlyList<string> OnlinePlayers => Selected?.OnlinePlayers ?? [];

    /// <summary>All servers that currently have a live Java process.</summary>
    public IReadOnlyList<ServerProfile> RunningServers
    {
        get { lock (_instances) return _instances.Values.Where(i => i.IsRunning).Select(i => i.Profile).ToList(); }
    }
    public bool IsServerRunning(ServerProfile profile) => GetInstance(profile).IsRunning;
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

        RecoverPreviousSessions();
        _selectedServer = RunningServers.FirstOrDefault() ?? Servers.FirstOrDefault();
    }

    private ServerInstance GetInstance(ServerProfile profile)
    {
        lock (_instances)
        {
            if (_instances.TryGetValue(profile, out var existing)) return existing;
            var instance = new ServerInstance(profile);
            instance.ConsoleLineReceived += Instance_ConsoleLineReceived;
            instance.PlayersChanged += Instance_PlayersChanged;
            instance.Exited += Instance_Exited;
            _instances[profile] = instance;
            return instance;
        }
    }

    private void RecoverPreviousSessions()
    {
        var recovered = new List<RuntimeSession>();
        foreach (var session in _runtimeSessions.LoadAll())
        {
            var profile = Servers.FirstOrDefault(s => PathsEqual(s.Folder, session.ServerFolder));
            if (profile is null) continue;
            var instance = GetInstance(profile);
            if (instance.IsRunning || !instance.Launcher.TryAttach(session.ProcessId)) continue;
            instance.StartedAt = session.StartedAt;
            profile.RuntimeState = "Recovered";
            instance.AddConsole("[Manager] Reconnected to the existing Java server process. Console input is unavailable for recovered processes.");
            recovered.Add(session);
        }
        _runtimeSessions.ReplaceAll(recovered);
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
        int port,
        bool eulaAccepted,
        string? customJarPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a server name.");
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose an install folder.");
        if (Servers.Any(server => string.Equals(server.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A MystMC server profile with this name already exists.");
        if (Servers.Any(server => PathsEqual(server.Folder, folder)))
            throw new InvalidOperationException("A MystMC server profile already uses this folder.");
        var portOwner = Servers.FirstOrDefault(server => server.Port == port);
        if (portOwner is not null)
            throw new InvalidOperationException($"Port {port} is already used by '{portOwner.Name}'. Try port {SuggestFreePort()}.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("The generated server folder already exists and is not empty. Choose a different server name/install root, or use Add Existing Server.");

        var request = new ServerInstallRequest(
            name.Trim(),
            folder.Trim(),
            string.IsNullOrWhiteSpace(type) ? "Vanilla" : type.Trim(),
            version?.Trim() ?? string.Empty,
            Math.Max(1, memoryGb),
            customJarPath,
            eulaAccepted,
            port);

        var profile = await _serverInstaller.InstallAsync(request, cancellationToken);
        profile.JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim();
        profile.RefreshDerivedProperties();
        Servers.Add(profile);
        SaveProfiles();
        SelectServer(profile);
        GetInstance(profile).AddConsole($"[Manager] Installed {profile.Type} {profile.Version} as '{profile.Name}' in {profile.Folder}.");
        return profile;
    }

    /// <summary>
    /// The first port from 25565 upwards that no server profile uses and nothing on this machine is listening on.
    /// </summary>
    public int SuggestFreePort()
    {
        var used = Servers.Select(server => server.Port).ToHashSet();
        try
        {
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()) used.Add(endpoint.Port);
        }
        catch (NetworkInformationException) { }
        var port = 25565;
        while (used.Contains(port) && port < 65535) port++;
        return port;
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

    public void UpdateSelectedProfile(string name, int memoryGb, string javaPath, string jar, string type, string version)
    {
        var profile = RequireSelected();
        profile.Name = string.IsNullOrWhiteSpace(name) ? profile.Name : name.Trim();
        profile.Type = string.IsNullOrWhiteSpace(type) ? profile.Type : type.Trim();
        profile.Version = version?.Trim() ?? string.Empty;
        profile.MemoryGb = Math.Max(1, memoryGb);
        profile.JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim();
        profile.Jar = string.IsNullOrWhiteSpace(jar) ? profile.Jar : jar.Trim();
        profile.RefreshDerivedProperties();
        SaveProfiles();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync(RequireSelected(), cancellationToken);

    public async Task StartAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = GetInstance(profile);
        if (instance.IsRunning) throw new InvalidOperationException($"{profile.Name} is already running.");
        ValidateStart(profile);
        EnsurePortAvailable(profile);
        instance.Launcher.Start(profile);
        instance.StartedAt = DateTimeOffset.Now;
        profile.RuntimeState = "Running";
        if (instance.ProcessId is int pid) _runtimeSessions.Save(new RuntimeSession(pid, profile.Folder, instance.StartedAt.Value));
        instance.AddConsole($"[Manager] Started {profile.Name} on port {profile.Port}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public Task<bool> StopGracefullyAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        StopGracefullyAsync(RequireSelected(), timeout, cancellationToken);

    public async Task<bool> StopGracefullyAsync(ServerProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var instance = GetInstance(profile);
        if (!instance.IsRunning) return true;
        instance.AddConsole("[Manager] Sending graceful stop command...");
        var stopped = await instance.Launcher.TryStopGracefullyAsync(timeout, cancellationToken);
        if (!stopped) instance.AddConsole("[Manager] The server did not stop before the timeout.");
        return stopped;
    }

    public Task ForceKillAsync(CancellationToken cancellationToken = default) => ForceKillAsync(RequireSelected(), cancellationToken);

    public async Task ForceKillAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        var instance = GetInstance(profile);
        if (!instance.IsRunning) return;
        instance.AddConsole("[Manager] Force-terminating the Java process.");
        await instance.Launcher.ForceKillAsync(cancellationToken);
    }

    /// <summary>Stops every running server in parallel. Returns the servers that did not stop in time.</summary>
    public async Task<IReadOnlyList<ServerProfile>> StopAllGracefullyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var running = RunningServers;
        var results = await Task.WhenAll(running.Select(async p => (Profile: p, Stopped: await StopGracefullyAsync(p, timeout, cancellationToken))));
        return results.Where(r => !r.Stopped).Select(r => r.Profile).ToList();
    }

    public Task RestartAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => RestartAsync(RequireSelected(), timeout, cancellationToken);

    public async Task RestartAsync(ServerProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (GetInstance(profile).IsRunning)
        {
            var stopped = await StopGracefullyAsync(profile, timeout, cancellationToken);
            if (!stopped) throw new TimeoutException("The server did not stop gracefully. Force kill it before restarting.");
        }
        await StartAsync(profile, cancellationToken);
    }

    public void SendCommand(string command) => SendCommand(RequireSelected(), command);

    public void SendCommand(ServerProfile profile, string command)
    {
        var instance = GetInstance(profile);
        if (!instance.CanSendCommands) throw new InvalidOperationException(instance.IsRecoveredProcess
            ? "Commands are unavailable because MystMC recovered an already-running Java process."
            : $"{profile.Name} is not running.");
        if (string.IsNullOrWhiteSpace(command)) return;
        instance.AddConsole("> " + command.Trim());
        instance.Launcher.SendCommand(command.Trim());
    }

    public void RequestPlayers()
    {
        if (Selected is { CanSendCommands: true } instance) instance.Launcher.SendCommand("list");
    }

    public async Task<BackupRecord> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var profile = RequireSelected();
        await FlushBeforeBackupAsync(profile, 1200, cancellationToken);
        var backup = await Backups.CreateAsync(profile, "Manual", cancellationToken: cancellationToken);
        GetInstance(profile).AddConsole($"[Manager] Backup created: {backup.Name}");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return backup;
    }

    private async Task FlushBeforeBackupAsync(ServerProfile profile, int delayMs, CancellationToken cancellationToken)
    {
        var instance = GetInstance(profile);
        if (instance.IsRunning && !instance.CanSendCommands)
            throw new InvalidOperationException("A safe live backup is unavailable for a recovered server process. Stop it first.");
        if (instance.CanSendCommands)
        {
            instance.Launcher.SendCommand("save-all flush");
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    public IReadOnlyList<string> ConsoleSnapshot() => Selected?.ConsoleSnapshot() ?? [];

    public bool TryGetPerformanceSnapshot(out TimeSpan cpuTime, out long memoryBytes)
    {
        cpuTime = TimeSpan.Zero;
        memoryBytes = 0;
        return Selected?.Launcher.TryGetPerformanceSnapshot(out cpuTime, out memoryBytes) ?? false;
    }

    public Task<ServerHealthReport> AnalyzeHealthAsync(CancellationToken cancellationToken = default) =>
        _healthAnalyzer.AnalyzeAsync(RequireSelected(), BackupRoot, cancellationToken);

    public sealed record PluginPackResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Skipped);

    /// <summary>Searches Modrinth for plugins or mods that fit the selected server.</summary>
    public Task<IReadOnlyList<PluginCatalogItem>> SearchAddonsAsync(string query, CancellationToken cancellationToken = default)
    {
        var (profile, kind) = RequireAddonServer();
        return _pluginCatalog.SearchAsync(query, profile.Version, kind, cancellationToken);
    }

    /// <summary>
    /// Downloads the newest compatible build of a Modrinth project, verifies its SHA-512,
    /// checks it is the right kind of add-on, and installs it into plugins/ or mods/.
    /// </summary>
    public async Task<PluginRecord> InstallAddonAsync(PluginCatalogItem item, CancellationToken cancellationToken = default)
    {
        var (profile, kind) = RequireAddonServer();
        await _pluginCatalog.ResolveInstallAsync(item, profile.Version, kind, cancellationToken);
        if (!item.CanInstall || string.IsNullOrWhiteSpace(item.FileName))
            throw new InvalidOperationException($"{item.Name} has no {profile.Type} build for Minecraft {profile.Version}.");
        var temp = Path.Combine(Path.GetTempPath(), "MystMC-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            await _pluginCatalog.DownloadAsync(item, temp, cancellationToken);
            var record = Plugins.InstallDownloaded(profile, temp, item.FileName, overwrite: true);
            GetInstance(profile).AddConsole($"[Manager] Installed {AddonKinds.Noun(kind)} {item.Name} {item.LatestVersion}. Restart the server to load it.");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return record;
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    /// <summary>Installs a Modrinth project by its exact id or slug (never a search guess).</summary>
    public async Task<PluginRecord> InstallAddonByIdAsync(string projectIdOrSlug, CancellationToken cancellationToken = default)
    {
        var item = await _pluginCatalog.GetProjectAsync(projectIdOrSlug, cancellationToken)
                   ?? throw new InvalidOperationException($"'{projectIdOrSlug}' was not found on Modrinth.");
        return await InstallAddonAsync(item, cancellationToken);
    }

    private (ServerProfile Profile, AddonKind Kind) RequireAddonServer()
    {
        var profile = RequireSelected();
        var kind = AddonKinds.For(profile);
        if (kind == AddonKind.None)
            throw new InvalidOperationException($"{profile.Type} servers don't support plugins or mods. Create a Paper, Purpur, Folia, or Fabric server to use add-ons.");
        if (string.IsNullOrWhiteSpace(profile.Version) || profile.Version.Equals("Detected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set this server's Minecraft version in Settings first, so MystMC can pick compatible add-ons.");
        return (profile, kind);
    }

    public async Task<PluginPackResult> InstallPluginPackAsync(PluginPack pack, CancellationToken cancellationToken = default)
    {
        var (_, kind) = RequireAddonServer();
        if (kind != AddonKind.Plugin)
            throw new InvalidOperationException("Plugin packs require a Bukkit-compatible server such as Paper, Purpur, or Folia.");

        var installed = new List<string>();
        var skipped = new List<string>();
        foreach (var projectId in PluginPacks.GetProjectIds(pack))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var record = await InstallAddonByIdAsync(projectId, cancellationToken);
                installed.Add(record.Name);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpRequestException)
            {
                skipped.Add($"{projectId}: {ex.Message}");
            }
        }
        return new PluginPackResult(installed, skipped);
    }

    public async Task RunDueMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;

        foreach (var profile in Servers.Where(p => p.AutomaticBackupsEnabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interval = TimeSpan.FromMinutes(Math.Max(5, profile.BackupIntervalMinutes));
            if (profile.LastAutomaticBackupAt is { } last && now - last < interval) continue;
            var instance = GetInstance(profile);
            if (instance.IsRunning && !instance.CanSendCommands) continue;
            try
            {
                await FlushBeforeBackupAsync(profile, 1000, cancellationToken);
                await Backups.CreateAsync(profile, "Automatic", cancellationToken: cancellationToken);
                profile.LastAutomaticBackupAt = now;
                Backups.ApplyAutomaticRetention(profile, Math.Max(1, profile.BackupRetentionCount));
                SaveProfiles();
                instance.AddConsole($"[Manager] Automatic backup completed for {profile.Name}.");
            }
            catch (Exception ex)
            {
                instance.AddConsole($"[Manager] Automatic backup failed for {profile.Name}: {ex.Message}");
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
                var targetInstance = GetInstance(target);
                switch (task.Action.ToLowerInvariant())
                {
                    case "broadcast":
                        SendCommand(target, "say " + task.Payload);
                        break;
                    case "command":
                        SendCommand(target, task.Payload);
                        break;
                    case "backup":
                        await FlushBeforeBackupAsync(target, 1000, cancellationToken);
                        await Backups.CreateAsync(target, "Automatic", cancellationToken: cancellationToken);
                        break;
                    case "start":
                        if (targetInstance.IsRunning) throw new InvalidOperationException($"{target.Name} is already running.");
                        await StartAsync(target, cancellationToken);
                        break;
                    case "stop":
                        if (!targetInstance.IsRunning) throw new InvalidOperationException($"{target.Name} is not running.");
                        if (!await StopGracefullyAsync(target, TimeSpan.FromSeconds(20), cancellationToken)) throw new TimeoutException("Server did not stop within 20 seconds.");
                        break;
                    case "restart":
                        if (!targetInstance.IsRunning) throw new InvalidOperationException($"{target.Name} is not running.");
                        await RestartAsync(target, TimeSpan.FromSeconds(20), cancellationToken);
                        break;
                    default: throw new InvalidOperationException($"Unsupported scheduled action '{task.Action}'.");
                }
                task.LastResult = "Completed";
            }
            catch (Exception ex)
            {
                task.LastResult = ex.Message;
                if (target is not null) GetInstance(target).AddConsole($"[Manager] Scheduled task '{task.Name}' failed: {ex.Message}");
            }
            task.LastRunAt = now;
            changed = true;
        }
        if (changed) ScheduledTasks.Save(tasks);
    }

    public ServerProfile RequireSelected() => _selectedServer ?? throw new InvalidOperationException("Select a server first.");

    // Console and player events are only forwarded for the selected server, so the
    // console and players pages always show the server the user is looking at.
    private void Instance_ConsoleLineReceived(object? sender, string line)
    {
        if (sender is ServerInstance instance && ReferenceEquals(instance.Profile, _selectedServer))
            ConsoleLineReceived?.Invoke(this, line);
    }

    private void Instance_PlayersChanged(object? sender, EventArgs e)
    {
        if (sender is ServerInstance instance && ReferenceEquals(instance.Profile, _selectedServer))
            PlayersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Instance_Exited(object? sender, ServerProcessExitedEventArgs e)
    {
        if (sender is not ServerInstance instance) return;
        var profile = instance.Profile;
        _runtimeSessions.Remove(profile.Folder);
        profile.RuntimeState = e.StopWasRequested ? "Stopped" : "Failed";
        var text = e.WasForceKilled ? "force killed" : e.StopWasRequested ? "stopped" : "exited unexpectedly";
        instance.AddConsole($"[Manager] Server {text}{(e.ExitCode is int code ? $" (exit code {code})" : string.Empty)}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Refuses to start a server whose port is already used by another MystMC server
    /// or by any other program listening on this machine.
    /// </summary>
    private void EnsurePortAvailable(ServerProfile profile)
    {
        var port = profile.Port;
        var clash = RunningServers.FirstOrDefault(p => !ReferenceEquals(p, profile) && p.Port == port);
        if (clash is not null)
            throw new InvalidOperationException($"Port {port} is already used by '{clash.Name}'. Give '{profile.Name}' a different server-port in its settings.");

        bool inUse;
        try { inUse = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(ep => ep.Port == port); }
        catch (NetworkInformationException) { inUse = false; }
        if (inUse)
            throw new InvalidOperationException($"Port {port} is already in use by another program. Give '{profile.Name}' a different server-port in its settings.");
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
        lock (_instances)
        {
            foreach (var instance in _instances.Values)
            {
                instance.ConsoleLineReceived -= Instance_ConsoleLineReceived;
                instance.PlayersChanged -= Instance_PlayersChanged;
                instance.Exited -= Instance_Exited;
                instance.Dispose();
            }
            _instances.Clear();
        }
        _pluginCatalog.Dispose();
        _downloads.Dispose();
        GC.SuppressFinalize(this);
    }
}
