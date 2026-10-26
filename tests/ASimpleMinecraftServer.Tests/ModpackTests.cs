using ASimpleMinecraftServer.Core;
using System.IO;
using System.Text.Json;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class ModpackTests
{
    private const string Sha = "aa11";

    private static string Index(string files, string deps = "\"minecraft\":\"1.21.1\",\"fabric-loader\":\"0.16.5\"") =>
        $"{{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"2.0\",\"name\":\"Test Pack\",\"dependencies\":{{{deps}}},\"files\":[{files}]}}";

    private static string File(string path, string url = "https://cdn.modrinth.com/data/abc/versions/1/mod.jar", string server = "required") =>
        $"{{\"path\":\"{path}\",\"hashes\":{{\"sha1\":\"x\",\"sha512\":\"{Sha}\"}},\"env\":{{\"client\":\"required\",\"server\":\"{server}\"}},\"downloads\":[\"{url}\"],\"fileSize\":10}}";

    private static ModpackPlan Parse(string json) => ModpackReader.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Reads_versions_loader_and_server_files()
    {
        var plan = Parse(Index(File("mods/lithium.jar") + "," + File("mods/minimap.jar", server: "unsupported")));

        Assert.Equal("Test Pack", plan.Name);
        Assert.Equal("1.21.1", plan.MinecraftVersion);
        Assert.Equal("Fabric", plan.Loader);
        Assert.Equal("0.16.5", plan.LoaderVersion);
        Assert.Equal("mods/lithium.jar", Assert.Single(plan.ServerFiles).Path);
        Assert.Equal(1, plan.SkippedClientOnly);
    }

    [Theory]
    [InlineData("\"minecraft\":\"1.20.1\",\"forge\":\"47.2.0\"", "Forge")]
    [InlineData("\"minecraft\":\"1.21.1\",\"neoforge\":\"21.1.66\"", "NeoForge")]
    [InlineData("\"minecraft\":\"1.21.1\",\"quilt-loader\":\"0.26.0\"", "Quilt")]
    [InlineData("\"minecraft\":\"1.21.1\"", "Vanilla")]
    public void Detects_the_loader(string deps, string loader) =>
        Assert.Equal(loader, Parse(Index("", deps)).Loader);

    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("mods/../../outside.jar")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("mods\\\\..\\\\..\\\\evil.jar")]
    public void Rejects_paths_that_escape_the_server_folder(string path) =>
        Assert.Throws<InvalidDataException>(() => Parse(Index(File(path))));

    [Theory]
    [InlineData("https://evil.example.com/mod.jar")]
    [InlineData("http://cdn.modrinth.com/data/abc/mod.jar")]   // not HTTPS
    public void Rejects_downloads_from_disallowed_hosts(string url) =>
        Assert.Throws<InvalidDataException>(() => Parse(Index(File("mods/a.jar", url))));

    [Fact]
    public void Rejects_packs_for_other_games() =>
        Assert.Throws<InvalidDataException>(() => Parse("{\"game\":\"other\",\"dependencies\":{\"minecraft\":\"1\"}}"));

    [Fact]
    public void Server_overrides_win_over_overrides()
    {
        using var dir = new TempDir();
        var pack = dir.Jar("pack.mrpack",
            ("modrinth.index.json", Index("")),
            ("overrides/config/a.toml", "client"),
            ("server-overrides/config/a.toml", "server"),
            ("overrides/server.properties", "motd=Pack"));
        var server = Path.Combine(dir.Path, "server");

        var count = ModpackReader.ApplyOverrides(pack, server);

        Assert.Equal(3, count);
        Assert.Equal("server", System.IO.File.ReadAllText(Path.Combine(server, "config", "a.toml")));
        Assert.True(System.IO.File.Exists(Path.Combine(server, "server.properties")));
    }

    [Fact]
    public void Overrides_cannot_escape_the_server_folder()
    {
        using var dir = new TempDir();
        var pack = dir.Jar("pack.mrpack", ("modrinth.index.json", Index("")), ("overrides/../escaped.txt", "x"));
        var server = Path.Combine(dir.Path, "server");

        Assert.Throws<InvalidDataException>(() => ModpackReader.ApplyOverrides(pack, server));
        Assert.False(System.IO.File.Exists(Path.Combine(dir.Path, "escaped.txt")));
    }

    [Fact]
    public void Reads_a_real_mrpack_file()
    {
        using var dir = new TempDir();
        var pack = dir.Jar("pack.mrpack", ("modrinth.index.json", Index(File("mods/lithium.jar"))));
        Assert.Single(ModpackReader.Read(pack).ServerFiles);
    }
}
