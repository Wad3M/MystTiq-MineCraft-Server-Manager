using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

public sealed record ServerHealthCheck(string Name, bool Passed, string Details);

public static class ServerManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<ServerHealthCheck> Verify(ServerProfile profile)
    {
        var checks = new List<ServerHealthCheck>();
        var folderOk = Directory.Exists(profile.Folder);
        checks.Add(new("Server folder", folderOk, folderOk ? profile.Folder : "Folder was not found."));
        var jar = Path.Combine(profile.Folder, profile.Jar);
        checks.Add(new("Server JAR", File.Exists(jar), File.Exists(jar) ? profile.Jar : $"Missing: {jar}"));
        var eula = Path.Combine(profile.Folder, "eula.txt");
        var eulaOk = File.Exists(eula) && File.ReadAllLines(eula).Any(line => line.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
        checks.Add(new("Minecraft EULA", eulaOk, eulaOk ? "Accepted" : "eula=true was not found."));
        var properties = Path.Combine(profile.Folder, "server.properties");
        checks.Add(new("server.properties", File.Exists(properties), File.Exists(properties) ? "Found" : "File will normally be created after first launch."));
        var world = Path.Combine(profile.Folder, "world");
        checks.Add(new("World folder", Directory.Exists(world), Directory.Exists(world) ? "Found" : "No world has been generated yet."));
        var javaOk = profile.JavaPath.Equals("java", StringComparison.OrdinalIgnoreCase) || File.Exists(profile.JavaPath);
        checks.Add(new("Java runtime", javaOk, javaOk ? profile.JavaPath : "Configured Java executable was not found."));
        return checks;
    }

    public static void ExportProfile(ServerProfile profile, string destination)
    {
        File.WriteAllText(destination, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public static ServerProfile ImportProfile(string source)
    {
        var profile = JsonSerializer.Deserialize<ServerProfile>(File.ReadAllText(source), JsonOptions)
            ?? throw new InvalidDataException("The selected profile file is invalid.");
        if (string.IsNullOrWhiteSpace(profile.Folder)) throw new InvalidDataException("The profile does not contain a server folder.");
        return profile;
    }

    public static async Task CopyDirectoryAsync(string source, string destination, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        Directory.CreateDirectory(destination);
        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, files[i]);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
            progress?.Report(files.Length == 0 ? 100 : (i + 1) * 100 / files.Length);
        }
    }

    public static void ExportServerZip(ServerProfile profile, string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        ZipFile.CreateFromDirectory(profile.Folder, destination, CompressionLevel.Fastest, includeBaseDirectory: false);
    }
}
