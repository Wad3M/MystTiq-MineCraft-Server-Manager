using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
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
