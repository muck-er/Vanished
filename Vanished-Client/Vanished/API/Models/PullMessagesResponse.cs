using System.Collections.Generic;

namespace Vanished.API.Models
{
    public class PullMessagesResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public List<EncryptedMessageEnvelope> messages { get; set; } = new();
        public string thread_status { get; set; } = string.Empty;
        public long next_before_id { get; set; }
    }
}
