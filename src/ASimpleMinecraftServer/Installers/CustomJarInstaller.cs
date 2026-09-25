using ASimpleMinecraftServer.Core;
using System.IO;

namespace ASimpleMinecraftServer.Installers;

public sealed class CustomJarInstaller : IServerTypeInstaller
{
    public Task InstallAsync(ServerInstallRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CustomJarPath) || !File.Exists(request.CustomJarPath)) throw new FileNotFoundException("Select a valid custom JAR.", request.CustomJarPath);
        File.Copy(request.CustomJarPath, Path.Combine(request.Folder, "server.jar"), true);
        return Task.CompletedTask;
    }
}
