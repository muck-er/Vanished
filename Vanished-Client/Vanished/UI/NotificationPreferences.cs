using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vanished.UI;

public sealed class NotificationPreferences
{
    public bool EnableNotifications { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool RequestNotifications { get; set; } = true;

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished");

    private static string PrefsPath => Path.Combine(DirectoryPath, "notification_prefs.json");

    public static NotificationPreferences Load()
    {
        try
        {
            if (!File.Exists(PrefsPath))
                return new NotificationPreferences();
            var json = File.ReadAllText(PrefsPath);
            return JsonSerializer.Deserialize<NotificationPreferences>(json) ?? new NotificationPreferences();
        }
        catch
        {
            return new NotificationPreferences();
        }
    }

    public static Task<NotificationPreferences> LoadAsync()
        => Task.FromResult(Load());

    public static async Task SaveAsync(NotificationPreferences prefs)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PrefsPath, json);
    }
}
