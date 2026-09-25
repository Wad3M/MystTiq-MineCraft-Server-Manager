namespace ASimpleMinecraftServer.Models;

public sealed record PluginPack(string Name, string Description, IReadOnlyList<string> ModrinthProjectIds);
