using System.IO;

namespace ASimpleMinecraftServer.Models;

public sealed class WorldRecord
{
    public string Name { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Dimension { get; init; } = "Overworld";
    public long SizeBytes { get; init; }
    public DateTime LastModified { get; init; }
    public bool IsPrimary { get; init; }
    public bool HasLevelDat { get; init; }
    public bool HasRegionData { get; init; }
    public int PlayerDataCount { get; init; }
    public string Health { get; init; } = "Unknown";
    public string HealthDetails { get; init; } = "World has not been verified.";
    public string SizeText => FormatBytes(SizeBytes);
    public string LastModifiedText => LastModified == DateTime.MinValue ? "—" : LastModified.ToString("g");
    public string FolderName => Path.GetFileName(FolderPath);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
