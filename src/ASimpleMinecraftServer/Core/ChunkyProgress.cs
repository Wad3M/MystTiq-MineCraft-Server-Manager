using System.Globalization;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

/// <summary>Where a Chunky pre-generation task is, as last reported in the server console.</summary>
public sealed record PregenStatus(string World, long Chunks, double Percent, string Eta, bool Finished, DateTimeOffset Updated);

/// <summary>
/// World pre-generation with the Chunky plugin/mod: the console commands to drive it, and parsing
/// of its progress lines, e.g.
/// "[Chunky] Task running for world. Processed: 6913 chunks (4.32%), ETA: 0:08:51, Rate: 288.9 cps, Current: -26, 22"
/// "[Chunky] Task finished for world. Processed: 160000 chunks (100.00%), Total time: 0:09:14"
/// </summary>
public static partial class ChunkyProgress
{
    public const int MinRadius = 500;
    public const int MaxRadius = 50_000;

    /// <summary>Rough size of one generated chunk on disk, for the estimate shown before starting.</summary>
    private const double BytesPerChunk = 8 * 1024;

    public static PregenStatus? Parse(string line, DateTimeOffset now)
    {
        var match = ProgressLine().Match(line);
        if (!match.Success) return null;
        var percent = double.TryParse(match.Groups["percent"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
        var chunks = long.TryParse(match.Groups["chunks"].Value.Replace(",", string.Empty).Replace(".", string.Empty), out var c) ? c : 0;
        var finished = match.Groups["state"].Value.Equals("finished", StringComparison.OrdinalIgnoreCase);
        return new PregenStatus(match.Groups["world"].Value, chunks, finished ? 100 : percent, match.Groups["eta"].Value, finished, now);
    }

    /// <summary>Commands that pre-generate a square of <paramref name="radius"/> blocks around 0,0, optionally with a matching world border.</summary>
    public static IReadOnlyList<string> StartCommands(int radius, bool setWorldBorder)
    {
        if (radius is < MinRadius or > MaxRadius)
            throw new InvalidOperationException($"Choose a radius between {MinRadius:N0} and {MaxRadius:N0} blocks.");
        var commands = new List<string> { "chunky center 0 0", "chunky shape square", $"chunky radius {radius}", "chunky start" };
        if (setWorldBorder) commands.AddRange(["worldborder center 0 0", $"worldborder set {radius * 2}"]);
        return commands;
    }

    public static long EstimateChunks(int radius)
    {
        var side = (long)Math.Ceiling(radius * 2 / 16.0);
        return side * side;
    }

    public static double EstimateGigabytes(int radius) => EstimateChunks(radius) * BytesPerChunk / (1024d * 1024 * 1024);

    [GeneratedRegex(@"\[Chunky\] Task (?<state>running|finished) for (?<world>\S+?)\.? Processed: (?<chunks>[\d,.]+) chunks \((?<percent>[\d.,]+)%\)(?:, ETA: (?<eta>[\d:]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressLine();
}
