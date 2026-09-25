using System.IO;
using System.Net.Http;
using System.Reflection;

namespace ASimpleMinecraftServer.Services;

public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public DownloadService()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.2.1";
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ASimpleMinecraftServer/{version} (https://github.com/ASimpleMinecraftServer; desktop server manager)");
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

    public void Dispose() => _httpClient.Dispose();
}
