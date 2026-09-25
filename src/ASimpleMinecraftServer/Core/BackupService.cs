using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;

namespace ASimpleMinecraftServer.Core;

public sealed class BackupService
{
    public string BackupRoot { get; }

    public BackupService(string backupRoot)
    {
        BackupRoot = backupRoot;
        Directory.CreateDirectory(BackupRoot);
    }

    public string GetServerBackupFolder(ServerProfile profile)
    {
        var safe = string.Concat(profile.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "Minecraft Server";
        var folder = Path.Combine(BackupRoot, safe);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public IReadOnlyList<BackupRecord> List(ServerProfile profile)
    {
        var folder = GetServerBackupFolder(profile);
        return Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var type = info.Name.Contains("Safety", StringComparison.OrdinalIgnoreCase) ? "Safety" :
                           info.Name.Contains("Automatic", StringComparison.OrdinalIgnoreCase) ? "Automatic" : "Manual";
                return new BackupRecord { FilePath = path, CreatedAt = info.CreationTime, SizeBytes = info.Length, Type = type };
            })
            .OrderByDescending(item => item.CreatedAt).ToList();
    }

    public async Task<BackupRecord> CreateAsync(ServerProfile profile, string type, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(profile.Folder)) throw new DirectoryNotFoundException("The server folder no longer exists.");
        var targetFolder = GetServerBackupFolder(profile);
        var fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{type}.zip";
        var target = Path.Combine(targetFolder, fileName);
        var temp = target + ".partial";
        progress?.Report("Scanning server files…");
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(temp, ZipArchiveMode.Create);
                foreach (var file in Directory.EnumerateFiles(profile.Folder, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(profile.Folder, file);
                    if (relative.StartsWith("_Backups" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relative.StartsWith(".asms" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    try { archive.CreateEntryFromFile(file, relative, CompressionLevel.Fastest); }
                    catch (IOException) { /* Skip transient locked log/session files. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }, cancellationToken);
            File.Move(temp, target);
            var info = new FileInfo(target);
            progress?.Report("Backup complete.");
            return new BackupRecord { FilePath = target, CreatedAt = info.CreationTime, SizeBytes = info.Length, Type = type };
        }
        catch { if (File.Exists(temp)) File.Delete(temp); throw; }
    }

    public async Task RestoreAsync(ServerProfile profile, BackupRecord backup, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backup.FilePath)) throw new FileNotFoundException("The selected backup archive was not found.");
        var staging = Path.Combine(Path.GetTempPath(), "ASMS-Restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report("Verifying backup archive…");
            await Task.Run(() => ZipFile.ExtractToDirectory(backup.FilePath, staging, true), cancellationToken);
            progress?.Report("Replacing server files…");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(profile.Folder);
                foreach (var file in Directory.EnumerateFiles(profile.Folder, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
                foreach (var directory in Directory.EnumerateDirectories(profile.Folder, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                    if (Directory.Exists(directory)) Directory.Delete(directory, false);
                foreach (var directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(profile.Folder, Path.GetRelativePath(staging, directory)));
                foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(profile.Folder, Path.GetRelativePath(staging, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, true);
                }
            }, cancellationToken);
            progress?.Report("Restore complete.");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public void Delete(BackupRecord backup) { if (File.Exists(backup.FilePath)) File.Delete(backup.FilePath); }

    public void ApplyRetention(ServerProfile profile, int keep)
    {
        if (keep < 1) return;
        foreach (var backup in List(profile).Skip(keep)) Delete(backup);
    }

    public void ApplyAutomaticRetention(ServerProfile profile, int keep)
    {
        if (keep < 1) return;
        foreach (var backup in List(profile)
                     .Where(item => item.Type.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                     .Skip(keep))
        {
            Delete(backup);
        }
    }
}
