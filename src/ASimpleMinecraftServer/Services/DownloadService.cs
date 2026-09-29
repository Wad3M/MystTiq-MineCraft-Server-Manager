using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;

namespace ASimpleMinecraftServer.Services;

public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public DownloadService()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"MystTiq-MineCraft-Server-Manager/{version} (github.com/Wad3M/MystTiq-MineCraft-Server-Manager)");
    }

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default) =>
        _httpClient.GetStringAsync(url, cancellationToken);

    public Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken = default) =>
        DownloadFileAsync(url, destination, null, cancellationToken);

    public async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total is > 0) progress?.Report(written * 100d / total.Value);
        }
        progress?.Report(100);
    }

    /// <summary>
    /// Downloads a server JAR and, when the provider published a checksum, deletes the file and
    /// throws if it does not match.
    /// </summary>
    public async Task DownloadVerifiedAsync(ServerDownload download, string destination, CancellationToken cancellationToken = default)
    {
        await DownloadFileAsync(download.Url, destination, cancellationToken);
        if (string.IsNullOrWhiteSpace(download.Hash)) return;

        byte[] actual;
        await using (var stream = File.OpenRead(destination))
        {
            actual = download.HashAlgorithm switch
            {
                "SHA256" => await SHA256.HashDataAsync(stream, cancellationToken),
                "SHA1" => await SHA1.HashDataAsync(stream, cancellationToken),
                "MD5" => await MD5.HashDataAsync(stream, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported checksum type {download.HashAlgorithm}.")
            };
        }
        if (!Convert.ToHexString(actual).Equals(download.Hash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException($"The downloaded server JAR failed its {download.HashAlgorithm} check and was deleted. Try again, and report it if it keeps happening.");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
