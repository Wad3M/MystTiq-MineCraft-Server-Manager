using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ASimpleMinecraftServer.Core;

/// <summary>One file a modpack wants on the server, with where to get it and its expected hash.</summary>
public sealed record ModpackFile(string Path, Uri Download, string Sha512, long Size);

/// <summary>What a Modrinth .mrpack needs to become a server.</summary>
public sealed record ModpackPlan(string Name, string PackVersion, string MinecraftVersion, string Loader, string LoaderVersion, IReadOnlyList<ModpackFile> ServerFiles, int SkippedClientOnly);

/// <summary>
/// Reads Modrinth modpacks (.mrpack: a zip with modrinth.index.json) and installs their server
/// side safely: only files the pack marks for servers, only from Modrinth's allowed download
/// hosts, SHA-512 verified, and never written outside the server folder.
/// </summary>
public static class ModpackReader
{
    // Download hosts the .mrpack format permits.
    private static readonly string[] AllowedHosts = ["cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com"];

    public static ModpackPlan Read(string mrpackPath)
    {
        using var archive = ZipFile.OpenRead(mrpackPath);
        var entry = archive.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("This isn't a Modrinth modpack: modrinth.index.json is missing.");
        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream);
        return Parse(json.RootElement);
    }

    public static ModpackPlan Parse(JsonElement root)
    {
        if (!root.TryGetProperty("game", out var game) || game.GetString() != "minecraft")
            throw new InvalidDataException("This modpack isn't for Minecraft.");
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Modpack" : "Modpack";
        var packVersion = root.TryGetProperty("versionId", out var v) ? v.GetString() ?? string.Empty : string.Empty;

        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The modpack doesn't say which Minecraft version it needs.");
        var minecraft = deps.TryGetProperty("minecraft", out var mc) ? mc.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(minecraft)) throw new InvalidDataException("The modpack doesn't say which Minecraft version it needs.");

        var (loader, loaderVersion) = ("Vanilla", string.Empty);
        foreach (var (key, type) in new[] { ("fabric-loader", "Fabric"), ("quilt-loader", "Quilt"), ("neoforge", "NeoForge"), ("forge", "Forge") })
            if (deps.TryGetProperty(key, out var value)) { (loader, loaderVersion) = (type, value.GetString() ?? string.Empty); break; }

        var files = new List<ModpackFile>();
        var skipped = 0;
        if (root.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in list.EnumerateArray())
            {
                var path = file.GetProperty("path").GetString() ?? string.Empty;
                if (file.TryGetProperty("env", out var env) && env.TryGetProperty("server", out var server) && server.GetString() == "unsupported")
                {
                    skipped++;
                    continue;
                }
                EnsureSafeRelativePath(path);
                var sha512 = file.GetProperty("hashes").GetProperty("sha512").GetString()
                             ?? throw new InvalidDataException($"{path} has no SHA-512 hash, so it can't be verified.");
                var url = file.GetProperty("downloads").EnumerateArray()
                    .Select(d => Uri.TryCreate(d.GetString(), UriKind.Absolute, out var uri) ? uri : null)
                    .FirstOrDefault(uri => uri is not null && uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"{path} isn't downloadable from a host Modrinth allows.");
                var size = file.TryGetProperty("fileSize", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                files.Add(new ModpackFile(path, url, sha512, size));
            }
        }
        return new ModpackPlan(name, packVersion, minecraft, loader, loaderVersion, files, skipped);
    }

    /// <summary>Throws unless <paramref name="relative"/> stays inside the folder it's combined with.</summary>
    public static void EnsureSafeRelativePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)
            || Path.IsPathRooted(relative)
            || relative.Contains(':')
            || relative.Replace('\\', '/').Split('/').Any(part => part == ".."))
            throw new InvalidDataException($"The modpack contains an unsafe file path: {relative}");
    }

    /// <summary>Copies the pack's overrides/ then server-overrides/ folders into the server folder.</summary>
    public static int ApplyOverrides(string mrpackPath, string serverFolder)
    {
        var root = Path.GetFullPath(serverFolder) + Path.DirectorySeparatorChar;
        var count = 0;
        using var archive = ZipFile.OpenRead(mrpackPath);
        foreach (var prefix in new[] { "overrides/", "server-overrides/" })
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                var relative = entry.FullName[prefix.Length..];
                EnsureSafeRelativePath(relative);
                var destination = Path.GetFullPath(Path.Combine(serverFolder, relative));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The modpack contains an unsafe file path: {entry.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                count++;
            }
        }
        return count;
    }
}
