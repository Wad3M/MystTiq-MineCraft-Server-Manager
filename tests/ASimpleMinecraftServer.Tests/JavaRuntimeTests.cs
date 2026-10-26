using ASimpleMinecraftServer.Services;
using System.IO;
using System.Text.Json;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class JavaRuntimeTests
{
    [Theory]
    [InlineData(8, 8)]
    [InlineData(16, 17)]
    [InlineData(17, 17)]
    [InlineData(21, 21)]
    [InlineData(25, 25)]
    public void Rounds_up_to_a_temurin_release(int required, int runtime) =>
        Assert.Equal(runtime, JavaRuntimeService.RuntimeFor(required));

    [Theory]
    [InlineData("1.16.5", 8)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.21.4", 21)]
    [InlineData("26.1", 25)]
    public void Picks_the_java_a_minecraft_version_needs(string minecraft, int runtime) =>
        Assert.Equal(runtime, JavaRuntimeService.RuntimeFor(JavaDetector.GetRequiredMajorVersion(minecraft)));

    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Parses_the_windows_zip_from_adoptium()
    {
        var json = $$"""
        [{"binary":{"os":"windows","architecture":"x64","image_type":"jre",
          "installer":{"link":"https://github.com/adoptium/x/OpenJDK21U-jre_x64_windows.msi","checksum":"{{Sha}}"},
          "package":{"link":"https://github.com/adoptium/x/OpenJDK21U-jre_x64_windows.zip","checksum":"{{Sha}}","size":47185920}},
          "release_name":"jdk-21.0.5+11"}]
        """;
        using var doc = JsonDocument.Parse(json);
        var asset = JavaRuntimeService.ParseLatestAsset(doc.RootElement);
        Assert.EndsWith(".zip", asset.Url);
        Assert.Equal(Sha, asset.Sha256);
        Assert.Equal("jdk-21.0.5+11", asset.ReleaseName);
        Assert.Equal(47185920, asset.Size);
    }

    [Fact]
    public void Refuses_an_asset_without_a_checksum()
    {
        using var doc = JsonDocument.Parse("""[{"binary":{"package":{"link":"https://github.com/a/b.zip"}},"release_name":"x"}]""");
        Assert.Throws<InvalidDataException>(() => JavaRuntimeService.ParseLatestAsset(doc.RootElement));
    }

    [Fact]
    public void Refuses_a_plain_http_link()
    {
        using var doc = JsonDocument.Parse($$"""[{"binary":{"package":{"link":"http://example.com/b.zip","checksum":"{{Sha}}"}}}]""");
        Assert.Throws<InvalidDataException>(() => JavaRuntimeService.ParseLatestAsset(doc.RootElement));
    }

    [Fact]
    public void Finds_a_downloaded_runtime_and_ignores_unfinished_downloads()
    {
        using var dir = new TempDir();
        var service = new JavaRuntimeService(new DownloadService(), dir.Path);
        Assert.Null(service.FindInstalled(21));
        dir.File("temurin-21/.download-abc/x/bin/java.exe");
        Assert.Null(service.FindInstalled(21));
        var java = dir.File("temurin-21/jdk-21.0.5_11-jre/bin/java.exe");
        Assert.Equal(java, service.FindInstalled(21));
        Assert.Null(service.FindInstalled(17));
    }
}
