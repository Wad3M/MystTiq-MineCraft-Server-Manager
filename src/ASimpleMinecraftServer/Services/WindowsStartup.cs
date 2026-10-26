using Microsoft.Win32;

namespace ASimpleMinecraftServer.Services;

/// <summary>
/// "Start MystTiq when I sign in to Windows": a value under the current user's Run key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). No admin rights or scheduled tasks needed.
/// </summary>
public static class WindowsStartup
{
    public const string MinimizedArgument = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MystTiq Minecraft Server Manager";

    /// <summary>The command Windows runs at sign-in: this exe, started in the tray.</summary>
    public static string Command(string exePath) => $"\"{exePath}\" {MinimizedArgument}";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Turns sign-in start on or off. When on, it always points at the exe that's running now,
    /// so moving the app and turning it on again fixes a stale entry.
    /// </summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("MystTiq couldn't find its own program file.");
            key.SetValue(ValueName, Command(exe), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
