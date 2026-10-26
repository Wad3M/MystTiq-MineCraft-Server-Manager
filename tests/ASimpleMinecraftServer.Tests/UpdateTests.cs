using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class ServerUpdateTests
{
    [Theory]
    [InlineData("1.21.4", "1.21.3", true)]
    [InlineData("1.21.4", "1.21.4", false)]
    [InlineData("1.21.4", "1.21.10", false)]
    [InlineData("1.21", "1.21.1", false)]
    [InlineData("1.21.1", "1.21", true)]
    [InlineData("1.21.11", "26.1", false)]
    [InlineData("26.1", "1.21.11", true)]
    [InlineData("", "1.21.4", false)]
    public void Detects_downgrades(string current, string candidate, bool downgrade) =>
        Assert.Equal(downgrade, ServerInstaller.IsDowngrade(current, candidate));

    [Fact]
    public async Task Refuses_to_update_custom_jars()
    {
        using var dir = new TempDir();
        using var downloads = new DownloadService();
        var installer = new ServerInstaller(downloads, new VersionManager(downloads));
        var profile = new ServerProfile { Name = "Custom", Folder = dir.Path, Type = "Custom JAR", Version = "1.21.4" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.UpdateAsync(profile, "1.21.4"));
    }

    [Fact]
    public void Newest_forge_install_wins_after_an_update()
    {
        using var dir = new TempDir();
        var old = dir.File("libraries/net/neoforged/neoforge/21.1.9/win_args.txt");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        dir.File("libraries/net/neoforged/neoforge/21.1.72/win_args.txt");
        Assert.Equal("@libraries/net/neoforged/neoforge/21.1.72/win_args.txt", ServerFolderInspector.FindForgeLaunchTarget(dir.Path));
    }
}
