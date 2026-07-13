using System;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public static class RecoveryKeyManager
{
    public static string GenerateRecoveryKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var text = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"VRK-{text[..8]}-{text[8..16]}-{text[16..24]}-{text[24..32]}-{text[32..]}";
    }

    public static string HashRecoveryKey(string recoveryKey)
    {
        var normalized = (recoveryKey ?? string.Empty).Trim().ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("vanished:recovery:v1:" + normalized));
        return Convert.ToBase64String(bytes);
    }
}
