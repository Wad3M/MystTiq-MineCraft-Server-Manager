namespace ASimpleMinecraftServer.Models;

public sealed record WorldSummary(string Name,string Folder,long SizeBytes,DateTimeOffset LastModified,bool HasNether,bool HasEnd,bool HasLevelDatOld);
