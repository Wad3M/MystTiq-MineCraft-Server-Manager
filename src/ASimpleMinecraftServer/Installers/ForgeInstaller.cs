using ASimpleMinecraftServer.Core;
using ASimpleMinecraftServer.Services;
using System.Diagnostics;
using System.IO;

namespace ASimpleMinecraftServer.Installers;

/// <summary>
/// Forge and NeoForge: downloads the official installer (SHA-1 checked against the Maven
/// checksum), runs it with --installServer in the server folder, then removes the installer.
/// The installer downloads Minecraft and the loader's libraries itself, so this needs Java.
/// </summary>
public sealed class ForgeInstaller(DownloadService downloads, VersionManager versions, string serverType) : IServerTypeInstaller
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);

    public async Task InstallAsync(ServerInstallRequest request, CancellationToken cancellationToken = default)
    {
        var download = await versions.ResolveDownloadAsync(serverType, request.Version, cancellationToken, request.LoaderVersion);
        var installer = Path.Combine(request.Folder, $"{serverType.ToLowerInvariant()}-installer.jar");
        await downloads.DownloadVerifiedAsync(download, installer, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(request.JavaPath) ? "java" : request.JavaPath,
            WorkingDirectory = request.Folder,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(Path.GetFileName(installer));
        startInfo.ArgumentList.Add("--installServer");

        var tail = new Queue<string>();
        void Keep(string? line) { if (line is null) return; lock (tail) { tail.Enqueue(line); if (tail.Count > 15) tail.Dequeue(); } }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Java did not start the {serverType} installer.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Java wasn't found, and the {serverType} installer needs it. Install Java (or set the Java path) and try again.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InstallTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"The {serverType} installer didn't finish within {InstallTimeout.TotalMinutes:0} minutes.");
        }

        if (process.ExitCode != 0 || ServerFolderInspector.FindForgeLaunchTarget(request.Folder) is null)
        {
            string detail;
            lock (tail) detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException($"The {serverType} installer failed (exit code {process.ExitCode}).{Environment.NewLine}{detail}");
        }

        TryDelete(installer);
        TryDelete(installer + ".log");
        TryDelete(Path.Combine(request.Folder, "installer.log"));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
