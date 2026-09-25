using ASimpleMinecraftServer.Models;

namespace ASimpleMinecraftServer.Services;

public sealed record OperationGuardResult(bool IsAllowed, string Message)
{
    public static OperationGuardResult Allowed() => new(true, string.Empty);
    public static OperationGuardResult Blocked(string message) => new(false, message);
}

public sealed class ServerOperationGuard
{
    public OperationGuardResult RequireSelectedAndStopped(
        ServerProfile? selectedProfile,
        ServerProfile? runningProfile,
        bool launcherIsRunning,
        string runningMessage)
    {
        if (selectedProfile is null)
            return OperationGuardResult.Blocked("Select a server first.");

        if (launcherIsRunning && ReferenceEquals(selectedProfile, runningProfile))
            return OperationGuardResult.Blocked(runningMessage);

        return OperationGuardResult.Allowed();
    }
}
