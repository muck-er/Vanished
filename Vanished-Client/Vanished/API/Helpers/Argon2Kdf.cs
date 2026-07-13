using Konscious.Security.Cryptography;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public static class Argon2Kdf
{
    public const string Name = "argon2id";
    public const int DefaultIterations = 3;
    public const int DefaultMemorySizeKb = 65_536;
    public const int DefaultParallelism = 2;
    public const int DefaultKeySize = 32;

    public static byte[] DeriveKey(
        string password,
        byte[] salt,
        int keySize = DefaultKeySize,
        int iterations = DefaultIterations,
        int memorySizeKb = DefaultMemorySizeKb,
        int parallelism = DefaultParallelism,
        string purpose = "vanished-generic-kdf-v1")
    {
        if (salt == null || salt.Length < 16)
            throw new ArgumentException("Salt Argon2id inválido.", nameof(salt));

        byte[]? material = null;
        try
        {
            material = Encoding.UTF8.GetBytes($"{purpose}:{password ?? string.Empty}");
            var argon2 = new Argon2id(material)
            {
                Salt = salt,
                DegreeOfParallelism = Math.Clamp(parallelism, 1, 8),
                Iterations = Math.Clamp(iterations, 1, 10),
                MemorySize = Math.Clamp(memorySizeKb, 19_456, 262_144)
            };

            return argon2.GetBytes(keySize <= 0 ? DefaultKeySize : keySize);
        }
        finally
        {
            if (material != null)
                CryptographicOperations.ZeroMemory(material);
        }
    }
}
