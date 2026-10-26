using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_to_closing_to_the_tray()
    {
        using var dir = new TempDir();
        var settings = new AppSettingsStore(dir.Path).Load();
        Assert.True(settings.CloseToTray);
        Assert.False(settings.TrayTipShown);
    }

    [Fact]
    public void Saves_and_loads_settings()
    {
        using var dir = new TempDir();
        var store = new AppSettingsStore(dir.Path);
        store.Save(new AppSettings { CloseToTray = false, TrayTipShown = true });
        var loaded = store.Load();
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.TrayTipShown);
    }

    [Fact]
    public void A_broken_file_means_defaults()
    {
        using var dir = new TempDir();
        dir.File("app-settings.json", "{ not json");
        Assert.True(new AppSettingsStore(dir.Path).Load().CloseToTray);
    }

    [Fact]
    public void Sign_in_command_quotes_the_path_and_starts_in_the_tray() =>
        Assert.Equal("\"C:\\Program Files\\MystTiq\\MystTiq.exe\" --minimized", WindowsStartup.Command(@"C:\Program Files\MystTiq\MystTiq.exe"));

    [Fact]
    public void Auto_start_is_off_by_default_and_saved()
    {
        using var dir = new TempDir();
        Assert.False(new ServerProfile().AutoStart);
        var store = new ProfileStore(dir.Path);
        store.Save([new ServerProfile { Name = "Survival", Folder = Path.Combine(dir.Path, "s"), AutoStart = true }]);
        Assert.True(store.Load().Single().AutoStart);
    }
}
