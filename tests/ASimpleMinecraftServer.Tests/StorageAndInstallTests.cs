using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class ProfileStoreTests
{
    [Fact]
    public void Keeps_servers_whose_folder_is_missing()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save([new ServerProfile { Name = "On USB drive", Folder = @"Z:\definitely\missing\server" }]);

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Single(store.Load());
        Assert.Equal("On USB drive", store.Load()[0].Name);
    }

    [Fact]
    public void Recovers_from_corrupt_file_using_backup_copy()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save([new ServerProfile { Name = "First" }]);
        store.Save([new ServerProfile { Name = "First" }, new ServerProfile { Name = "Second" }]); // .bak now holds "First"
        File.WriteAllText(Path.Combine(dir.Path, "servers.json"), "{ broken");

        var loaded = store.Load();

        Assert.Equal(new[] { "First" }, loaded.Select(p => p.Name));
        Assert.NotNull(store.LoadWarning);
        Assert.Contains(Directory.GetFiles(dir.Path), f => Path.GetFileName(f).StartsWith("servers.json.corrupt-"));
    }

    [Fact]
    public void Corrupt_file_without_backup_starts_empty_instead_of_crashing()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "servers.json"), "not json");
        var store = new ProfileStore(dir.Path);

        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadWarning);
    }
}

public class RuntimeSessionStoreTests
{
    [Fact]
    public void Tracks_one_session_per_server_folder()
    {
        using var dir = new TempDir();
        var store = new RuntimeSessionStore(dir.Path);
        store.Save(new RuntimeSession(100, @"C:\Servers\A", DateTimeOffset.Now));
        store.Save(new RuntimeSession(200, @"C:\Servers\B", DateTimeOffset.Now));
        store.Save(new RuntimeSession(101, @"C:\Servers\A\", DateTimeOffset.Now)); // same folder, new PID

        var sessions = store.LoadAll();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.ProcessId == 101);

        store.Remove(@"C:\Servers\A");
        Assert.Equal(new[] { 200 }, store.LoadAll().Select(s => s.ProcessId));
    }

    [Fact]
    public void Migrates_the_old_single_session_file()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "runtime-session.json"),
            "{\"ProcessId\":42,\"ServerFolder\":\"C:\\\\Servers\\\\Old\",\"StartedAt\":\"2026-01-01T00:00:00+00:00\"}");

        var sessions = new RuntimeSessionStore(dir.Path).LoadAll();

        Assert.Equal(42, Assert.Single(sessions).ProcessId);
        Assert.False(File.Exists(Path.Combine(dir.Path, "runtime-session.json")));
    }
}

public class ServerProfileTests
{
    [Fact]
    public void Port_comes_from_server_properties()
    {
        using var dir = new TempDir();
        dir.File("server.properties", "motd=Test\nserver-port=25570\n");
        Assert.Equal(25570, new ServerProfile { Folder = dir.Path }.Port);
    }

    [Fact]
    public void Port_defaults_to_25565()
    {
        using var dir = new TempDir();
        Assert.Equal(25565, new ServerProfile { Folder = dir.Path }.Port);
        Assert.Equal(25565, new ServerProfile { Folder = @"Z:\missing" }.Port);
    }

    [Fact]
    public void Picker_label_marks_running_servers()
    {
        var profile = new ServerProfile { Name = "Survival" };
        Assert.Equal("Survival", profile.PickerLabel);
        profile.RuntimeState = "Running";
        Assert.Equal("● Survival", profile.PickerLabel);
    }
}

public class ScheduledTaskTests
{
    [Fact]
    public void Next_run_follows_last_run()
    {
        var created = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var task = new ScheduledTaskRecord { CreatedAt = created, IntervalMinutes = 30 };
        Assert.Equal(created.AddMinutes(30), task.NextRunAt);

        task.LastRunAt = created.AddHours(2);
        Assert.Equal(created.AddHours(2).AddMinutes(30), task.NextRunAt);
    }
}

public class BackupServiceTests
{
    [Fact]
    public async Task Restore_brings_back_files_and_removes_newer_ones()
    {
        using var dir = new TempDir();
        var server = Path.Combine(dir.Path, "server");
        Directory.CreateDirectory(Path.Combine(server, "world"));
        File.WriteAllText(Path.Combine(server, "world", "level.dat"), "original");
        var profile = new ServerProfile { Name = "Survival", Folder = server };
        var backups = new BackupService(Path.Combine(dir.Path, "backups"));

        var backup = await backups.CreateAsync(profile, "Manual");
        File.WriteAllText(Path.Combine(server, "world", "level.dat"), "changed");
        File.WriteAllText(Path.Combine(server, "griefed.txt"), "new");
        await backups.RestoreAsync(profile, backup);

        Assert.Equal("original", File.ReadAllText(Path.Combine(server, "world", "level.dat")));
        Assert.False(File.Exists(Path.Combine(server, "griefed.txt")));
    }

    [Fact]
    public async Task Backups_follow_a_server_rename()
    {
        using var dir = new TempDir();
        var server = Path.Combine(dir.Path, "server");
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(server, "server.properties"), "server-port=25565");
        var backups = new BackupService(Path.Combine(dir.Path, "backups"));
        var profile = new ServerProfile { Name = "Old Name", Folder = server };
        await backups.CreateAsync(profile, "Manual");

        backups.MoveBackupsForRename("Old Name", "New Name");
        profile.Name = "New Name";

        Assert.Single(backups.List(profile));
    }

    [Fact]
    public async Task Automatic_retention_keeps_manual_and_safety_backups()
    {
        using var dir = new TempDir();
        var server = Path.Combine(dir.Path, "server");
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(server, "a.txt"), "x");
        var backups = new BackupService(Path.Combine(dir.Path, "backups"));
        var profile = new ServerProfile { Name = "S", Folder = server };
        await backups.CreateAsync(profile, "Manual");
        await backups.CreateAsync(profile, "Safety-BeforeRestore");
        for (var i = 0; i < 3; i++) { await Task.Delay(1100); await backups.CreateAsync(profile, "Automatic"); }

        backups.ApplyAutomaticRetention(profile, keep: 1);

        var remaining = backups.List(profile);
        Assert.Single(remaining, b => b.Type == "Automatic");
        Assert.Contains(remaining, b => b.Type == "Manual");
        Assert.Contains(remaining, b => b.Type == "Safety");
    }
}

public class AddonInstallTests
{
    private const string PluginYml = "name: TestPlugin\nversion: 1.0\nmain: test.Main\n";
    private const string FabricJson = "{\"schemaVersion\":1,\"id\":\"testmod\",\"name\":\"Test Mod\",\"version\":\"2.0\",\"depends\":{\"fabric-api\":\"*\",\"minecraft\":\"1.21\"}}";

    [Fact]
    public void Plugin_installs_into_plugins_folder_on_paper()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/test-plugin.jar", ("plugin.yml", PluginYml));
        var profile = new ServerProfile { Type = "Paper", Folder = Path.Combine(dir.Path, "server") };

        var record = new PluginService().Install(profile, jar, overwrite: false);

        Assert.Equal("TestPlugin", record.Name);
        Assert.True(File.Exists(Path.Combine(profile.Folder, "plugins", "test-plugin.jar")));
    }

    [Fact]
    public void Mod_installs_into_mods_folder_on_fabric_and_reads_dependencies()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/test-mod.jar", ("fabric.mod.json", FabricJson));
        var profile = new ServerProfile { Type = "Fabric", Folder = Path.Combine(dir.Path, "server") };

        var record = new PluginService().Install(profile, jar, overwrite: false);

        Assert.Equal("Test Mod", record.Name);
        Assert.Equal("fabric-api", record.Dependencies);
        Assert.True(File.Exists(Path.Combine(profile.Folder, "mods", "test-mod.jar")));
    }

    [Fact]
    public void Refuses_a_mod_on_a_paper_server()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/test-mod.jar", ("fabric.mod.json", FabricJson));
        var profile = new ServerProfile { Type = "Paper", Folder = Path.Combine(dir.Path, "server") };
        var error = Assert.Throws<InvalidDataException>(() => new PluginService().Install(profile, jar, false));
        Assert.Contains("Fabric mod", error.Message);
    }

    [Fact]
    public void Refuses_a_plugin_on_a_fabric_server()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/test-plugin.jar", ("plugin.yml", PluginYml));
        var profile = new ServerProfile { Type = "Fabric", Folder = Path.Combine(dir.Path, "server") };
        Assert.Throws<InvalidDataException>(() => new PluginService().Install(profile, jar, false));
    }

    [Fact]
    public void Refuses_client_only_mods()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/minimap.jar", ("fabric.mod.json", "{\"id\":\"minimap\",\"name\":\"Minimap\",\"environment\":\"client\"}"));
        var profile = new ServerProfile { Type = "Fabric", Folder = Path.Combine(dir.Path, "server") };
        var error = Assert.Throws<InvalidDataException>(() => new PluginService().Install(profile, jar, false));
        Assert.Contains("client-only", error.Message);
    }

    [Fact]
    public void Refuses_addons_on_vanilla()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/test-plugin.jar", ("plugin.yml", PluginYml));
        var profile = new ServerProfile { Type = "Vanilla", Folder = Path.Combine(dir.Path, "server") };
        Assert.Throws<InvalidOperationException>(() => new PluginService().Install(profile, jar, false));
    }
}

public class ServerInstallerTests
{
    [Fact]
    public async Task Refuses_to_install_without_eula_acceptance()
    {
        using var dir = new TempDir();
        using var downloads = new DownloadService();
        var installer = new ServerInstaller(downloads, new VersionManager(downloads));
        var request = new ServerInstallRequest("Test", Path.Combine(dir.Path, "Test"), "Vanilla", "1.21.4", 2, null, EulaAccepted: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(request));

        Assert.Contains("EULA", error.Message);
        Assert.False(File.Exists(Path.Combine(dir.Path, "Test", "eula.txt")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public async Task Refuses_invalid_ports(int port)
    {
        using var dir = new TempDir();
        using var downloads = new DownloadService();
        var installer = new ServerInstaller(downloads, new VersionManager(downloads));
        var request = new ServerInstallRequest("Test", Path.Combine(dir.Path, "Test"), "Vanilla", "1.21.4", 2, null, EulaAccepted: true, Port: port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(request));
    }
}

public class NetworkAddressTests
{
    [Fact]
    public void Lan_addresses_are_ipv4_and_never_loopback()
    {
        foreach (var address in NetworkAddressService.GetLanAddresses())
        {
            Assert.True(System.Net.IPAddress.TryParse(address, out var ip));
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, ip!.AddressFamily);
            Assert.False(System.Net.IPAddress.IsLoopback(ip));
            Assert.False(address.StartsWith("169.254."));
        }
    }
}
