using System;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;

namespace Vanished.API.Helpers
{
    public static class MessageCrypto
    {
        private const string Info = "vanished_dm_v1";
        private const int X25519PubLen = 32;
        private const int NonceLen = 12;
        private const int TagLen = 16;

        public sealed class EncryptedPayload
        {
            public string EphPubB64 { get; set; } = string.Empty;
            public string NonceB64 { get; set; } = string.Empty;
            public string CiphertextB64 { get; set; } = string.Empty;
        }

        public static EncryptedPayload Encrypt(string plaintext, string recipientPublicKeyB64)
        {
            if (plaintext == null)
                plaintext = string.Empty;

            byte[] recipientPubBytes = DecodePublicKey(recipientPublicKeyB64);
            if (recipientPubBytes.Length != X25519PubLen)
                throw new CryptographicException("Chave pública do contacto inválida.");

            var recipientPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, recipientPubBytes, KeyBlobFormat.RawPublicKey);

            using var ephKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });

            byte[] ephPubBytes = ephKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            byte[] sharedBytes = AgreeAndExportRawSharedSecret(ephKey, recipientPub);

            byte[] aeadKey = HkdfSha256(sharedBytes, null, Encoding.UTF8.GetBytes(Info), 32);

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceLen);
            byte[] pt = Encoding.UTF8.GetBytes(plaintext);

            byte[] combined = AesGcmEncryptManaged(aeadKey, nonce, pt);

            CryptographicOperations.ZeroMemory(sharedBytes);
            CryptographicOperations.ZeroMemory(aeadKey);

            return new EncryptedPayload
            {
                EphPubB64 = Convert.ToBase64String(ephPubBytes),
                NonceB64 = Convert.ToBase64String(nonce),
                CiphertextB64 = Convert.ToBase64String(combined)
            };
        }

        public static string Decrypt(NSec.Cryptography.Key myPrivateKey, string senderEphPubB64, string nonceB64, string ciphertextB64)
        {
            byte[] ephPubBytes = DecodeB64(senderEphPubB64);
            byte[] nonce = DecodeB64(nonceB64);
            byte[] combined = DecodeB64(ciphertextB64);

            if (nonce.Length != NonceLen)
                throw new CryptographicException("Nonce inválido.");
            if (ephPubBytes.Length != X25519PubLen)
                throw new CryptographicException("Ephemeral pub inválida.");
            if (combined.Length < TagLen)
                throw new CryptographicException("Ciphertext inválido.");

            var ephPub = PublicKey.Import(KeyAgreementAlgorithm.X25519, ephPubBytes, KeyBlobFormat.RawPublicKey);

            byte[] sharedBytes = AgreeAndExportRawSharedSecret(myPrivateKey, ephPub);
            byte[] aeadKey = HkdfSha256(sharedBytes, null, Encoding.UTF8.GetBytes(Info), 32);


            byte[] pt = AesGcmDecryptManaged(aeadKey, nonce, combined);

            CryptographicOperations.ZeroMemory(sharedBytes);
            CryptographicOperations.ZeroMemory(aeadKey);

            return Encoding.UTF8.GetString(pt);
        }

        private static byte[] AgreeAndExportRawSharedSecret(NSec.Cryptography.Key privateKey, PublicKey publicKey)
        {
            var creationParameters = new SharedSecretCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            };

            using var shared = KeyAgreementAlgorithm.X25519.Agree(privateKey, publicKey, in creationParameters);

            if (shared == null)
                throw new CryptographicException("Falha ao derivar segredo partilhado X25519.");

            return shared.Export(SharedSecretBlobFormat.RawSharedSecret);
        }

        private static byte[] AesGcmEncryptManaged(byte[] key32, byte[] nonce12, byte[] plaintext)
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            var parms = new AeadParameters(new KeyParameter(key32), TagLen * 8, nonce12, null);
            cipher.Init(true, parms);

            byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
            int len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
            len += cipher.DoFinal(output, len);
            if (len != output.Length)
                Array.Resize(ref output, len);
            return output;
        }

        private static byte[] AesGcmDecryptManaged(byte[] key32, byte[] nonce12, byte[] ciphertextWithTag)
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            var parms = new AeadParameters(new KeyParameter(key32), TagLen * 8, nonce12, null);
            cipher.Init(false, parms);

            byte[] output = new byte[cipher.GetOutputSize(ciphertextWithTag.Length)];
            int len = cipher.ProcessBytes(ciphertextWithTag, 0, ciphertextWithTag.Length, output, 0);
            len += cipher.DoFinal(output, len);
            if (len != output.Length)
                Array.Resize(ref output, len);
            return output;
        }

        private static byte[] DecodePublicKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<byte>();

            var s = input.Trim();

            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                s = s.Substring(1, s.Length - 2);

            int idx = s.LastIndexOf(':');
            if (idx >= 0 && idx < s.Length - 1)
                s = s.Substring(idx + 1);

            s = new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            s = s.Replace('-', '+').Replace('_', '/');

            int mod = s.Length % 4;
            if (mod == 2) s += "==";
            else if (mod == 3) s += "=";

            try
            {
                return Convert.FromBase64String(s);
            }
            catch
            {
                try
                {
                    s = s.Replace(" ", "").Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                    return Convert.FromHexString(s);
                }
                catch
                {
                    throw new FormatException("Chave pública do contacto inválida.");
                }
            }
        }

        private static byte[] DecodeB64(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<byte>();

            var s = input.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                s = s.Substring(1, s.Length - 2);
            s = new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            s = s.Replace('-', '+').Replace('_', '/');
            int mod = s.Length % 4;
            if (mod == 2) s += "==";
            else if (mod == 3) s += "=";
            return Convert.FromBase64String(s);
        }

        private static byte[] HkdfSha256(byte[] ikm, byte[]? salt, byte[]? info, int length)
        {
            salt ??= new byte[32];
            info ??= Array.Empty<byte>();

            byte[] prk;
            using (var hmac = new HMACSHA256(salt))
                prk = hmac.ComputeHash(ikm);

            byte[] okm = new byte[length];
            byte[] t = Array.Empty<byte>();
            int offset = 0;
            byte counter = 1;

            using var hmac2 = new HMACSHA256(prk);
            while (offset < length)
            {
                byte[] input = new byte[t.Length + info.Length + 1];
                Buffer.BlockCopy(t, 0, input, 0, t.Length);
                Buffer.BlockCopy(info, 0, input, t.Length, info.Length);
                input[input.Length - 1] = counter;

                t = hmac2.ComputeHash(input);
                int toCopy = Math.Min(t.Length, length - offset);
                Buffer.BlockCopy(t, 0, okm, offset, toCopy);
                offset += toCopy;
                counter++;
            }

            CryptographicOperations.ZeroMemory(prk);
            return okm;
        }
    }
}
