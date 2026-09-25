using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Services;

public sealed class ServerUpdateService
{
    private readonly DownloadService _downloads;
    public ServerUpdateService(DownloadService downloads) => _downloads = downloads;

    public async Task<ServerUpdateInfo> CheckAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Version)) throw new InvalidOperationException("The selected profile does not have a Minecraft version.");

        return profile.Type.Trim().ToLowerInvariant() switch
        {
            "paper" => await CheckPaperAsync(profile, cancellationToken),
            "purpur" => await CheckPurpurAsync(profile, cancellationToken),
            "vanilla" => await CheckVanillaAsync(profile, cancellationToken),
            _ => new ServerUpdateInfo
            {
                ServerType = profile.Type,
                MinecraftVersion = profile.Version,
                Summary = "Automatic updates are not available for custom JAR servers."
            }
        };
    }


    public async Task<IReadOnlyList<string>> GetAvailableMinecraftVersionsAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        var manifestJson = await _downloads.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
        using var manifest = JsonDocument.Parse(manifestJson);
        var releases = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .Where(item => string.Equals(GetString(item, "type"), "release", StringComparison.OrdinalIgnoreCase))
            .Select(item => GetString(item, "id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Take(80)
            .ToList();
        if (!releases.Contains(profile.Version, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(profile.Version))
            releases.Insert(0, profile.Version);
        return releases;
    }

    public Task<ServerUpdateInfo> ResolveVersionChangeAsync(ServerProfile profile, string targetVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetVersion)) throw new ArgumentException("Choose a Minecraft version.", nameof(targetVersion));
        var target = new ServerProfile
        {
            Name = profile.Name, Folder = profile.Folder, Type = profile.Type, Version = targetVersion.Trim(),
            MemoryGb = profile.MemoryGb, Jar = profile.Jar, JavaPath = profile.JavaPath
        };
        return CheckAsync(target, cancellationToken);
    }

    public async Task<string> DownloadAndVerifyAsync(ServerUpdateInfo update, string destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await _downloads.DownloadFileAsync(update.DownloadUrl, destination, progress, cancellationToken);

        if (!File.Exists(destination) || new FileInfo(destination).Length < 1024 * 1024)
            throw new InvalidDataException("The downloaded server JAR is missing or unexpectedly small.");

        using (var archive = ZipFile.OpenRead(destination))
        {
            if (archive.Entries.Count == 0)
                throw new InvalidDataException("The downloaded file is not a valid Java archive.");
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var stream = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded JAR failed SHA-256 verification.");
        }

        return destination;
    }

    private async Task<ServerUpdateInfo> CheckPaperAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        var json = await _downloads.GetStringAsync($"https://fill.papermc.io/v3/projects/paper/versions/{Uri.EscapeDataString(profile.Version)}/builds", cancellationToken);
        using var document = JsonDocument.Parse(json);
        var builds = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : new List<JsonElement>();
        var stable = builds.FirstOrDefault(item => string.Equals(GetString(item, "channel"), "STABLE", StringComparison.OrdinalIgnoreCase));
        if (stable.ValueKind == JsonValueKind.Undefined)
            return Unsupported(profile, "Paper has no stable build for this Minecraft version.");

        var latest = GetString(stable, "id") ?? GetString(stable, "number") ?? "Unknown";
        var downloads = stable.TryGetProperty("downloads", out var d) ? d : default;
        var server = downloads.ValueKind == JsonValueKind.Object && downloads.TryGetProperty("server:default", out var s) ? s : default;
        var url = GetString(server, "url") ?? string.Empty;
        var sha = server.ValueKind == JsonValueKind.Object && server.TryGetProperty("checksums", out var checksums) ? GetString(checksums, "sha256") : null;
        var size = server.ValueKind == JsonValueKind.Object && server.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var length) ? (long?)length : null;
        var installed = DetectInstalledBuild(profile, "paper");

        return Build(profile, installed, latest, url, sha, size, "Latest stable Paper build");
    }

    private async Task<ServerUpdateInfo> CheckPurpurAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        var json = await _downloads.GetStringAsync($"https://api.purpurmc.org/v2/purpur/{Uri.EscapeDataString(profile.Version)}", cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var latest = root.TryGetProperty("builds", out var builds) ? GetString(builds, "latest") : null;
        if (string.IsNullOrWhiteSpace(latest)) return Unsupported(profile, "Purpur has no build for this Minecraft version.");
        var installed = DetectInstalledBuild(profile, "purpur");
        var url = $"https://api.purpurmc.org/v2/purpur/{Uri.EscapeDataString(profile.Version)}/{latest}/download";
        return Build(profile, installed, latest, url, null, null, "Latest Purpur build");
    }

    private async Task<ServerUpdateInfo> CheckVanillaAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        var manifestJson = await _downloads.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
        using var manifest = JsonDocument.Parse(manifestJson);
        var version = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .FirstOrDefault(item => string.Equals(GetString(item, "id"), profile.Version, StringComparison.OrdinalIgnoreCase));
        if (version.ValueKind == JsonValueKind.Undefined) return Unsupported(profile, "This Minecraft version is not present in Mojang's release manifest.");
        var metadataUrl = GetString(version, "url") ?? throw new InvalidDataException("Mojang version metadata did not contain a download URL.");
        var metadataJson = await _downloads.GetStringAsync(metadataUrl, cancellationToken);
        using var metadata = JsonDocument.Parse(metadataJson);
        var server = metadata.RootElement.GetProperty("downloads").GetProperty("server");
        var url = GetString(server, "url") ?? string.Empty;
        var sha1 = GetString(server, "sha1");
        var size = server.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var length) ? (long?)length : null;
        var installed = profile.Version;
        return new ServerUpdateInfo
        {
            ServerType = profile.Type,
            MinecraftVersion = profile.Version,
            InstalledBuild = installed,
            LatestBuild = profile.Version,
            DownloadUrl = url,
            SizeBytes = size,
            Channel = "Release",
            Summary = "The selected Vanilla profile already targets this Mojang release. Reinstall is available for repair purposes.",
            IsUpdateAvailable = false
        };
    }

    private static ServerUpdateInfo Build(ServerProfile profile, string installed, string latest, string url, string? sha, long? size, string summary)
    {
        var update = installed == "Unknown" || !string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase);
        return new ServerUpdateInfo
        {
            ServerType = profile.Type,
            MinecraftVersion = profile.Version,
            InstalledBuild = installed,
            LatestBuild = latest,
            DownloadUrl = url,
            Sha256 = sha,
            SizeBytes = size,
            Summary = update ? summary + " is available." : "The installed server JAR appears to be current.",
            IsUpdateAvailable = update
        };
    }

    private static ServerUpdateInfo Unsupported(ServerProfile profile, string message) => new()
    {
        ServerType = profile.Type,
        MinecraftVersion = profile.Version,
        Summary = message
    };

    private static string DetectInstalledBuild(ServerProfile profile, string product)
    {
        var names = new[] { profile.Jar, Path.GetFileName(profile.Jar) };
        foreach (var name in names.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var match = Regex.Match(name!, $@"{Regex.Escape(product)}[-_].*?[-_]([0-9]+)\.jar$", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }
        var marker = Path.Combine(profile.Folder, ".asms", "server-build.txt");
        if (File.Exists(marker))
        {
            var value = File.ReadAllText(marker).Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "Unknown";
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }
}
