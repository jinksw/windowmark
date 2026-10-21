using System;
using System.IO;

namespace WindowMark.Core;

internal static class Log
{
    private static string FilePath => Path.Combine(LabelStore.DataDirectory, "log.txt");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}: {ex}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LabelStore.DataDirectory);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\r\n");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
