using System;
using NSec.Cryptography;

namespace Vanished.API.Helpers
{
    public static class CryptoHelper
    {
        public static (string publicKeyBase64, Key privateKey) GenerateX25519KeyPair()
        {
            var algorithm = KeyAgreementAlgorithm.X25519;
            var key = Key.Create(algorithm, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });

            byte[] publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            return (Convert.ToBase64String(publicKeyBytes), key);
        }
    }
}
