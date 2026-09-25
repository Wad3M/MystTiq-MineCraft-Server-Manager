using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Services;
using System.IO;

namespace ASimpleMinecraftServer.Installers;

public sealed class FoliaInstaller(DownloadService downloads, VersionManager versions) : IServerTypeInstaller
{
    public async Task InstallAsync(ServerInstallRequest request, CancellationToken cancellationToken = default)
    {
        var url = await versions.ResolveDownloadUrlAsync("Folia", request.Version, cancellationToken);
        await downloads.DownloadFileAsync(url, Path.Combine(request.Folder, "server.jar"), cancellationToken);
    }
}
