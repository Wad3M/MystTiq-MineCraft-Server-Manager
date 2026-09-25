using ASimpleMinecraftServer.Core;

namespace ASimpleMinecraftServer.Installers;

public interface IServerTypeInstaller
{
    Task InstallAsync(ServerInstallRequest request, CancellationToken cancellationToken = default);
}
