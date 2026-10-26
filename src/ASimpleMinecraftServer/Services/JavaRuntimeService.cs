using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ASimpleMinecraftServer.Services;

/// <summary>An Eclipse Temurin download from the Adoptium API.</summary>
public sealed record JavaRuntimeAsset(string Url, string Sha256, string ReleaseName, long Size);

/// <summary>
/// Downloads and keeps Eclipse Temurin Java runtimes (from Adoptium) for servers that need a
/// Java version this PC doesn't have. Runtimes live in MystTiq's own folder, one per Java major
/// version, and are shared by every server that needs that version. Nothing is installed system-wide.
/// </summary>
public sealed class JavaRuntimeService(DownloadService downloads, string? rootFolder = null)
{
    public string RootFolder { get; } = rootFolder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASimpleMinecraftServer", "Java");

    /// <summary>
    /// The Temurin release line to use for a required Java version. Temurin only ships long-term
    /// releases (8, 11, 17, 21, 25), so other versions round up to the next one.
    /// </summary>
    public static int RuntimeFor(int requiredMajor) => requiredMajor switch
    {
        <= 8 => 8,
        <= 11 => 11,
        <= 17 => 17,
        <= 21 => 21,
        _ => 25
    };

    /// <summary>java.exe of an already downloaded runtime for this major version, or null.</summary>
    public string? FindInstalled(int major)
    {
        var folder = Path.Combine(RootFolder, $"temurin-{major}");
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateDirectories(folder)
            .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
            .Select(dir => Path.Combine(dir, "bin", "java.exe"))
            .Where(File.Exists)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Picks the Windows x64 ZIP package out of an Adoptium "assets/latest" response.</summary>
    public static JavaRuntimeAsset ParseLatestAsset(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Adoptium returned an unexpected response.");
        foreach (var asset in root.EnumerateArray())
        {
            if (!asset.TryGetProperty("binary", out var binary) || !binary.TryGetProperty("package", out var package)) continue;
            var link = package.TryGetProperty("link", out var l) ? l.GetString() : null;
            var checksum = package.TryGetProperty("checksum", out var c) ? c.GetString() : null;
            if (link is null || !link.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Adoptium returned a download link that isn't HTTPS.");
            if (string.IsNullOrWhiteSpace(checksum) || checksum.Length != 64)
                throw new InvalidDataException("Adoptium returned no SHA-256 checksum, so the download can't be verified.");
            var release = asset.TryGetProperty("release_name", out var r) ? r.GetString() ?? "temurin" : "temurin";
            var size = package.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            return new JavaRuntimeAsset(link, checksum, release, size);
        }
        throw new InvalidDataException("Adoptium has no Windows x64 download for this Java version.");
    }

    /// <summary>
    /// Returns java.exe for <paramref name="major"/>, downloading Temurin first when it isn't here yet.
    /// The JRE is preferred (smaller); the JDK is used when no JRE is published.
    /// </summary>
    public async Task<string> EnsureAsync(int major, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (FindInstalled(major) is { } existing) return existing;

        progress?.Report($"Looking up Java {major} on Adoptium…");
        JavaRuntimeAsset? asset = null;
        foreach (var imageType in new[] { "jre", "jdk" })
        {
            try
            {
                var json = await downloads.GetStringAsync(
                    $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type={imageType}&os=windows&vendor=eclipse", cancellationToken);
                using var doc = JsonDocument.Parse(json);
                asset = ParseLatestAsset(doc.RootElement);
                break;
            }
            catch (InvalidDataException) when (imageType == "jre") { }
        }
        if (asset is null) throw new InvalidOperationException($"Adoptium has no Java {major} download for Windows.");

        var target = Path.Combine(RootFolder, $"temurin-{major}");
        var staging = Path.Combine(target, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var zip = Path.Combine(staging, "java.zip");
            var sizeText = asset.Size > 0 ? $" ({asset.Size / 1024 / 1024} MB)" : string.Empty;
            var percent = new Progress<double>(p => progress?.Report($"Downloading Java {major} ({asset.ReleaseName}){sizeText}… {p:0}%"));
            await downloads.DownloadVerifiedAsync(new ServerDownload(asset.Url, "SHA256", asset.Sha256), zip, cancellationToken, percent);

            progress?.Report($"Unpacking Java {major}…");
            var extracted = Path.Combine(staging, "x");
            // ExtractToDirectory refuses entries that would land outside the folder.
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extracted), cancellationToken);
            var java = Directory.EnumerateFiles(extracted, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin")
                ?? throw new InvalidDataException("The Java download didn't contain bin\\java.exe.");
            var home = Path.GetDirectoryName(Path.GetDirectoryName(java))!;
            var final = Path.Combine(target, SafeName(asset.ReleaseName));
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(home, final);
            return Path.Combine(final, "bin", "java.exe");
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) || ch == '+' ? '_' : ch).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "temurin" : cleaned;
    }
}
