using System.IO;

namespace ASimpleMinecraftServer.Core;

public static class FileOperationService
{
    public static string GetSafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The requested path is outside the managed server folder.");
        return candidate;
    }

    public static void CopyFileAtomically(string source, string destination, bool overwrite)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The source file was not found.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && !overwrite)
            throw new IOException($"{Path.GetFileName(destination)} already exists.");

        var temp = destination + ".asms-copy-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temp, overwrite: false);
            if (File.Exists(destination)) File.Replace(temp, destination, null, ignoreMetadataErrors: true);
            else File.Move(temp, destination);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    public static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"The source folder was not found: {source}");
        if (Directory.Exists(destination))
            throw new IOException($"The destination folder already exists: {destination}");

        Directory.CreateDirectory(destination);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(GetSafeChildPath(destination, relative));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = GetSafeChildPath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    public static void MoveFileSafely(string source, string destination)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The source file was not found.", source);
        if (File.Exists(destination)) throw new IOException($"{Path.GetFileName(destination)} already exists.");
        File.Move(source, destination);
    }

    public static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { }
    }
}
