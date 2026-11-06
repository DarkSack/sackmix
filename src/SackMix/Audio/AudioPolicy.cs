using System.Runtime.InteropServices;

namespace SackMix.Audio;

/// <summary>
/// Which output a program plays on: what Settings › Sound › Volume mixer sets per app. Windows
/// keeps it per program and moves the program's sound there at once. The interface is not
/// documented (Windows.Media.Internal.AudioPolicyConfig, as EarTrumpet uses it), so every failure
/// just means "not available" and the program stays on Windows' output.
/// Call it from the UI thread only.
/// </summary>
internal static unsafe partial class AudioPolicy
{
    private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
    // Windows 10 21H2 / Windows 11 and later, then 1803–21H1. Same methods, other id.
    private static readonly Guid[] Iids = [new("ab3d4648-e242-459f-b02f-541c70306324"), new("2a59116d-6c4f-45e0-a74f-707e3fef9258")];
    // IInspectable's 6 methods + 19 others come first in its vtable.
    private const int SetSlot = 25, GetSlot = 26;
    private const int Render = 0, Console = 0, Multimedia = 1;
    // An MMDevice id as this interface wants it.
    private const string Prefix = @"\\?\SWD#MMDEVAPI#", RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    private static IntPtr _factory;
    private static bool _tried;

    public static bool Available => Factory() != IntPtr.Zero;

    /// <param name="deviceId">The output's MMDevice id, or null for Windows' own output.</param>
    public static bool Set(int pid, string? deviceId)
    {
        var f = Factory();
        if (f == IntPtr.Zero) return false;
        var h = IntPtr.Zero;
        if (deviceId is not null)
        {
            var full = Prefix + deviceId + RenderSuffix;
            if (WindowsCreateString(full, full.Length, out h) < 0) return false;
        }
        try
        {
            var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)(*(IntPtr**)f)[SetSlot];
            // Both roles, as the Settings page does.
            return set(f, (uint)pid, Render, Console, h) >= 0 & set(f, (uint)pid, Render, Multimedia, h) >= 0;
        }
        finally
        {
            if (h != IntPtr.Zero) WindowsDeleteString(h);
        }
    }

    /// <summary>The MMDevice id Windows has for the program, or null when it follows Windows' output.</summary>
    public static string? Get(int pid)
    {
        var f = Factory();
        if (f == IntPtr.Zero) return null;
        var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)(*(IntPtr**)f)[GetSlot];
        IntPtr h;
        if (get(f, (uint)pid, Render, Multimedia, &h) < 0 || h == IntPtr.Zero) return null;
        try
        {
            var s = new string(WindowsGetStringRawBuffer(h, out var length), 0, (int)length);
            if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) s = s[Prefix.Length..];
            var end = s.IndexOf('#');
            return end >= 0 ? s[..end] : s;
        }
        finally { WindowsDeleteString(h); }
    }

    private static IntPtr Factory()
    {
        if (_tried) return _factory;
        _tried = true;
        if (WindowsCreateString(ClassName, ClassName.Length, out var name) < 0) return IntPtr.Zero;
        try
        {
            foreach (var iid in Iids)
            {
                var id = iid;
                if (RoGetActivationFactory(name, ref id, out var f) >= 0) return _factory = f;
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
        finally { WindowsDeleteString(name); }
        return IntPtr.Zero;
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial char* WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
