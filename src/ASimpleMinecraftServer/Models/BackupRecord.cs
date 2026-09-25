using System.IO;

namespace ASimpleMinecraftServer.Models;

public sealed class BackupRecord
{
    public string FilePath { get; init; } = string.Empty;
    public string Name => Path.GetFileName(FilePath);
    public DateTime CreatedAt { get; init; }
    public long SizeBytes { get; init; }
    public string CreatedText => CreatedAt.ToString("yyyy-MM-dd  HH:mm");
    public string SizeText => FormatBytes(SizeBytes);
    public string Type { get; init; } = "Manual";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}
