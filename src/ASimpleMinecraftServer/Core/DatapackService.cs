using ASimpleMinecraftServer.Models;
using System.IO.Compression;

namespace ASimpleMinecraftServer.Core;

public sealed class DatapackService
{
    public IReadOnlyList<DatapackRecord> Scan(string worldFolder)
    {
        var folder=Path.Combine(worldFolder,"datapacks"); if(!Directory.Exists(folder)) return Array.Empty<DatapackRecord>();
        return Directory.EnumerateFileSystemEntries(folder).Select(p=>new DatapackRecord(Path.GetFileName(p),p,!Path.GetFileName(p).EndsWith(".disabled",StringComparison.OrdinalIgnoreCase),GetSize(p))).ToList();
    }
    public void Install(string source,string worldFolder)
    {
        var targetFolder=Path.Combine(worldFolder,"datapacks"); Directory.CreateDirectory(targetFolder);
        if(File.Exists(source) && Path.GetExtension(source).Equals(".zip",StringComparison.OrdinalIgnoreCase))
        {
            using var a=ZipFile.OpenRead(source); if(a.GetEntry("pack.mcmeta") is null) throw new InvalidDataException("The ZIP does not contain pack.mcmeta at its root.");
            File.Copy(source,Path.Combine(targetFolder,Path.GetFileName(source)),true);
        } else throw new FileNotFoundException("Datapack ZIP not found.",source);
    }
    public void Remove(DatapackRecord record)
    {
        if (File.Exists(record.Path)) File.Delete(record.Path);
        else if (Directory.Exists(record.Path)) Directory.Delete(record.Path, true);
    }

    public string SetEnabled(DatapackRecord record, bool enabled)
    {
        var current = record.Path;
        var disabled = current.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        if (enabled && disabled)
        {
            var target = current[..^9];
            if (File.Exists(current)) File.Move(current, target); else Directory.Move(current, target);
            return target;
        }
        if (!enabled && !disabled)
        {
            var target = current + ".disabled";
            if (File.Exists(current)) File.Move(current, target); else Directory.Move(current, target);
            return target;
        }
        return current;
    }


    private static long GetSize(string p)=>File.Exists(p)?new FileInfo(p).Length:Directory.EnumerateFiles(p,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
}
