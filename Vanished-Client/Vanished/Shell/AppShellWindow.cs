using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using System;
using System.Runtime.InteropServices;
using Vanished.Pages;
using Vanished.UI;

namespace Vanished.Shell;

public sealed class AppShellWindow : Window
{
    public static AppShellWindow? Instance { get; private set; }

    private readonly ContentControl _host = new();
    private readonly Grid _root = new();
    public Grid OverlayLayer { get; } = new();
    public StackPanel ToastLayer { get; } = new();

    private bool _quitRequested;
    private bool _backgroundNoticeShown;
    private bool _initialNavigationCompleted;

    public AppShellWindow()
    {
        Title = "Vanished";
        WindowState = WindowState.Maximized;
        MinWidth = 980;
        MinHeight = 680;
        Instance = this;
        Background = Ui.Bg;
        TryApplyWindowIcon();
        OverlayLayer.IsHitTestVisible = false;
        OverlayLayer.Background = Brushes.Transparent;
        ToastLayer.HorizontalAlignment = HorizontalAlignment.Right;
        ToastLayer.VerticalAlignment = VerticalAlignment.Bottom;
        ToastLayer.Spacing = 6;
        ToastLayer.Margin = new Thickness(0, 0, 16, 16);
        ToastLayer.IsHitTestVisible = false;
        _root.Children.Add(_host);
        _root.Children.Add(OverlayLayer);
        _root.Children.Add(ToastLayer);
        Content = _root;
        NavigationService.Initialize(_host);
        Opened += async (_, _) =>
        {
            await NotificationService.InitializeAsync();
            ApplyNativeTitleBarColor();
            if (!_initialNavigationCompleted)
            {
                _initialNavigationCompleted = true;
                NavigationService.Navigate(new AuthPage());
            }
        };
        Closing += OnWindowClosing;
        Closed += (_, _) => NotificationService.Shutdown();
    }


    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitRequested ||
            e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;
        e.Cancel = true;
        HideToBackground();
    }

    public void HideToBackground()
    {
        if (!AppTrayService.IsAvailable)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        ShowInTaskbar = false;
        Hide();

        if (_backgroundNoticeShown)
            return;

        _backgroundNoticeShown = true;
        _ = NotificationService.ShowBackgroundNotificationAsync();
    }

    public void RestoreFromBackground()
    {
        ShowInTaskbar = true;
        Show();
        Activate();
        ApplyNativeTitleBarColor();
    }

    public void ResetToAuthAfterAccountDeletion()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
            Show();

        Activate();
        ApplyNativeTitleBarColor();
        NavigationService.Reset(new AuthPage());
    }

    public void QuitApplication()
    {
        _quitRequested = true;

        try
        {
            NotificationService.Shutdown();
            AppTrayService.Dispose();

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
        catch
        {
            Close();
        }
    }


    private void TryApplyWindowIcon()
    {
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Vanished/Resources/Logo/icon2.ico")));
        }
        catch { }
    }
    public void RefreshThemeChrome()
    {
        Background = Ui.Bg;
        ApplyNativeTitleBarColor();
    }

    public void ShowOverlay(Control overlay)
    {
        OverlayLayer.Children.Clear();
        OverlayLayer.IsHitTestVisible = true;
        OverlayLayer.Children.Add(overlay);
    }

    public void ClearOverlay()
    {
        OverlayLayer.Children.Clear();
        OverlayLayer.IsHitTestVisible = false;
    }

    private void ApplyNativeTitleBarColor()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var platformHandle = TryGetPlatformHandle();
            if (platformHandle == null || platformHandle.Handle == IntPtr.Zero)
                return;

            var caption = Ui.CurrentTheme == VanishedThemeMode.Light ? 0x00FAF6F3 : 0x0021160E;
            var text = Ui.CurrentTheme == VanishedThemeMode.Light ? 0x00251A10 : 0x00FAF8F5;
            _ = DwmSetWindowAttribute(platformHandle.Handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(platformHandle.Handle, 36, ref text, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
