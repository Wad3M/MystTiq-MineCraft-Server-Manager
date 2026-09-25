namespace ASimpleMinecraftServer.Models;

public sealed record MigrationPlan(string SourceFolder, string DestinationFolder, bool CopyWorlds, bool CopyPlugins, bool CopyConfiguration);
