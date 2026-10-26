using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class ForgeVersionTests
{
    [Theory]
    [InlineData("21.1.72", "1.21.1")]
    [InlineData("21.0.10-beta", "1.21")]
    [InlineData("20.4.237", "1.20.4")]
    [InlineData("26.1.0.5", "26.1")]
    [InlineData("26.1.1.2-beta", "26.1.1")]
    [InlineData("47.1.82", null)]
    [InlineData("nonsense", null)]
    public void Maps_neoforge_versions_to_minecraft(string neoForge, string? minecraft) =>
        Assert.Equal(minecraft, VersionManager.NeoForgeMinecraftVersion(neoForge));

    [Theory]
    [InlineData("1.20.1-47.3.0", "1.20.1")]
    [InlineData("1.21-51.0.33", "1.21")]
    [InlineData("1.5.2-7.8.1.738", null)]
    [InlineData("1.7.10_pre4-10.12.2.1149-prerelease", null)]
    public void Maps_forge_versions_to_minecraft(string forge, string? minecraft) =>
        Assert.Equal(minecraft, VersionManager.ForgeMinecraftVersion(forge));

    [Fact]
    public void Picks_the_newest_stable_loader_for_the_minecraft_version()
    {
        string[] versions = ["21.1.9", "21.1.72", "21.1.100-beta", "21.0.167", "20.4.237"];
        Assert.Equal("21.1.72", VersionManager.PickLoaderVersion(versions, "1.21.1", VersionManager.NeoForgeMinecraftVersion));
        Assert.Equal("21.0.167", VersionManager.PickLoaderVersion(versions, "1.21", VersionManager.NeoForgeMinecraftVersion));
        Assert.Null(VersionManager.PickLoaderVersion(versions, "1.19.2", VersionManager.NeoForgeMinecraftVersion));
    }

    [Fact]
    public void Falls_back_to_a_beta_when_no_stable_exists()
    {
        string[] versions = ["26.1.0.1-beta", "26.1.0.3-beta"];
        Assert.Equal("26.1.0.3-beta", VersionManager.PickLoaderVersion(versions, "26.1", VersionManager.NeoForgeMinecraftVersion));
    }

    [Fact]
    public void Lists_forge_and_neoforge_as_server_types() =>
        Assert.Contains("Forge", VersionManager.SupportedServerTypes);
}

public class ForgeFolderTests
{
    [Fact]
    public void Finds_neoforge_argument_file()
    {
        using var dir = new TempDir();
        dir.File("libraries/net/neoforged/neoforge/21.1.72/win_args.txt", "-cp x");
        var target = ServerFolderInspector.FindForgeLaunchTarget(dir.Path);
        Assert.Equal("@libraries/net/neoforged/neoforge/21.1.72/win_args.txt", target);
        Assert.Equal("NeoForge", ServerFolderInspector.DetectServerType(dir.Path, target!));
        Assert.Equal("1.21.1", ServerFolderInspector.DetectVersion(dir.Path, target!));
    }

    [Fact]
    public void Finds_forge_argument_file()
    {
        using var dir = new TempDir();
        dir.File("libraries/net/minecraftforge/forge/1.20.1-47.3.0/win_args.txt", "-cp x");
        var target = ServerFolderInspector.FindForgeLaunchTarget(dir.Path);
        Assert.Equal("@libraries/net/minecraftforge/forge/1.20.1-47.3.0/win_args.txt", target);
        Assert.Equal("Forge", ServerFolderInspector.DetectServerType(dir.Path, target!));
        Assert.Equal("1.20.1", ServerFolderInspector.DetectVersion(dir.Path, target!));
    }

    [Fact]
    public void Finds_old_style_forge_jar_but_not_the_installer()
    {
        using var dir = new TempDir();
        dir.File("forge-1.12.2-14.23.5.2860-installer.jar");
        dir.File("forge-1.12.2-14.23.5.2860.jar");
        var target = ServerFolderInspector.FindForgeLaunchTarget(dir.Path);
        Assert.Equal("forge-1.12.2-14.23.5.2860.jar", target);
        Assert.Equal("Forge", ServerFolderInspector.DetectServerType(dir.Path, target!));
        Assert.Equal("1.12.2", ServerFolderInspector.DetectVersion(dir.Path, target!));
    }

    [Fact]
    public void No_forge_target_in_a_paper_folder()
    {
        using var dir = new TempDir();
        dir.File("paper.jar");
        Assert.Null(ServerFolderInspector.FindForgeLaunchTarget(dir.Path));
    }

    [Fact]
    public void Profile_launch_file_strips_the_at_sign()
    {
        var profile = new ServerProfile { Folder = @"C:\srv", Jar = "@libraries/net/neoforged/neoforge/21.1.72/win_args.txt" };
        Assert.True(profile.UsesArgsFile);
        Assert.Equal(Path.Combine(@"C:\srv", "libraries/net/neoforged/neoforge/21.1.72/win_args.txt"), profile.LaunchFilePath);
        Assert.False(new ServerProfile { Folder = @"C:\srv", Jar = "server.jar" }.UsesArgsFile);
    }
}

public class ForgeModTests
{
    private const string NeoToml = "modLoader=\"javafml\"\nloaderVersion=\"[4,)\"\n[[mods]]\nmodId=\"testmod\"\nversion=\"3.1\"\ndisplayName=\"Test Neo Mod\"\n";

    [Fact]
    public void Installs_a_neoforge_mod_and_reads_mods_toml()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/neo.jar", ("META-INF/neoforge.mods.toml", NeoToml));
        var profile = new ServerProfile { Type = "NeoForge", Folder = Path.Combine(dir.Path, "server") };

        var record = new PluginService().Install(profile, jar, overwrite: false);

        Assert.Equal("Test Neo Mod", record.Name);
        Assert.Equal("3.1", record.Version);
        Assert.Equal("neoforge", record.ModLoader);
        Assert.True(File.Exists(Path.Combine(profile.Folder, "mods", "neo.jar")));
    }

    [Fact]
    public void Refuses_a_neoforge_mod_on_forge()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/neo.jar", ("META-INF/neoforge.mods.toml", NeoToml));
        var profile = new ServerProfile { Type = "Forge", Folder = Path.Combine(dir.Path, "server") };
        var error = Assert.Throws<InvalidDataException>(() => new PluginService().Install(profile, jar, false));
        Assert.Contains("NeoForge mod", error.Message);
    }

    [Fact]
    public void Refuses_a_fabric_mod_on_neoforge()
    {
        using var dir = new TempDir();
        var jar = dir.Jar("incoming/fab.jar", ("fabric.mod.json", "{\"id\":\"fab\",\"name\":\"Fab\"}"));
        var profile = new ServerProfile { Type = "NeoForge", Folder = Path.Combine(dir.Path, "server") };
        Assert.Throws<InvalidDataException>(() => new PluginService().Install(profile, jar, false));
    }

    [Theory]
    [InlineData("NeoForge", "forge", true)]
    [InlineData("NeoForge", "neoforge", true)]
    [InlineData("Forge", "neoforge", false)]
    [InlineData("Fabric", "forge", false)]
    [InlineData("Paper", "fabric", false)]
    public void Loader_compatibility(string serverType, string modLoader, bool fits) =>
        Assert.Equal(fits, PluginService.LoaderFits(serverType, modLoader));

    [Fact]
    public void Modrinth_loader_facets_follow_the_server_type()
    {
        Assert.Equal(["neoforge"], AddonKinds.ModrinthLoaders("NeoForge"));
        Assert.Equal(["forge"], AddonKinds.ModrinthLoaders("Forge"));
        Assert.Contains("paper", AddonKinds.ModrinthLoaders("Purpur"));
        Assert.Equal(AddonKind.Mod, AddonKinds.For(new ServerProfile { Type = "Forge" }));
    }
}
