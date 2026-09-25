using ASimpleMinecraftServer.Models;

namespace ASimpleMinecraftServer.Core;

public sealed class MigrationService
{
    public IReadOnlyList<string> Validate(MigrationPlan plan)
    {
        var errors=new List<string>(); if(!Directory.Exists(plan.SourceFolder))errors.Add("Source server folder does not exist.");
        if(string.IsNullOrWhiteSpace(plan.DestinationFolder))errors.Add("Destination folder is required.");
        if(Path.GetFullPath(plan.SourceFolder).Equals(Path.GetFullPath(plan.DestinationFolder),StringComparison.OrdinalIgnoreCase))errors.Add("Source and destination must be different."); return errors;
    }
    public IReadOnlyList<string> Preview(MigrationPlan plan)
    {
        var items = new List<string>();
        if (plan.CopyConfiguration) items.Add("Configuration files");
        if (plan.CopyPlugins) items.Add("Plugin folder");
        if (plan.CopyWorlds) items.Add("Detected Minecraft worlds");
        return items;
    }

    public long EstimateBytes(MigrationPlan plan)
    {
        if (!Directory.Exists(plan.SourceFolder)) return 0;
        IEnumerable<string> files = Directory.EnumerateFiles(plan.SourceFolder, "*", SearchOption.AllDirectories);
        if (!plan.CopyPlugins) files = files.Where(f => !f.StartsWith(Path.Combine(plan.SourceFolder, "plugins") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        long total = 0; foreach (var file in files) { try { total += new FileInfo(file).Length; } catch { } }
        return total;
    }


    public void Execute(MigrationPlan plan)
    {
        var errors=Validate(plan); if(errors.Count>0)throw new InvalidOperationException(string.Join(Environment.NewLine,errors)); Directory.CreateDirectory(plan.DestinationFolder);
        if(plan.CopyConfiguration) foreach(var file in new[]{"server.properties","eula.txt","ops.json","whitelist.json","banned-players.json","banned-ips.json"}) CopyFile(plan.SourceFolder,plan.DestinationFolder,file);
        if(plan.CopyPlugins) CopyDirectory(Path.Combine(plan.SourceFolder,"plugins"),Path.Combine(plan.DestinationFolder,"plugins"));
        if(plan.CopyWorlds) foreach(var dir in Directory.EnumerateDirectories(plan.SourceFolder).Where(d=>File.Exists(Path.Combine(d,"level.dat")))) CopyDirectory(dir,Path.Combine(plan.DestinationFolder,Path.GetFileName(dir)));
    }
    private static void CopyFile(string s,string d,string n){var p=Path.Combine(s,n);if(File.Exists(p))File.Copy(p,Path.Combine(d,n),true);}
    private static void CopyDirectory(string s,string d){if(!Directory.Exists(s))return;Directory.CreateDirectory(d);foreach(var f in Directory.EnumerateFiles(s))File.Copy(f,Path.Combine(d,Path.GetFileName(f)),true);foreach(var x in Directory.EnumerateDirectories(s))CopyDirectory(x,Path.Combine(d,Path.GetFileName(x)));}
}
