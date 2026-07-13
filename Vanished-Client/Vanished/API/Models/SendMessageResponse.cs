namespace Vanished.API.Models
{
    public class SendMessageResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public long server_message_id { get; set; }
        public string created_at { get; set; } = string.Empty;
        public bool deduped { get; set; }
        public string thread_status { get; set; } = string.Empty;
    }
}
