using NSec.Cryptography;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SigningKey = NSec.Cryptography.Key;
using AgreementKey = NSec.Cryptography.Key;

namespace Vanished.API.Helpers;

public sealed class DeviceKeyMaterial
{
    public string DeviceId { get; init; } = string.Empty;
    public string SigningPublicKeyBase64 { get; init; } = string.Empty;
    public string EncryptionPublicKeyBase64 { get; init; } = string.Empty;
    public SigningKey SigningPrivateKey { get; init; } = null!;
    public AgreementKey EncryptionPrivateKey { get; init; } = null!;

    public string PublicKeyBase64 => SigningPublicKeyBase64;
    public SigningKey PrivateKey => SigningPrivateKey;
}

public static class DeviceKeyManager
{
    private const byte CurrentVersion = 3;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DerivedKeySize = 32;
    private const int DefaultIterations = Argon2Kdf.DefaultIterations;
    private const int DefaultMemorySizeKb = Argon2Kdf.DefaultMemorySizeKb;
    private const int DefaultParallelism = Argon2Kdf.DefaultParallelism;
    private const string KdfName = Argon2Kdf.Name;
    private const string KdfPurpose = "vanished-local-device-keys-v3";

    private static readonly string BaseFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished",
        "Devices");

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string GetDevicePath(string email)
    {
        Directory.CreateDirectory(BaseFolderPath);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("Vanished_DeviceKey_Path_v2"));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(Normalize(email)));
        return Path.Combine(BaseFolderPath, Convert.ToHexString(hash).ToLowerInvariant() + ".vdevice2");
    }

    private static string InstallationIdPath
    {
        get
        {
            Directory.CreateDirectory(BaseFolderPath);
            return Path.Combine(BaseFolderPath, "installation.id");
        }
    }

    private static string? TryReadDeviceId(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path).Trim();
            return IsSafeDeviceId(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSafeDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length < 16 || value.Length > 64) return false;
        return value.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_');
    }

    private static void EnsureInstallationId(string deviceId)
    {
        if (!IsSafeDeviceId(deviceId)) return;

        var path = InstallationIdPath;
        if (TryReadDeviceId(path) != null) return;

        File.WriteAllText(path, deviceId);
        TryRestrictUnixPermissions(path);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int memorySizeKb, int parallelism)
        => Argon2Kdf.DeriveKey(password, salt, DerivedKeySize, iterations, memorySizeKb, parallelism, KdfPurpose);

    public static string GetOrCreateDeviceId(string email)
    {
        var installationPath = InstallationIdPath;
        var installationId = TryReadDeviceId(installationPath);
        if (installationId != null)
            return installationId;
            
        var legacyId = TryReadDeviceId(GetDevicePath(email) + ".id");
        var id = legacyId ?? Guid.NewGuid().ToString("N");

        File.WriteAllText(installationPath, id);
        TryRestrictUnixPermissions(installationPath);
        return id;
    }

    public static bool HasDeviceKey(string email) => File.Exists(GetDevicePath(email));

    public static DeviceKeyMaterial CreateAndSave(string email, string password)
    {
        var signing = SigningKey.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var encryption = AgreementKey.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var material = BuildMaterial(GetOrCreateDeviceId(email), signing, encryption);
        Save(email, password, material.DeviceId, signing, encryption);
        return material;
    }

    public static DeviceKeyMaterial CreateForExistingDeviceId(string email)
    {
        var signing = SigningKey.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var encryption = AgreementKey.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return BuildMaterial(GetOrCreateDeviceId(email), signing, encryption);
    }

    public static DeviceKeyMaterial EnsureDeviceKey(string email, string password)
    {
        var existing = Load(email, password);
        return existing ?? CreateAndSave(email, password);
    }

    private static DeviceKeyMaterial BuildMaterial(string deviceId, SigningKey signing, AgreementKey encryption)
        => new()
        {
            DeviceId = deviceId,
            SigningPublicKeyBase64 = Convert.ToBase64String(signing.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            EncryptionPublicKeyBase64 = Convert.ToBase64String(encryption.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            SigningPrivateKey = signing,
            EncryptionPrivateKey = encryption
        };

    public static void Save(string email, string password, string deviceId, SigningKey signingKey, AgreementKey encryptionKey)
    {
        Directory.CreateDirectory(BaseFolderPath);
        string path = GetDevicePath(email);
        File.WriteAllText(path + ".id", deviceId ?? string.Empty);
        EnsureInstallationId(deviceId ?? string.Empty);

        byte[]? derivedKey = null;
        byte[]? signingPrivateBytes = null;
        byte[]? encryptionPrivateBytes = null;
        byte[]? payload = null;
        try
        {
            signingPrivateBytes = signingKey.Export(KeyBlobFormat.RawPrivateKey);
            encryptionPrivateBytes = encryptionKey.Export(KeyBlobFormat.RawPrivateKey);

            using var payloadMs = new MemoryStream();
            using (var payloadWriter = new BinaryWriter(payloadMs, Encoding.UTF8, leaveOpen: true))
            {
                payloadWriter.Write(deviceId ?? string.Empty);
                payloadWriter.Write(signingPrivateBytes.Length);
                payloadWriter.Write(signingPrivateBytes);
                payloadWriter.Write(encryptionPrivateBytes.Length);
                payloadWriter.Write(encryptionPrivateBytes);
            }
            payload = payloadMs.ToArray();

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            derivedKey = DeriveKey(password, salt, DefaultIterations, DefaultMemorySizeKb, DefaultParallelism);
            var ciphertext = new byte[payload.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(derivedKey, TagSize))
                aes.Encrypt(nonce, payload, ciphertext, tag);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(CurrentVersion);
            bw.Write(KdfName);
            bw.Write(DefaultIterations);
            bw.Write(DefaultMemorySizeKb);
            bw.Write(DefaultParallelism);
            bw.Write(salt.Length); bw.Write(salt);
            bw.Write(nonce.Length); bw.Write(nonce);
            bw.Write(tag.Length); bw.Write(tag);
            bw.Write(ciphertext.Length); bw.Write(ciphertext);
            File.WriteAllBytes(path, ms.ToArray());
            TryRestrictUnixPermissions(path);
            TryRestrictUnixPermissions(path + ".id");
        }
        finally
        {
            if (derivedKey != null) CryptographicOperations.ZeroMemory(derivedKey);
            if (signingPrivateBytes != null) CryptographicOperations.ZeroMemory(signingPrivateBytes);
            if (encryptionPrivateBytes != null) CryptographicOperations.ZeroMemory(encryptionPrivateBytes);
            if (payload != null) CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static DeviceKeyMaterial? Load(string email, string password)
    {
        string path = GetDevicePath(email);
        if (!File.Exists(path)) return null;

        byte[]? derivedKey = null;
        byte[]? payload = null;
        byte[]? signingPrivateBytes = null;
        byte[]? encryptionPrivateBytes = null;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var br = new BinaryReader(ms);
            var version = br.ReadByte();
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
            payload = new byte[ciphertext.Length];
            using (var aes = new AesGcm(derivedKey, tag.Length))
                aes.Decrypt(nonce, ciphertext, tag, payload);

            using var payloadMs = new MemoryStream(payload);
            using var payloadReader = new BinaryReader(payloadMs, Encoding.UTF8);
            var deviceId = payloadReader.ReadString();
            signingPrivateBytes = payloadReader.ReadBytes(payloadReader.ReadInt32());
            encryptionPrivateBytes = payloadReader.ReadBytes(payloadReader.ReadInt32());

            var signing = SigningKey.Import(
                SignatureAlgorithm.Ed25519,
                signingPrivateBytes,
                KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

            var encryption = AgreementKey.Import(
                KeyAgreementAlgorithm.X25519,
                encryptionPrivateBytes,
                KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

            return BuildMaterial(deviceId, signing, encryption);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (derivedKey != null) CryptographicOperations.ZeroMemory(derivedKey);
            if (payload != null) CryptographicOperations.ZeroMemory(payload);
            if (signingPrivateBytes != null) CryptographicOperations.ZeroMemory(signingPrivateBytes);
            if (encryptionPrivateBytes != null) CryptographicOperations.ZeroMemory(encryptionPrivateBytes);
        }
    }

    public static string SignChallenge(SigningKey devicePrivateKey, string challenge)
    {
        var data = Encoding.UTF8.GetBytes(challenge ?? string.Empty);
        var sig = SignatureAlgorithm.Ed25519.Sign(devicePrivateKey, data);
        return Convert.ToBase64String(sig);
    }

    private static void TryRestrictUnixPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(path))
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}
