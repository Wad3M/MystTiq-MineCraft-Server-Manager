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
    public ScheduledTaskStore ScheduledTasks { get; } = new();
    public NetworkAddressService Network { get; }

    public ServerProfile? SelectedServer => _selectedServer;
    /// <summary>Set when the server list had to be recovered at startup.</summary>
    public string? StartupWarning => _profileStore.LoadWarning;

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
    public bool IsRestartPending(ServerProfile profile) => GetInstance(profile).PendingRestart is not null;
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
        Network = new NetworkAddressService(_downloads);
        JavaRuntimes = new JavaRuntimeService(_downloads);

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
        CancellationToken cancellationToken = default,
        string? loaderVersion = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a server name.");
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose an install folder.");
        if (Servers.Any(server => string.Equals(server.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A MystTiq server profile with this name already exists.");
        if (Servers.Any(server => PathsEqual(server.Folder, folder)))
            throw new InvalidOperationException("A MystTiq server profile already uses this folder.");
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
            port,
            LoaderVersion: loaderVersion,
            JavaPath: string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim());

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
    /// Creates a server from a Modrinth modpack (.mrpack): installs the Minecraft version and
    /// loader the pack names, downloads its server-side files (SHA-512 verified, Modrinth's allowed
    /// hosts only), then applies its overrides and server-overrides.
    /// </summary>
    public async Task<(ServerProfile Profile, ModpackPlan Plan)> InstallModpackAsync(
        string mrpackPath, string name, string folder, int memoryGb, string javaPath, int port, bool eulaAccepted,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var plan = ModpackReader.Read(mrpackPath);
        var serverType = plan.Loader switch
        {
            "Fabric" or "Vanilla" or "Forge" or "NeoForge" => plan.Loader,
            _ => throw new NotSupportedException($"{plan.Name} uses {plan.Loader}, which MystTiq can't install yet. Fabric, Forge and NeoForge modpacks are supported.")
        };

        progress?.Report($"Installing {serverType} for Minecraft {plan.MinecraftVersion}…");
        var profile = await InstallServerAsync(string.IsNullOrWhiteSpace(name) ? plan.Name : name, folder, serverType, plan.MinecraftVersion,
            memoryGb, javaPath, port, eulaAccepted, cancellationToken: cancellationToken,
            loaderVersion: string.IsNullOrWhiteSpace(plan.LoaderVersion) ? null : plan.LoaderVersion);

        try
        {
            await DownloadModpackFilesAsync(mrpackPath, plan, profile, progress, cancellationToken);
        }
        catch
        {
            // The folder was empty (or new) before this install, so removing it only undoes our work.
            RemoveServer(profile);
            try { Directory.Delete(profile.Folder, recursive: true); } catch { }
            throw;
        }
        return (profile, plan);
    }

    private async Task DownloadModpackFilesAsync(string mrpackPath, ModpackPlan plan, ServerProfile profile, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(profile.Folder) + Path.DirectorySeparatorChar;
        for (var i = 0; i < plan.ServerFiles.Count; i++)
        {
            var file = plan.ServerFiles[i];
            progress?.Report($"Downloading {i + 1} of {plan.ServerFiles.Count}: {Path.GetFileName(file.Path)}");
            var destination = Path.GetFullPath(Path.Combine(profile.Folder, file.Path));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The modpack contains an unsafe file path: {file.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await _downloads.DownloadVerifiedAsync(new ServerDownload(file.Download.AbsoluteUri, "SHA512", file.Sha512), destination, cancellationToken);
        }

        progress?.Report("Applying the modpack's configuration…");
        var overrides = ModpackReader.ApplyOverrides(mrpackPath, profile.Folder);
        GetInstance(profile).AddConsole($"[Manager] Installed modpack {plan.Name} {plan.PackVersion}: {plan.ServerFiles.Count} server file(s), {overrides} override file(s); skipped {plan.SkippedClientOnly} client-only file(s).");
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

        var jar = ServerFolderInspector.FindForgeLaunchTarget(folder) ?? ServerFolderInspector.FindServerJar(folder);
        var profile = new ServerProfile
        {
            Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Folder = Path.GetFullPath(folder),
            Jar = jar,
            Version = ServerFolderInspector.DetectVersion(folder, jar),
            Type = ServerFolderInspector.DetectServerType(folder, jar),
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

    /// <summary>Java runtimes MystTiq downloads for servers (Eclipse Temurin from Adoptium).</summary>
    public JavaRuntimeService JavaRuntimes { get; }

    /// <summary>Points a server at a different java.exe (e.g. one MystTiq downloaded) and saves.</summary>
    public void SetJavaPath(ServerProfile profile, string javaPath)
    {
        if (string.Equals(profile.JavaPath, javaPath, StringComparison.OrdinalIgnoreCase)) return;
        profile.JavaPath = javaPath;
        profile.RefreshDerivedProperties();
        SaveProfiles();
        GetInstance(profile).AddConsole($"[Manager] Java for this server set to {javaPath}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSelectedProfile(string name, int memoryGb, string javaPath, string jar, string type, string version)
    {
        var profile = RequireSelected();
        var newName = string.IsNullOrWhiteSpace(name) ? profile.Name : name.Trim();
        if (!string.Equals(newName, profile.Name, StringComparison.Ordinal))
        {
            if (Servers.Any(s => !ReferenceEquals(s, profile) && string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Another server is already called '{newName}'.");
            // Backups are kept in a folder named after the server, so move them with the rename.
            Backups.MoveBackupsForRename(profile.Name, newName);
            profile.Name = newName;
        }
        profile.Type = string.IsNullOrWhiteSpace(type) ? profile.Type : type.Trim();
        profile.Version = version?.Trim() ?? string.Empty;
        profile.MemoryGb = Math.Max(1, memoryGb);
        profile.JavaPath = string.IsNullOrWhiteSpace(javaPath) ? "java" : javaPath.Trim();
        profile.Jar = string.IsNullOrWhiteSpace(jar) ? profile.Jar : jar.Trim();
        profile.RefreshDerivedProperties();
        SaveProfiles();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a server from the list. Its folder, worlds and backups stay on disk; it can be
    /// added again with Add Existing Server.
    /// </summary>
    public void RemoveServer(ServerProfile profile)
    {
        if (GetInstance(profile).IsRunning) throw new InvalidOperationException($"Stop {profile.Name} before removing it from the list.");
        lock (_instances)
        {
            if (_instances.Remove(profile, out var instance))
            {
                instance.ConsoleLineReceived -= Instance_ConsoleLineReceived;
                instance.PlayersChanged -= Instance_PlayersChanged;
                instance.Exited -= Instance_Exited;
                instance.Dispose();
            }
        }
        Servers.Remove(profile);
        SaveProfiles();
        SelectServer(Servers.FirstOrDefault());
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync(RequireSelected(), cancellationToken);

    /// <summary>Starts a server at the user's request; cancels any pending crash restart and resets its count.</summary>
    public Task StartAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        var instance = GetInstance(profile);
        instance.CancelPendingRestart();
        instance.CrashRestarts.Reset();
        return StartCoreAsync(profile, cancellationToken);
    }

    private async Task StartCoreAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = GetInstance(profile);
        if (instance.IsRunning) throw new InvalidOperationException($"{profile.Name} is already running.");
        ValidateStart(profile);
        EnsurePortAvailable(profile);
        EnsureBedrockPortAvailable(profile, instance);
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
        if (instance.PendingRestart is not null)
        {
            instance.CancelPendingRestart();
            instance.AddConsole("[Manager] Automatic restart cancelled.");
            profile.RuntimeState = "Stopped";
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        instance.CrashRestarts.Reset();
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
        instance.CancelPendingRestart();
        instance.CrashRestarts.Reset();
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
        await StartCoreAsync(profile, cancellationToken);
    }

    /// <summary>Turns crash auto-restart on or off for a server and sets how many restarts are allowed.</summary>
    public void SetCrashRecovery(ServerProfile profile, bool enabled, int restartLimit, int windowMinutes, int delaySeconds)
    {
        profile.CrashRecoveryEnabled = enabled;
        profile.CrashRestartLimit = Math.Clamp(restartLimit, 1, 20);
        profile.CrashRestartWindowMinutes = Math.Clamp(windowMinutes, 1, 1440);
        profile.CrashRestartDelaySeconds = Math.Clamp(delaySeconds, 0, 600);
        if (!enabled) GetInstance(profile).CancelPendingRestart();
        SaveProfiles();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendCommand(string command) => SendCommand(RequireSelected(), command);

    public void SendCommand(ServerProfile profile, string command)
    {
        var instance = GetInstance(profile);
        if (!instance.CanSendCommands) throw new InvalidOperationException(instance.IsRecoveredProcess
            ? "Commands are unavailable because MystTiq recovered an already-running Java process."
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
        var backup = await CreateConsistentBackupAsync(profile, "Manual", cancellationToken);
        GetInstance(profile).AddConsole($"[Manager] Backup created: {backup.Name}");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return backup;
    }

    /// <summary>
    /// Backs up a server safely while it runs: pauses world saving, forces a full save and waits
    /// for the server to confirm it, zips the folder, then always turns saving back on.
    /// </summary>
    private async Task<BackupRecord> CreateConsistentBackupAsync(ServerProfile profile, string type, CancellationToken cancellationToken)
    {
        var instance = GetInstance(profile);
        if (instance.IsRunning && !instance.CanSendCommands)
            throw new InvalidOperationException("A safe live backup is unavailable for a recovered server process. Stop it first.");
        var savingPaused = false;
        try
        {
            if (instance.CanSendCommands)
            {
                instance.Launcher.SendCommand("save-off");
                savingPaused = true;
                var saved = await instance.SendAndWaitForLineAsync("save-all flush",
                    line => line.Contains("Saved the game", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(60), cancellationToken);
                if (!saved) instance.AddConsole("[Manager] The server didn't confirm the save within 60 seconds; backing up anyway.");
            }
            return await Backups.CreateAsync(profile, type, cancellationToken: cancellationToken);
        }
        finally
        {
            if (savingPaused && instance.CanSendCommands) instance.Launcher.SendCommand("save-on");
        }
    }

    /// <summary>
    /// Restores a backup over the server folder, after first taking a safety backup of the
    /// current files so a bad restore can be undone.
    /// </summary>
    public async Task<BackupRecord> RestoreBackupAsync(ServerProfile profile, BackupRecord backup, CancellationToken cancellationToken = default)
    {
        if (GetInstance(profile).IsRunning) throw new InvalidOperationException($"Stop {profile.Name} before restoring a backup.");
        var safety = await Backups.CreateAsync(profile, "Safety-BeforeRestore", cancellationToken: cancellationToken);
        await Backups.RestoreAsync(profile, backup, cancellationToken: cancellationToken);
        GetInstance(profile).AddConsole($"[Manager] Restored {backup.Name}. The previous files were saved first as {safety.Name}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return safety;
    }

    /// <summary>
    /// Add-ons that have no release for <paramref name="minecraftVersion"/> on Modrinth (or couldn't
    /// be found there), so they may not load after a Minecraft update.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindAddonsWithoutVersionAsync(ServerProfile profile, string minecraftVersion, CancellationToken cancellationToken = default)
    {
        if (AddonKinds.For(profile) == AddonKind.None) return [];
        var problems = new List<string>();
        foreach (var addon in Plugins.List(profile).Where(p => p.IsEnabled))
        {
            try
            {
                await _pluginCatalog.ResolveInstalledUpdateAsync(addon, minecraftVersion, profile.Type, cancellationToken);
                if (addon.UpdateStatus == "No compatible release") problems.Add($"{addon.Name}: no {minecraftVersion} version yet");
                else if (addon.UpdateStatus == "Not found on Modrinth") problems.Add($"{addon.Name}: not on Modrinth, check it yourself");
            }
            catch (System.Net.Http.HttpRequestException) { problems.Add($"{addon.Name}: couldn't be checked"); }
        }
        return problems;
    }

    /// <summary>
    /// Updates a stopped server to the newest build of <paramref name="minecraftVersion"/> (same or
    /// newer than now). A full backup is made first; returns that backup.
    /// </summary>
    public async Task<BackupRecord> UpdateServerSoftwareAsync(ServerProfile profile, string minecraftVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (GetInstance(profile).IsRunning) throw new InvalidOperationException($"Stop {profile.Name} before updating it.");
        if (string.IsNullOrWhiteSpace(minecraftVersion)) throw new InvalidOperationException("Choose a Minecraft version.");
        if (ServerInstaller.IsDowngrade(profile.Version, minecraftVersion))
            throw new InvalidOperationException($"{profile.Name} runs Minecraft {profile.Version}. Going back to {minecraftVersion} isn't supported: worlds can't be opened by older versions.");

        progress?.Report("Backing up the server first…");
        var safety = await Backups.CreateAsync(profile, "Safety-BeforeUpdate", cancellationToken: cancellationToken);
        progress?.Report($"Downloading {profile.Type} for Minecraft {minecraftVersion}…");
        var oldVersion = profile.Version;
        profile.Jar = await _serverInstaller.UpdateAsync(profile, minecraftVersion.Trim(), cancellationToken);
        profile.Version = minecraftVersion.Trim();
        profile.RefreshDerivedProperties();
        SaveProfiles();
        GetInstance(profile).AddConsole($"[Manager] Updated {profile.Type} from {oldVersion} to the newest {profile.Version} build. Backup made first: {safety.Name}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return safety;
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
        return _pluginCatalog.SearchAsync(query, profile.Version, profile.Type, cancellationToken);
    }

    /// <summary>
    /// Downloads the newest compatible build of a Modrinth project, verifies its SHA-512,
    /// checks it is the right kind of add-on, and installs it into plugins/ or mods/.
    /// </summary>
    public async Task<PluginRecord> InstallAddonAsync(PluginCatalogItem item, CancellationToken cancellationToken = default)
    {
        var (profile, kind) = RequireAddonServer();
        await _pluginCatalog.ResolveInstallAsync(item, profile.Version, profile.Type, cancellationToken);
        if (!item.CanInstall || string.IsNullOrWhiteSpace(item.FileName))
            throw new InvalidOperationException($"{item.Name} has no {profile.Type} build for Minecraft {profile.Version}.");
        var temp = Path.Combine(Path.GetTempPath(), "MystTiq-" + Guid.NewGuid().ToString("N") + ".jar");
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
            throw new InvalidOperationException($"{profile.Type} servers don't support plugins or mods. Create a Paper, Purpur, Folia, Fabric, Forge or NeoForge server to use add-ons.");
        if (string.IsNullOrWhiteSpace(profile.Version) || profile.Version.Equals("Detected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set this server's Minecraft version in Settings first, so MystTiq can pick compatible add-ons.");
        return (profile, kind);
    }

    // ---- Whitelist and operators ----

    /// <summary>
    /// Adds a player to the whitelist or operators. Running servers get Minecraft's own command
    /// (whitelist add / op); stopped servers have the file edited, with the UUID looked up from
    /// Mojang (online mode) or computed the way offline-mode servers do.
    /// </summary>
    public async Task AddToPlayerListAsync(ServerProfile profile, string fileName, string playerName, CancellationToken cancellationToken = default)
    {
        var name = playerName.Trim();
        if (!PlayerListFiles.IsValidName(name)) throw new InvalidOperationException($"'{name}' isn't a valid Minecraft name (3–16 letters, numbers or underscores).");
        var instance = GetInstance(profile);
        if (instance.CanSendCommands)
        {
            SendCommand(profile, fileName == PlayerListFiles.Ops ? $"op {name}" : $"whitelist add {name}");
            return;
        }
        if (instance.IsRunning) throw new InvalidOperationException("This server was recovered after MystTiq restarted, so it can't take commands. Stop it first, then edit the list.");

        string uuid;
        if (PlayerListFiles.IsOnlineMode(profile.Folder))
        {
            var found = await PlayerListFiles.LookupMojangAsync(_downloads.Http, name, cancellationToken)
                        ?? throw new InvalidOperationException($"No Minecraft account is called '{name}'. Check the spelling.");
            (name, uuid) = found;
        }
        else uuid = PlayerListFiles.OfflineUuid(name);
        PlayerListFiles.Add(profile.Folder, fileName, name, uuid);
        instance.AddConsole($"[Manager] Added {name} to {fileName}.");
    }

    public void RemoveFromPlayerList(ServerProfile profile, string fileName, string playerName)
    {
        var instance = GetInstance(profile);
        if (instance.CanSendCommands)
        {
            SendCommand(profile, fileName == PlayerListFiles.Ops ? $"deop {playerName}" : $"whitelist remove {playerName}");
            return;
        }
        if (instance.IsRunning) throw new InvalidOperationException("This server was recovered after MystTiq restarted, so it can't take commands. Stop it first, then edit the list.");
        if (PlayerListFiles.Remove(profile.Folder, fileName, playerName)) instance.AddConsole($"[Manager] Removed {playerName} from {fileName}.");
    }

    /// <summary>Turns the whitelist on or off (and enforce-whitelist, which kicks unlisted players).</summary>
    public void SetWhitelistEnforced(ServerProfile profile, bool enabled)
    {
        var instance = GetInstance(profile);
        if (instance.CanSendCommands) SendCommand(profile, enabled ? "whitelist on" : "whitelist off");
        else if (instance.IsRunning) throw new InvalidOperationException("This server was recovered after MystTiq restarted, so it can't take commands. Stop it first.");
        PlayerListFiles.SetWhitelistEnforced(profile.Folder, enabled);
    }

    // ---- World pre-generation (Chunky) ----

    public bool IsChunkyInstalled(ServerProfile profile) =>
        Directory.Exists(profile.Folder) && Plugins.List(profile).Any(p => p.Name.Equals("Chunky", StringComparison.OrdinalIgnoreCase) || p.FileName.StartsWith("Chunky", StringComparison.OrdinalIgnoreCase));

    /// <summary>Installs Chunky from Modrinth (exact project, hash checked) on the selected server.</summary>
    public async Task<string> InstallChunkyAsync(CancellationToken cancellationToken = default) =>
        (await InstallAddonByIdAsync("chunky", cancellationToken)).Name;

    public PregenStatus? GetPregenStatus(ServerProfile profile) => GetInstance(profile).Pregen;

    /// <summary>Starts pre-generating a square of <paramref name="radius"/> blocks around 0,0 on the running server.</summary>
    public void StartPregeneration(ServerProfile profile, int radius, bool setWorldBorder)
    {
        var instance = GetInstance(profile);
        if (!instance.CanSendCommands) throw new InvalidOperationException($"Start {profile.Name} first. Chunky runs inside the server.");
        if (!IsChunkyInstalled(profile)) throw new InvalidOperationException("Install Chunky first.");
        var commands = ChunkyProgress.StartCommands(radius, setWorldBorder);
        instance.Pregen = new PregenStatus("world", 0, 0, string.Empty, false, DateTimeOffset.Now);
        foreach (var command in commands) instance.Launcher.SendCommand(command);
        instance.AddConsole($"[Manager] Pre-generating {radius:N0} blocks around 0,0{(setWorldBorder ? $" with a {radius * 2:N0}-block world border" : string.Empty)}.");
    }

    /// <summary>Pauses, continues or cancels Chunky. Chunky remembers paused tasks across restarts.</summary>
    public void ControlPregeneration(ServerProfile profile, string action)
    {
        var instance = GetInstance(profile);
        if (!instance.CanSendCommands) throw new InvalidOperationException($"{profile.Name} isn't running.");
        switch (action)
        {
            case "pause": instance.Launcher.SendCommand("chunky pause"); break;
            case "continue": instance.Launcher.SendCommand("chunky continue"); break;
            case "cancel":
                instance.Launcher.SendCommand("chunky cancel");
                instance.Launcher.SendCommand("chunky confirm");
                instance.Pregen = null;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    // ---- Bedrock crossplay (Geyser + Floodgate) ----

    public bool IsCrossplayInstalled(ServerProfile profile) => GeyserConfig.IsInstalled(profile, Plugins.List(profile));

    /// <summary>The server's Bedrock UDP port, or null until Geyser has created its config (first start).</summary>
    public int? GetBedrockPort(ServerProfile profile) => Directory.Exists(profile.Folder) ? GeyserConfig.GetBedrockPort(profile.Folder) : null;

    /// <summary>
    /// Installs Geyser and Floodgate from Modrinth (exact projects, hash checked) so Bedrock players
    /// (phones, consoles, Windows) can join. On Fabric, Fabric API is installed too if missing.
    /// </summary>
    public async Task<IReadOnlyList<string>> InstallCrossplayAsync(CancellationToken cancellationToken = default)
    {
        var (profile, _) = RequireAddonServer();
        if (profile.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase) || profile.Type.Equals("Forge", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Geyser doesn't support {profile.Type} servers. Use Paper, Purpur, Fabric or NeoForge for crossplay.");
        var installed = new List<string>();
        if (profile.Type.Equals("Fabric", StringComparison.OrdinalIgnoreCase) && !Plugins.List(profile).Any(p => p.FileName.StartsWith("fabric-api", StringComparison.OrdinalIgnoreCase)))
            installed.Add((await InstallAddonByIdAsync("fabric-api", cancellationToken)).Name);
        installed.Add((await InstallAddonByIdAsync("geyser", cancellationToken)).Name);
        installed.Add((await InstallAddonByIdAsync("floodgate", cancellationToken)).Name);
        ConfigureCrossplay(profile, SuggestBedrockPort(profile));
        return installed;
    }

    /// <summary>Writes the Bedrock port (and Floodgate login) into Geyser's config. False if Geyser hasn't made its config yet.</summary>
    public bool ConfigureCrossplay(ServerProfile profile, int bedrockPort)
    {
        if (bedrockPort is < 1 or > 65535) throw new InvalidOperationException("The Bedrock port must be between 1 and 65535.");
        var floodgate = Plugins.List(profile).Any(p => p.Name.Contains("floodgate", StringComparison.OrdinalIgnoreCase) || p.FileName.Contains("floodgate", StringComparison.OrdinalIgnoreCase));
        return GeyserConfig.Configure(profile.Folder, bedrockPort, floodgate);
    }

    /// <summary>The first UDP port from 19132 that no other server's Geyser uses and nothing is listening on.</summary>
    public int SuggestBedrockPort(ServerProfile profile)
    {
        var used = Servers.Where(s => !ReferenceEquals(s, profile) && Directory.Exists(s.Folder))
            .Select(s => GeyserConfig.GetBedrockPort(s.Folder)).OfType<int>().ToHashSet();
        try { foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()) used.Add(endpoint.Port); }
        catch (NetworkInformationException) { }
        var port = GeyserConfig.DefaultBedrockPort;
        while (used.Contains(port) && port < 65535) port++;
        return port;
    }

    /// <summary>
    /// Before starting, moves this server's Bedrock port if another running server already uses it,
    /// so two crossplay servers never fight over UDP 19132.
    /// </summary>
    private void EnsureBedrockPortAvailable(ServerProfile profile, ServerInstance instance)
    {
        if (GetBedrockPort(profile) is not int bedrock) return;
        var clash = RunningServers.Any(s => !ReferenceEquals(s, profile) && GetBedrockPort(s) == bedrock);
        if (!clash) return;
        var free = SuggestBedrockPort(profile);
        if (ConfigureCrossplay(profile, free))
            instance.AddConsole($"[Manager] Bedrock port {bedrock} is used by another running server; moved this server's Bedrock port to {free}.");
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
                await CreateConsistentBackupAsync(profile, "Automatic", cancellationToken);
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
                        await CreateConsistentBackupAsync(target, "Automatic", cancellationToken);
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
        if (CrashRestartPolicy.IsCrash(e) && profile.CrashRecoveryEnabled) ScheduleCrashRestart(instance);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// After a crash, waits the configured delay and starts the server again, unless the restart
    /// limit for the time window is used up or the user starts, stops or removes it first.
    /// </summary>
    private void ScheduleCrashRestart(ServerInstance instance)
    {
        var profile = instance.Profile;
        var window = TimeSpan.FromMinutes(Math.Max(1, profile.CrashRestartWindowMinutes));
        if (!instance.CrashRestarts.TryRegisterRestart(DateTimeOffset.Now, profile.CrashRestartLimit, window, out var attempt))
        {
            instance.AddConsole($"[Manager] {profile.Name} crashed {attempt} time(s) within {window.TotalMinutes:0} minutes. Not restarting again; check the Health → Logs tab, then start it yourself.");
            profile.RuntimeState = "Failed";
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(0, profile.CrashRestartDelaySeconds));
        instance.CancelPendingRestart();
        var cancel = new CancellationTokenSource();
        instance.PendingRestart = cancel;
        profile.RuntimeState = "Restarting";
        instance.AddConsole($"[Manager] Restarting {profile.Name} in {delay.TotalSeconds:0} seconds (automatic restart {attempt} of {profile.CrashRestartLimit}).");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancel.Token);
                if (!ReferenceEquals(instance.PendingRestart, cancel) || instance.IsRunning || !Servers.Contains(profile)) return;
                instance.PendingRestart = null;
                cancel.Dispose();
                await StartCoreAsync(profile, CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                instance.AddConsole($"[Manager] Automatic restart failed: {ex.Message}");
                profile.RuntimeState = "Failed";
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>
    /// Refuses to start a server whose port is already used by another MystTiq server
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
        if (!File.Exists(profile.LaunchFilePath)) throw new FileNotFoundException("The configured server JAR was not found.", profile.LaunchFilePath);
        if (!string.Equals(profile.JavaPath, "java", StringComparison.OrdinalIgnoreCase) && !File.Exists(profile.JavaPath))
            throw new FileNotFoundException("The configured Java executable was not found.", profile.JavaPath);
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'), Path.GetFullPath(b).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
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
