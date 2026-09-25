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
    int Port = 25565,
    int MaxPlayers = 20,
    string Difficulty = "normal",
    bool OnlineMode = true);

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

        var eulaText = "# Accepted by A Simple Minecraft Server" + Environment.NewLine
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
            Jar = "server.jar"
        };
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
