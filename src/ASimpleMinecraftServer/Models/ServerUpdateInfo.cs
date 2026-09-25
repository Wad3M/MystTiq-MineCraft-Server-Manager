namespace ASimpleMinecraftServer.Models;

public sealed class ServerUpdateInfo
{
    public string ServerType { get; init; } = string.Empty;
    public string MinecraftVersion { get; init; } = string.Empty;
    public string InstalledBuild { get; init; } = "Unknown";
    public string LatestBuild { get; init; } = "Unknown";
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Sha256 { get; init; }
    public long? SizeBytes { get; init; }
    public string Channel { get; init; } = "Stable";
    public string Summary { get; init; } = string.Empty;
    public bool IsUpdateAvailable { get; init; }
    public bool CanInstall => IsUpdateAvailable && !string.IsNullOrWhiteSpace(DownloadUrl);
}
