using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace Vanished.API.Helpers;

public static class AvatarHelper
{
    public static Bitmap? ToBitmap(string? avatarBase64)
    {
        if (string.IsNullOrWhiteSpace(avatarBase64))
            return null;

        try
        {
            var comma = avatarBase64.IndexOf(',');
            var raw = comma >= 0 ? avatarBase64[(comma + 1)..] : avatarBase64;
            var bytes = Convert.FromBase64String(raw);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    public static string GuessMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }
}
