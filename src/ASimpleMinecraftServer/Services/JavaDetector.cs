using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ASimpleMinecraftServer.Services;

public sealed record JavaInstallation(string ExecutablePath, string VersionText, int MajorVersion, string Architecture)
{
    public string Summary => MajorVersion > 0 ? $"Java {MajorVersion} ({Architecture})" : VersionText.Split('\n')[0].Trim();
}

public sealed record JavaValidationResult(bool IsValid, JavaInstallation? Installation, int RequiredMajorVersion, string Message);

public static partial class JavaDetector
{
    public static async Task<JavaInstallation?> DetectAsync(CancellationToken cancellationToken = default) =>
        await InspectAsync("java", cancellationToken);

    public static async Task<JavaInstallation?> InspectAsync(string? executablePath, CancellationToken cancellationToken = default)
    {
        var candidate = string.IsNullOrWhiteSpace(executablePath) ? "java" : executablePath.Trim().Trim('"');
        if (!candidate.Equals("java", StringComparison.OrdinalIgnoreCase) && !File.Exists(candidate)) return null;

        try
        {
            var info = new ProcessStartInfo(candidate, "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(info);
            if (process is null) return null;
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            var output = await outputTask;
            if (process.ExitCode != 0) return null;
            var text = string.Join(Environment.NewLine, new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            var major = ParseMajorVersion(text);
            var architecture = text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "64-bit" :
                               text.Contains("32-Bit", StringComparison.OrdinalIgnoreCase) ? "32-bit" : "architecture unknown";
            return new JavaInstallation(candidate, text, major, architecture);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<JavaValidationResult> ValidateAsync(string? executablePath, string? minecraftVersion, CancellationToken cancellationToken = default)
    {
        var required = GetRequiredMajorVersion(minecraftVersion);
        var installation = await InspectAsync(executablePath, cancellationToken);
        if (installation is null)
        {
            var checkedPath = string.IsNullOrWhiteSpace(executablePath) || executablePath.Equals("java", StringComparison.OrdinalIgnoreCase)
                ? "Windows PATH (java.exe)"
                : executablePath;
            return new(false, null, required, $"Java was not found or could not be started. Checked: {checkedPath}.");
        }
        if (installation.MajorVersion == 0)
            return new(false, installation, required, "Java was found, but its version could not be identified.");
        if (installation.MajorVersion < required)
            return new(false, installation, required, $"Minecraft {minecraftVersion} requires Java {required} or newer, but Java {installation.MajorVersion} was found.");
        return new(true, installation, required, $"{installation.Summary} is compatible with Minecraft {minecraftVersion}.");
    }

    public static int GetRequiredMajorVersion(string? minecraftVersion)
    {
        if (!Version.TryParse(NormalizeVersion(minecraftVersion), out var version)) return 21;
        if (version.Major >= 26) return 25; // Year-based versions (26.1+) require Java 25.
        if (version >= new Version(1, 20, 5)) return 21;
        if (version >= new Version(1, 18)) return 17;
        if (version >= new Version(1, 17)) return 16;
        return 8;
    }

    public static int ParseMajorVersion(string versionText)
    {
        var match = JavaVersionRegex().Match(versionText ?? string.Empty);
        if (!match.Success) return 0;
        var raw = match.Groups[1].Value;
        if (raw.StartsWith("1.", StringComparison.Ordinal) && int.TryParse(raw.Split('.')[1], out var legacy)) return legacy;
        return int.TryParse(raw.Split('.')[0], out var major) ? major : 0;
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = MinecraftVersionRegex().Match(value);
        return match.Success ? match.Value : value.Trim();
    }

    [GeneratedRegex("version\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex JavaVersionRegex();

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?")]
    private static partial Regex MinecraftVersionRegex();
}
