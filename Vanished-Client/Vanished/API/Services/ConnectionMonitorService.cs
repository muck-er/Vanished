using System;
using System.Threading;
using System.Threading.Tasks;
using Vanished.API.Helpers;

namespace Vanished.API.Services;

public enum ConnectionState
{
    Connected,
    Connecting,
    Reconnecting,
    Disconnected
}

public sealed class ConnectionMonitorService
{
    private readonly WebSocketService _webSocketService;
    private CancellationTokenSource? _cts;
    private bool _started;
    private int _reconnectAttempt;
    private const int MaxReconnectDelaySeconds = 30;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ConnectionState>? StateChanged;

    public ConnectionMonitorService(WebSocketService webSocketService)
    {
        _webSocketService = webSocketService;
    }

    public Task ConnectAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { }
        _started = false;
        SetState(ConnectionState.Disconnected);
        await _webSocketService.DisconnectAsync();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(TokenHelper.CurrentToken))
            {
                SetState(ConnectionState.Disconnected);
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ContinueWith(_ => { });
                continue;
            }

            SetState(_reconnectAttempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);
            await _webSocketService.ConnectAsync();

            if (_webSocketService.IsConnected)
            {
                _reconnectAttempt = 0;
                SetState(ConnectionState.Connected);
                while (!ct.IsCancellationRequested && _webSocketService.IsConnected)
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ContinueWith(_ => { });
            }

            if (ct.IsCancellationRequested) break;
            SetState(ConnectionState.Reconnecting);
            var delay = Math.Min(2 * Math.Pow(2, _reconnectAttempt++), MaxReconnectDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delay), ct).ContinueWith(_ => { });
        }
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
