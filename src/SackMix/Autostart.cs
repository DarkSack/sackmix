using Microsoft.Win32;

namespace SackMix;

/// <summary>
/// Start with Windows: a value under HKCU\...\Run, the same one the installer writes. Per user,
/// without admin, and it shows in Task Manager › Startup apps. --autostart starts in the tray.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SackMix";

    private static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    /// <summary>On for this copy of the exe (one pointing at another copy counts as off).</summary>
    public static bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && v.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (on) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
