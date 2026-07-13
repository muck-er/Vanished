using NSec.Cryptography;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Key = NSec.Cryptography.Key;

namespace Vanished.API.Helpers;

public static class KeyManager
{
    private const byte CurrentVersion = 4;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DerivedKeySize = 32;
    private const int DefaultIterations = Argon2Kdf.DefaultIterations;
    private const int DefaultMemorySizeKb = Argon2Kdf.DefaultMemorySizeKb;
    private const int DefaultParallelism = Argon2Kdf.DefaultParallelism;
    private const string KdfName = Argon2Kdf.Name;
    private const string KdfPurpose = "vanished-local-identity-key-v4";

    private static readonly string BaseFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished",
        "Keys");

    private static string GetKeyPath(string userIdentifier)
    {
        if (string.IsNullOrWhiteSpace(userIdentifier))
            throw new ArgumentException("O identificador do utilizador não pode ser vazio.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("AppVanished_Secret_Suffix_2024"));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(userIdentifier.Trim().ToLowerInvariant()));
        return Path.Combine(BaseFolderPath, Convert.ToHexString(hash).ToLowerInvariant() + ".vkey");
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int memorySizeKb, int parallelism)
        => Argon2Kdf.DeriveKey(password, salt, DerivedKeySize, iterations, memorySizeKb, parallelism, KdfPurpose);

    public static void SavePrivateKey(Key key, string userIdentifier, string password)
    {
        Directory.CreateDirectory(BaseFolderPath);
        string path = GetKeyPath(userIdentifier);

        byte[]? derivedKey = null;
        byte[]? privateKeyBytes = null;

        try
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            derivedKey = DeriveKey(password, salt, DefaultIterations, DefaultMemorySizeKb, DefaultParallelism);
            privateKeyBytes = key.Export(KeyBlobFormat.RawPrivateKey);
            var ciphertext = new byte[privateKeyBytes.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(derivedKey, TagSize))
                aes.Encrypt(nonce, privateKeyBytes, ciphertext, tag);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(CurrentVersion);
            bw.Write(KdfName);
            bw.Write(DefaultIterations);
            bw.Write(DefaultMemorySizeKb);
            bw.Write(DefaultParallelism);
            bw.Write(salt.Length);
            bw.Write(salt);
            bw.Write(nonce.Length);
            bw.Write(nonce);
            bw.Write(tag.Length);
            bw.Write(tag);
            bw.Write(ciphertext.Length);
            bw.Write(ciphertext);

            File.WriteAllBytes(path, ms.ToArray());
            TryRestrictUnixPermissions(path);
        }
        finally
        {
            if (derivedKey != null) CryptographicOperations.ZeroMemory(derivedKey);
            if (privateKeyBytes != null) CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    public static Key? LoadPrivateKey(string userIdentifier, string password)
    {
        string path = GetKeyPath(userIdentifier);
        if (!File.Exists(path))
            return null;

        byte[]? derivedKey = null;
        byte[]? privateKeyBytes = null;

        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var br = new BinaryReader(ms);

            byte version = br.ReadByte();
            byte[] salt;
            byte[] nonce;
            byte[] tag;
            byte[] ciphertext;
            if (version == CurrentVersion)
            {
                var kdfName = br.ReadString();
                int iterations = br.ReadInt32();
                int memorySizeKb = br.ReadInt32();
                int parallelism = br.ReadInt32();
                salt = br.ReadBytes(br.ReadInt32());
                nonce = br.ReadBytes(br.ReadInt32());
                tag = br.ReadBytes(br.ReadInt32());
                ciphertext = br.ReadBytes(br.ReadInt32());

                if (!string.Equals(kdfName, KdfName, StringComparison.OrdinalIgnoreCase))
                    return null;

                derivedKey = DeriveKey(password, salt, iterations, memorySizeKb, parallelism);
            }
            else
            {
                return null;
            }
            privateKeyBytes = new byte[ciphertext.Length];

            using (var aes = new AesGcm(derivedKey, tag.Length))
                aes.Decrypt(nonce, ciphertext, tag, privateKeyBytes);

            var importedKey = Key.Import(
                KeyAgreementAlgorithm.X25519,
                privateKeyBytes,
                KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

            return importedKey;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (derivedKey != null) CryptographicOperations.ZeroMemory(derivedKey);
            if (privateKeyBytes != null) CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    public static bool HasKey(string userIdentifier)
        => File.Exists(GetKeyPath(userIdentifier));

    public static bool RotatePassword(string userIdentifier, string oldPassword, string newPassword)
    {
        using var key = LoadPrivateKey(userIdentifier, oldPassword);
        if (key == null)
            return false;

        SavePrivateKey(key, userIdentifier, newPassword);
        return true;
    }

    private static void TryRestrictUnixPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}
