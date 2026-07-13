using System.Threading.Tasks;
using Vanished.API.Models;

namespace Vanished.API.Services;

public sealed class ContactSecurityService : BaseService
{
    public async Task<ContactIdentityResponse?> GetIdentityFingerprintAsync(int userId)
        => await GetAsync<ContactIdentityResponse>($"api/chat/users/{userId}/identity-fingerprint");

    public async Task<ApiResponse?> VerifyIdentityAsync(int userId)
        => await PostAsync<object, ApiResponse>($"api/chat/users/{userId}/verify-identity", new { });

    public async Task<ContactIdentityVerificationResponse?> GetVerificationStatusAsync(int userId)
        => await GetAsync<ContactIdentityVerificationResponse>($"api/chat/users/{userId}/identity-verification");
}
