using ASimpleMinecraftServer.Core;
using Xunit;

namespace ASimpleMinecraftServer.Tests;

public class ChunkyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_a_running_progress_line()
    {
        var status = ChunkyProgress.Parse("[12:00:01 INFO]: [Chunky] Task running for world. Processed: 6913 chunks (4.32%), ETA: 0:08:51, Rate: 288.9 cps, Current: -26, 22", Now);
        Assert.NotNull(status);
        Assert.Equal("world", status!.World);
        Assert.Equal(6913, status.Chunks);
        Assert.Equal(4.32, status.Percent, 2);
        Assert.Equal("0:08:51", status.Eta);
        Assert.False(status.Finished);
    }

    [Fact]
    public void Parses_a_finished_line_on_modded_servers()
    {
        var status = ChunkyProgress.Parse("[Server thread/INFO] [Chunky] Task finished for minecraft:overworld. Processed: 160000 chunks (100.00%), Total time: 0:09:14", Now);
        Assert.NotNull(status);
        Assert.Equal("minecraft:overworld", status!.World);
        Assert.True(status.Finished);
        Assert.Equal(100.0, status.Percent);
    }

    [Fact]
    public void Accepts_a_comma_decimal_separator() =>
        Assert.Equal(12.5, ChunkyProgress.Parse("[Chunky] Task running for world. Processed: 100 chunks (12,50%), ETA: 1:00:00", Now)!.Percent, 2);

    [Fact]
    public void Ignores_other_lines() =>
        Assert.Null(ChunkyProgress.Parse("[Server thread/INFO]: Steve joined the game", Now));

    [Fact]
    public void Builds_start_commands_with_a_matching_border()
    {
        var commands = ChunkyProgress.StartCommands(2500, setWorldBorder: true);
        Assert.Contains("chunky radius 2500", commands);
        Assert.Contains("worldborder set 5000", commands);
        Assert.True(commands.ToList().IndexOf("chunky start") < commands.ToList().IndexOf("worldborder set 5000"));
        Assert.DoesNotContain(ChunkyProgress.StartCommands(2500, setWorldBorder: false), c => c.StartsWith("worldborder"));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(60000)]
    public void Refuses_unreasonable_radii(int radius) =>
        Assert.Throws<InvalidOperationException>(() => ChunkyProgress.StartCommands(radius, false));

    [Fact]
    public void Estimates_chunks_for_a_square() =>
        Assert.Equal(625L * 625, ChunkyProgress.EstimateChunks(5000));
}
