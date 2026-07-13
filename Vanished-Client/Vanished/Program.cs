using Avalonia;
using System;
using Vanished.Shell;

namespace Vanished;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SingleInstanceService.TryAcquire())
        {
            SingleInstanceService.SignalExistingInstance();
            return;
        }

        try
        {
            SingleInstanceService.StartActivationServer();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstanceService.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
