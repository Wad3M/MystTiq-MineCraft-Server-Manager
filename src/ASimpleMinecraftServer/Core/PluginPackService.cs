using ASimpleMinecraftServer.Models;

namespace ASimpleMinecraftServer.Core;

public sealed class PluginPackService
{
    public IReadOnlyList<PluginPack> GetBuiltInPacks() => new[]
    {
        new PluginPack("Essentials", "Core administration and permissions foundation.", new[]{"essentialsx","luckperms","vault"}),
        new PluginPack("Protection", "Common protection and rollback tools.", new[]{"coreprotect","worldguard","worldedit"}),
        new PluginPack("Performance", "Monitoring and profiling tools.", new[]{"spark","chunky"})
    };

    public string Preview(PluginPack pack) => string.Join(Environment.NewLine, pack.ModrinthProjectIds.Select(id => $"• {id}"));

    public IReadOnlyList<string> GetProjectIds(PluginPack pack) => pack.ModrinthProjectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();


    public IReadOnlyList<string> Validate(PluginPack pack) => pack.ModrinthProjectIds.Where(string.IsNullOrWhiteSpace).Select(_=>"Plugin project id is missing.").ToList();
}
