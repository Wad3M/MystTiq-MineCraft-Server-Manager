namespace ASimpleMinecraftServer.Models;

public sealed record ServerTemplate(string Name, string Description, string ServerType, int MemoryGb, IReadOnlyDictionary<string,string> Properties);
