using Avalonia.Threading;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.API.Helpers;

public static class SessionExpiredEvent
{
    private static bool _isShown;
    public static bool IsShown => _isShown;

    public static void Trigger()
    {
        if (_isShown) return;
        _isShown = true;
        var email = UserSession.Current.Email;
        Dispatcher.UIThread.Post(() =>
        {
            var shell = AppShellWindow.Instance;
            if (shell == null) return;
            shell.ShowOverlay(new SessionExpiredOverlay(email));
        });
    }

    public static void Reset() => _isShown = false;
}
