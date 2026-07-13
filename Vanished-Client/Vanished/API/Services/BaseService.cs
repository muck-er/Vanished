using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using Vanished.API.Helpers;
using Vanished.API.Models;

namespace Vanished.API.Services;

public abstract class BaseService
{
    protected static readonly HttpClient _client;
    public static Uri ApiBaseAddress => _client.BaseAddress ?? new Uri("https://api.vanished.pt");
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    static BaseService()
    {
        string baseUrl = ResolveBaseUrl();

        bool isDev = (Environment.GetEnvironmentVariable("VANISHED_ENVIRONMENT") ?? "Dev")
            .Equals("Dev", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"ApiBaseUrl inválida: {baseUrl}");

        bool isLocalhost =
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1") ||
            uri.Host.Equals("::1");

        if (!isDev && !isLocalhost && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Em produção, VANISHED_API_BASE_URL tem de usar HTTPS.");

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            CheckCertificateRevocationList = true,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        string? pinnedThumbprints = Environment.GetEnvironmentVariable("VANISHED_API_PINNED_CERT_THUMBPRINTS");
        if (!string.IsNullOrWhiteSpace(pinnedThumbprints) && !isLocalhost)
        {
            var pins = pinnedThumbprints
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeThumbprint)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            handler.ServerCertificateCustomValidationCallback = (_, cert, __, errors) =>
            {
                if (errors != System.Net.Security.SslPolicyErrors.None)
                    return false;

                var thumb = NormalizeThumbprint(cert?.GetCertHashString());
                return !string.IsNullOrEmpty(thumb) && pins.Contains(thumb);
            };
        }

        _client = new HttpClient(handler)
        {
            BaseAddress = EnsureTrailingSlash(uri),
            Timeout = TimeSpan.FromSeconds(40)
        };

        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Debug.WriteLine($"[API] BaseAddress: {_client.BaseAddress}");
    }

    private static string ResolveBaseUrl()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("VANISHED_API_BASE_URL")
            ?? Environment.GetEnvironmentVariable("ApiBaseUrl");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return NormalizeBaseUrl(fromEnv);

        foreach (var path in CandidateConfigFiles())
        {
            var configured = TryReadBaseUrlFromConfig(path);
            if (!string.IsNullOrWhiteSpace(configured))
                return NormalizeBaseUrl(configured);
        }

        return "https://api.vanished.pt/";
    }

    private static IEnumerable<string> CandidateConfigFiles()
    {
        string baseDir = AppContext.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();

        yield return Path.Combine(baseDir, "vanished.client.json");
        yield return Path.Combine(baseDir, "appsettings.json");
        yield return Path.Combine(currentDir, "vanished.client.json");
        yield return Path.Combine(currentDir, "appsettings.json");
    }

    private static string? TryReadBaseUrlFromConfig(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
            return (string?)root["ApiBaseUrl"]
                ?? (string?)root["VANISHED_API_BASE_URL"]
                ?? (string?)root["Vanished"]?["ApiBaseUrl"]
                ?? (string?)root["Vanished"]?["ApiBaseAddress"];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] Config inválida em {path}: {ex.Message}");
            return null;
        }
    }

    private static string NormalizeBaseUrl(string value)
        => value.Trim().TrimEnd('/') + "/";

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var text = uri.ToString();
        return text.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(text + "/");
    }

    protected static HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var req = new HttpRequestMessage(method, endpoint);
        if (!string.IsNullOrWhiteSpace(TokenHelper.CurrentToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenHelper.CurrentToken);
        return req;
    }

    protected async Task<ApiResponse> PostAnyAsync<T>(IEnumerable<string> endpoints, T data)
    {
        ApiResponse? lastResponse = null;

        foreach (var endpoint in endpoints.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var response = await PostAsync(endpoint, data);
            lastResponse = response;
            if (response.success)
                return response;
        }

        return lastResponse ?? new ApiResponse { success = false, message = "Não foi possível contactar o servidor." };
    }

    private static string BuildHttpErrorMessage(string endpoint, HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code switch
        {
            404 => $"Endpoint indisponível no servidor: {endpoint} (404).",
            401 => "Autenticação necessária. Inicie sessão novamente.",
            403 => "Pedido recusado pelo servidor.",
            >= 500 => "Erro interno no servidor.",
            _ => $"Pedido falhou no servidor ({code})."
        };
    }

    protected async Task<ApiResponse> PostAsync<T>(string endpoint, T data, bool allowRefresh = true, bool signRequest = true)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Post, endpoint);
            var body = JsonConvert.SerializeObject(data);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (signRequest)
                RequestSigningHelper.ApplySignature(req, body);

            using var response = await _client.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[API] POST {endpoint} -> {(int)response.StatusCode} {response.StatusCode}");
            LogSanitizedBody(json);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !IsRefreshEndpoint(endpoint))
            {
                if (allowRefresh && await TryRefreshTokenAsync())
                    return await PostAsync(endpoint, data, false, signRequest);

                SessionExpiredEvent.Trigger();
                return new ApiResponse { success = false, message = BuildHttpErrorMessage(endpoint, response.StatusCode) };
            }

            var parsed = TryDeserialize<ApiResponse>(json);
            if (parsed != null)
                return parsed;

            return new ApiResponse
            {
                success = false,
                message = response.IsSuccessStatusCode ? "Erro ao processar resposta." : BuildHttpErrorMessage(endpoint, response.StatusCode)
            };
        }
        catch (TaskCanceledException)
        {
            return new ApiResponse { success = false, message = "Timeout ao contactar o servidor." };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] POST {endpoint} falhou: {ex}");
            Console.Error.WriteLine($"[API] POST {endpoint} falhou: {ex.GetType().Name}: {ex.Message}");
            return new ApiResponse { success = false, message = "Erro de ligação ao servidor." };
        }
    }


    protected async Task<BinaryDownloadResult> PostBinaryAsync<T>(string endpoint, T data, bool allowRefresh = true, bool signRequest = true)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Post, endpoint);
            var body = JsonConvert.SerializeObject(data);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (signRequest)
                RequestSigningHelper.ApplySignature(req, body);

            using var response = await _client.SendAsync(req);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !IsRefreshEndpoint(endpoint))
            {
                if (allowRefresh && await TryRefreshTokenAsync())
                    return await PostBinaryAsync(endpoint, data, false, signRequest);

                SessionExpiredEvent.Trigger();
                return new BinaryDownloadResult { success = false, message = BuildHttpErrorMessage(endpoint, response.StatusCode) };
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                var parsed = TryDeserialize<ApiResponse>(errorJson);
                return new BinaryDownloadResult { success = false, message = parsed?.message ?? BuildHttpErrorMessage(endpoint, response.StatusCode) };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var name = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName
                       ?? string.Empty;
            return new BinaryDownloadResult
            {
                success = true,
                message = "Download concluído.",
                Bytes = bytes,
                FileName = (name ?? string.Empty).Trim('"')
            };
        }
        catch (TaskCanceledException)
        {
            return new BinaryDownloadResult { success = false, message = "Timeout ao contactar o servidor." };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] POST binary {endpoint} falhou: {ex}");
            return new BinaryDownloadResult { success = false, message = "Erro de ligação ao servidor." };
        }
    }

    protected async Task<TResponse?> PostMultipartAsync<TResponse>(string endpoint, MultipartFormDataContent form, bool allowRefresh = true)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Post, endpoint);
            req.Content = form;

            using var response = await _client.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized && !IsRefreshEndpoint(endpoint))
            {
                if (allowRefresh && await TryRefreshTokenAsync())
                    return await PostMultipartAsync<TResponse>(endpoint, form, false);
                SessionExpiredEvent.Trigger();
                return default;
            }
            return TryDeserialize<TResponse>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] POST multipart {endpoint} falhou: {ex}");
            return default;
        }
    }

    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data, bool allowRefresh = true, bool signRequest = true)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Post, endpoint);
            var body = JsonConvert.SerializeObject(data);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (signRequest)
                RequestSigningHelper.ApplySignature(req, body);

            using var response = await _client.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[API] POST {endpoint} -> {(int)response.StatusCode} {response.StatusCode}");
            LogSanitizedBody(json);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !IsRefreshEndpoint(endpoint))
            {
                if (allowRefresh && await TryRefreshTokenAsync())
                    return await PostAsync<TRequest, TResponse>(endpoint, data, false, signRequest);

                SessionExpiredEvent.Trigger();
                return default;
            }

            return TryDeserialize<TResponse>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] POST {endpoint} falhou: {ex}");
            Console.Error.WriteLine($"[API] POST {endpoint} falhou: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    protected async Task<TResponse?> GetAsync<TResponse>(string endpoint, CancellationToken ct = default, bool allowRefresh = true)
    {
        try
        {
            using var req = CreateRequest(HttpMethod.Get, endpoint);
            RequestSigningHelper.ApplySignature(req, string.Empty);
            using var response = await _client.SendAsync(req, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[API] GET {endpoint} -> {(int)response.StatusCode} {response.StatusCode}");
            LogSanitizedBody(json);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !IsRefreshEndpoint(endpoint))
            {
                if (allowRefresh && await TryRefreshTokenAsync())
                    return await GetAsync<TResponse>(endpoint, ct, false);

                SessionExpiredEvent.Trigger();
                return default;
            }

            return TryDeserialize<TResponse>(json);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[API] GET {endpoint} cancelado.");
            return default;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] GET {endpoint} falhou: {ex}");
            return default;
        }
    }


    private static bool IsRefreshEndpoint(string endpoint)
        => endpoint.Contains("/refresh", StringComparison.OrdinalIgnoreCase) || endpoint.EndsWith("refresh", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> TryRefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(TokenHelper.CurrentRefreshToken))
            return false;

        await _refreshLock.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(TokenHelper.CurrentRefreshToken))
                return false;

            var body = JsonConvert.SerializeObject(new { RefreshToken = TokenHelper.CurrentRefreshToken });
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/user/refresh");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            RequestSigningHelper.ApplySignature(req, body);

            using var response = await _client.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[API] REFRESH -> {(int)response.StatusCode} {response.StatusCode}");
            LogSanitizedBody(json);

            if (!response.IsSuccessStatusCode)
            {
                TokenHelper.ClearToken();
                return false;
            }

            var parsed = TryDeserialize<ApiResponse>(json);
            if (parsed?.success == true && !string.IsNullOrWhiteSpace(parsed.access_token))
            {
                TokenHelper.SaveToken(parsed.access_token);
                if (!string.IsNullOrWhiteSpace(parsed.refresh_token))
                    TokenHelper.SaveRefreshToken(parsed.refresh_token);
                return true;
            }

            TokenHelper.ClearToken();
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] refresh falhou: {ex}");
            TokenHelper.ClearToken();
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }


    private static void LogSanitizedBody(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        Debug.WriteLine($"[API] BODY: {SanitizeJsonForLog(json)}");
    }

    private static string SanitizeJsonForLog(string json)
    {
        try
        {
            var token = JToken.Parse(json);
            RedactSensitiveFields(token);
            var text = token.ToString(Formatting.None);
            const int maxLogLength = 4000;
            return text.Length <= maxLogLength ? text : text[..maxLogLength] + "…[truncated]";
        }
        catch
        {
            return "[resposta não JSON ocultada]";
        }
    }

    private static void RedactSensitiveFields(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties().ToList())
            {
                if (IsSensitiveFieldName(prop.Name))
                    prop.Value = "[redacted]";
                else
                    RedactSensitiveFields(prop.Value);
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr)
                RedactSensitiveFields(item);
        }
    }

    private static bool IsSensitiveFieldName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var n = name.ToLowerInvariant();
        return n.Contains("token", StringComparison.Ordinal) ||
               n.Contains("secret", StringComparison.Ordinal) ||
               n.Contains("password", StringComparison.Ordinal) ||
               n.Contains("passwd", StringComparison.Ordinal) ||
               n.Contains("mfa", StringComparison.Ordinal) ||
               n.Contains("totp", StringComparison.Ordinal) ||
               n.Contains("recovery", StringComparison.Ordinal) ||
               n.Contains("private_key", StringComparison.Ordinal) ||
               n.Contains("ciphertext", StringComparison.Ordinal) ||
               n.Contains("nonce", StringComparison.Ordinal) ||
               n.Contains("eph_pub", StringComparison.Ordinal) ||
               n.Contains("public_key", StringComparison.Ordinal) ||
               n.Contains("device_encryption", StringComparison.Ordinal);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] JSON inválido: {ex.Message}\n{SanitizeJsonForLog(json)}");
            return default;
        }
    }

    private static string? NormalizeThumbprint(string? thumb)
        => string.IsNullOrWhiteSpace(thumb) ? null : thumb.Replace(" ", "").Replace(":", "").Trim();
}

public sealed class BinaryDownloadResult : ApiResponse
{
    public byte[]? Bytes { get; set; }
    public string FileName { get; set; } = string.Empty;
}

