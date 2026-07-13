namespace Vanished.API.Models
{
    public class Message
    {
        public int PeerId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string LastMessage { get; set; } = "Disponível";
    }
}
