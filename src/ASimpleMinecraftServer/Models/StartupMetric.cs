namespace ASimpleMinecraftServer.Models;

public sealed class StartupMetric
{
    public string Stage { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
