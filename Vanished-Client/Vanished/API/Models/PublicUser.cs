using Avalonia.Media.Imaging;
using Newtonsoft.Json;
using Vanished.API.Helpers;

namespace Vanished.API.Models;

public class PublicUser
{
    public int id { get; set; }
    public string username { get; set; } = string.Empty;
    public string full_name { get; set; } = string.Empty;
    public string public_key { get; set; } = string.Empty;
    public int key_version { get; set; }
    public string avatar_base64 { get; set; } = string.Empty;
    public string avatar_mime { get; set; } = string.Empty;
    public bool is_online { get; set; }
    public string last_seen_at { get; set; } = string.Empty;
    public string status_text { get; set; } = string.Empty;
    public string bio { get; set; } = string.Empty;
    public string created_at { get; set; } = string.Empty;
    public bool is_blocked { get; set; }
    public string message_status { get; set; } = string.Empty;
    public string request_direction { get; set; } = string.Empty;

    [JsonIgnore]
    public Bitmap? AvatarImageSource => AvatarHelper.ToBitmap(avatar_base64);

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(full_name) ? full_name : username;
}
