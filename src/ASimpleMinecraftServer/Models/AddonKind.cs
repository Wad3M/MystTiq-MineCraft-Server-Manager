namespace ASimpleMinecraftServer.Models;

/// <summary>What kind of add-ons a server runs: Bukkit-style plugins, Fabric mods, or none.</summary>
public enum AddonKind { None, Plugin, Mod }

public static class AddonKinds
{
    public static AddonKind For(ServerProfile profile) => profile.Type.ToLowerInvariant() switch
    {
        "paper" or "purpur" or "folia" or "paper/spigot" => AddonKind.Plugin,
        "fabric" => AddonKind.Mod,
        _ => AddonKind.None
    };

    public static string FolderName(AddonKind kind) => kind == AddonKind.Mod ? "mods" : "plugins";
    public static string Noun(AddonKind kind) => kind == AddonKind.Mod ? "mod" : "plugin";
}
