using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using System;

namespace Vanished.Shell;

public static class AppTrayService
{
    private static TrayIcon? _trayIcon;
    private static bool _initialized;

    public static bool IsAvailable => _initialized;

    public static void Initialize(AppShellWindow window)
    {
        if (_initialized || Application.Current == null)
            return;

        try
        {
            var openItem = new NativeMenuItem("Abrir Vanished");
            openItem.Click += (_, _) => RunOnUiThread(window.RestoreFromBackground);

            var hideItem = new NativeMenuItem("Ocultar janela");
            hideItem.Click += (_, _) => RunOnUiThread(window.HideToBackground);

            var quitItem = new NativeMenuItem("Sair do Vanished");
            quitItem.Click += (_, _) => RunOnUiThread(window.QuitApplication);

            var menu = new NativeMenu
            {
                openItem,
                hideItem,
                new NativeMenuItemSeparator(),
                quitItem
            };

            _trayIcon = new TrayIcon
            {
                Icon = LoadTrayIcon(),
                ToolTipText = "Vanished - a correr em segundo plano",
                IsVisible = true,
                Menu = menu
            };

            _trayIcon.Clicked += (_, _) => RunOnUiThread(window.RestoreFromBackground);

            TrayIcon.SetIcons(Application.Current, new TrayIcons { _trayIcon });
            _initialized = true;
        }
        catch
        {
            Dispose();
        }
    }

    public static void Dispose()
    {
        try
        {
            if (Application.Current != null)
                TrayIcon.SetIcons(Application.Current, null);
        }
        catch
        {
        }

        try { _trayIcon?.Dispose(); } catch { }
        _trayIcon = null;
        _initialized = false;
    }

    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://Vanished/Resources/Logo/icon2.ico")));
        }
        catch
        {
            return null;
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

}
