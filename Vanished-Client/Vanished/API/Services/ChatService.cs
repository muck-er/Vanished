using System;
using System.Threading.Tasks;
using Vanished.API.Models;

namespace Vanished.API.Services
{
    public class ChatService : BaseService
    {
        public async Task<MeResponse?> GetMeAsync()
            => await GetAsync<MeResponse>("api/chat/me");

        public async Task<SearchUsersResponse?> SearchUsersAsync(string query)
            => await GetAsync<SearchUsersResponse>($"api/chat/users/search?q={Uri.EscapeDataString(query ?? string.Empty)}");

        public async Task<SearchUsersResponse?> GetBlockedUsersAsync()
            => await GetAsync<SearchUsersResponse>("api/chat/blocked-users");

        public async Task<ConversationsResponse?> GetConversationsAsync()
            => await GetAsync<ConversationsResponse>("api/chat/conversations");

        public async Task<ConversationsResponse?> GetMessageRequestsAsync()
            => await GetAsync<ConversationsResponse>("api/chat/message-requests");

        public async Task<ConversationsResponse?> GetSentMessageRequestsAsync()
            => await GetAsync<ConversationsResponse>("api/chat/message-requests/sent");

        public async Task<ApiResponse?> AcceptMessageRequestAsync(int peerId)
            => await PostAsync<object, ApiResponse>($"api/chat/message-requests/{peerId}/accept", new { });

        public async Task<ApiResponse?> RejectMessageRequestAsync(int peerId)
            => await PostAsync<object, ApiResponse>($"api/chat/message-requests/{peerId}/reject", new { });

        public async Task<ApiResponse?> CancelMessageRequestAsync(int peerId)
            => await PostAsync<object, ApiResponse>($"api/chat/message-requests/{peerId}/cancel", new { });

        public async Task<ApiResponse?> CreateMessageRequestAsync(int peerId)
            => await PostAsync<object, ApiResponse>($"api/chat/message-requests/{peerId}/create", new { });

        public async Task<UserDevicesResponse?> GetUserDevicesAsync(int userId)
            => await GetAsync<UserDevicesResponse>($"api/chat/users/{userId}/devices");

        public async Task<GetUserResponse?> UpdateProfileAsync(string usernameIgnored, string fullName, string bio, string avatarBase64, string avatarMime)
            => await PostAsync<object, GetUserResponse>("api/chat/profile", new
            {
                full_name = fullName,
                bio,
                avatar_base64 = avatarBase64,
                avatar_mime = avatarMime
            });


        public async Task<(bool ok, PublicUser? user, string? error)> GetUserAsync(int userId)
        {
            var typed = await GetAsync<GetUserResponse>($"api/chat/users/{userId}");
            if (typed != null && typed.success)
                return (true, typed.user, null);
            return (false, null, typed?.message ?? "Utilizador não encontrado.");
        }

        public class GetUserResponse
        {
            public bool success { get; set; }
            public string message { get; set; } = string.Empty;
            public PublicUser user { get; set; } = new();
        }
    }
}
