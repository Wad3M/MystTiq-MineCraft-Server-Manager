using ASimpleMinecraftServer.Core;
using System.IO;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class PlayerListTests
{
    [Theory]
    [InlineData("Notch", "b50ad385-829d-3141-a216-7e7d7539ba7f")]
    [InlineData("Steve", "5627dd98-e6be-3c21-b8a8-e92344183641")]
    public void Offline_uuid_matches_minecraft(string name, string expected) =>
        Assert.Equal(expected, PlayerListFiles.OfflineUuid(name));

    [Theory]
    [InlineData("Notch", true)]
    [InlineData("a_b_c", true)]
    [InlineData("ab", false)]                     // too short
    [InlineData("seventeen_chars_x", false)]      // too long
    [InlineData("bad name", false)]
    [InlineData("bad;stop", false)]               // can't smuggle a second command
    public void Validates_usernames(string name, bool valid) =>
        Assert.Equal(valid, PlayerListFiles.IsValidName(name));

    [Fact]
    public void Adds_updates_and_removes_whitelist_entries()
    {
        using var dir = new TempDir();
        PlayerListFiles.Add(dir.Path, PlayerListFiles.Whitelist, "Alex", "uuid-1");
        PlayerListFiles.Add(dir.Path, PlayerListFiles.Whitelist, "alex", "uuid-2");   // same player, different case
        PlayerListFiles.Add(dir.Path, PlayerListFiles.Whitelist, "Steve", "uuid-3");

        var list = PlayerListFiles.Read(dir.Path, PlayerListFiles.Whitelist);
        Assert.Equal(new[] { "alex", "Steve" }, list.Select(p => p.Name));
        Assert.Equal("uuid-2", list[0].Uuid);

        Assert.True(PlayerListFiles.Remove(dir.Path, PlayerListFiles.Whitelist, "STEVE"));
        Assert.Single(PlayerListFiles.Read(dir.Path, PlayerListFiles.Whitelist));
        Assert.False(PlayerListFiles.Remove(dir.Path, PlayerListFiles.Whitelist, "Nobody"));
    }

    [Fact]
    public void Operators_get_level_4()
    {
        using var dir = new TempDir();
        PlayerListFiles.Add(dir.Path, PlayerListFiles.Ops, "Admin", "uuid-1");
        Assert.Equal(4, Assert.Single(PlayerListFiles.Read(dir.Path, PlayerListFiles.Ops)).Level);
        Assert.Contains("\"bypassesPlayerLimit\"", File.ReadAllText(Path.Combine(dir.Path, PlayerListFiles.Ops)));
    }

    [Fact]
    public void Whitelist_switch_sets_both_properties_and_keeps_others()
    {
        using var dir = new TempDir();
        dir.File("server.properties", "motd=Hi\nwhite-list=false\nserver-port=25566\n");

        PlayerListFiles.SetWhitelistEnforced(dir.Path, true);

        Assert.True(PlayerListFiles.IsWhitelistEnforced(dir.Path));
        Assert.Equal("true", PlayerListFiles.ReadProperty(dir.Path, "enforce-whitelist"));
        Assert.Equal("25566", PlayerListFiles.ReadProperty(dir.Path, "server-port"));
        Assert.Equal("Hi", PlayerListFiles.ReadProperty(dir.Path, "motd"));
    }

    [Fact]
    public void Online_mode_defaults_to_true()
    {
        using var dir = new TempDir();
        Assert.True(PlayerListFiles.IsOnlineMode(dir.Path));
        dir.File("server.properties", "online-mode=false\n");
        Assert.False(PlayerListFiles.IsOnlineMode(dir.Path));
    }

    [Fact]
    public void Broken_list_file_reads_as_empty()
    {
        using var dir = new TempDir();
        dir.File(PlayerListFiles.Whitelist, "{ not json");
        Assert.Empty(PlayerListFiles.Read(dir.Path, PlayerListFiles.Whitelist));
    }
}
