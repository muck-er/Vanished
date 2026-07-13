namespace Vanished.API.Models
{
    public class MeResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public PublicUser user { get; set; } = new();
    }
}
