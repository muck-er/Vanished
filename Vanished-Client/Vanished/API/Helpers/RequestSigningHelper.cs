using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Vanished.API.Helpers;

public static class RequestSigningHelper
{
    public static void ApplySignature(HttpRequestMessage request, string body)
    {
        if (SessionContext.DevicePrivateKey == null || string.IsNullOrWhiteSpace(SessionContext.DeviceId))
            return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var path = request.RequestUri switch
        {
            null => "/",
            { IsAbsoluteUri: true } absoluteUri => absoluteUri.PathAndQuery,
            var relativeUri => relativeUri.OriginalString
        };
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path;
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty))).ToLowerInvariant();

        var canonical = string.Join("\n",
            "v2",
            request.Method.Method.ToUpperInvariant(),
            path,
            bodyHash,
            timestamp,
            nonce,
            SessionContext.DeviceId);

        var signature = SignatureAlgorithm.Ed25519.Sign(SessionContext.DevicePrivateKey, Encoding.UTF8.GetBytes(canonical));

        request.Headers.Remove("X-Vanished-Device-Id");
        request.Headers.Remove("X-Vanished-Timestamp");
        request.Headers.Remove("X-Vanished-Nonce");
        request.Headers.Remove("X-Vanished-Body-SHA256");
        request.Headers.Remove("X-Vanished-Signature");

        request.Headers.Add("X-Vanished-Device-Id", SessionContext.DeviceId);
        request.Headers.Add("X-Vanished-Timestamp", timestamp);
        request.Headers.Add("X-Vanished-Nonce", nonce);
        request.Headers.Add("X-Vanished-Body-SHA256", bodyHash);
        request.Headers.Add("X-Vanished-Signature", Convert.ToBase64String(signature));
    }
}
