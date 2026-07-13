using NSec.Cryptography;
using System;
using System.Security.Cryptography;
using System.Text;
using Key = NSec.Cryptography.Key;

namespace Vanished.API.Helpers;

public sealed class RecoveryEnvelope
{
    public string CiphertextB64 { get; init; } = string.Empty;
    public string NonceB64 { get; init; } = string.Empty;
    public RecoveryEnvelopeKdf Kdf { get; init; } = new();
}

public sealed class RecoveryEnvelopeKdf
{
    public string Name { get; init; } = Argon2Kdf.Name;
    public int Iterations { get; init; } = Argon2Kdf.DefaultIterations;
    public string SaltB64 { get; init; } = string.Empty;
    public int KeySize { get; init; } = 32;
    public int MemorySizeKb { get; init; } = Argon2Kdf.DefaultMemorySizeKb;
    public int Parallelism { get; init; } = Argon2Kdf.DefaultParallelism;
    public string Purpose { get; init; } = "vanished-recovery-envelope-v2";
}

public static class RecoveryEnvelopeManager
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DefaultIterations = Argon2Kdf.DefaultIterations;
    private const int DefaultMemorySizeKb = Argon2Kdf.DefaultMemorySizeKb;
    private const int DefaultParallelism = Argon2Kdf.DefaultParallelism;
    private const int DerivedKeySize = 32;

    public static RecoveryEnvelope EncryptIdentityPrivateKey(Key identityPrivateKey, string recoveryKey)
    {
        byte[]? derived = null;
        byte[]? privateKey = null;

        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            derived = Derive(recoveryKey, salt, DefaultIterations, DerivedKeySize, Argon2Kdf.Name, DefaultMemorySizeKb, DefaultParallelism);
            privateKey = identityPrivateKey.Export(KeyBlobFormat.RawPrivateKey);

            var cipherAndTag = new byte[privateKey.Length + TagSize];
            using (var aes = new AesGcm(derived, TagSize))
            {
                aes.Encrypt(
                    nonce,
                    privateKey,
                    cipherAndTag.AsSpan(0, privateKey.Length),
                    cipherAndTag.AsSpan(privateKey.Length, TagSize),
                    Encoding.UTF8.GetBytes("Vanished recovery identity envelope v1"));
            }

            return new RecoveryEnvelope
            {
                CiphertextB64 = Convert.ToBase64String(cipherAndTag),
                NonceB64 = Convert.ToBase64String(nonce),
                Kdf = new RecoveryEnvelopeKdf
                {
                    Name = Argon2Kdf.Name,
                    Iterations = DefaultIterations,
                    SaltB64 = Convert.ToBase64String(salt),
                    KeySize = DerivedKeySize,
                    MemorySizeKb = DefaultMemorySizeKb,
                    Parallelism = DefaultParallelism,
                    Purpose = "vanished-recovery-envelope-v2"
                }
            };
        }
        finally
        {
            if (derived != null) CryptographicOperations.ZeroMemory(derived);
            if (privateKey != null) CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    public static Key DecryptIdentityPrivateKey(RecoveryEnvelope envelope, string recoveryKey)
    {
        byte[]? derived = null;
        byte[]? plain = null;

        try
        {
            byte[] salt = Convert.FromBase64String(envelope.Kdf.SaltB64);
            byte[] nonce = Convert.FromBase64String(envelope.NonceB64);
            byte[] cipherAndTag = Convert.FromBase64String(envelope.CiphertextB64);

            if (cipherAndTag.Length <= TagSize)
                throw new CryptographicException("Envelope inválido.");

            int cipherLen = cipherAndTag.Length - TagSize;
            plain = new byte[cipherLen];
            derived = Derive(recoveryKey, salt, envelope.Kdf.Iterations, envelope.Kdf.KeySize <= 0 ? DerivedKeySize : envelope.Kdf.KeySize, envelope.Kdf.Name, envelope.Kdf.MemorySizeKb, envelope.Kdf.Parallelism);

            using (var aes = new AesGcm(derived, TagSize))
            {
                aes.Decrypt(
                    nonce,
                    cipherAndTag.AsSpan(0, cipherLen),
                    cipherAndTag.AsSpan(cipherLen, TagSize),
                    plain,
                    Encoding.UTF8.GetBytes("Vanished recovery identity envelope v1"));
            }

            return Key.Import(
                KeyAgreementAlgorithm.X25519,
                plain,
                KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        }
        finally
        {
            if (derived != null) CryptographicOperations.ZeroMemory(derived);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Derive(string recoveryKey, byte[] salt, int iterations, int keySize, string? kdfName, int memorySizeKb, int parallelism)
    {
        string normalized = (recoveryKey ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.Equals(kdfName, Argon2Kdf.Name, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("KDF do recovery envelope não aceite.");

        return Argon2Kdf.DeriveKey(
            normalized,
            salt,
            keySize,
            iterations <= 0 ? Argon2Kdf.DefaultIterations : iterations,
            memorySizeKb <= 0 ? Argon2Kdf.DefaultMemorySizeKb : memorySizeKb,
            parallelism <= 0 ? Argon2Kdf.DefaultParallelism : parallelism,
            "vanished-recovery-envelope-v2");
    }
}
