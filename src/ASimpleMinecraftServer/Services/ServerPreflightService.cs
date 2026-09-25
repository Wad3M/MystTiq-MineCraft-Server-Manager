using ASimpleMinecraftServer.Models;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ASimpleMinecraftServer.Services;

public sealed record ServerPreflightResult(bool IsValid, string Message)
{
    public static ServerPreflightResult Success() => new(true, string.Empty);
    public static ServerPreflightResult Failure(string message) => new(false, message);
}

public sealed class ServerPreflightService
{
    public ServerPreflightResult Validate(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var eulaPath = Path.Combine(profile.Folder, "eula.txt");
        if (!IsEulaAccepted(eulaPath))
        {
            return ServerPreflightResult.Failure(
                "The Minecraft EULA has not been accepted for this server. Open eula.txt and set eula=true before starting.");
        }

        var port = profile.Port;
        if (!IsPortAvailable(port))
        {
            return ServerPreflightResult.Failure(
                $"Port {port} is already in use. Stop the other application or change server-port in Server Configuration.");
        }

        return ServerPreflightResult.Success();
    }

    private static bool IsEulaAccepted(string eulaPath)
    {
        try
        {
            return File.Exists(eulaPath) && File.ReadLines(eulaPath).Any(line =>
                line.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsPortAvailable(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
