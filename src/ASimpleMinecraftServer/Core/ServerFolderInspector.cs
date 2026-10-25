using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

/// <summary>
/// Works out what an existing server folder contains (server JAR, server type, Minecraft version)
/// when it is added with Add Existing Server. Reads files only; never changes the folder.
/// </summary>
public static partial class ServerFolderInspector
{
    /// <summary>
    /// The server JAR to launch: server.jar if present, otherwise a JAR whose name names a
    /// known server, otherwise the first JAR. Returns "server.jar" when the folder has none.
    /// </summary>
    public static string FindServerJar(string folder)
    {
        var jars = Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return jars.FirstOrDefault(name => name.Equals("server.jar", StringComparison.OrdinalIgnoreCase))
               ?? jars.FirstOrDefault(name => KnownServerJar().IsMatch(name))
               ?? jars.FirstOrDefault()
               ?? "server.jar";
    }

    public static string DetectServerType(string folder, string jar)
    {
        var name = jar.ToLowerInvariant();
        if (name.Contains("folia")) return "Folia";
        if (name.Contains("purpur")) return "Purpur";
        if (name.Contains("paper")) return "Paper";
        if (name.Contains("fabric")) return "Fabric";
        if (File.Exists(Path.Combine(folder, ".fabric", "server", "fabric-server-launch.properties")) || Directory.Exists(Path.Combine(folder, ".fabric"))) return "Fabric";
        if (File.Exists(Path.Combine(folder, "purpur.yml"))) return "Purpur";
        if (Directory.Exists(Path.Combine(folder, "config")) && File.Exists(Path.Combine(folder, "config", "paper-global.yml"))) return "Paper";
        if (Directory.Exists(Path.Combine(folder, "plugins"))) return "Paper/Spigot";
        return "Vanilla";
    }

    /// <summary>
    /// The Minecraft version, from Paper/Purpur's version_history.json ("... (MC: 1.21.4)") or the
    /// version.json inside a vanilla server JAR. Empty when it can't be told.
    /// </summary>
    public static string DetectVersion(string folder, string jar)
    {
        try
        {
            var history = Path.Combine(folder, "version_history.json");
            if (File.Exists(history))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(history));
                if (json.RootElement.TryGetProperty("currentVersion", out var current) && current.GetString() is { } text
                    && McVersionInText().Match(text) is { Success: true } match)
                    return match.Groups[1].Value;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }

        try
        {
            var jarPath = Path.Combine(folder, jar);
            if (File.Exists(jarPath))
            {
                using var archive = ZipFile.OpenRead(jarPath);
                if (archive.GetEntry("version.json") is { } entry)
                {
                    using var stream = entry.Open();
                    using var json = JsonDocument.Parse(stream);
                    foreach (var key in new[] { "id", "name" })
                        if (json.RootElement.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } version
                            && McVersion().IsMatch(version))
                            return version;
                }
            }
        }
        catch (InvalidDataException) { }
        catch (JsonException) { }
        catch (IOException) { }

        return string.Empty;
    }

    [GeneratedRegex(@"(paper|purpur|folia|fabric|spigot|minecraft[_-]?server|server)", RegexOptions.IgnoreCase)]
    private static partial Regex KnownServerJar();

    [GeneratedRegex(@"\(MC:\s*([0-9][0-9A-Za-z.\-]*)\)")]
    private static partial Regex McVersionInText();

    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?([-.]?(pre|rc)\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex McVersion();
}
