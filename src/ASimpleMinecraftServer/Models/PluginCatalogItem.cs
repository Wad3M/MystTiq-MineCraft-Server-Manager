namespace ASimpleMinecraftServer.Models;

public sealed class PluginCatalogItem
{
    public required string ProjectId { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "Unknown";
    public string Description { get; init; } = string.Empty;
    public long Downloads { get; init; }
    public string DownloadsText => Downloads >= 1_000_000 ? $"{Downloads / 1_000_000d:0.0}M" : Downloads >= 1_000 ? $"{Downloads / 1_000d:0.0}K" : Downloads.ToString("N0");
    public string LatestVersion { get; set; } = "—";
    public string CompatibilityText { get; set; } = "Compatible version available";
    public Uri? DownloadUri { get; set; }
    public string? FileName { get; set; }
    /// <summary>SHA-512 of the download as published by Modrinth, checked after downloading.</summary>
    public string? Sha512 { get; set; }
    public bool CanInstall => DownloadUri is not null && !string.IsNullOrWhiteSpace(FileName);
}
