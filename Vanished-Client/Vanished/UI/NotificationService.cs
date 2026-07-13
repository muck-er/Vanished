using Avalonia.Platform;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vanished.Shell;

namespace Vanished.UI;

public static class NotificationService
{
    private const string AppName = "Vanished";
    private static NotificationPreferences _prefs = new();
    private static string? _windowsIconPath;
    private static string? _linuxIconPath;
    private static string? _messageSoundPath;
    private static string? _requestSoundPath;

    public static NotificationPreferences Preferences => _prefs;

    public static async Task InitializeAsync()
    {
        _prefs = await NotificationPreferences.LoadAsync();
        _windowsIconPath = EnsureAssetFile("avares://Vanished/Resources/Logo/icon2.ico", "icon2.ico");
        _linuxIconPath = EnsureAssetFile("avares://Vanished/Resources/Logo/LogoWithoutText.png", "vanished.png");
        _messageSoundPath = EnsureAssetFile("avares://Vanished/Assets/Sounds/message.wav", "message.wav");
        _requestSoundPath = EnsureAssetFile("avares://Vanished/Assets/Sounds/request.wav", "request.wav");
    }

    public static async Task UpdateAsync(NotificationPreferences prefs)
    {
        _prefs = prefs;
        await NotificationPreferences.SaveAsync(prefs);
    }

    public static void Shutdown()
    {
    }

    public static Task ShowMessageNotificationAsync(string senderName, string messageContent, string conversationId)
    {
        if (!_prefs.EnableNotifications)
            return Task.CompletedTask;

        var title = string.IsNullOrWhiteSpace(senderName) ? "Nova mensagem" : $"Mensagem de {senderName}";
        const string body = "Recebeste uma nova mensagem.";

        _ = ShowNativeOrFallbackAsync(title, body, ToastType.Info, _messageSoundPath);
        return Task.CompletedTask;
    }

    public static Task ShowRequestNotificationAsync(string senderName)
    {
        if (!_prefs.EnableNotifications || !_prefs.RequestNotifications)
            return Task.CompletedTask;

        var who = string.IsNullOrWhiteSpace(senderName) ? "Alguém" : senderName;
        _ = ShowNativeOrFallbackAsync("Novo pedido de mensagem", $"{who} enviou-te um pedido de mensagem.", ToastType.Info, _requestSoundPath);
        return Task.CompletedTask;
    }


    public static Task ShowBackgroundNotificationAsync()
    {
        if (!_prefs.EnableNotifications)
            return Task.CompletedTask;

        _ = ShowNativeOrFallbackAsync(
            "Vanished continua em segundo plano",
            "Usa o ícone da área de notificação para abrir a app ou sair.",
            ToastType.Info,
            soundPath: null);

        return Task.CompletedTask;
    }

    private static async Task ShowNativeOrFallbackAsync(string title, string body, ToastType type, string? soundPath)
    {
        if (AppShellWindow.Instance?.IsActive == true)
            return;

        var shown = TryShowNativeNotification(title, body);
        var nativeWindowsNotification = shown && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (_prefs.SoundEnabled && !nativeWindowsNotification)
            _ = PlaySoundAsync(soundPath);

        if (!shown)
            ToastService.Show($"{title}: {body}", "notification", type, 4500);

        await Task.CompletedTask;
    }

    private static bool TryShowNativeNotification(string title, string body)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return TryShowWindowsBalloon(title, body);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return TryShowLinuxNotifySend(title, body);
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool TryShowWindowsBalloon(string title, string body)
    {
        var iconPath = File.Exists(_windowsIconPath) ? _windowsIconPath! : string.Empty;
        var script = new StringBuilder()
            .AppendLine("Add-Type -AssemblyName System.Windows.Forms")
            .AppendLine("Add-Type -AssemblyName System.Drawing")
            .AppendLine("$n = New-Object System.Windows.Forms.NotifyIcon")
            .AppendLine("$n.Text = 'Vanished'")
            .AppendLine("$n.Visible = $true")
            .AppendLine("$n.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info")
            .AppendLine("$n.BalloonTipTitle = " + ToPowerShellString(title))
            .AppendLine("$n.BalloonTipText = " + ToPowerShellString(body))
            .AppendLine(string.IsNullOrWhiteSpace(iconPath)
                ? "$n.Icon = [System.Drawing.SystemIcons]::Information"
                : "$n.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon(" + ToPowerShellString(iconPath) + ")")
            .AppendLine("$n.ShowBalloonTip(5000)")
            .AppendLine("Start-Sleep -Milliseconds 5500")
            .AppendLine("$n.Dispose()")
            .ToString();

        return StartPowerShellEncoded(script);
    }

    private static bool TryShowLinuxNotifySend(string title, string body)
    {
        var notifySend = FindExecutable("notify-send");
        if (notifySend == null)
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = notifySend,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--app-name");
        psi.ArgumentList.Add(AppName);
        if (File.Exists(_linuxIconPath))
        {
            psi.ArgumentList.Add("--icon");
            psi.ArgumentList.Add(_linuxIconPath!);
        }
        psi.ArgumentList.Add(title);
        psi.ArgumentList.Add(body);
        Process.Start(psi)?.Dispose();
        return true;
    }

    private static Task PlaySoundAsync(string? soundPath)
    {
        if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
            return Task.CompletedTask;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = "$p = New-Object System.Media.SoundPlayer " + ToPowerShellString(soundPath) + "; $p.PlaySync()";
                StartPowerShellEncoded(script);
                return Task.CompletedTask;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var player = FindExecutable("paplay") ?? FindExecutable("aplay") ?? FindExecutable("ffplay");
                if (player == null)
                    return Task.CompletedTask;

                var psi = new ProcessStartInfo
                {
                    FileName = player,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (Path.GetFileName(player).Equals("ffplay", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("-nodisp");
                    psi.ArgumentList.Add("-autoexit");
                    psi.ArgumentList.Add("-loglevel");
                    psi.ArgumentList.Add("quiet");
                }
                psi.ArgumentList.Add(soundPath);
                Process.Start(psi)?.Dispose();
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private static bool StartPowerShellEncoded(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? EnsureAssetFile(string assetUri, string fileName)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vanished", "assets");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, fileName);

            using var input = AssetLoader.Open(new Uri(assetUri));
            using var output = File.Create(target);
            input.CopyTo(output);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindExecutable(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string ToPowerShellString(string value)
        => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

}
