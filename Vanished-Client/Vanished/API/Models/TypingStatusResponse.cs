namespace Vanished.API.Models
{
    public class TypingStatusResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public bool is_typing { get; set; }
        public int peer_id { get; set; }
    }
}
