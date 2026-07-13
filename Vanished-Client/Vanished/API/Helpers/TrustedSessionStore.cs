using Newtonsoft.Json;
using System;
using System.IO;

namespace Vanished.API.Helpers;

public sealed class TrustedSessionInfo
{
    public string Email { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class TrustedSessionStore
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished");

    private static string FilePath => Path.Combine(DirectoryPath, "trusted-session.json");

    public static TrustedSessionInfo? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            var info = JsonConvert.DeserializeObject<TrustedSessionInfo>(json);
            if (info == null || string.IsNullOrWhiteSpace(info.Email)) return null;
            info.Email = info.Email.Trim().ToLowerInvariant();
            return info;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string email, string deviceId)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var info = new TrustedSessionInfo
            {
                Email = (email ?? string.Empty).Trim().ToLowerInvariant(),
                DeviceId = deviceId ?? string.Empty,
                SavedAtUtc = DateTime.UtcNow
            };
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(info, Formatting.Indented));
        }
        catch { }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch { }
    }
}
