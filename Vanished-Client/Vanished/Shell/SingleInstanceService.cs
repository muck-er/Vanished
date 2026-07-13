using Avalonia.Threading;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vanished.Shell;


public static class SingleInstanceService
{
    private const string ActivationMessage = "show";
    private static readonly string InstanceId = BuildPerUserInstanceId();

    private static Mutex? _mutex;
    private static CancellationTokenSource? _serverCts;
    private static Task? _serverTask;
    private static bool _ownsMutex;
    private static bool _pendingActivation;

    public static event Action? ActivationRequested;

    public static bool TryAcquire()
    {
        if (_mutex != null)
            return _ownsMutex;

        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
            _ownsMutex = createdNew;
            return createdNew;
        }
        catch
        {
            _ownsMutex = true;
            return true;
        }
    }

    public static void StartActivationServer()
    {
        if (!_ownsMutex || _serverCts != null)
            return;

        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunActivationServerAsync(_serverCts.Token));
    }

    public static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            client.Connect(timeout: 600);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(ActivationMessage);
        }
        catch
        {
        }
    }

    public static void RequestActivation()
    {
        var handler = ActivationRequested;
        if (handler == null)
        {
            _pendingActivation = true;
            return;
        }

        handler.Invoke();
    }

    public static bool ConsumePendingActivation()
    {
        if (!_pendingActivation)
            return false;

        _pendingActivation = false;
        return true;
    }

    public static void Dispose()
    {
        try
        {
            _serverCts?.Cancel();
        }
        catch { }

        try
        {
            _serverTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch { }

        try
        {
            if (_ownsMutex)
                _mutex?.ReleaseMutex();
        }
        catch { }

        try
        {
            _mutex?.Dispose();
        }
        catch { }

        _serverCts?.Dispose();
        _serverCts = null;
        _serverTask = null;
        _mutex = null;
        _ownsMutex = false;
        _pendingActivation = false;
        ActivationRequested = null;
    }

    private static async Task RunActivationServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName: PipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (string.Equals(message, ActivationMessage, StringComparison.Ordinal))
                {
                    Dispatcher.UIThread.Post(RequestActivation);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string MutexName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $@"Local\{InstanceId}"
            : InstanceId;

    private static string PipeName => InstanceId;

    private static string BuildPerUserInstanceId()
    {
        var userName = SafeEnvironmentValue(() => Environment.UserName, "unknown-user");
        var userProfile = SafeEnvironmentValue(
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppContext.BaseDirectory);

        var userScope = $"{userName}/{userProfile}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userScope))).ToLowerInvariant()[..16];
        return $"Vanished.Desktop.SingleInstance.{hash}";
    }

    private static string SafeEnvironmentValue(Func<string> factory, string fallback)
    {
        try
        {
            var value = factory();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
