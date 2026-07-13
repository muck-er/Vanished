using Newtonsoft.Json;
using NSec.Cryptography;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public sealed class EncryptedExportPayload
{
    public string CiphertextB64 { get; init; } = string.Empty;
    public string NonceB64 { get; init; } = string.Empty;
    public object Manifest { get; init; } = new { };
}

public static class EncryptedExportManager
{
    private const int Iterations = Argon2Kdf.DefaultIterations;
    private const int MemorySizeKb = Argon2Kdf.DefaultMemorySizeKb;
    private const int Parallelism = Argon2Kdf.DefaultParallelism;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public static EncryptedExportPayload Create(string email, string localPassword, string exportPassword)
    {
        var identity = KeyManager.LoadPrivateKey(email, localPassword)
            ?? throw new InvalidOperationException("Password local incorreta para abrir a identity key.");
        var device = DeviceKeyManager.Load(email, localPassword)
            ?? throw new InvalidOperationException("Password local incorreta para abrir as device keys.");

        var identityPrivate = identity.Export(KeyBlobFormat.RawPrivateKey);
        var deviceSigningPrivate = device.SigningPrivateKey.Export(KeyBlobFormat.RawPrivateKey);
        var deviceEncryptionPrivate = device.EncryptionPrivateKey.Export(KeyBlobFormat.RawPrivateKey);

        try
        {
            var plaintextObject = new
            {
                version = 1,
                created_at = DateTimeOffset.UtcNow.ToString("O"),
                email = email.Trim().ToLowerInvariant(),
                identity_private_key_b64 = Convert.ToBase64String(identityPrivate),
                identity_public_key_b64 = Convert.ToBase64String(identity.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
                device_id = device.DeviceId,
                device_signing_private_key_b64 = Convert.ToBase64String(deviceSigningPrivate),
                device_signing_public_key_b64 = device.SigningPublicKeyBase64,
                device_encryption_private_key_b64 = Convert.ToBase64String(deviceEncryptionPrivate),
                device_encryption_public_key_b64 = device.EncryptionPublicKeyBase64
            };

            var plaintext = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(plaintextObject));
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = Derive(exportPassword, salt);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plaintext, cipher, tag);
                var combined = new byte[cipher.Length + tag.Length];
                Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length);
                Buffer.BlockCopy(tag, 0, combined, cipher.Length, tag.Length);

                return new EncryptedExportPayload
                {
                    CiphertextB64 = Convert.ToBase64String(combined),
                    NonceB64 = Convert.ToBase64String(nonce),
                    Manifest = new
                    {
                        version = 2,
                        algorithm = "AES-256-GCM",
                        kdf = "Argon2id",
                        iterations = Iterations,
                        memory_size_kb = MemorySizeKb,
                        parallelism = Parallelism,
                        salt_b64 = Convert.ToBase64String(salt),
                        contains = new[] { "identity_private_key", "device_signing_private_key", "device_encryption_private_key" },
                        warning = "Tudo neste export está cifrado no cliente antes de chegar ao servidor."
                    }
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identityPrivate);
            CryptographicOperations.ZeroMemory(deviceSigningPrivate);
            CryptographicOperations.ZeroMemory(deviceEncryptionPrivate);
        }
    }

    private static byte[] Derive(string password, byte[] salt)
        => Argon2Kdf.DeriveKey(password, salt, KeySize, Iterations, MemorySizeKb, Parallelism, "vanished-client-export-v2");
}
