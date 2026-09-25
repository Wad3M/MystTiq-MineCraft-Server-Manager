using System.Text.Json;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Services;

public sealed class VersionManager(DownloadService downloads)
{
    public static readonly IReadOnlyList<string> SupportedServerTypes =
        ["Vanilla", "Paper", "Purpur", "Folia", "Fabric", "Custom JAR"];

    public async Task<IReadOnlyList<string>> GetVersionsAsync(string serverType, CancellationToken cancellationToken = default)
    {
        IEnumerable<string> versions = serverType switch
        {
            "Paper" => await GetPaperMcVersionsAsync("paper", cancellationToken),
            "Folia" => await GetPaperMcVersionsAsync("folia", cancellationToken),
            "Purpur" => await GetPurpurVersionsAsync(cancellationToken),
            "Fabric" => await GetFabricVersionsAsync(cancellationToken),
            "Custom JAR" => [],
            _ => await GetVanillaVersionsAsync(cancellationToken)
        };

        return versions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(VersionKey)
            .ToList();
    }

    public async Task<string> ResolveDownloadUrlAsync(string serverType, string version, CancellationToken cancellationToken = default)
    {
        return serverType switch
        {
            "Vanilla" => await ResolveVanillaAsync(version, cancellationToken),
            "Paper" => await ResolvePaperMcAsync("paper", version, cancellationToken),
            "Folia" => await ResolvePaperMcAsync("folia", version, cancellationToken),
            "Purpur" => $"https://api.purpurmc.org/v2/purpur/{Uri.EscapeDataString(version)}/latest/download",
            "Fabric" => await ResolveFabricAsync(version, cancellationToken),
            _ => throw new InvalidOperationException($"No automatic download provider exists for {serverType}.")
        };
    }

    private async Task<IReadOnlyList<string>> GetVanillaVersionsAsync(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await downloads.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token));
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "release")
            .Select(item => item.GetProperty("id").GetString()!)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetPaperMcVersionsAsync(string project, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await downloads.GetStringAsync($"https://fill.papermc.io/v3/projects/{project}", token));
        var list = new List<string>();
        foreach (var group in doc.RootElement.GetProperty("versions").EnumerateObject())
            list.AddRange(group.Value.EnumerateArray().Select(value => value.GetString()!));
        return list;
    }

    private async Task<IReadOnlyList<string>> GetPurpurVersionsAsync(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await downloads.GetStringAsync("https://api.purpurmc.org/v2/purpur", token));
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetFabricVersionsAsync(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await downloads.GetStringAsync("https://meta.fabricmc.net/v2/versions/game", token));
        return doc.RootElement.EnumerateArray()
            .Where(item => !item.TryGetProperty("stable", out var stable) || stable.GetBoolean())
            .Select(item => item.GetProperty("version").GetString()!)
            .ToList();
    }

    private async Task<string> ResolveVanillaAsync(string version, CancellationToken token)
    {
        using var manifest = JsonDocument.Parse(await downloads.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token));
        var item = manifest.RootElement.GetProperty("versions").EnumerateArray().FirstOrDefault(value => value.GetProperty("id").GetString() == version);
        if (item.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Vanilla version not found.");
        using var detail = JsonDocument.Parse(await downloads.GetStringAsync(item.GetProperty("url").GetString()!, token));
        if (!detail.RootElement.GetProperty("downloads").TryGetProperty("server", out var server)) throw new InvalidOperationException("This version has no server download.");
        return server.GetProperty("url").GetString()!;
    }

    private async Task<string> ResolvePaperMcAsync(string project, string version, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await downloads.GetStringAsync($"https://fill.papermc.io/v3/projects/{project}/versions/{Uri.EscapeDataString(version)}/builds", token));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"No {project} build is available for Minecraft {version}.");

        JsonElement chosen = default;
        foreach (var build in doc.RootElement.EnumerateArray())
        {
            if (build.GetProperty("channel").GetString()?.Equals("STABLE", StringComparison.OrdinalIgnoreCase) == true)
            {
                chosen = build;
                break;
            }
            if (chosen.ValueKind == JsonValueKind.Undefined) chosen = build;
        }
        if (chosen.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException($"No {project} build is available for Minecraft {version}.");
        return chosen.GetProperty("downloads").GetProperty("server:default").GetProperty("url").GetString()!;
    }

    private async Task<string> ResolveFabricAsync(string version, CancellationToken token)
    {
        using var loaderDoc = JsonDocument.Parse(await downloads.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(version)}", token));
        if (loaderDoc.RootElement.ValueKind != JsonValueKind.Array || loaderDoc.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException($"No Fabric loader is available for Minecraft {version}.");

        var loaderVersion = loaderDoc.RootElement[0].GetProperty("loader").GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(loaderVersion)) throw new InvalidOperationException("Fabric loader version could not be resolved.");

        using var installerDoc = JsonDocument.Parse(await downloads.GetStringAsync("https://meta.fabricmc.net/v2/versions/installer", token));
        if (installerDoc.RootElement.ValueKind != JsonValueKind.Array || installerDoc.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Fabric installer version could not be resolved.");

        string? installerVersion = null;
        foreach (var installer in installerDoc.RootElement.EnumerateArray())
        {
            if (installer.TryGetProperty("stable", out var stable) && stable.GetBoolean())
            {
                installerVersion = installer.GetProperty("version").GetString();
                break;
            }
            installerVersion ??= installer.GetProperty("version").GetString();
        }
        if (string.IsNullOrWhiteSpace(installerVersion)) throw new InvalidOperationException("Fabric installer version could not be resolved.");

        return $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(loaderVersion)}/{Uri.EscapeDataString(installerVersion)}/server/jar";
    }

    private static long VersionKey(string version)
    {
        var numbers = Regex.Matches(version, @"\d+").Select(match => long.Parse(match.Value)).Take(4);
        return numbers.Aggregate(0L, (current, number) => current * 1000 + number);
    }
}
