using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public static class LocalTotpManager
{
    private const byte CurrentVersion = 2;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = Argon2Kdf.DefaultIterations;
    private const int MemorySizeKb = Argon2Kdf.DefaultMemorySizeKb;
    private const int Parallelism = Argon2Kdf.DefaultParallelism;
    private const string KdfName = Argon2Kdf.Name;
    private const string KdfPurpose = "vanished-local-totp-secret-v2";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static readonly string BaseFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished",
        "MFA");

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string GetPath(string email)
    {
        Directory.CreateDirectory(BaseFolderPath);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("Vanished_Local_TOTP_Path_v1"));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(Normalize(email)));
        return Path.Combine(BaseFolderPath, Convert.ToHexString(hash).ToLowerInvariant() + ".vtotp");
    }

    public static bool IsEnabled(string email) => File.Exists(GetPath(email));

    public static string GenerateSecret()
        => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static string GetCode(string secretBase32, DateTimeOffset? now = null)
    {
        var secret = FromBase32(secretBase32);
        long timestep = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter);
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24) |
                     ((hash[offset + 1] & 0xff) << 16) |
                     ((hash[offset + 2] & 0xff) << 8) |
                     (hash[offset + 3] & 0xff);
        int code = binary % 1_000_000;
        return code.ToString("D6");
    }

    public static bool Verify(string secretBase32, string code, int window = 2)
    {
        code = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (code.Length != 6) return false;
        var now = DateTimeOffset.UtcNow;
        for (int i = -window; i <= window; i++)
        {
            var candidate = GetCode(secretBase32, now.AddSeconds(i * 30));
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    public static void Save(string email, string localPassword, string secretBase32)
    {
        var path = GetPath(email);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Derive(localPassword, salt, Iterations, MemorySizeKb, Parallelism);
        var plaintext = Encoding.UTF8.GetBytes(secretBase32.Trim().ToUpperInvariant());
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, cipher, tag);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(CurrentVersion);
            bw.Write(KdfName);
            bw.Write(Iterations);
            bw.Write(MemorySizeKb);
            bw.Write(Parallelism);
            bw.Write(salt.Length); bw.Write(salt);
            bw.Write(nonce.Length); bw.Write(nonce);
            bw.Write(tag.Length); bw.Write(tag);
            bw.Write(cipher.Length); bw.Write(cipher);
            File.WriteAllBytes(path, ms.ToArray());
            TryRestrictUnixPermissions(path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string? Load(string email, string localPassword)
    {
        var path = GetPath(email);
        if (!File.Exists(path)) return null;
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var br = new BinaryReader(ms);
            var version = br.ReadByte();
            byte[] salt;
            byte[] nonce;
            byte[] tag;
            byte[] cipher;
            if (version == CurrentVersion)
            {
                var kdfName = br.ReadString();
                var iterations = br.ReadInt32();
                var memorySizeKb = br.ReadInt32();
                var parallelism = br.ReadInt32();
                salt = br.ReadBytes(br.ReadInt32());
                nonce = br.ReadBytes(br.ReadInt32());
                tag = br.ReadBytes(br.ReadInt32());
                cipher = br.ReadBytes(br.ReadInt32());

                if (!string.Equals(kdfName, KdfName, StringComparison.OrdinalIgnoreCase))
                    return null;

                key = Derive(localPassword, salt, iterations, memorySizeKb, parallelism);
            }
            else
            {
                return null;
            }
            plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (key != null) CryptographicOperations.ZeroMemory(key);
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static void Disable(string email)
    {
        try
        {
            var path = GetPath(email);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }


    public static string BuildOtpAuthUri(string email, string secretBase32, string issuer = "Vanished App")
    {
        var normalizedEmail = Normalize(email);
        var safeIssuer = string.IsNullOrWhiteSpace(issuer) ? "Vanished App" : issuer.Trim();
        var safeSecret = new string((secretBase32 ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant().TrimEnd('=');
        var label = $"{Uri.EscapeDataString(safeIssuer)}:{Uri.EscapeDataString(normalizedEmail)}";
        return $"otpauth://totp/{label}?secret={safeSecret}&issuer={Uri.EscapeDataString(safeIssuer)}&algorithm=SHA1&digits=6&period=30";
    }

    public static string FormatSecret(string secret)
        => string.Join(" ", Enumerable.Range(0, (secret.Length + 3) / 4).Select(i => secret.Substring(i * 4, Math.Min(4, secret.Length - i * 4))));

    private static byte[] Derive(string password, byte[] salt, int iterations, int memorySizeKb, int parallelism)
        => Argon2Kdf.DeriveKey(password, salt, KeySize, iterations, memorySizeKb, parallelism, KdfPurpose);

    private static string ToBase32(byte[] data)
    {
        if (data == null || data.Length == 0)
            return string.Empty;

        var result = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;

        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length)
                {
                    buffer = (buffer << 8) | (data[next++] & 0xff);
                    bitsLeft += 8;
                }
                else
                {
                    buffer <<= 5 - bitsLeft;
                    bitsLeft = 5;
                }
            }

            int index = (buffer >> (bitsLeft - 5)) & 0x1f;
            bitsLeft -= 5;
            result.Append(Alphabet[index]);

            if (bitsLeft > 0)
                buffer &= (1 << bitsLeft) - 1;
            else
                buffer = 0;
        }

        return result.ToString();
    }

    private static byte[] FromBase32(string input)
    {
        input = new string((input ?? string.Empty)
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '=')
            .ToArray())
            .ToUpperInvariant();

        int buffer = 0;
        int bitsLeft = 0;
        using var ms = new MemoryStream();

        foreach (char c in input)
        {
            int idx = Alphabet.IndexOf(c);
            if (idx < 0)
                throw new FormatException("TOTP secret inválido.");

            buffer = (buffer << 5) | idx;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                ms.WriteByte((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
                buffer = bitsLeft > 0 ? buffer & ((1 << bitsLeft) - 1) : 0;
            }
        }

        return ms.ToArray();
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
