using ASimpleMinecraftServer.Models;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ASimpleMinecraftServer.Core;

public sealed class PluginService
{
    public string GetPluginFolder(ServerProfile profile) => Path.Combine(profile.Folder, "plugins");

    public IReadOnlyList<PluginRecord> List(ServerProfile profile)
    {
        var folder = GetPluginFolder(profile);
        if (!Directory.Exists(folder)) return Array.Empty<PluginRecord>();

        var records = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(ReadRecord)
            .OrderByDescending(plugin => plugin.IsEnabled)
            .ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in records.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var record in group) { record.IsDuplicate = true; record.RefreshHealth(); }

        return records;
    }

    public PluginRecord Install(ServerProfile profile, string sourcePath, bool overwrite)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected plugin file was not found.", sourcePath);
        if (!sourcePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only plugin JAR files can be installed.");

        var candidate = ReadRecord(sourcePath);
        if (!candidate.IsReadableJar) throw new InvalidDataException("The selected file is not a readable JAR/ZIP archive.");
        if (!candidate.HasDescriptor) throw new InvalidDataException("The selected JAR does not contain plugin.yml or paper-plugin.yml.");

        var folder = GetPluginFolder(profile);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(sourcePath));
        if (File.Exists(destination) && !overwrite)
            throw new IOException($"{Path.GetFileName(sourcePath)} is already installed.");
        FileOperationService.CopyFileAtomically(sourcePath, destination, overwrite);
        return ReadRecord(destination);
    }


    public PluginRecord InstallDownloaded(ServerProfile profile, string temporaryPath, string destinationFileName, bool overwrite)
    {
        if (!File.Exists(temporaryPath)) throw new FileNotFoundException("The downloaded plugin file was not found.", temporaryPath);
        if (!destinationFileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The catalog download is not a plugin JAR.");
        var candidate = ReadRecord(temporaryPath);
        if (!candidate.IsReadableJar) throw new InvalidDataException("The downloaded file is not a readable JAR/ZIP archive.");
        if (!candidate.HasDescriptor) throw new InvalidDataException("The downloaded JAR does not contain plugin.yml or paper-plugin.yml.");
        var folder = GetPluginFolder(profile);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(destinationFileName));
        if (File.Exists(destination) && !overwrite) throw new IOException($"{Path.GetFileName(destination)} is already installed.");
        FileOperationService.CopyFileAtomically(temporaryPath, destination, overwrite);
        return ReadRecord(destination);
    }


    public PluginRecord ReplaceWithDownloaded(ServerProfile profile, PluginRecord installed, string temporaryPath, string destinationFileName)
    {
        if (!File.Exists(temporaryPath)) throw new FileNotFoundException("The downloaded plugin update was not found.", temporaryPath);
        var candidate = ReadRecord(temporaryPath);
        if (!candidate.IsReadableJar) throw new InvalidDataException("The downloaded update is not a readable JAR/ZIP archive.");
        if (!candidate.HasDescriptor) throw new InvalidDataException("The downloaded update does not contain plugin.yml or paper-plugin.yml.");

        var folder = GetPluginFolder(profile);
        Directory.CreateDirectory(folder);
        var enabledDestination = Path.Combine(folder, Path.GetFileName(destinationFileName));
        if (!enabledDestination.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The downloaded update is not a plugin JAR.");
        var destination = installed.IsEnabled ? enabledDestination : enabledDestination + ".disabled";
        var backupFolder = Path.Combine(folder, ".asms-update-backups");
        Directory.CreateDirectory(backupFolder);
        var backup = Path.Combine(backupFolder, $"{Path.GetFileName(installed.FullPath)}.{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        File.Copy(installed.FullPath, backup, overwrite: false);

        try
        {
            FileOperationService.CopyFileAtomically(temporaryPath, destination, overwrite: true);
            if (!string.Equals(installed.FullPath, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(installed.FullPath))
                File.Delete(installed.FullPath);
            return ReadRecord(destination);
        }
        catch
        {
            if (File.Exists(backup)) File.Copy(backup, installed.FullPath, overwrite: true);
            throw;
        }
    }

    public PluginRecord SetEnabled(PluginRecord plugin, bool enabled)
    {
        var current = plugin.FullPath;
        var target = enabled
            ? current.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? current[..^9] : current
            : current.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? current : current + ".disabled";

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) return plugin;
        if (File.Exists(target)) throw new IOException($"Cannot rename the plugin because {Path.GetFileName(target)} already exists.");
        FileOperationService.MoveFileSafely(current, target);
        return ReadRecord(target);
    }

    public void Delete(PluginRecord plugin)
    {
        if (File.Exists(plugin.FullPath)) File.Delete(plugin.FullPath);
    }

    private static PluginRecord ReadRecord(string path)
    {
        var enabled = path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        var logicalFileName = enabled ? Path.GetFileName(path) : Path.GetFileName(path)[..^9];
        var fallbackName = Path.GetFileNameWithoutExtension(logicalFileName);
        var result = ReadMetadata(path);
        var metadata = result.Values;
        var info = new FileInfo(path);

        return new PluginRecord
        {
            Name = metadata.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallbackName,
            Version = metadata.TryGetValue("version", out var version) ? version : "Unknown",
            ApiVersion = metadata.TryGetValue("api-version", out var api) ? api : "—",
            MainClass = metadata.TryGetValue("main", out var main) ? main : "—",
            Authors = First(metadata, "authors", "author"),
            Dependencies = First(metadata, "depend", "dependencies"),
            SoftDependencies = First(metadata, "softdepend", "soft-dependencies"),
            FileName = Path.GetFileName(path),
            FullPath = path,
            SizeBytes = info.Exists ? info.Length : 0,
            LastModified = info.Exists ? info.LastWriteTime : DateTime.MinValue,
            IsEnabled = enabled,
            HasDescriptor = result.HasDescriptor,
            IsReadableJar = result.IsReadable
        };
    }

    private static string First(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys) if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return "—";
    }

    private static MetadataResult ReadMetadata(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(item =>
                item.FullName.Equals("plugin.yml", StringComparison.OrdinalIgnoreCase) ||
                item.FullName.Equals("paper-plugin.yml", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return new(values, false, true);
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? activeListKey = null;
            while (reader.ReadLine() is { } raw)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                if (trimmed.StartsWith('-') && activeListKey is not null)
                {
                    var item = trimmed[1..].Trim().Trim('"', '\'');
                    if (item.Length > 0) values[activeListKey] = values.TryGetValue(activeListKey, out var old) ? old + ", " + item : item;
                    continue;
                }
                var colon = trimmed.IndexOf(':');
                if (colon <= 0) { activeListKey = null; continue; }
                var key = trimmed[..colon].Trim();
                if (key is not ("name" or "version" or "api-version" or "main" or "author" or "authors" or "depend" or "dependencies" or "softdepend" or "soft-dependencies"))
                { activeListKey = null; continue; }
                var value = trimmed[(colon + 1)..].Trim().Trim('"', '\'', '[', ']');
                if (value.Length > 0) values[key] = value;
                activeListKey = value.Length == 0 ? key : null;
            }
            return new(values, true, true);
        }
        catch (InvalidDataException) { return new(values, false, false); }
        catch (IOException) { return new(values, false, false); }
    }

    private sealed record MetadataResult(Dictionary<string, string> Values, bool HasDescriptor, bool IsReadable);
}
