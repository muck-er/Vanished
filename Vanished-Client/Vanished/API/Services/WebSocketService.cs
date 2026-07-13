using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vanished.API.Helpers;

namespace Vanished.API.Services;

public sealed class WsEvent
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JToken>? Extra { get; set; }

    public T? Get<T>(string key)
    {
        if (Extra == null || !Extra.TryGetValue(key, out var value))
            return default;

        try
        {
            if (value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
                return default;

            if (typeof(T) == typeof(string))
            {
                var text = value.Type == JTokenType.String
                    ? value.Value<string>()
                    : value.ToString(Formatting.None);
                return (T?)(object?)text;
            }

            return value.ToObject<T>();
        }
        catch
        {
            return default;
        }
    }
}

public sealed class WebSocketService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _connecting;
    private readonly object _presenceLock = new();
    private readonly HashSet<int> _onlinePeers = new();

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public event Func<WsEvent, Task>? EventReceived;

    public async Task ConnectAsync()
    {
        if (_connecting || IsConnected) return;
        if (string.IsNullOrWhiteSpace(TokenHelper.CurrentToken)) return;

        _connecting = true;
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ws?.Dispose();
            _ws = new ClientWebSocket();

            var uri = BuildWsUri(TokenHelper.CurrentToken!);
            await _ws.ConnectAsync(uri, _cts.Token);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _ = Task.Run(() => KeepaliveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WS] connect falhou: {ex.Message}");
            _ws?.Dispose();
            _ws = null;
        }
        finally
        {
            _connecting = false;
        }
    }

    public IReadOnlyCollection<int> GetOnlinePeerSnapshot()
    {
        lock (_presenceLock)
            return _onlinePeers.ToArray();
    }

    public bool IsPeerOnline(int peerId)
    {
        lock (_presenceLock)
            return _onlinePeers.Contains(peerId);
    }

    public async Task SendAsync(object payload)
    {
        if (!IsConnected || _ws == null || _cts == null) return;
        try
        {
            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WS] send falhou: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
        => await CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing");

    public async Task CloseAsync(WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.CloseReceived)
                await _ws.CloseAsync(status, reason, CancellationToken.None);
        }
        catch { }
        finally
        {
            try { _cts?.Cancel(); } catch { }
            _ws?.Dispose();
            _ws = null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var ev = JsonConvert.DeserializeObject<WsEvent>(json);
                if (ev == null) continue;
                TrackPresenceEvent(ev);
                if (ev.Type == "session.expired")
                {
                    SessionExpiredEvent.Trigger();
                    continue;
                }
                var handler = EventReceived;
                if (handler != null)
                    await handler(ev);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WS] receive falhou: {ex.Message}");
                await Task.Delay(1000, ct).ContinueWith(_ => { });
            }
        }
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), ct);
                await SendAsync(new { type = "ping" });
            }
            catch (OperationCanceledException) { return; }
            catch { }
        }
    }

    private void TrackPresenceEvent(WsEvent ev)
    {
        if (!string.Equals(ev.Type, "user.status", StringComparison.OrdinalIgnoreCase))
            return;

        var peerId = ev.Get<int>("peer_id");
        if (peerId <= 0)
            return;

        var online = ev.Get<bool>("online");
        lock (_presenceLock)
        {
            if (online)
                _onlinePeers.Add(peerId);
            else
                _onlinePeers.Remove(peerId);
        }
    }

    private static Uri BuildWsUri(string token)
    {
        var baseUri = BaseService.ApiBaseAddress;
        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "ws",
            Query = "token=" + Uri.EscapeDataString(token)
        };
        return builder.Uri;
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
        _cts?.Dispose();
    }
}
