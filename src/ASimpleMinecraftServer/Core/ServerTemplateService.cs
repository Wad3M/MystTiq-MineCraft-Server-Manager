using ASimpleMinecraftServer.Models;

namespace ASimpleMinecraftServer.Core;

public sealed class ServerTemplateService
{
    public IReadOnlyList<ServerTemplate> GetBuiltInTemplates() => new[]
    {
        new ServerTemplate("Vanilla Starter", "Small private vanilla server.", "Vanilla", 4, new Dictionary<string,string>{{"difficulty","normal"},{"view-distance","10"},{"simulation-distance","10"},{"max-players","10"}}),
        new ServerTemplate("Paper Performance", "Balanced Paper server for friends and plugins.", "Paper", 6, new Dictionary<string,string>{{"difficulty","normal"},{"view-distance","8"},{"simulation-distance","6"},{"max-players","20"}}),
        new ServerTemplate("Creative Builder", "Creative mode with command blocks enabled.", "Paper", 6, new Dictionary<string,string>{{"gamemode","creative"},{"enable-command-block","true"},{"spawn-protection","0"}})
    };

    public IReadOnlyList<string> Validate(ServerTemplate template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template.Name)) errors.Add("Template name is required.");
        if (template.MemoryGb < 1) errors.Add("Template memory must be at least 1 GB.");
        if (template.Properties.Count == 0) errors.Add("Template does not contain any server properties.");
        return errors;
    }

    public string Preview(ServerTemplate template) => string.Join(Environment.NewLine, template.Properties.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));


    public void Apply(ServerTemplate template, string serverFolder)
    {
        Directory.CreateDirectory(serverFolder);
        var path=Path.Combine(serverFolder,"server.properties");
        var existing=File.Exists(path)?File.ReadAllLines(path).ToList():new List<string>();
        foreach(var pair in template.Properties)
        {
            var prefix=pair.Key+"="; var i=existing.FindIndex(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
            if(i>=0) existing[i]=prefix+pair.Value; else existing.Add(prefix+pair.Value);
        }
        File.WriteAllLines(path,existing);
    }
}
