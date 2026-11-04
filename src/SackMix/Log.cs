using SackMix.Core;

namespace SackMix;

/// <summary>%LOCALAPPDATA%\SackMix\sackmix.log, started over past 256 KB.</summary>
internal static class Log
{
    private static readonly Lock Gate = new();

    public static void Error(string where, Exception e)
    {
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(ConfigStore.Folder, "sackmix.log");
                Directory.CreateDirectory(ConfigStore.Folder);
                if (new FileInfo(path) is { Exists: true, Length: > 256 * 1024 }) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {e}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
