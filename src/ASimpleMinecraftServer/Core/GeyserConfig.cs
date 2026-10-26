using ASimpleMinecraftServer.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

/// <summary>
/// Reads and edits Geyser's config.yml (the plugin/mod that lets Bedrock players join Java
/// servers). Geyser writes this file the first time the server starts with it installed.
/// Only two settings are touched: the Bedrock UDP port and the login mode (Floodgate).
/// </summary>
public static partial class GeyserConfig
{
    public const int DefaultBedrockPort = 19132;

    private static readonly string[] CandidatePaths =
    [
        Path.Combine("plugins", "Geyser-Spigot", "config.yml"),
        Path.Combine("plugins", "Geyser-Paper", "config.yml"),
        Path.Combine("config", "Geyser-Fabric", "config.yml"),
        Path.Combine("config", "geyser", "config.yml"),
    ];

    /// <summary>The Geyser config file for a server, or null if Geyser hasn't created one yet.</summary>
    public static string? Find(string serverFolder) =>
        CandidatePaths.Select(p => Path.Combine(serverFolder, p)).FirstOrDefault(File.Exists);

    public static bool IsInstalled(ServerProfile profile, IEnumerable<PluginRecord> addons) =>
        addons.Any(a => a.Name.Contains("Geyser", StringComparison.OrdinalIgnoreCase) || a.FileName.StartsWith("Geyser", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Bedrock port in the config, or null when there's no config yet.</summary>
    public static int? GetBedrockPort(string serverFolder)
    {
        var path = Find(serverFolder);
        if (path is null) return null;
        return ReadKeyInSection(File.ReadAllLines(path), "bedrock", "port") is { } value && int.TryParse(value, out var port) ? port : DefaultBedrockPort;
    }

    /// <summary>Sets the Bedrock UDP port and, when Floodgate is present, Floodgate login. Returns false if Geyser has no config yet.</summary>
    public static bool Configure(string serverFolder, int bedrockPort, bool useFloodgate)
    {
        var path = Find(serverFolder);
        if (path is null) return false;
        var lines = File.ReadAllLines(path).ToList();
        SetKeyInSection(lines, "bedrock", "port", bedrockPort.ToString());
        if (useFloodgate) SetKeyInSection(lines, "remote", "auth-type", "floodgate");
        File.WriteAllLines(path, lines);
        return true;
    }

    /// <summary>Value of <paramref name="key"/> directly under the top-level <paramref name="section"/>.</summary>
    public static string? ReadKeyInSection(IReadOnlyList<string> lines, string section, string key)
    {
        var index = FindKeyInSection(lines, section, key);
        if (index < 0) return null;
        var match = KeyValue().Match(lines[index]);
        return match.Success ? match.Groups["value"].Value.Trim().Trim('"', '\'') : null;
    }

    /// <summary>Replaces the value of a key inside a top-level section, keeping indentation and any comment.</summary>
    public static bool SetKeyInSection(List<string> lines, string section, string key, string value)
    {
        var index = FindKeyInSection(lines, section, key);
        if (index < 0) return false;
        var match = KeyValue().Match(lines[index]);
        lines[index] = $"{match.Groups["indent"].Value}{key}: {value}{match.Groups["comment"].Value}";
        return true;
    }

    private static int FindKeyInSection(IReadOnlyList<string> lines, string section, string key)
    {
        var inSection = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var topLevel = !char.IsWhiteSpace(line[0]);
            if (topLevel)
            {
                inSection = line.TrimEnd().Equals(section + ":", StringComparison.Ordinal);
                continue;
            }
            if (inSection && KeyValue().Match(line) is { Success: true } m && m.Groups["key"].Value == key && m.Groups["indent"].Value.Length <= 4)
                return i;
        }
        return -1;
    }

    [GeneratedRegex(@"^(?<indent>\s+)(?<key>[A-Za-z0-9_-]+):(?<value>[^#]*)(?<comment>\s+#.*)?$")]
    private static partial Regex KeyValue();
}
