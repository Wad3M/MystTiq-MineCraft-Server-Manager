using ASimpleMinecraftServer.Models;
using System.IO.Compression;

namespace ASimpleMinecraftServer.Core;

public sealed class ExpandedWorldService
{
    public IReadOnlyList<WorldSummary> Discover(string serverFolder)=>Directory.Exists(serverFolder)?Directory.EnumerateDirectories(serverFolder).Where(d=>File.Exists(Path.Combine(d,"level.dat"))).Select(Summarize).OrderBy(w=>w.Name).ToList():Array.Empty<WorldSummary>();
    public WorldSummary Summarize(string folder)=>new(Path.GetFileName(folder),folder,Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Sum(f=>SafeLength(f)),Directory.GetLastWriteTimeUtc(folder),Directory.Exists(Path.Combine(folder,"DIM-1")),Directory.Exists(Path.Combine(folder,"DIM1")),File.Exists(Path.Combine(folder,"level.dat_old")));
    public string Backup(string worldFolder,string backupRoot)
    {
        if(!File.Exists(Path.Combine(worldFolder,"level.dat")))throw new InvalidDataException("Selected folder is not a Minecraft world."); Directory.CreateDirectory(backupRoot); var path=Path.Combine(backupRoot,$"{Path.GetFileName(worldFolder)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"); ZipFile.CreateFromDirectory(worldFolder,path,CompressionLevel.Fastest,false); return path;
    }
    public void Rename(string worldFolder,string newName)
    {
        if(string.IsNullOrWhiteSpace(newName)||newName.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Invalid world name.",nameof(newName)); var target=Path.Combine(Path.GetDirectoryName(worldFolder)!,newName); if(Directory.Exists(target))throw new IOException("A world folder with that name already exists."); Directory.Move(worldFolder,target);
    }
    public void ImportZip(string zipPath,string serverFolder,string worldName)
    {
        if(!File.Exists(zipPath))throw new FileNotFoundException("World ZIP not found.",zipPath); var target=Path.Combine(serverFolder,worldName); if(Directory.Exists(target))throw new IOException("The destination world already exists."); Directory.CreateDirectory(target); ZipFile.ExtractToDirectory(zipPath,target); if(!File.Exists(Path.Combine(target,"level.dat"))){Directory.Delete(target,true);throw new InvalidDataException("Imported archive does not contain level.dat at its root.");}
    }
    private static long SafeLength(string p){try{return new FileInfo(p).Length;}catch{return 0;}}
}
