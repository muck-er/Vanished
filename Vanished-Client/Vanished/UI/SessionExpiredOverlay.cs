using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using Vanished.API.Helpers;
using Vanished.Shell;
using Vanished.Pages;

namespace Vanished.UI;

public sealed class SessionExpiredOverlay : UserControl
{
    public SessionExpiredOverlay(string email)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Ui.Bg;
        Focusable = true;

        Control logo;
        try
        {
            var uri = new Uri("avares://Vanished/Resources/Logo/LogoWithoutText.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            logo = new Image { Source = new Avalonia.Media.Imaging.Bitmap(stream), Width = 120, Height = 120, Stretch = Stretch.Uniform };
        }
        catch { logo = Ui.Avatar("Vanished", 120); }

        var login = Ui.PrimaryButton("Iniciar sessão novamente");
        login.MaxWidth = 260;
        login.Click += (_, _) =>
        {
            var prefill = string.IsNullOrWhiteSpace(email) ? UserSession.Current.Email : email;
            TokenHelper.ClearToken();
            SessionContext.Clear();
            AuthFlowState.PendingEmail = prefill;
            AppShellWindow.Instance?.ClearOverlay();
            SessionExpiredEvent.Reset();
            NavigationService.Reset(new AuthPage(prefill));
        };

        var panel = Ui.V(16,
            logo,
            Center("A tua sessão expirou", 22, Ui.Text, FontWeight.SemiBold),
            Center("Por favor, autentica-te novamente para continuar.", 14, Ui.Muted, FontWeight.Normal),
            login);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;

        Content = new Grid
        {
            Background = Ui.Bg,
            Children = { panel }
        };

        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            Ui.SoftFadeIn(this);
        };
    }

    private static TextBlock Center(string text, double size, IBrush brush, FontWeight weight)
    {
        var tb = Ui.TextBlock(text, size, brush, weight);
        tb.HorizontalAlignment = HorizontalAlignment.Center;
        tb.TextAlignment = TextAlignment.Center;
        return tb;
    }
}
