using ASimpleMinecraftServer.Models;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASimpleMinecraftServer.Services;

public sealed class ModrinthPluginCatalogService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ModrinthPluginCatalogService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.modrinth.com/v2/"), Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"MystTiq-MineCraft-Server-Manager/{typeof(ModrinthPluginCatalogService).Assembly.GetName().Version?.ToString(3)} (github.com/Wad3M/MystTiq-MineCraft-Server-Manager)");
    }

    public async Task<IReadOnlyList<PluginCatalogItem>> SearchAsync(string query, string minecraftVersion, CancellationToken cancellationToken)
    {
        var facets = JsonSerializer.Serialize(new[]
        {
            new[] { "project_type:plugin" },
            new[] { $"versions:{minecraftVersion}" },
            new[] { "server_side:required", "server_side:optional" }
        });
        var url = $"search?query={Uri.EscapeDataString(query ?? string.Empty)}&facets={Uri.EscapeDataString(facets)}&index=downloads&limit=20";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<SearchResponse>(stream, _jsonOptions, cancellationToken) ?? new SearchResponse();
        return payload.Hits.Select(hit => new PluginCatalogItem
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Name = hit.Title,
            Author = hit.Author,
            Description = hit.Description,
            Downloads = hit.Downloads
        }).ToList();
    }

    /// <summary>
    /// Looks up one Modrinth project by its exact id or slug. Returns null when it does not exist,
    /// so callers never fall back to an unrelated search result.
    /// </summary>
    public async Task<PluginCatalogItem?> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"project/{Uri.EscapeDataString(idOrSlug)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var project = await JsonSerializer.DeserializeAsync<ProjectResponse>(stream, _jsonOptions, cancellationToken);
        if (project is null || string.IsNullOrWhiteSpace(project.Id)) return null;
        return new PluginCatalogItem
        {
            ProjectId = project.Id,
            Slug = project.Slug,
            Name = project.Title,
            Description = project.Description,
            Downloads = project.Downloads
        };
    }

    public async Task ResolveInstallAsync(PluginCatalogItem item, string minecraftVersion, CancellationToken cancellationToken)
    {
        var loaders = Uri.EscapeDataString("[\"paper\",\"purpur\",\"folia\",\"spigot\",\"bukkit\"]");
        var versions = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }));
        var url = $"project/{Uri.EscapeDataString(item.ProjectId)}/version?loaders={loaders}&game_versions={versions}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<List<VersionResponse>>(stream, _jsonOptions, cancellationToken) ?? new();
        var release = releases
            .Where(version => string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(version => version.DatePublished)
            .FirstOrDefault() ?? releases.OrderByDescending(version => version.DatePublished).FirstOrDefault();
        var file = release?.Files.FirstOrDefault(candidate => candidate.Primary) ?? release?.Files.FirstOrDefault();
        item.LatestVersion = release?.VersionNumber ?? "—";
        item.DownloadUri = file is null ? null : new Uri(file.Url);
        item.FileName = file?.FileName;
        item.Sha512 = file?.Hashes.GetValueOrDefault("sha512");
        item.CompatibilityText = file is null ? $"No downloadable {minecraftVersion} build found" : $"{minecraftVersion} • {string.Join(", ", release!.Loaders)}";
    }


    public async Task ResolveInstalledUpdateAsync(PluginRecord plugin, string minecraftVersion, CancellationToken cancellationToken)
    {
        plugin.LatestVersion = "—";
        plugin.UpdateStatus = "Checking...";
        plugin.UpdateProjectId = null;
        plugin.UpdateDownloadUri = null;
        plugin.UpdateFileName = null;
        plugin.UpdateSha512 = null;

        var facets = JsonSerializer.Serialize(new[]
        {
            new[] { "project_type:plugin" },
            new[] { $"versions:{minecraftVersion}" },
            new[] { "server_side:required", "server_side:optional" }
        });
        var searchUrl = $"search?query={Uri.EscapeDataString(plugin.Name)}&facets={Uri.EscapeDataString(facets)}&limit=10";
        using var searchResponse = await _httpClient.GetAsync(searchUrl, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();
        await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
        var search = await JsonSerializer.DeserializeAsync<SearchResponse>(searchStream, _jsonOptions, cancellationToken) ?? new SearchResponse();
        var normalizedName = Normalize(plugin.Name);
        var hit = search.Hits.FirstOrDefault(candidate => Normalize(candidate.Title) == normalizedName || Normalize(candidate.Slug) == normalizedName)
                  ?? search.Hits.FirstOrDefault(candidate => Normalize(candidate.Title).Contains(normalizedName, StringComparison.Ordinal) || normalizedName.Contains(Normalize(candidate.Title), StringComparison.Ordinal));
        if (hit is null)
        {
            plugin.UpdateStatus = "Not found on Modrinth";
            return;
        }

        var item = new PluginCatalogItem
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Name = hit.Title,
            Author = hit.Author,
            Description = hit.Description,
            Downloads = hit.Downloads
        };
        await ResolveInstallAsync(item, minecraftVersion, cancellationToken);
        if (!item.CanInstall)
        {
            plugin.UpdateStatus = "No compatible release";
            return;
        }

        plugin.LatestVersion = item.LatestVersion;
        plugin.UpdateProjectId = item.ProjectId;
        plugin.UpdateDownloadUri = item.DownloadUri;
        plugin.UpdateFileName = item.FileName;
        plugin.UpdateSha512 = item.Sha512;
        plugin.UpdateStatus = VersionsEquivalent(plugin.Version, item.LatestVersion) ? "Up to date" : "Update available";
    }

    public async Task DownloadUpdateAsync(PluginRecord plugin, string destinationPath, CancellationToken cancellationToken)
    {
        if (plugin.UpdateDownloadUri is null) throw new InvalidOperationException("No compatible plugin update is available.");
        await DownloadVerifiedAsync(plugin.UpdateDownloadUri, plugin.UpdateSha512, destinationPath, cancellationToken);
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool VersionsEquivalent(string installed, string latest)
    {
        static string Clean(string value)
        {
            value = value.Trim();
            if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
            var plus = value.IndexOf('+');
            if (plus >= 0) value = value[..plus];
            return value;
        }
        return string.Equals(Clean(installed), Clean(latest), StringComparison.OrdinalIgnoreCase);
    }

    public async Task DownloadAsync(PluginCatalogItem item, string destinationPath, CancellationToken cancellationToken)
    {
        if (item.DownloadUri is null) throw new InvalidOperationException("No compatible download is available for this plugin.");
        await DownloadVerifiedAsync(item.DownloadUri, item.Sha512, destinationPath, cancellationToken);
    }

    /// <summary>Downloads a file and refuses it unless its SHA-512 matches the hash Modrinth published.</summary>
    private async Task DownloadVerifiedAsync(Uri uri, string? expectedSha512, string destinationPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha512))
            throw new InvalidDataException("Modrinth did not publish a SHA-512 hash for this file, so it cannot be verified.");
        using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
        await using var check = File.OpenRead(destinationPath);
        var actual = Convert.ToHexString(await SHA512.HashDataAsync(check, cancellationToken));
        if (!actual.Equals(expectedSha512, StringComparison.OrdinalIgnoreCase))
        {
            check.Close();
            File.Delete(destinationPath);
            throw new InvalidDataException($"The downloaded file failed its SHA-512 check and was deleted ({uri.Host}).");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class SearchResponse { [JsonPropertyName("hits")] public List<SearchHit> Hits { get; init; } = new(); }
    private sealed class SearchHit
    {
        [JsonPropertyName("project_id")] public string ProjectId { get; init; } = string.Empty;
        [JsonPropertyName("slug")] public string Slug { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
        [JsonPropertyName("author")] public string Author { get; init; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
        [JsonPropertyName("downloads")] public long Downloads { get; init; }
    }
    private sealed class VersionResponse
    {
        [JsonPropertyName("version_number")] public string VersionNumber { get; init; } = string.Empty;
        [JsonPropertyName("version_type")] public string VersionType { get; init; } = string.Empty;
        [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; init; }
        [JsonPropertyName("loaders")] public List<string> Loaders { get; init; } = new();
        [JsonPropertyName("files")] public List<FileResponse> Files { get; init; } = new();
    }
    private sealed class FileResponse
    {
        [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
        [JsonPropertyName("filename")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("primary")] public bool Primary { get; init; }
        [JsonPropertyName("hashes")] public Dictionary<string, string> Hashes { get; init; } = new();
    }
    private sealed class ProjectResponse
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("slug")] public string Slug { get; init; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
        [JsonPropertyName("downloads")] public long Downloads { get; init; }
    }
}
