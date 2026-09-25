using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;

namespace ASimpleMinecraftServer.Core;

public sealed class WorldService
{
    public IReadOnlyList<WorldRecord> Scan(ServerProfile profile)
    {
        if (!Directory.Exists(profile.Folder)) return [];
        var levelName = ReadLevelName(profile.Folder);
        var candidates = new[]
        {
            (levelName, "Overworld", true),
            ($"{levelName}_nether", "Nether", false),
            ($"{levelName}_the_end", "The End", false)
        };
        var records = new List<WorldRecord>();
        foreach (var item in candidates)
        {
            var path = Path.Combine(profile.Folder, item.Item1);
            if (!Directory.Exists(path)) continue;
            records.Add(CreateRecord(path, item.Item2, item.Item3));
        }
        foreach (var path in Directory.EnumerateDirectories(profile.Folder))
        {
            if (records.Any(r => string.Equals(r.FolderPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (File.Exists(Path.Combine(path, "level.dat"))) records.Add(CreateRecord(path, "Additional world", false));
        }
        return records.OrderByDescending(r => r.IsPrimary).ThenBy(r => r.Dimension).ThenBy(r => r.Name).ToList();
    }

    public async Task CreateArchiveAsync(WorldRecord world, string destination, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(world.FolderPath, "*", SearchOption.AllDirectories).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempDestination = destination + ".asms-backup-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempDestination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                for (var i = 0; i < files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = files[i];
                    try { archive.CreateEntryFromFile(file, Path.GetRelativePath(world.FolderPath, file), CompressionLevel.Fastest); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    progress?.Report(files.Count == 0 ? 100 : (i + 1) * 100 / files.Count);
                    await Task.Yield();
                }
            }

            if (File.Exists(destination)) File.Delete(destination);
            File.Move(tempDestination, destination);
        }
        catch
        {
            FileOperationService.TryDeleteFile(tempDestination);
            throw;
        }
    }

    public void Rename(ServerProfile profile, WorldRecord world, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("Enter a valid world folder name.");
        var destination = Path.Combine(profile.Folder, newName);
        if (Directory.Exists(destination)) throw new IOException("A folder with that name already exists.");
        Directory.Move(world.FolderPath, destination);
        try
        {
            if (world.IsPrimary) UpdateLevelName(profile.Folder, newName);
        }
        catch
        {
            try { if (!Directory.Exists(world.FolderPath) && Directory.Exists(destination)) Directory.Move(destination, world.FolderPath); }
            catch { }
            throw;
        }
    }

    public void Delete(WorldRecord world) => Directory.Delete(world.FolderPath, true);

    public void ImportArchive(ServerProfile profile, string archivePath, string folderName)
    {
        folderName = folderName.Trim();
        if (string.IsNullOrWhiteSpace(folderName) || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("Enter a valid destination folder name.");
        var destination = Path.Combine(profile.Folder, folderName);
        if (Directory.Exists(destination)) throw new IOException("The destination world folder already exists.");

        using var archive = ZipFile.OpenRead(archivePath);
        var fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (fileEntries.Count == 0) throw new InvalidDataException("The selected ZIP archive is empty.");
        var normalized = fileEntries.Select(e => e.FullName.Replace('\\', '/').TrimStart('/')).ToList();
        var hasRootLevelDat = normalized.Any(n => string.Equals(n, "level.dat", StringComparison.OrdinalIgnoreCase));
        var roots = normalized.Select(n => n.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wrappedRoot = !hasRootLevelDat && roots.Count == 1 ? roots[0] : null;
        var hasWrappedLevelDat = wrappedRoot is not null && normalized.Any(n => string.Equals(n, wrappedRoot + "/level.dat", StringComparison.OrdinalIgnoreCase));
        if (!hasRootLevelDat && !hasWrappedLevelDat) throw new InvalidDataException("The ZIP does not contain a valid Minecraft world (level.dat was not found).");

        Directory.CreateDirectory(destination);
        try
        {
            foreach (var entry in fileEntries)
            {
                var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (wrappedRoot is not null && relative.StartsWith(wrappedRoot + "/", StringComparison.OrdinalIgnoreCase)) relative = relative[(wrappedRoot.Length + 1)..];
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var target = FileOperationService.GetSafeChildPath(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: false);
            }
        }
        catch { FileOperationService.TryDeleteDirectory(destination); throw; }
    }

    public void Duplicate(ServerProfile profile, WorldRecord world, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidOperationException("Enter a valid world folder name.");
        var destination = Path.Combine(profile.Folder, newName);
        if (Directory.Exists(destination)) throw new IOException("A folder with that name already exists.");
        FileOperationService.CopyDirectory(world.FolderPath, destination);
    }

    public void RestoreArchive(ServerProfile profile, string archivePath, string folderName, bool overwrite)
    {
        var destination = Path.Combine(profile.Folder, folderName.Trim());
        if (Directory.Exists(destination))
        {
            if (!overwrite) throw new IOException("The destination world folder already exists.");
            Directory.Delete(destination, true);
        }
        ImportArchive(profile, archivePath, folderName);
    }

    public void Export(WorldRecord world, string destination) =>
        ZipFile.CreateFromDirectory(world.FolderPath, destination, CompressionLevel.Fastest, false);

    public IReadOnlyList<string> ValidateSafety(WorldRecord world)
    {
        var issues = new List<string>();
        if (!Directory.Exists(world.FolderPath)) issues.Add("World folder is missing.");
        if (!File.Exists(Path.Combine(world.FolderPath, "level.dat"))) issues.Add("level.dat is missing.");
        if (File.Exists(Path.Combine(world.FolderPath, "session.lock")))
        {
            try { using var stream = File.Open(Path.Combine(world.FolderPath, "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch { issues.Add("The world appears to be locked by another process."); }
        }
        if (world.SizeBytes == 0) issues.Add("World folder appears empty.");
        return issues;
    }


    private static WorldRecord CreateRecord(string path, string dimension, bool primary)
    {
        long size = 0; DateTime modified = DateTime.MinValue;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { var info = new FileInfo(file); size += info.Length; if (info.LastWriteTime > modified) modified = info.LastWriteTime; } catch { }
            }
        }
        catch { }
        var levelDat = Path.Combine(path, "level.dat");
        var levelDatOld = Path.Combine(path, "level.dat_old");
        var region = Path.Combine(path, "region");
        var hasLevelDat = File.Exists(levelDat);
        var hasRegionData = Directory.Exists(region) && Directory.EnumerateFiles(region, "*.mca", SearchOption.TopDirectoryOnly).Any();
        var playerDataFolder = Path.Combine(path, "playerdata");
        var playerDataCount = Directory.Exists(playerDataFolder) ? Directory.EnumerateFiles(playerDataFolder, "*.dat", SearchOption.TopDirectoryOnly).Count() : 0;
        var issues = new List<string>();
        if (!hasLevelDat) issues.Add("level.dat is missing");
        else
        {
            try { using var stream = File.Open(levelDat, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); if (stream.Length < 16) issues.Add("level.dat appears incomplete"); }
            catch (Exception ex) { issues.Add("level.dat cannot be read: " + ex.Message); }
        }
        if (!hasRegionData && string.Equals(dimension, "Overworld", StringComparison.OrdinalIgnoreCase)) issues.Add("no Overworld region files were found");
        var health = issues.Count == 0 ? "Healthy" : hasLevelDat || File.Exists(levelDatOld) ? "Warning" : "Critical";
        var details = issues.Count == 0
            ? $"Required world files are present. Region data detected. Player data files: {playerDataCount}."
            : string.Join("; ", issues) + ".";
        return new WorldRecord
        {
            Name = Path.GetFileName(path), FolderPath = path, Dimension = dimension, IsPrimary = primary,
            SizeBytes = size, LastModified = modified, HasLevelDat = hasLevelDat, HasRegionData = hasRegionData,
            PlayerDataCount = playerDataCount, Health = health, HealthDetails = details
        };
    }

    private static string ReadLevelName(string serverFolder)
    {
        var properties = Path.Combine(serverFolder, "server.properties");
        if (!File.Exists(properties)) return "world";
        var line = File.ReadLines(properties).FirstOrDefault(l => l.StartsWith("level-name=", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(line) ? "world" : line.Split('=', 2)[1].Trim();
    }

    private static void UpdateLevelName(string serverFolder, string value)
    {
        var path = Path.Combine(serverFolder, "server.properties");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var index = lines.FindIndex(l => l.StartsWith("level-name=", StringComparison.OrdinalIgnoreCase));
        if (index >= 0) lines[index] = $"level-name={value}"; else lines.Add($"level-name={value}");
        File.WriteAllLines(path, lines);
    }
}
