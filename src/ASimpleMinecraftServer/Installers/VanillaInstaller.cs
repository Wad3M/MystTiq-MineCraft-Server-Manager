using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Services;
using System.IO;

namespace ASimpleMinecraftServer.Installers;

public sealed class VanillaInstaller(DownloadService downloads, VersionManager versions) : IServerTypeInstaller
{
    public async Task InstallAsync(ServerInstallRequest request, CancellationToken cancellationToken = default)
    {
        var download = await versions.ResolveDownloadAsync("Vanilla", request.Version, cancellationToken);
        await downloads.DownloadVerifiedAsync(download, Path.Combine(request.Folder, "server.jar"), cancellationToken);
    }
}
