using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class JavaDetectorTests
{
    [Theory]
    [InlineData("1.8.9", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.18", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.11-rc1", 21)]
    [InlineData("26.1", 25)]
    [InlineData("", 21)]
    [InlineData(null, 21)]
    [InlineData("Detected", 21)]
    public void Required_java_matches_minecraft_version(string? minecraft, int expected) =>
        Assert.Equal(expected, JavaDetector.GetRequiredMajorVersion(minecraft));

    [Theory]
    [InlineData("openjdk version \"21.0.2\" 2024-01-16", 21)]
    [InlineData("java version \"1.8.0_391\"", 8)]
    [InlineData("openjdk version \"17.0.10\" 2024-01-16 LTS", 17)]
    [InlineData("openjdk version \"25\" 2025-09-16", 25)]
    [InlineData("not java output", 0)]
    public void Parses_java_major_version(string output, int expected) =>
        Assert.Equal(expected, JavaDetector.ParseMajorVersion(output));
}

public class AddonKindTests
{
    [Theory]
    [InlineData("Paper", AddonKind.Plugin)]
    [InlineData("Purpur", AddonKind.Plugin)]
    [InlineData("Folia", AddonKind.Plugin)]
    [InlineData("Paper/Spigot", AddonKind.Plugin)]
    [InlineData("Fabric", AddonKind.Mod)]
    [InlineData("Vanilla", AddonKind.None)]
    [InlineData("Custom JAR", AddonKind.None)]
    public void Server_type_decides_addon_kind(string type, AddonKind expected) =>
        Assert.Equal(expected, AddonKinds.For(new ServerProfile { Type = type }));

    [Fact]
    public void Folder_names_match_minecraft_conventions()
    {
        Assert.Equal("mods", AddonKinds.FolderName(AddonKind.Mod));
        Assert.Equal("plugins", AddonKinds.FolderName(AddonKind.Plugin));
    }
}

public class ServerFolderInspectorTests
{
    [Fact]
    public void Prefers_server_jar_over_other_jars()
    {
        using var dir = new TempDir();
        dir.File("aaa-library.jar");
        dir.File("server.jar");
        Assert.Equal("server.jar", ServerFolderInspector.FindServerJar(dir.Path));
    }

    [Fact]
    public void Picks_named_server_jar_when_no_server_jar()
    {
        using var dir = new TempDir();
        dir.File("aaa-library.jar");
        dir.File("paper-1.21.4-123.jar");
        Assert.Equal("paper-1.21.4-123.jar", ServerFolderInspector.FindServerJar(dir.Path));
    }

    [Fact]
    public void Defaults_to_server_jar_when_folder_has_none()
    {
        using var dir = new TempDir();
        Assert.Equal("server.jar", ServerFolderInspector.FindServerJar(dir.Path));
    }

    [Fact]
    public void Reads_version_from_paper_version_history()
    {
        using var dir = new TempDir();
        dir.File("version_history.json", "{\"currentVersion\":\"git-Paper-123 (MC: 1.21.4)\"}");
        Assert.Equal("1.21.4", ServerFolderInspector.DetectVersion(dir.Path, "server.jar"));
    }

    [Fact]
    public void Reads_version_from_vanilla_jar()
    {
        using var dir = new TempDir();
        dir.Jar("server.jar", ("version.json", "{\"id\":\"1.21.4\",\"name\":\"1.21.4\"}"));
        Assert.Equal("1.21.4", ServerFolderInspector.DetectVersion(dir.Path, "server.jar"));
    }

    [Fact]
    public void Unknown_version_is_empty_not_a_placeholder()
    {
        using var dir = new TempDir();
        dir.File("version_history.json", "not json");
        dir.File("server.jar", "not a zip");
        Assert.Equal(string.Empty, ServerFolderInspector.DetectVersion(dir.Path, "server.jar"));
    }

    [Theory]
    [InlineData("purpur-1.21.jar", "Purpur")]
    [InlineData("paper.jar", "Paper")]
    [InlineData("folia.jar", "Folia")]
    [InlineData("fabric-server-launch.jar", "Fabric")]
    public void Detects_type_from_jar_name(string jar, string expected)
    {
        using var dir = new TempDir();
        Assert.Equal(expected, ServerFolderInspector.DetectServerType(dir.Path, jar));
    }

    [Fact]
    public void Detects_bukkit_style_server_from_plugins_folder()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "plugins"));
        Assert.Equal("Paper/Spigot", ServerFolderInspector.DetectServerType(dir.Path, "server.jar"));
    }
}

public class CrashRestartPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, 1, true)]    // exited with an error, nobody asked: crash
    [InlineData(false, null, true)] // exit code unknown: treat as crash
    [InlineData(false, 0, false)]   // clean exit (e.g. /stop in game): not a crash
    [InlineData(true, 1, false)]    // user stopped or killed it: not a crash
    public void Classifies_crashes(bool stopRequested, int? exitCode, bool expected) =>
        Assert.Equal(expected, CrashRestartPolicy.IsCrash(new ServerProcessExitedEventArgs { StopWasRequested = stopRequested, ExitCode = exitCode }));

    [Fact]
    public void Allows_restarts_up_to_the_limit_within_the_window()
    {
        var policy = new CrashRestartPolicy();
        var window = TimeSpan.FromMinutes(15);

        Assert.True(policy.TryRegisterRestart(T0, 3, window, out var first));
        Assert.True(policy.TryRegisterRestart(T0.AddMinutes(1), 3, window, out _));
        Assert.True(policy.TryRegisterRestart(T0.AddMinutes(2), 3, window, out var third));
        Assert.False(policy.TryRegisterRestart(T0.AddMinutes(3), 3, window, out var refused));

        Assert.Equal(1, first);
        Assert.Equal(3, third);
        Assert.Equal(3, refused);
    }

    [Fact]
    public void Old_crashes_fall_out_of_the_window()
    {
        var policy = new CrashRestartPolicy();
        var window = TimeSpan.FromMinutes(15);
        policy.TryRegisterRestart(T0, 1, window, out _);

        Assert.False(policy.TryRegisterRestart(T0.AddMinutes(10), 1, window, out _));
        Assert.True(policy.TryRegisterRestart(T0.AddMinutes(16), 1, window, out _));
    }

    [Fact]
    public void Reset_forgets_earlier_crashes()
    {
        var policy = new CrashRestartPolicy();
        policy.TryRegisterRestart(T0, 1, TimeSpan.FromHours(1), out _);
        policy.Reset();
        Assert.True(policy.TryRegisterRestart(T0.AddSeconds(1), 1, TimeSpan.FromHours(1), out _));
    }
}

public class OverlayNetworkTests
{
    [Theory]
    [InlineData("Tailscale Tunnel", "100.101.102.103", "Tailscale")]
    [InlineData("Wintun Userspace Tunnel", "100.64.0.1", "Tailscale")]   // recognised by its address range
    [InlineData("ZeroTier Virtual Port", "10.147.17.5", "ZeroTier")]
    [InlineData("LogMeIn Hamachi Virtual Ethernet Adapter", "25.1.2.3", "Hamachi")]
    [InlineData("Famatech Radmin VPN Ethernet Adapter", "26.10.20.30", "Radmin VPN")]
    [InlineData("Intel(R) Wi-Fi 6 AX201", "192.168.1.20", null)]
    [InlineData("Realtek PCIe GbE Family Controller", "100.63.255.255", null)]   // just below the Tailscale range
    [InlineData("Realtek PCIe GbE Family Controller", "100.128.0.1", null)]      // just above it
    public void Classifies_overlay_networks(string description, string address, string? expected) =>
        Assert.Equal(expected, NetworkAddressService.ClassifyOverlay(description, address));
}

public class GeyserConfigTests
{
    private const string Sample = """
        # Geyser configuration
        bedrock:
          # The IP address that will listen for connections.
          address: 0.0.0.0
          # The port that will listen for connections
          port: 19132
          clone-remote-port: false
        remote:
          address: auto
          port: 25565
          auth-type: online
        """;

    [Fact]
    public void Reads_and_sets_only_the_bedrock_port()
    {
        using var dir = new TempDir();
        var path = dir.File(Path.Combine("plugins", "Geyser-Spigot", "config.yml"), Sample);

        Assert.Equal(19132, GeyserConfig.GetBedrockPort(dir.Path));
        Assert.True(GeyserConfig.Configure(dir.Path, 19133, useFloodgate: true));

        var lines = File.ReadAllLines(path);
        Assert.Equal(19133, GeyserConfig.GetBedrockPort(dir.Path));
        Assert.Equal("25565", GeyserConfig.ReadKeyInSection(lines, "remote", "port"));       // Java port untouched
        Assert.Equal("floodgate", GeyserConfig.ReadKeyInSection(lines, "remote", "auth-type"));
        Assert.Contains(lines, l => l.Trim() == "# The port that will listen for connections"); // comments kept
    }

    [Fact]
    public void No_config_until_geyser_has_run()
    {
        using var dir = new TempDir();
        Assert.Null(GeyserConfig.GetBedrockPort(dir.Path));
        Assert.False(GeyserConfig.Configure(dir.Path, 19133, useFloodgate: false));
    }

    [Fact]
    public void Finds_the_fabric_config_location()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("config", "Geyser-Fabric", "config.yml"), Sample);
        Assert.Equal(19132, GeyserConfig.GetBedrockPort(dir.Path));
    }

    [Fact]
    public void Keeps_trailing_comments_when_setting_a_value()
    {
        var lines = new List<string> { "bedrock:", "  port: 19132 # default" };
        GeyserConfig.SetKeyInSection(lines, "bedrock", "port", "19140");
        Assert.Equal("  port: 19140 # default", lines[1]);
    }
}
