using ASimpleMinecraftServer.Models;
using System.Diagnostics;
using System.IO;

namespace ASimpleMinecraftServer.Core;

public sealed class ServerProcessExitedEventArgs : EventArgs
{
    public int? ExitCode { get; init; }
    public bool StopWasRequested { get; init; }
    public bool WasForceKilled { get; init; }
    public bool WasAttachedProcess { get; init; }
}

public sealed class ServerLauncher : IDisposable
{
    private Process? _process;
    private bool _stopRequested;
    private bool _forceKilled;
    private bool _attachedProcess;

    public bool IsRunning => _process is { HasExited: false };
    public bool CanSendCommands => IsRunning && !_attachedProcess;
    public bool IsAttachedProcess => IsRunning && _attachedProcess;
    public int? ProcessId => IsRunning ? _process?.Id : null;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<ServerProcessExitedEventArgs>? Exited;

    public bool TryGetPerformanceSnapshot(out TimeSpan totalProcessorTime, out long workingSetBytes)
    {
        totalProcessorTime = TimeSpan.Zero;
        workingSetBytes = 0;
        var process = _process;
        if (process is null) return false;

        try
        {
            if (process.HasExited) return false;
            process.Refresh();
            totalProcessorTime = process.TotalProcessorTime;
            workingSetBytes = process.WorkingSet64;
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public void Start(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (IsRunning) throw new InvalidOperationException("A Minecraft server process is already being managed.");
        if (string.IsNullOrWhiteSpace(profile.Folder) || !Directory.Exists(profile.Folder))
            throw new DirectoryNotFoundException("The selected server folder does not exist.");

        var jarPath = Path.Combine(profile.Folder, profile.Jar);
        if (!File.Exists(jarPath)) throw new FileNotFoundException("The configured server JAR could not be found.", jarPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(profile.JavaPath) ? "java" : profile.JavaPath,
            WorkingDirectory = profile.Folder,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-Xms1G");
        startInfo.ArgumentList.Add($"-Xmx{Math.Max(1, profile.MemoryGb)}G");
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(profile.Jar);
        startInfo.ArgumentList.Add("nogui");

        _stopRequested = false;
        _forceKilled = false;
        _attachedProcess = false;
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;

        try
        {
            if (!_process.Start()) throw new InvalidOperationException("Java did not start the server process.");
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch
        {
            DisposeProcess();
            throw;
        }
    }

    public bool TryAttach(int processId)
    {
        if (IsRunning) return false;
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited || !process.ProcessName.StartsWith("java", StringComparison.OrdinalIgnoreCase))
            {
                process.Dispose();
                return false;
            }
            _process = process;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
            _attachedProcess = true;
            _stopRequested = false;
            _forceKilled = false;
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public void SendCommand(string command)
    {
        if (!CanSendCommands || string.IsNullOrWhiteSpace(command)) return;
        _process!.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    public async Task ForceKillAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        var process = _process!;
        _stopRequested = true;
        _forceKilled = true;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public async Task<bool> TryStopGracefullyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return true;
        var process = _process!;
        _stopRequested = true;

        if (!CanSendCommands) return false;
        SendCommand("stop");

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return !IsRunning;
        }
    }

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!IsRunning) return;
        var process = _process!;
        _stopRequested = true;

        if (CanSendCommands) SendCommand("stop");

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_attachedProcess ? TimeSpan.FromSeconds(2) : timeout);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    _forceKilled = true;
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException) { }
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null) OutputReceived?.Invoke(this, e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null) OutputReceived?.Invoke(this, "[ERR] " + e.Data);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int? exitCode = null;
        try { exitCode = _process?.ExitCode; } catch { }
        var args = new ServerProcessExitedEventArgs
        {
            ExitCode = exitCode,
            StopWasRequested = _stopRequested,
            WasForceKilled = _forceKilled,
            WasAttachedProcess = _attachedProcess
        };
        Exited?.Invoke(this, args);
        DisposeProcess();
    }

    private void DisposeProcess()
    {
        if (_process is null) return;
        _process.OutputDataReceived -= OnOutputDataReceived;
        _process.ErrorDataReceived -= OnErrorDataReceived;
        _process.Exited -= OnProcessExited;
        _process.Dispose();
        _process = null;
        _attachedProcess = false;
    }

    public void Dispose()
    {
        DisposeProcess();
        GC.SuppressFinalize(this);
    }
}
