using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Models;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class ProfilePage : UserControl
{
    private PublicUser? _user;
    private readonly bool _isSelf;
    private readonly TextBlock _status = Ui.StatusBlock();
    private bool _refreshing;

    public ProfilePage(PublicUser? user = null, bool isSelf = true)
    {
        _user = user;
        _isSelf = isSelf || user == null || user.id == SessionContext.UserId;
        Content = BuildRoot();

        AttachedToVisualTree += (_, _) =>
        {
            if (_isSelf)
            {
                SessionContext.ProfileUpdated -= OnCurrentProfileUpdated;
                SessionContext.ProfileUpdated += OnCurrentProfileUpdated;
                Content = BuildRoot();
            }
        };
        DetachedFromVisualTree += (_, _) => SessionContext.ProfileUpdated -= OnCurrentProfileUpdated;

        if (!_isSelf && user != null)
            _ = RefreshUserAsync(user.id);
    }

    private void OnCurrentProfileUpdated()
    {
        if (!_isSelf) return;
        Dispatcher.UIThread.Post(() => Content = BuildRoot());
    }

    private Control BuildRoot()
    {
        var back = Ui.BackButton();
        back.Margin = new Thickness(16, 16, 0, 0);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.VerticalAlignment = VerticalAlignment.Top;
        back.IsVisible = NavigationService.CanGoBack;
        back.Click += (_, _) => NavigationService.GoBack();

        var name = _isSelf ? UserSession.Current.DisplayLabel : (_user?.DisplayName ?? "Utilizador");
        var username = _isSelf ? UserSession.Current.Username : (_user?.username ?? string.Empty);
        var avatarBase64 = _isSelf ? SessionContext.AvatarBase64 : _user?.avatar_base64;
        var bio = _isSelf ? UserSession.Current.Bio : (_user?.bio ?? string.Empty);
        var createdAt = FormatMemberSince(_user?.created_at);

        var avatar = AvatarWithStatus(avatarBase64, name, _isSelf ? true : (_user?.is_online ?? false), 80);
        Control action = new Border();
        if (!_isSelf && _user != null)
        {
            var block = _user.is_blocked ? Ui.GhostButton("Desbloquear utilizador") : Ui.DangerButton("Bloquear utilizador");
            block.Width = 180;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            block.Click += async (_, _) =>
            {
                block.IsEnabled = false;
                var resp = _user.is_blocked
                    ? await ApiService.Messages.UnblockUserAsync(_user.id)
                    : await ApiService.Messages.BlockUserAsync(_user.id);
                if (resp?.success == true)
                {
                    _user.is_blocked = !_user.is_blocked;
                    _status.Text = _user.is_blocked ? "Utilizador bloqueado." : "Utilizador desbloqueado.";
                    _status.Foreground = Ui.Success;
                    block.Content = _user.is_blocked ? "Desbloquear utilizador" : "Bloquear utilizador";
                    block.Foreground = _user.is_blocked ? Ui.Muted : Ui.Danger;
                    block.BorderBrush = _user.is_blocked ? Ui.Border : Ui.Danger;
                }
                else
                {
                    _status.Text = resp?.message ?? "Não foi possível alterar o bloqueio.";
                    _status.Foreground = Ui.Danger;
                }
                block.IsEnabled = true;
            };

            action = Ui.V(8, block);
            action.HorizontalAlignment = HorizontalAlignment.Center;
        }

        var bioCard = string.IsNullOrWhiteSpace(bio)
            ? new Border()
            : Ui.Card(CenterText(bio, 14, Ui.Text, FontWeight.Normal), new Thickness(16), 12);

        var content = Ui.V(16,
            avatar,
            CenterText(name, 24, Ui.Text, FontWeight.SemiBold),
            CenterText(string.IsNullOrWhiteSpace(username) ? "@" : "@" + username, 13, Ui.Muted, FontWeight.Normal),
            bioCard,
            CenterText(createdAt, 13, Ui.Muted, FontWeight.Normal),
            action,
            _status);

        var root = new Grid
        {
            Background = Ui.Bg,
            Children =
            {
                back,
                new Border
                {
                    MaxWidth = 520,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Ui.Card(content, new Thickness(30), 22)
                }
            }
        };
        Ui.SoftFadeIn(root);
        return root;
    }

    private async Task RefreshUserAsync(int userId)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var (ok, user, _) = await ApiService.Chat.GetUserAsync(userId);
            if (ok && user != null)
            {
                _user = user;
                await Dispatcher.UIThread.InvokeAsync(() => Content = BuildRoot());
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static Control AvatarWithStatus(string? avatarBase64, string text, bool online, double size)
    {
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        grid.Children.Add(Ui.AvatarImage(avatarBase64, text, size));
        grid.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = online ? Ui.Success : Ui.Muted2,
            BorderBrush = Ui.Surface,
            BorderThickness = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2)
        });
        return grid;
    }

    private static TextBlock CenterText(string text, double size, IBrush brush, FontWeight weight)
    {
        var block = Ui.TextBlock(text, size, brush, weight);
        block.TextAlignment = TextAlignment.Center;
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }

    private static string FormatMemberSince(string? value)
    {
        if (DateTime.TryParse(value, out var dt))
            return "Membro desde " + dt.ToLocalTime().ToString("dd/MM/yyyy");
        return "Membro desde recentemente";
    }
}
