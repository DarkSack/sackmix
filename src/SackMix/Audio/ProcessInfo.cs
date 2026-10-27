using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SackMix.Audio;

/// <summary>Path and readable name of the program behind a session.</summary>
internal static class ProcessInfo
{
    private const int QueryLimitedInformation = 0x1000;

    // Programs whose own description says little: javaw's is "Java(TM) Platform SE binary", and for a gamer it is Minecraft.
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["javaw.exe"] = "Minecraft (Java)",
        ["wallpaper32.exe"] = "Wallpaper Engine",
        ["wallpaper64.exe"] = "Wallpaper Engine",
    };

    /// <summary>Works for elevated programs too; null for protected ones (some anti-cheats).</summary>
    public static string? ImagePath(int pid)
    {
        using var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process.IsInvalid) return null;
        var buffer = new char[1024];
        var size = buffer.Length;
        return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }

    public static string ExeName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return $"pid{pid}.exe";
        }
    }

    /// <summary>The session's own name, else the program's description, else its exe name.</summary>
    public static string Describe(string exe, string? path, string? sessionName)
    {
        // "@%SystemRoot%\..." names point into resource files.
        if (!string.IsNullOrWhiteSpace(sessionName) && !sessionName.StartsWith('@')) return sessionName.Trim();
        if (KnownNames.TryGetValue(exe, out var known)) return known;
        if (path is not null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description)) return description;
            }
            catch (IOException) { }
        }
        var name = Path.GetFileNameWithoutExtension(exe);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : exe;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, int flags, char[] name, ref int size);
}
