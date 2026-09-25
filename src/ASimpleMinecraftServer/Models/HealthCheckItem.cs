namespace ASimpleMinecraftServer.Models;

public sealed class HealthCheckItem
{
    public string Category { get; init; } = string.Empty;
    public string Check { get; init; } = string.Empty;
    public string Status { get; init; } = "Unknown";
    public string Details { get; init; } = string.Empty;
    public int Score { get; init; }
}

public sealed record ServerHealthReport(int Score, string Grade, DateTimeOffset CheckedAt, IReadOnlyList<HealthCheckItem> Checks);
