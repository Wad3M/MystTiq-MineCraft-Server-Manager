using ASimpleMinecraftServer.Models;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

public sealed class ResourcePackService
{
    public ResourcePackInfo Validate(string zipPath)
    {
        if(!File.Exists(zipPath)) throw new FileNotFoundException("Resource-pack ZIP not found.",zipPath);
        using(var archive=ZipFile.OpenRead(zipPath)) if(archive.GetEntry("pack.mcmeta") is null) throw new InvalidDataException("The ZIP does not contain pack.mcmeta at its root.");
        using var stream=File.OpenRead(zipPath); var hash=Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
        return new ResourcePackInfo(Path.GetFileName(zipPath),hash,new FileInfo(zipPath).Length);
    }
    public IReadOnlyList<string> ValidateDownloadSettings(string downloadUrl, string sha1)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) errors.Add("Enter a valid HTTP or HTTPS resource-pack URL.");
        if (!Regex.IsMatch(sha1 ?? string.Empty, "^[a-fA-F0-9]{40}$")) errors.Add("SHA-1 must contain exactly 40 hexadecimal characters.");
        return errors;
    }

    public void ClearFromProperties(string propertiesPath)
    {
        ApplyToProperties(propertiesPath, string.Empty, string.Empty, false);
    }


    public void ApplyToProperties(string propertiesPath,string downloadUrl,string sha1,bool required)
    {
        var values=new Dictionary<string,string>{{"resource-pack",downloadUrl},{"resource-pack-sha1",sha1},{"require-resource-pack",required.ToString().ToLowerInvariant()}};
        var lines=File.Exists(propertiesPath)?File.ReadAllLines(propertiesPath).ToList():new List<string>();
        foreach(var pair in values){var prefix=pair.Key+"=";var i=lines.FindIndex(x=>x.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));if(i>=0)lines[i]=prefix+pair.Value;else lines.Add(prefix+pair.Value);}
        File.WriteAllLines(propertiesPath,lines);
    }
}
