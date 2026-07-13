using System;
using System.Collections.Generic;

namespace Vanished.API.Models
{
    public class ConversationsResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public List<ConversationSummary> conversations { get; set; } = new();
    }

    public class ConversationSummary
    {
        public long thread_id { get; set; }
        public string status { get; set; } = string.Empty;
        public PublicUser peer { get; set; } = new();
        public EncryptedMessageEnvelope? last { get; set; }
        public int unread_count { get; set; }
    }

    public class EncryptedMessageEnvelope
    {
        public long id { get; set; }
        public int sender_id { get; set; }
        public int recipient_id { get; set; }
        public string eph_pub_b64 { get; set; } = string.Empty;
        public string nonce_b64 { get; set; } = string.Empty;
        public string ciphertext_b64 { get; set; } = string.Empty;
        public string client_msg_id { get; set; } = string.Empty;
        public string created_at { get; set; } = string.Empty;
        public bool? is_delivered { get; set; }
        public bool? is_read { get; set; }
        public string delivery_state { get; set; } = string.Empty;
        public bool? is_deleted_for_all { get; set; }
        public bool? is_deleted_for_me { get; set; }
        public string sender_ciphertext_b64 { get; set; } = string.Empty;
    }
}
