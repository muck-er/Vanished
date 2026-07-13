using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Vanished/")) { Source = new Uri("avares://Vanished/Resources/Styles/ScrollBars.axaml") });
        Ui.ApplySavedTheme();
        Ui.SyncApplicationResources();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var window = new AppShellWindow();
            desktop.MainWindow = window;

            SingleInstanceService.ActivationRequested += window.RestoreFromBackground;
            if (SingleInstanceService.ConsumePendingActivation())
                window.RestoreFromBackground();

            AppTrayService.Initialize(window);

            desktop.Exit += (_, _) =>
            {
                SingleInstanceService.ActivationRequested -= window.RestoreFromBackground;
                AppTrayService.Dispose();
                NotificationService.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
