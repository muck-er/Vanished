using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public static class LocalUserProfileStore
{
    private static readonly string BaseFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished",
        "Profiles");

    private static string GetPathForUser(string email)
    {
        Directory.CreateDirectory(BaseFolderPath);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes((email ?? string.Empty).Trim().ToLowerInvariant()));
        return Path.Combine(BaseFolderPath, Convert.ToHexString(hash).ToLowerInvariant() + ".json");
    }

    public static LocalUserProfile Load(string email)
    {
        try
        {
            var path = GetPathForUser(email);
            if (!File.Exists(path))
                return new LocalUserProfile { Email = email ?? string.Empty };

            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<LocalUserProfile>(json) ?? new LocalUserProfile { Email = email ?? string.Empty };
        }
        catch
        {
            return new LocalUserProfile { Email = email ?? string.Empty };
        }
    }

    public static void Save(LocalUserProfile profile)
    {
        try
        {
            var normalizedEmail = (profile?.Email ?? string.Empty).Trim().ToLowerInvariant();
            var path = GetPathForUser(normalizedEmail);
            var sanitized = new LocalUserProfile
            {
                Email = normalizedEmail,
                Username = profile?.Username ?? string.Empty,
                FullName = profile?.FullName ?? string.Empty,
                Bio = profile?.Bio ?? string.Empty,
                AvatarBase64 = profile?.AvatarBase64 ?? string.Empty,
                AvatarMime = profile?.AvatarMime ?? string.Empty,
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(sanitized, Formatting.Indented), Encoding.UTF8);
            TryRestrictUnixPermissions(path);
        }
        catch { }
    }

    private static void TryRestrictUnixPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}

public class LocalUserProfile
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string AvatarBase64 { get; set; } = string.Empty;
    public string AvatarMime { get; set; } = string.Empty;
}
