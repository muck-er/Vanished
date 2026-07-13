using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Models;
using Vanished.UI;

namespace Vanished.API.Services
{
    public class AuthService : BaseService
    {
        public async Task<LoginBeginResult> BeginLoginAsync(string email, string password)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            password ??= string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return new LoginBeginResult { success = false, message = AuthErrorMapper.GenericCredentials };

            var device = DeviceKeyManager.Load(email, password);
            if (device == null)
                return new LoginBeginResult
                {
                    success = false,
                    message = AuthErrorMapper.GenericCredentials,
                    RequiresRecovery = true
                };

            var identityKey = KeyManager.LoadPrivateKey(email, password);
            if (identityKey == null)
                return new LoginBeginResult
                {
                    success = false,
                    message = AuthErrorMapper.GenericCredentials
                };

            if (!LocalTotpManager.IsEnabled(email))
                return new LoginBeginResult
                {
                    success = false,
                    message = AuthErrorMapper.GenericCredentials
                };

            var mfaSecret = LocalTotpManager.Load(email, password);
            if (string.IsNullOrWhiteSpace(mfaSecret))
                return new LoginBeginResult
                {
                    success = false,
                    message = AuthErrorMapper.GenericCredentials
                };

            var start = await PostAsync<object, LoginStartResponse>("api/user/login/start", new
            {
                Email = email,
                DeviceId = device.DeviceId,
                Client = "Vanished Avalonia"
            }, signRequest: false);

            if (start == null || !start.success)
                return new LoginBeginResult
                {
                    success = false,
                    message = AuthErrorMapper.GenericCredentials,
                    RequiresRecovery = (start?.message ?? string.Empty).Contains("Dispositivo", StringComparison.OrdinalIgnoreCase)
                        || (start?.message ?? string.Empty).Contains("device", StringComparison.OrdinalIgnoreCase)
                };

            return new LoginBeginResult
            {
                success = true,
                message = "Credenciais locais validadas. Introduz o Vanished PIN e o código MFA.",
                requires_mfa = start.requires_mfa,
                requires_pin = start.requires_pin,
                Pending = new PendingLoginSession
                {
                    Email = email,
                    ChallengeId = start.challenge_id,
                    ServerNonce = start.server_nonce,
                    Device = device,
                    IdentityPrivateKey = identityKey,
                    MfaSecret = mfaSecret,
                    RequiresPin = start.requires_pin
                }
            };
        }

        public async Task<ApiResponse> FinishLoginAsync(PendingLoginSession pending, string? totpCode, string? accountPin)
        {
            if (pending == null)
                return new ApiResponse { success = false, message = "Sessão de login inválida. Volta a introduzir as credenciais." };

            if (pending.RequiresPin && string.IsNullOrWhiteSpace(accountPin))
                return new ApiResponse { success = false, message = AuthErrorMapper.GenericIdentity };

            if (!LocalTotpManager.Verify(pending.MfaSecret, totpCode ?? string.Empty))
                return new ApiResponse { success = false, message = AuthErrorMapper.GenericIdentity };

            var proofPayload = $"{pending.ChallengeId}.{pending.ServerNonce}.{pending.Email}.{pending.Device.DeviceId}";
            var signature = DeviceKeyManager.SignChallenge(pending.Device.SigningPrivateKey, proofPayload);

            var result = await PostAsync<object, ApiResponse>("api/user/login/finish", new
            {
                Email = pending.Email,
                DeviceId = pending.Device.DeviceId,
                ChallengeId = pending.ChallengeId,
                ServerNonce = pending.ServerNonce,
                Signature = signature,
                ClientMfaSatisfied = true,
                AccountPin = accountPin ?? string.Empty,
                UseAccountPinUnlock = false
            }, signRequest: false);

            var token = !string.IsNullOrWhiteSpace(result?.access_token)
                ? result.access_token
                : result?.token;

            if (result != null && result.success && !string.IsNullOrWhiteSpace(token))
            {
                TokenHelper.SaveToken(token);
                if (!string.IsNullOrWhiteSpace(result.refresh_token))
                    TokenHelper.SaveRefreshToken(result.refresh_token);

                SessionContext.SetDevice(pending.Device.DeviceId, pending.Device.SigningPrivateKey, pending.Device.EncryptionPrivateKey);
                SessionContext.SetMfaSecret(pending.MfaSecret);
                TrustedSessionStore.Save(pending.Email, pending.Device.DeviceId);
            }

            return result ?? new ApiResponse { success = false, message = "Resposta inválida do servidor." };
        }

        public async Task<ApiResponse> LoginAsync(string email, string password, string? totpCode = null, string? accountPin = null)
        {
            var begin = await BeginLoginAsync(email, password);
            if (begin.success != true || begin.Pending == null)
                return new ApiResponse { success = false, message = begin.message ?? "Login recusado." };

            return await FinishLoginAsync(begin.Pending, totpCode, accountPin);
        }

        public async Task<ApiResponse> RefreshAsync()
        {
            if (string.IsNullOrWhiteSpace(TokenHelper.CurrentRefreshToken))
                return new ApiResponse { success = false, message = "Refresh token local em falta." };

            var result = await PostAsync<object, ApiResponse>("api/user/refresh", new
            {
                RefreshToken = TokenHelper.CurrentRefreshToken
            });

            if (result?.success == true)
            {
                if (!string.IsNullOrWhiteSpace(result.access_token))
                    TokenHelper.SaveToken(result.access_token);
                if (!string.IsNullOrWhiteSpace(result.refresh_token))
                    TokenHelper.SaveRefreshToken(result.refresh_token);
            }
            return result ?? new ApiResponse { success = false, message = "Não foi possível rodar sessão." };
        }

        public async Task<ApiResponse> LogoutAsync()
        {
            if (string.IsNullOrWhiteSpace(TokenHelper.CurrentToken))
                return new ApiResponse { success = true, message = "Sem sessão ativa." };

            ApiResponse result;

            try
            {
                result = await PostAsync("api/user/logout", new { });
            }
            finally
            {
                await Vanished.ApiService.Connection.StopAsync();
                TrustedSessionStore.Clear();
                TokenHelper.ClearToken();
                SessionContext.Clear();
            }

            return result ?? new ApiResponse { success = false, message = "Erro ao efetuar logout." };
        }

        public async Task<ApiResponse> VanishAccountAsync(string accountPin)
        {
            if (string.IsNullOrWhiteSpace(TokenHelper.CurrentToken))
                return new ApiResponse { success = false, message = "Sessão inválida." };
            if (string.IsNullOrWhiteSpace(accountPin))
                return new ApiResponse { success = false, message = AuthErrorMapper.GenericIdentity };

            return await PostAsync("api/user/vanish", new { AccountPin = accountPin });
        }

        private static string NormalizeUsername(string? value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text.StartsWith("@", StringComparison.Ordinal))
                text = text[1..];
            return text;
        }

        public async Task<ApiResponse> StartRegistrationEmailVerificationAsync(string email)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
                return new ApiResponse { success = false, message = "Email inválido." };

            return await PostAsync("api/user/register/email/start", new { email }, signRequest: false);
        }

        public async Task<ApiResponse> VerifyRegistrationEmailAsync(string email, string code)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            code = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(email) || code.Length != 6)
                return new ApiResponse { success = false, message = "Código de validação inválido." };

            return await PostAsync("api/user/register/email/verify", new { email, code }, signRequest: false);
        }

        public async Task<ApiResponse> CheckUsernameAvailabilityAsync(string username)
        {
            username = NormalizeUsername(username);
            if (string.IsNullOrWhiteSpace(username))
                return new ApiResponse { success = false, available = false, message = "Escolhe um @handle." };

            var result = await PostAsync("api/user/register/handle/check", new { username }, signRequest: false);
            if (result != null && !result.success && string.IsNullOrWhiteSpace(result.message))
                result.message = "Não foi possível verificar o @handle. Tenta novamente.";
            return result ?? new ApiResponse { success = false, available = false, message = "Não foi possível verificar o @handle. Tenta novamente." };
        }

        public async Task<(ApiResponse result, string recoveryKey)> RegisterAsync(
            string fullName,
            string username,
            string email,
            string password,
            string accountPin,
            string emailVerificationToken,
            bool localMfaEnabled = true)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            username = NormalizeUsername(username);

            var (identityPublicKeyBase64, identityPrivateKey) = CryptoHelper.GenerateX25519KeyPair();
            var device = DeviceKeyManager.CreateAndSave(email, password);
            var recoveryKey = RecoveryKeyManager.GenerateRecoveryKey();
            var recoveryHash = RecoveryKeyManager.HashRecoveryKey(recoveryKey);
            var recoveryEnvelope = RecoveryEnvelopeManager.EncryptIdentityPrivateKey(identityPrivateKey, recoveryKey);

            var data = new Dictionary<string, object?>
            {
                ["full_name"] = fullName?.Trim(),
                ["username"] = username,
                ["handle"] = username,
                ["email"] = email,
                ["email_verification_token"] = (emailVerificationToken ?? string.Empty).Trim(),
                ["identity_public_key"] = identityPublicKeyBase64,
                ["device_id"] = device.DeviceId,
                ["device_signing_public_key"] = device.SigningPublicKeyBase64,
                ["device_encryption_public_key"] = device.EncryptionPublicKeyBase64,
                ["recovery_key_hash"] = recoveryHash,
                ["account_pin"] = accountPin ?? string.Empty,
                ["local_mfa_enabled"] = localMfaEnabled,
                ["client_platform"] = RuntimeInformation.OSDescription,
                ["recovery_envelope"] = new Dictionary<string, object?>
                {
                    ["ciphertext_b64"] = recoveryEnvelope.CiphertextB64,
                    ["nonce_b64"] = recoveryEnvelope.NonceB64,
                    ["kdf"] = new Dictionary<string, object?>
                    {
                        ["name"] = recoveryEnvelope.Kdf.Name,
                        ["iterations"] = recoveryEnvelope.Kdf.Iterations,
                        ["salt_b64"] = recoveryEnvelope.Kdf.SaltB64,
                        ["key_size"] = recoveryEnvelope.Kdf.KeySize,
                        ["memory_size_kb"] = recoveryEnvelope.Kdf.MemorySizeKb,
                        ["parallelism"] = recoveryEnvelope.Kdf.Parallelism,
                        ["purpose"] = recoveryEnvelope.Kdf.Purpose
                    }
                },
                ["recovery_envelope_ciphertext_b64"] = recoveryEnvelope.CiphertextB64,
                ["recovery_envelope_nonce_b64"] = recoveryEnvelope.NonceB64,
                ["recovery_envelope_kdf_name"] = recoveryEnvelope.Kdf.Name,
                ["recovery_envelope_kdf_iterations"] = recoveryEnvelope.Kdf.Iterations,
                ["recovery_envelope_kdf_salt_b64"] = recoveryEnvelope.Kdf.SaltB64,
                ["recovery_envelope_kdf_key_size"] = recoveryEnvelope.Kdf.KeySize,
                ["recovery_envelope_kdf_memory_size_kb"] = recoveryEnvelope.Kdf.MemorySizeKb,
                ["recovery_envelope_kdf_parallelism"] = recoveryEnvelope.Kdf.Parallelism,
                ["recovery_envelope_kdf_purpose"] = recoveryEnvelope.Kdf.Purpose
            };

            var result = await PostAsync("api/user/register", data, signRequest: false);

            if (result != null && result.success)
            {
                KeyManager.SavePrivateKey(identityPrivateKey, email, password);
                var token = !string.IsNullOrWhiteSpace(result.access_token) ? result.access_token : result.token;
                if (!string.IsNullOrWhiteSpace(token))
                    TokenHelper.SaveToken(token);
                if (!string.IsNullOrWhiteSpace(result.refresh_token))
                    TokenHelper.SaveRefreshToken(result.refresh_token);
                SessionContext.SetDevice(device.DeviceId, device.SigningPrivateKey, device.EncryptionPrivateKey);
            }

            return (result ?? new ApiResponse { success = false, message = "Resposta inválida do servidor." }, recoveryKey);
        }


        public async Task<TrustedUnlockResult> UnlockTrustedDeviceAsync(string email, string password, string accountPin)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            password ??= string.Empty;
            accountPin ??= string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(accountPin))
                return new TrustedUnlockResult { success = false, message = AuthErrorMapper.GenericIdentity };

            var device = DeviceKeyManager.Load(email, password);
            if (device == null)
                return new TrustedUnlockResult { success = false, message = AuthErrorMapper.GenericCredentials };

            var identityKey = KeyManager.LoadPrivateKey(email, password);
            if (identityKey == null)
                return new TrustedUnlockResult { success = false, message = AuthErrorMapper.GenericCredentials };

            var start = await PostAsync<object, LoginStartResponse>("api/user/login/start", new
            {
                Email = email,
                DeviceId = device.DeviceId,
                Client = "Vanished Avalonia trusted unlock"
            }, signRequest: false);

            if (start == null || !start.success)
                return new TrustedUnlockResult { success = false, message = AuthErrorMapper.GenericIdentity };

            var proofPayload = $"{start.challenge_id}.{start.server_nonce}.{email}.{device.DeviceId}";
            var signature = DeviceKeyManager.SignChallenge(device.SigningPrivateKey, proofPayload);

            var result = await PostAsync<object, ApiResponse>("api/user/login/finish", new
            {
                Email = email,
                DeviceId = device.DeviceId,
                ChallengeId = start.challenge_id,
                ServerNonce = start.server_nonce,
                Signature = signature,
                ClientMfaSatisfied = false,
                AccountPin = accountPin,
                UseAccountPinUnlock = true
            }, signRequest: false);

            var token = !string.IsNullOrWhiteSpace(result?.access_token)
                ? result.access_token
                : result?.token;

            if (result != null && result.success && !string.IsNullOrWhiteSpace(token))
            {
                TokenHelper.SaveToken(token);
                if (!string.IsNullOrWhiteSpace(result.refresh_token))
                    TokenHelper.SaveRefreshToken(result.refresh_token);

                var mfaSecret = LocalTotpManager.Load(email, password);
                SessionContext.SetMfaSecret(mfaSecret);

                SessionContext.SetDevice(device.DeviceId, device.SigningPrivateKey, device.EncryptionPrivateKey);
                TrustedSessionStore.Save(email, device.DeviceId);
                return new TrustedUnlockResult
                {
                    success = true,
                    message = result.message,
                    Email = email,
                    Device = device,
                    IdentityPrivateKey = identityKey
                };
            }

            return new TrustedUnlockResult { success = false, message = AuthErrorMapper.GenericIdentity };
        }

        public async Task<ApiResponse> VerifyAccountPinAsync(string accountPin)
            => await PostAsync("api/user/pin/verify", new { AccountPin = accountPin ?? string.Empty });

        public async Task<ApiResponse> ChangeAccountPinAsync(string currentPin, string newPin)
            => await PostAsync("api/user/pin/change", new { CurrentPin = currentPin ?? string.Empty, NewPin = newPin ?? string.Empty });

        public async Task<RecoveryDeviceResult> RecoverDeviceAsync(string email, string recoveryKey, string newLocalPassword)
        {
            email = email?.Trim().ToLowerInvariant() ?? string.Empty;
            string oldRecoveryHash = RecoveryKeyManager.HashRecoveryKey(recoveryKey);
            var device = DeviceKeyManager.CreateAndSave(email, newLocalPassword);

            var response = await PostAsync<object, RecoveryDeviceResponse>("api/user/recovery/replace-device", new
            {
                Email = email,
                RecoveryKeyHash = oldRecoveryHash,
                DeviceId = device.DeviceId,
                DeviceSigningPublicKey = device.SigningPublicKeyBase64,
                DeviceEncryptionPublicKey = device.EncryptionPublicKeyBase64,
                ClientPlatform = RuntimeInformation.OSDescription
            });

            if (response == null || !response.success)
                return new RecoveryDeviceResult { success = false, message = AuthErrorMapper.GenericIdentity };

            try
            {
                var envelope = new RecoveryEnvelope
                {
                    CiphertextB64 = response.recovery_envelope.ciphertext_b64,
                    NonceB64 = response.recovery_envelope.nonce_b64,
                    Kdf = new RecoveryEnvelopeKdf
                    {
                        Name = response.recovery_envelope.kdf.name,
                        Iterations = response.recovery_envelope.kdf.iterations,
                        SaltB64 = response.recovery_envelope.kdf.salt_b64,
                        KeySize = response.recovery_envelope.kdf.key_size,
                        MemorySizeKb = response.recovery_envelope.kdf.memory_size_kb,
                        Parallelism = response.recovery_envelope.kdf.parallelism,
                        Purpose = response.recovery_envelope.kdf.purpose
                    }
                };

                using var identityKey = RecoveryEnvelopeManager.DecryptIdentityPrivateKey(envelope, recoveryKey);
                string publicKeyBase64 = Convert.ToBase64String(identityKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey));
                if (!string.Equals(publicKeyBase64, response.identity_public_key, StringComparison.Ordinal))
                    return new RecoveryDeviceResult { success = false, message = "Recovery envelope não corresponde à identidade do servidor." };

                var newRecoveryKey = RecoveryKeyManager.GenerateRecoveryKey();
                var newRecoveryHash = RecoveryKeyManager.HashRecoveryKey(newRecoveryKey);
                var newEnvelope = RecoveryEnvelopeManager.EncryptIdentityPrivateKey(identityKey, newRecoveryKey);

                var rotateResult = await PostAsync("api/user/recovery/rotate-after-replace", new
                {
                    Email = email,
                    DeviceId = device.DeviceId,
                    OldRecoveryKeyHash = oldRecoveryHash,
                    NewRecoveryKeyHash = newRecoveryHash,
                    RecoveryEnvelope = newEnvelope
                });

                if (rotateResult?.success != true)
                    return new RecoveryDeviceResult
                    {
                        success = false,
                        message = rotateResult?.message ?? "Dispositivo criado, mas não foi possível rodar a recovery key. Tenta novamente."
                    };

                KeyManager.SavePrivateKey(identityKey, email, newLocalPassword);
                return new RecoveryDeviceResult
                {
                    success = true,
                    message = "Dispositivo adicionado. A recovery key antiga foi revogada.",
                    recovery_key = newRecoveryKey
                };
            }
            catch
            {
                return new RecoveryDeviceResult { success = false, message = AuthErrorMapper.GenericIdentity };
            }
        }

        public async Task<AccountKeysSessionsResponse> GetAccountKeysSessionsAsync(string accountPin)
            => await PostAsync<object, AccountKeysSessionsResponse>("api/user/security/keys-sessions", new
            {
                AccountPin = accountPin ?? string.Empty,
                CurrentRefreshToken = TokenHelper.CurrentRefreshToken ?? string.Empty
            })
               ?? new AccountKeysSessionsResponse { success = false, message = "Não foi possível carregar gestão de chaves e sessões." };

        public async Task<ApiResponse?> RevokeSessionAsync(long sessionId)
            => await PostAsync<object, ApiResponse>("api/user/sessions/revoke", new
            {
                SessionId = sessionId,
                CurrentRefreshToken = TokenHelper.CurrentRefreshToken ?? string.Empty
            });


        public Task<ApiResponse> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            static Task<ApiResponse> Completed(ApiResponse response) => Task.FromResult(response);

            string email = TokenHelper.GetEmailFromToken();
            if (string.IsNullOrWhiteSpace(email))
                return Completed(new ApiResponse { success = false, message = "Sessão inválida." });

            bool identityOk = KeyManager.RotatePassword(email, currentPassword, newPassword);
            var device = DeviceKeyManager.Load(email, currentPassword);
            if (device == null || !identityOk)
                return Completed(new ApiResponse { success = false, message = AuthErrorMapper.GenericCredentials });

            DeviceKeyManager.Save(email, newPassword, device.DeviceId, device.SigningPrivateKey, device.EncryptionPrivateKey);

            if (LocalTotpManager.IsEnabled(email))
            {
                var secret = LocalTotpManager.Load(email, currentPassword);
                if (!string.IsNullOrWhiteSpace(secret))
                    LocalTotpManager.Save(email, newPassword, secret);
            }

            ChatStore.Clear(SessionContext.ChatStorageScope);
            return Completed(new ApiResponse { success = true, message = "Password local alterada. Nenhuma password foi enviada ao servidor." });
        }

        public async Task<(ApiResponse result, string recoveryKey)> RotateIdentityAsync(string currentPassword)
        {
            string email = TokenHelper.GetEmailFromToken();
            if (string.IsNullOrEmpty(email))
                return (new ApiResponse { success = false, message = "Sessão inválida ou expirada." }, string.Empty);

            var existingKey = KeyManager.LoadPrivateKey(email, currentPassword);
            if (existingKey == null)
                return (new ApiResponse { success = false, message = AuthErrorMapper.GenericCredentials }, string.Empty);

            var (publicKeyBase64, privateKey) = CryptoHelper.GenerateX25519KeyPair();
            var recoveryKey = RecoveryKeyManager.GenerateRecoveryKey();
            var recoveryHash = RecoveryKeyManager.HashRecoveryKey(recoveryKey);
            var recoveryEnvelope = RecoveryEnvelopeManager.EncryptIdentityPrivateKey(privateKey, recoveryKey);

            var result = await PostAsync("api/user/update-identity", new
            {
                IdentityPublicKey = publicKeyBase64,
                RecoveryKeyHash = recoveryHash,
                RecoveryEnvelope = recoveryEnvelope
            });

            if (result != null && result.success)
            {
                ChatStore.Clear(SessionContext.ChatStorageScope);
                KeyManager.SavePrivateKey(privateKey, email, currentPassword);
                return (result, recoveryKey);
            }

            return (result ?? new ApiResponse { success = false, message = "Falha ao rodar identidade." }, string.Empty);
        }

        public async Task<ApiResponse> RotateDeviceKeysAsync(string localPassword)
        {
            string email = TokenHelper.GetEmailFromToken();
            if (string.IsNullOrWhiteSpace(email))
                return new ApiResponse { success = false, message = "Sessão inválida." };

            var current = DeviceKeyManager.Load(email, localPassword);
            if (current == null)
                return new ApiResponse { success = false, message = AuthErrorMapper.GenericCredentials };

            var next = DeviceKeyManager.CreateForExistingDeviceId(email);
            var result = await PostAsync("api/user/devices/rotate", new
            {
                DeviceId = next.DeviceId,
                DeviceSigningPublicKey = next.SigningPublicKeyBase64,
                DeviceEncryptionPublicKey = next.EncryptionPublicKeyBase64,
                ClientPlatform = RuntimeInformation.OSDescription
            });

            if (result?.success == true)
            {
                DeviceKeyManager.Save(email, localPassword, next.DeviceId, next.SigningPrivateKey, next.EncryptionPrivateKey);
                SessionContext.SetDevice(next.DeviceId, next.SigningPrivateKey, next.EncryptionPrivateKey);
                ChatStore.Clear(SessionContext.ChatStorageScope);
            }

            return result ?? new ApiResponse { success = false, message = "Falha ao rodar device keys." };
        }

        public async Task<ApiResponse> SetLocalMfaEnabledAsync(bool enabled)
            => await PostAsync("api/user/mfa/local-status", new { Enabled = enabled });

        public async Task<ApiResponse> CreateEncryptedExportAsync(string localPassword, string exportPassword)
        {
            string email = TokenHelper.GetEmailFromToken();
            if (string.IsNullOrWhiteSpace(email))
                return new ApiResponse { success = false, message = "Sessão inválida." };

            var export = EncryptedExportManager.Create(email, localPassword, exportPassword);
            return await PostAsync("api/user/export/encrypted", new
            {
                CiphertextB64 = export.CiphertextB64,
                NonceB64 = export.NonceB64,
                Manifest = export.Manifest
            });
        }


        public async Task<BinaryDownloadResult> CreateEncryptedExportFileAsync(string exportPassword, string mfaCode)
            => await PostBinaryAsync("api/export/create", new { password = exportPassword, mfa_code = mfaCode });

        public async Task<ImportExportResponse> ImportEncryptedExportAsync(byte[] fileBytes, string fileName, string password, string mfaCode)
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "vanished_export.vne" : fileName);
            form.Add(new StringContent(password ?? string.Empty, Encoding.UTF8), "password");
            form.Add(new StringContent(mfaCode ?? string.Empty, Encoding.UTF8), "mfa_code");
            return await PostMultipartAsync<ImportExportResponse>("api/export/import", form)
                ?? new ImportExportResponse { success = false, message = "Não foi possível importar a exportação." };
        }

        public async Task<KeyValidationResult> ValidateIdentityKeyAsync(string email, string publicKeyBase64)
        {
            try
            {
                return await PostAsync<object, KeyValidationResult>("api/user/validate-key", new { Email = email, PublicKey = publicKeyBase64 })
                    ?? new KeyValidationResult { IsCurrent = false };
            }
            catch
            {
                return new KeyValidationResult { IsCurrent = false };
            }
        }
    }

    public sealed class LoginBeginResult : ApiResponse
    {
        public bool requires_pin { get; set; }
        public PendingLoginSession? Pending { get; set; }
        public bool RequiresRecovery { get; set; }
    }

    public sealed class PendingLoginSession
    {
        public string Email { get; init; } = string.Empty;
        public string ChallengeId { get; init; } = string.Empty;
        public string ServerNonce { get; init; } = string.Empty;
        public DeviceKeyMaterial Device { get; init; } = null!;
        public NSec.Cryptography.Key IdentityPrivateKey { get; init; } = null!;
        public string MfaSecret { get; init; } = string.Empty;
        public bool RequiresPin { get; init; } = true;
    }

    public sealed class LoginStartResponse : ApiResponse
    {
        public string challenge_id { get; set; } = string.Empty;
        public string server_nonce { get; set; } = string.Empty;
        public bool requires_pin { get; set; }
    }

    public sealed class TrustedUnlockResult : ApiResponse
    {
        public string Email { get; set; } = string.Empty;
        public DeviceKeyMaterial? Device { get; set; }
        public NSec.Cryptography.Key? IdentityPrivateKey { get; set; }
    }

    public sealed class RecoveryDeviceResult : ApiResponse
    {
        public string recovery_key { get; set; } = string.Empty;
    }

    public sealed class AccountKeysSessionsResponse : ApiResponse
    {
        public RecoveryKeyState recovery_key { get; set; } = new();
        public IdentityKeyState identity { get; set; } = new();
        public List<MyDeviceDescriptor> devices { get; set; } = new();
        public List<AccountSessionDescriptor> sessions { get; set; } = new();
    }

    public sealed class RecoveryKeyState
    {
        public string fingerprint { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public int key_version { get; set; }
        public string created_at { get; set; } = string.Empty;
    }

    public sealed class IdentityKeyState
    {
        public int key_version { get; set; }
    }

    public sealed class AccountSessionDescriptor
    {
        public long id { get; set; }
        public string device_id { get; set; } = string.Empty;
        public string family_id { get; set; } = string.Empty;
        public string created_at { get; set; } = string.Empty;
        public string expires_at { get; set; } = string.Empty;
        public string revoked_at { get; set; } = string.Empty;
        public bool is_active { get; set; }
        public bool is_current_device { get; set; }
        public bool is_current_session { get; set; }
    }

    public sealed class RecoveryDeviceResponse : ApiResponse
    {
        public string identity_public_key { get; set; } = string.Empty;
        public RecoveryEnvelopeDto recovery_envelope { get; set; } = new();
    }

    public sealed class RecoveryEnvelopeDto
    {
        public string ciphertext_b64 { get; set; } = string.Empty;
        public string nonce_b64 { get; set; } = string.Empty;
        public RecoveryEnvelopeKdfDto kdf { get; set; } = new();
    }

    public sealed class RecoveryEnvelopeKdfDto
    {
        public string name { get; set; } = string.Empty;
        public int iterations { get; set; }
        public string salt_b64 { get; set; } = string.Empty;
        public int key_size { get; set; }
        public int memory_size_kb { get; set; }
        public int parallelism { get; set; }
        public string purpose { get; set; } = string.Empty;
    }

    public sealed class ImportExportResponse : ApiResponse
    {
        public ImportedUserDto user { get; set; } = new();
    }

    public sealed class ImportedUserDto
    {
        public int user_id { get; set; }
        public string username { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string bio { get; set; } = string.Empty;
        public string created_at { get; set; } = string.Empty;
    }


    public sealed class KeyValidationResult
    {
        public bool IsCurrent { get; set; }
    }
}
