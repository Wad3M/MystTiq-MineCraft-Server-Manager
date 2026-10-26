using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Core;

/// <summary>A player on the whitelist or operator list.</summary>
public sealed record ListedPlayer(string Name, string Uuid, int Level = 0);

/// <summary>
/// Reads and edits a stopped server's whitelist.json, ops.json and the whitelist switches in
/// server.properties. (While a server runs, MystTiq sends Minecraft's own commands instead.)
/// </summary>
public static partial class PlayerListFiles
{
    public const string Whitelist = "whitelist.json";
    public const string Ops = "ops.json";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Minecraft Java usernames: 3–16 letters, digits or underscores.</summary>
    public static bool IsValidName(string name) => ValidName().IsMatch(name);

    public static IReadOnlyList<ListedPlayer> Read(string serverFolder, string fileName)
    {
        var path = Path.Combine(serverFolder, fileName);
        if (!File.Exists(path)) return [];
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray array) return [];
            return array.OfType<JsonObject>()
                .Select(o => new ListedPlayer(o["name"]?.GetValue<string>() ?? "?", o["uuid"]?.GetValue<string>() ?? string.Empty,
                    o["level"] is JsonValue level && level.TryGetValue<int>(out var l) ? l : 0))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Adds or updates a player (matched by name, case-insensitive). Ops get level 4 and the standard fields.</summary>
    public static void Add(string serverFolder, string fileName, string name, string uuid)
    {
        var array = Load(serverFolder, fileName);
        Remove(array, name);
        var entry = new JsonObject { ["uuid"] = uuid, ["name"] = name };
        if (fileName == Ops) { entry["level"] = 4; entry["bypassesPlayerLimit"] = false; }
        array.Add(entry);
        Save(serverFolder, fileName, array);
    }

    public static bool Remove(string serverFolder, string fileName, string name)
    {
        var array = Load(serverFolder, fileName);
        var removed = Remove(array, name);
        if (removed) Save(serverFolder, fileName, array);
        return removed;
    }

    public static bool IsWhitelistEnforced(string serverFolder) =>
        ReadProperty(serverFolder, "white-list")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Turns the whitelist on or off. "enforce-whitelist" also kicks online players who aren't listed.</summary>
    public static void SetWhitelistEnforced(string serverFolder, bool enabled)
    {
        SetProperty(serverFolder, "white-list", enabled ? "true" : "false");
        SetProperty(serverFolder, "enforce-whitelist", enabled ? "true" : "false");
    }

    public static bool IsOnlineMode(string serverFolder) =>
        !(ReadProperty(serverFolder, "online-mode")?.Equals("false", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// The UUID an offline-mode server gives a player: a name-based (version 3) UUID of
    /// "OfflinePlayer:&lt;name&gt;", the same as Java's UUID.nameUUIDFromBytes.
    /// </summary>
    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return FormatUuid(Convert.ToHexString(hash).ToLowerInvariant());
    }

    /// <summary>Formats a 32-character hex id as a dashed UUID.</summary>
    public static string FormatUuid(string hex32) =>
        $"{hex32[..8]}-{hex32[8..12]}-{hex32[12..16]}-{hex32[16..20]}-{hex32[20..32]}";

    /// <summary>Looks up an online-mode player's UUID from Mojang. Null if the name doesn't exist.</summary>
    public static async Task<(string Name, string Uuid)?> LookupMojangAsync(HttpClient http, string name, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var id = json.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("Mojang returned no id.");
        if (!HexId().IsMatch(id)) throw new InvalidDataException("Mojang returned an unexpected id.");
        return (json.RootElement.GetProperty("name").GetString() ?? name, FormatUuid(id.ToLowerInvariant()));
    }

    public static string? ReadProperty(string serverFolder, string key)
    {
        var path = Path.Combine(serverFolder, "server.properties");
        if (!File.Exists(path)) return null;
        var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        return line?[(key.Length + 1)..].Trim();
    }

    public static void SetProperty(string serverFolder, string key, string value)
    {
        var path = Path.Combine(serverFolder, "server.properties");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var index = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (index >= 0) lines[index] = $"{key}={value}"; else lines.Add($"{key}={value}");
        File.WriteAllLines(path, lines);
    }

    private static JsonArray Load(string serverFolder, string fileName)
    {
        var path = Path.Combine(serverFolder, fileName);
        try { return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray array ? array : new JsonArray(); }
        catch (JsonException) { return new JsonArray(); }
    }

    private static bool Remove(JsonArray array, string name)
    {
        var matches = array.OfType<JsonObject>().Where(o => string.Equals(o["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var match in matches) array.Remove(match);
        return matches.Count > 0;
    }

    private static void Save(string serverFolder, string fileName, JsonArray array)
    {
        Directory.CreateDirectory(serverFolder);
        File.WriteAllText(Path.Combine(serverFolder, fileName), array.ToJsonString(Indented));
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex ValidName();

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex HexId();
}
