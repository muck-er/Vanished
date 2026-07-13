using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers
{
    public static class ChatStore
    {
        public static LocalChatState Load(string storageScope) => new LocalChatState();

        public static void Save(string storageScope, LocalChatState state)
        {
            // no-op
        }

        public static void Clear(string storageScope)
        {
            // no-op
        }
    }

    public class LocalChatState
    {
        public long LastInboxId { get; set; } = 0;
        public Dictionary<int, LocalConversation> Conversations { get; set; } = new();
    }

    public class LocalConversation
    {
        public int PeerId { get; set; }
        public string PeerUsername { get; set; } = string.Empty;
        public string PeerFullName { get; set; } = string.Empty;
        public string PeerAvatarBase64 { get; set; } = string.Empty;
        public string PeerAvatarMime { get; set; } = string.Empty;
        public List<LocalMessage> Messages { get; set; } = new();
    }

    public class LocalMessage
    {
        public long ServerId { get; set; }
        public string ClientId { get; set; } = Guid.NewGuid().ToString();
        public bool IsMine { get; set; }
        public int FromId { get; set; }
        public int ToId { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Text { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public bool IsSystem { get; set; }
        public string SystemCode { get; set; } = string.Empty;
        public string DeliveryState { get; set; } = "pending"; // pending | sent | delivered | read | failed
        public bool SeenByPeer { get; set; }
        public bool DeliveredToPeer { get; set; }
        public bool IsExpanded { get; set; }
    }
}
