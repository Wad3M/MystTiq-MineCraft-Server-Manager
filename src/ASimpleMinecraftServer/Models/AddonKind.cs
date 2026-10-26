namespace ASimpleMinecraftServer.Models;

/// <summary>What kind of add-ons a server runs: Bukkit-style plugins, Fabric/Forge/NeoForge mods, or none.</summary>
public enum AddonKind { None, Plugin, Mod }

public static class AddonKinds
{
    public static AddonKind For(ServerProfile profile) => profile.Type.ToLowerInvariant() switch
    {
        "paper" or "purpur" or "folia" or "paper/spigot" => AddonKind.Plugin,
        "fabric" or "forge" or "neoforge" => AddonKind.Mod,
        _ => AddonKind.None
    };

    /// <summary>Modrinth loader names whose builds can run on this server type.</summary>
    public static string[] ModrinthLoaders(string serverType) => serverType.ToLowerInvariant() switch
    {
        "fabric" => ["fabric"],
        "forge" => ["forge"],
        "neoforge" => ["neoforge"],
        _ => ["paper", "purpur", "folia", "spigot", "bukkit"]
    };

    public static string FolderName(AddonKind kind) => kind == AddonKind.Mod ? "mods" : "plugins";
    public static string Noun(AddonKind kind) => kind == AddonKind.Mod ? "mod" : "plugin";
}
