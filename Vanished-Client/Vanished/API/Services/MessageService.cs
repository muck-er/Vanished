using System;
using System.Threading;
using System.Threading.Tasks;
using Vanished.API.Models;

namespace Vanished.API.Services
{
    public class MessageService : BaseService
    {
        private const int DefaultConversationPageSize = 500;
        private const int MaxConversationPageSize = 500;
        public async Task<SendMessageResponse?> SendAsync(
            int recipientId,
            string ciphertextB64,
            string nonceB64,
            string ephPubB64,
            string clientMsgId,
            string? senderCiphertextB64 = null,
            string? senderNonceB64 = null,
            string? senderEphPubB64 = null,
            string? recipientDeviceId = null)
        {
            return await PostAsync<object, SendMessageResponse>(
                "api/messages/send",
                new
                {
                    recipient_id = recipientId,
                    ciphertext_b64 = ciphertextB64,
                    nonce_b64 = nonceB64,
                    eph_pub_b64 = ephPubB64,
                    client_msg_id = clientMsgId,
                    sender_ciphertext_b64 = senderCiphertextB64,
                    sender_nonce_b64 = senderNonceB64,
                    sender_eph_pub_b64 = senderEphPubB64,
                    recipient_device_id = recipientDeviceId
                }
            );
        }

        public async Task<PullMessagesResponse?> PullAsync(long sinceId, int limit = 100, CancellationToken ct = default)
        {
            var endpoint = sinceId > 0
                ? $"api/messages/pull?since_id={sinceId}&limit={limit}"
                : $"api/messages/pull?limit={limit}";
            return await GetAsync<PullMessagesResponse>(endpoint, ct);
        }

        public async Task<PullMessagesResponse?> PullWaitAsync(long sinceId, int limit = 200, int waitSeconds = 25, CancellationToken ct = default)
        {
            var endpoint = sinceId > 0
                ? $"api/messages/pull?since_id={sinceId}&limit={limit}&wait={waitSeconds}"
                : $"api/messages/pull?limit={limit}&wait={waitSeconds}";
            return await GetAsync<PullMessagesResponse>(endpoint, ct);
        }

        public async Task<PullMessagesResponse?> GetConversationSnapshotAsync(
            int peerId,
            int limit = DefaultConversationPageSize,
            long beforeId = 0,
            CancellationToken ct = default)
        {
            var safeLimit = Math.Clamp(limit, 1, MaxConversationPageSize);
            var endpoint = $"api/messages/conversation?peer_id={peerId}&limit={safeLimit}";
            if (beforeId > 0)
                endpoint += $"&before_id={beforeId}";

            return await GetAsync<PullMessagesResponse>(endpoint, ct);
        }

        public async Task<ApiResponse?> MarkReadAsync(int peerId, long upToId)
        {
            var result = await PostAsync<object, ApiResponse>(
                "api/messages/mark-read",
                new { peer_id = peerId, up_to_id = upToId }
            );
            if (result?.success == true && Vanished.ApiService.WebSocket.IsConnected)
                await Vanished.ApiService.WebSocket.SendAsync(new { type = "message.read", peer_id = peerId, sender_id = peerId, up_to_id = upToId });
            return result;
        }

        public async Task<ApiResponse?> SetTypingAsync(int peerId, bool isTyping)
        {
            return await PostAsync<object, ApiResponse>(
                "api/messages/typing",
                new { peer_id = peerId, is_typing = isTyping }
            );
        }

        public async Task<TypingStatusResponse?> GetTypingStatusAsync(int peerId)
            => await GetAsync<TypingStatusResponse>($"api/messages/typing-status?peer_id={peerId}");

        public async Task<ApiResponse?> DeleteMessageAsync(long messageId, string scope = "me")
        {
            return await PostAsync<object, ApiResponse>(
                "api/messages/delete-message",
                new { message_id = messageId, scope = string.IsNullOrWhiteSpace(scope) ? "me" : scope }
            );
        }

        public async Task<ApiResponse?> DeleteConversationAsync(int peerId)
        {
            return await PostAnyAsync(
                new[]
                {
                    "api/messages/delete-chat",
                    "api/messages/delete_chat",
                    "api/chat/delete-chat"
                },
                new { peer_id = peerId }
            );
        }

        public async Task<ApiResponse?> BlockUserAsync(int peerId)
        {
            return await PostAnyAsync(
                new[]
                {
                    "api/messages/block-user",
                    "api/messages/block_user",
                    "api/chat/block-user"
                },
                new { peer_id = peerId }
            );
        }

        public async Task<ApiResponse?> UnblockUserAsync(int peerId)
        {
            return await PostAnyAsync(
                new[]
                {
                    "api/messages/unblock-user",
                    "api/messages/unblock_user",
                    "api/chat/unblock-user"
                },
                new { peer_id = peerId }
            );
        }
    }
}
