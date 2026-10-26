using ASimpleMinecraftServer.Installers;
using ASimpleMinecraftServer.Models;
using ASimpleMinecraftServer.Services;
using System.IO;

namespace ASimpleMinecraftServer.Core;

public sealed record ServerInstallRequest(
    string Name,
    string Folder,
    string Type,
    string Version,
    int MemoryGb,
    string? CustomJarPath,
    bool EulaAccepted,
    int Port = 25565,
    int MaxPlayers = 20,
    string Difficulty = "normal",
    bool OnlineMode = true,
    string? LoaderVersion = null,
    string JavaPath = "java");

public sealed class ServerInstaller
{
    private readonly IReadOnlyDictionary<string, IServerTypeInstaller> _installers;

    public ServerInstaller(DownloadService downloads, VersionManager versions)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(versions);

        _installers = new Dictionary<string, IServerTypeInstaller>(StringComparer.OrdinalIgnoreCase)
        {
            ["Vanilla"] = new VanillaInstaller(downloads, versions),
            ["Paper"] = new PaperInstaller(downloads, versions),
            ["Purpur"] = new PurpurInstaller(downloads, versions),
            ["Folia"] = new FoliaInstaller(downloads, versions),
            ["Fabric"] = new FabricInstaller(downloads, versions),
            ["Forge"] = new ForgeInstaller(downloads, versions, "Forge"),
            ["NeoForge"] = new ForgeInstaller(downloads, versions, "NeoForge"),
            ["Custom JAR"] = new CustomJarInstaller()
        };
    }

    public async Task<ServerProfile> InstallAsync(
        ServerInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var normalizedFolder = Path.GetFullPath(request.Folder.Trim());
        Directory.CreateDirectory(normalizedFolder);

        if (!_installers.TryGetValue(request.Type, out var installer))
        {
            throw new InvalidOperationException($"Unsupported server type: {request.Type}");
        }

        var normalizedRequest = request with { Folder = normalizedFolder };
        await installer.InstallAsync(normalizedRequest, cancellationToken);

        // Only reached when the user ticked the EULA box (checked in Validate).
        var eulaText = $"# Minecraft EULA (https://aka.ms/MinecraftEULA) accepted by the user in MystTiq Minecraft Server Manager on {DateTimeOffset.Now:yyyy-MM-dd}" + Environment.NewLine
            + "eula=true" + Environment.NewLine;
        await File.WriteAllTextAsync(
            Path.Combine(normalizedFolder, "eula.txt"),
            eulaText,
            cancellationToken);

        var propertiesPath = Path.Combine(normalizedFolder, "server.properties");
        if (!File.Exists(propertiesPath))
        {
            var safeName = request.Name
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            var propertiesText = string.Join(Environment.NewLine,
                $"server-port={request.Port}",
                $"max-players={request.MaxPlayers}",
                $"difficulty={request.Difficulty.ToLowerInvariant()}",
                $"motd={safeName}",
                $"online-mode={request.OnlineMode.ToString().ToLowerInvariant()}",
                "view-distance=10",
                "simulation-distance=10",
                string.Empty);

            await File.WriteAllTextAsync(propertiesPath, propertiesText, cancellationToken);
        }

        return new ServerProfile
        {
            Name = request.Name.Trim(),
            Folder = normalizedFolder,
            Type = request.Type,
            Version = request.Version,
            MemoryGb = Math.Max(1, request.MemoryGb),
            // Forge/NeoForge start from an argument file the installer wrote.
            Jar = installer is ForgeInstaller ? ServerFolderInspector.FindForgeLaunchTarget(normalizedFolder) ?? "server.jar" : "server.jar"
        };
    }

    /// <summary>
    /// Replaces an existing server's software with <paramref name="version"/> (the newest build).
    /// JAR-based types are downloaded to a staging folder first, so a failed download leaves the
    /// server untouched; the old JAR is kept next to it as "*.previous". Forge/NeoForge re-run their
    /// installer in place. Returns the profile's new Jar value.
    /// </summary>
    public async Task<string> UpdateAsync(ServerProfile profile, string version, CancellationToken cancellationToken = default)
    {
        if (!_installers.TryGetValue(profile.Type, out var installer) || installer is CustomJarInstaller)
            throw new InvalidOperationException($"MystTiq can't update {profile.Type} servers automatically. Replace the JAR yourself, or set the right server type in Settings.");
        var request = new ServerInstallRequest(profile.Name, profile.Folder, profile.Type, version, profile.MemoryGb, null, true, JavaPath: profile.JavaPath);

        if (installer is ForgeInstaller)
        {
            await installer.InstallAsync(request, cancellationToken);
            return ServerFolderInspector.FindForgeLaunchTarget(profile.Folder) ?? profile.Jar;
        }

        var staging = Path.Combine(profile.Folder, ".mysttiq-update");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            await installer.InstallAsync(request with { Folder = staging }, cancellationToken);
            var jarName = profile.UsesArgsFile || string.IsNullOrWhiteSpace(profile.Jar) ? "server.jar" : profile.Jar;
            var target = Path.Combine(profile.Folder, jarName);
            if (File.Exists(target)) File.Copy(target, target + ".previous", overwrite: true);
            File.Move(Path.Combine(staging, "server.jar"), target, overwrite: true);
            return jarName;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    /// <summary>True when <paramref name="candidate"/> is an older Minecraft version than <paramref name="current"/> (worlds can't be downgraded).</summary>
    public static bool IsDowngrade(string current, string candidate)
    {
        static int[] Parts(string v) => System.Text.RegularExpressions.Regex.Matches(v ?? string.Empty, @"\d+").Select(m => int.Parse(m.Value)).Take(4).ToArray();
        var a = Parts(current);
        var b = Parts(candidate);
        if (a.Length == 0 || b.Length == 0) return false;
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return y < x;
        }
        return false;
    }

    private static void Validate(ServerInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Enter a server name.");
        }

        if (string.IsNullOrWhiteSpace(request.Folder))
        {
            throw new InvalidOperationException("Choose an install folder.");
        }

        if (!request.EulaAccepted)
        {
            throw new InvalidOperationException("You must accept the Minecraft EULA before a server can be installed.");
        }

        if (request.MemoryGb < 1)
        {
            throw new InvalidOperationException("Memory must be at least 1 GB.");
        }

        if (request.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Server port must be between 1 and 65535.");
        }

        if (request.MaxPlayers is < 1 or > 1000)
        {
            throw new InvalidOperationException("Maximum players must be between 1 and 1000.");
        }

        if (!request.Type.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Version))
        {
            throw new InvalidOperationException("Choose a Minecraft version.");
        }

        if (request.Type.Equals("Custom JAR", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(request.CustomJarPath)
                || !File.Exists(request.CustomJarPath)))
        {
            throw new InvalidOperationException("Choose a valid custom server JAR.");
        }
    }
}
