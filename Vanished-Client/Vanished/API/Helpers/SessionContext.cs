using System;
using NSec.Cryptography;
using AgreementKey = NSec.Cryptography.Key;
using SigningKey = NSec.Cryptography.Key;

namespace Vanished.API.Helpers
{
    public static class SessionContext
    {
        public static bool IsReady => UserId > 0 && PrivateKey != null && DeviceEncryptionPrivateKey != null;

        public static UserSession Current => UserSession.Current;

        public static int UserId { get; private set; }
        public static int KeyVersion { get; private set; }
        public static string Email { get; private set; } = string.Empty;
        public static string Username { get; private set; } = string.Empty;
        public static string DisplayName { get; private set; } = string.Empty;
        public static string Bio { get; private set; } = string.Empty;
        public static string AvatarBase64 { get; private set; } = string.Empty;
        public static string AvatarMime { get; private set; } = string.Empty;
        public static event Action? ProfileUpdated;
        public static string DeviceId { get; private set; } = string.Empty;
        public static SigningKey? DevicePrivateKey { get; private set; }
        public static AgreementKey? DeviceEncryptionPrivateKey { get; private set; }
        public static string DeviceEncryptionPublicKeyBase64 { get; private set; } = string.Empty;
        public static string ChatStorageScope => $"{Email.Trim().ToLowerInvariant()}::kv{KeyVersion}::device::{DeviceId}";
        public static AgreementKey? PrivateKey { get; private set; }
        public static string PublicKeyBase64 { get; private set; } = string.Empty;
        public static string InMemoryMfaSecret { get; private set; } = string.Empty;

        public static void Set(int userId, string email, string username, AgreementKey privateKey, int keyVersion, string deviceId = "", SigningKey? devicePrivateKey = null, AgreementKey? deviceEncryptionPrivateKey = null)
        {
            UserId = userId;
            Email = email ?? string.Empty;
            Username = username ?? string.Empty;
            DisplayName = username ?? string.Empty;
            Current.SetProfile(UserId, Email, Username, DisplayName, Bio, AvatarBase64, AvatarMime);
            PrivateKey = privateKey;
            PublicKeyBase64 = privateKey == null ? string.Empty : Convert.ToBase64String(privateKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            KeyVersion = keyVersion;
            SetDevice(deviceId, devicePrivateKey, deviceEncryptionPrivateKey);
        }

        public static void UpdateProfile(string username, string displayName, string bio, string avatarBase64, string avatarMime)
        {
            Username = username ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Username : displayName;
            Bio = bio ?? string.Empty;
            AvatarBase64 = avatarBase64 ?? string.Empty;
            AvatarMime = avatarMime ?? string.Empty;
            Current.SetProfile(UserId, Email, Username, DisplayName, Bio, AvatarBase64, AvatarMime);
            ProfileUpdated?.Invoke();
        }

        public static void SetMfaSecret(string? secretBase32)
        {
            InMemoryMfaSecret = secretBase32 ?? string.Empty;
        }

        public static bool VerifyCurrentMfa(string code)
        {
            return !string.IsNullOrWhiteSpace(InMemoryMfaSecret)
                && LocalTotpManager.Verify(InMemoryMfaSecret, code ?? string.Empty);
        }

        public static void SetDevice(string deviceId, SigningKey? devicePrivateKey, AgreementKey? deviceEncryptionPrivateKey)
        {
            DeviceId = deviceId ?? string.Empty;
            DevicePrivateKey = devicePrivateKey;
            DeviceEncryptionPrivateKey = deviceEncryptionPrivateKey;
            DeviceEncryptionPublicKeyBase64 = deviceEncryptionPrivateKey == null
                ? string.Empty
                : Convert.ToBase64String(deviceEncryptionPrivateKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        }

        public static void Clear()
        {
            UserId = 0;
            KeyVersion = 0;
            Email = string.Empty;
            Username = string.Empty;
            DisplayName = string.Empty;
            Bio = string.Empty;
            AvatarBase64 = string.Empty;
            AvatarMime = string.Empty;
            DeviceId = string.Empty;
            PrivateKey = null;
            DevicePrivateKey = null;
            DeviceEncryptionPrivateKey = null;
            DeviceEncryptionPublicKeyBase64 = string.Empty;
            PublicKeyBase64 = string.Empty;
            InMemoryMfaSecret = string.Empty;
            Current.Clear();
            ProfileUpdated?.Invoke();
        }
    }
}
