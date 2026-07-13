using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Vanished.API.Helpers;

public static class SafetyNumberHelper
{
    public static string Fingerprint(string publicKeyBase64)
    {
        var raw = Convert.FromBase64String((publicKeyBase64 ?? string.Empty).Trim());
        var digest = SHA256.HashData(raw);
        var hex = Convert.ToHexString(digest);
        return string.Join(":", Enumerable.Range(0, hex.Length / 4).Select(i => hex.Substring(i * 4, 4)));
    }

    public static string ComputeSafetyNumber(int localUserId, string localPublicKeyBase64, int peerUserId, string peerPublicKeyBase64)
    {
        var first = (UserId: localUserId, Key: Convert.FromBase64String((localPublicKeyBase64 ?? string.Empty).Trim()));
        var second = (UserId: peerUserId, Key: Convert.FromBase64String((peerPublicKeyBase64 ?? string.Empty).Trim()));
        var ordered = new[] { first, second }.OrderBy(x => x.UserId).ToArray();

        using var sha = SHA256.Create();
        var prefix = Encoding.UTF8.GetBytes("vanished:safety-number:v1\0");
        var material = prefix
            .Concat(ToBigEndianUInt64(ordered[0].UserId))
            .Concat(ordered[0].Key)
            .Concat(ToBigEndianUInt64(ordered[1].UserId))
            .Concat(ordered[1].Key)
            .ToArray();

        var digest = sha.ComputeHash(material);
        var number = BigIntegerDecimal(digest);
        number = number.Length >= 60 ? number[..60] : number.PadRight(60, '0');
        return string.Join(" ", Enumerable.Range(0, 12).Select(i => number.Substring(i * 5, 5)));
    }

    private static byte[] ToBigEndianUInt64(int value)
    {
        var bytes = BitConverter.GetBytes((ulong)value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static string BigIntegerDecimal(byte[] bigEndian)
    {
        var digits = "0";
        foreach (var b in bigEndian)
        {
            digits = MultiplyDecimalString(digits, 256);
            digits = AddDecimalString(digits, b);
        }
        return digits.TrimStart('0') is { Length: > 0 } value ? value : "0";
    }

    private static string MultiplyDecimalString(string input, int multiplier)
    {
        var carry = 0;
        var sb = new StringBuilder(input.Length + 4);
        for (var i = input.Length - 1; i >= 0; i--)
        {
            var value = ((input[i] - '0') * multiplier) + carry;
            sb.Insert(0, (char)('0' + (value % 10)));
            carry = value / 10;
        }
        while (carry > 0)
        {
            sb.Insert(0, (char)('0' + (carry % 10)));
            carry /= 10;
        }
        return sb.ToString();
    }

    private static string AddDecimalString(string input, int addend)
    {
        var carry = addend;
        var sb = new StringBuilder(input);
        for (var i = sb.Length - 1; i >= 0 && carry > 0; i--)
        {
            var value = (sb[i] - '0') + (carry % 10);
            sb[i] = (char)('0' + (value % 10));
            carry = (carry / 10) + (value / 10);
        }
        while (carry > 0)
        {
            sb.Insert(0, (char)('0' + (carry % 10)));
            carry /= 10;
        }
        return sb.ToString();
    }
}
