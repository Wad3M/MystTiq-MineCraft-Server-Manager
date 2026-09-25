using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;

namespace ASimpleMinecraftServer.Services;

public sealed class ServerHealthAnalyzer
{
    public async Task<ServerHealthReport> AnalyzeAsync(ServerProfile profile, string backupRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var checks = new List<HealthCheckItem>();
        void Add(string category, string check, bool pass, string success, string failure, int weight = 10) =>
            checks.Add(new HealthCheckItem { Category = category, Check = check, Status = pass ? "Healthy" : "Needs attention", Details = pass ? success : failure, Score = pass ? weight : 0 });

        var folderExists = !string.IsNullOrWhiteSpace(profile.Folder) && Directory.Exists(profile.Folder);
        Add("Installation", "Server folder", folderExists, profile.Folder ?? "Server folder configured.", "The configured server folder does not exist.", 12);

        var jarPath = Path.Combine(profile.Folder ?? string.Empty, profile.Jar ?? string.Empty);
        var jarExists = folderExists && File.Exists(jarPath);
        Add("Installation", "Server JAR", jarExists, profile.Jar ?? "Server JAR configured.", "The configured server JAR is missing.", 12);
        if (jarExists)
        {
            try { using var archive = ZipFile.OpenRead(jarPath); Add("Installation", "JAR integrity", archive.Entries.Count > 0, "The JAR archive can be opened.", "The JAR archive appears empty or damaged.", 8); }
            catch { Add("Installation", "JAR integrity", false, string.Empty, "The JAR archive could not be opened.", 8); }
        }
        else Add("Installation", "JAR integrity", false, string.Empty, "Integrity cannot be checked until the JAR exists.", 8);

        var propertiesPath = Path.Combine(profile.Folder ?? string.Empty, "server.properties");
        Add("Configuration", "server.properties", File.Exists(propertiesPath), "Configuration file found.", "server.properties is missing.", 8);
        var eulaPath = Path.Combine(profile.Folder ?? string.Empty, "eula.txt");
        var eulaAccepted = File.Exists(eulaPath) && (await File.ReadAllTextAsync(eulaPath, cancellationToken)).Contains("eula=true", StringComparison.OrdinalIgnoreCase);
        Add("Configuration", "Minecraft EULA", eulaAccepted, "EULA is accepted.", "Open eula.txt and set eula=true after reviewing the Minecraft EULA.", 8);

        var memoryHealthy = profile.MemoryGb >= 2;
        Add("Resources", "Memory allocation", memoryHealthy, $"{profile.MemoryGb} GB configured.", "Less than 2 GB is configured; most modern servers need more memory.", 10);

        var java = await JavaDetector.ValidateAsync(profile.JavaPath, profile.Version, cancellationToken);
        Add("Runtime", "Java compatibility", java.IsValid, java.Message, java.Message, 15);

        var backupFolder = Path.Combine(backupRoot, Sanitize(profile.Name));
        var recentBackup = Directory.Exists(backupFolder) && Directory.EnumerateFiles(backupFolder, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(path => File.GetLastWriteTimeUtc(path)).Any(time => time >= DateTime.UtcNow.AddDays(-7));
        Add("Protection", "Recent backup", recentBackup, "A backup was created within the last 7 days.", "No backup from the last 7 days was found.", 12);

        var pluginsFolder = Path.Combine(profile.Folder ?? string.Empty, "plugins");
        var pluginCount = Directory.Exists(pluginsFolder) ? Directory.EnumerateFiles(pluginsFolder, "*.jar").Count() : 0;
        var pluginSuitable = profile.Type.Equals("Paper", StringComparison.OrdinalIgnoreCase) || profile.Type.Equals("Purpur", StringComparison.OrdinalIgnoreCase) || profile.Type.Equals("Folia", StringComparison.OrdinalIgnoreCase) || pluginCount == 0;
        Add("Plugins", "Plugin platform", pluginSuitable, pluginCount == 0 ? "No plugins installed." : $"{pluginCount} plugin JAR(s) on {profile.Type}.", $"{pluginCount} plugin JAR(s) were found, but {profile.Type} may not support Bukkit/Paper plugins.", 7);

        var possible = checks.Sum(item => item.Check == "JAR integrity" ? 8 : item.Check switch { "Server folder" => 12, "Server JAR" => 12, "Java compatibility" => 15, "Recent backup" => 12, _ => item.Check is "Memory allocation" ? 10 : item.Check is "Plugin platform" ? 7 : 8 });
        var earned = checks.Sum(item => item.Score);
        var score = possible == 0 ? 0 : (int)Math.Round(earned * 100d / possible);
        var grade = score switch { >= 90 => "Excellent", >= 75 => "Good", >= 60 => "Fair", _ => "Needs attention" };
        return new ServerHealthReport(score, grade, DateTimeOffset.Now, checks);
    }

    private static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Minecraft Server" : value;
    }
}
