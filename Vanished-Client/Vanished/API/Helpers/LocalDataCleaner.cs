using System;
using System.IO;

namespace Vanished.API.Helpers;

public static class LocalDataCleaner
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished");

    public static void DeleteAllLocalData()
    {
        try
        {
            TokenHelper.ClearToken();
            TrustedSessionStore.Clear();
            SessionContext.Clear();
        }
        catch { }

        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, recursive: true);
        }
        catch
        {
            TryDeleteDirectoryContents(RootDirectory);
        }
    }

    private static void TryDeleteDirectoryContents(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }

            foreach (var dir in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }

            try { Directory.Delete(directory, recursive: true); } catch { }
        }
        catch { }
    }
}
