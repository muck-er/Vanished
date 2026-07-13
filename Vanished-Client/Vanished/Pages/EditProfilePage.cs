using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Models;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class EditProfilePage : UserControl
{
    private readonly PublicUser? _user;
    private readonly TextBox _fullName = Ui.TextBox("Nome de display");
    private readonly TextBox _bio = Ui.TextBox("Bio");
    private readonly TextBlock _bioCounter = Ui.TextBlock("0/160", 12, Ui.Muted);
    private readonly TextBlock _validationError = Ui.TextBlock("", 12, Ui.Danger);
    private readonly TextBlock _status = Ui.StatusBlock();
    private readonly Border _avatarPreview;
    private string _avatarBase64 = string.Empty;
    private string _avatarMime = string.Empty;
    private bool _saving;
    private Button? _saveButton;

    public EditProfilePage(PublicUser? user = null)
    {
        _user = user;
        _fullName.Text = user?.full_name ?? (string.IsNullOrWhiteSpace(SessionContext.DisplayName) ? SessionContext.Username : SessionContext.DisplayName);
        _bio.Text = user?.bio ?? SessionContext.Bio ?? string.Empty;
        _avatarBase64 = user?.avatar_base64 ?? SessionContext.AvatarBase64 ?? string.Empty;
        _avatarMime = user?.avatar_mime ?? SessionContext.AvatarMime ?? string.Empty;
        _bio.AcceptsReturn = true;
        _bio.Height = 100;
        _bio.TextChanged += (_, _) => Validate();
        _fullName.TextChanged += (_, _) => Validate();
        _avatarPreview = Ui.AvatarImage(_avatarBase64, _fullName.Text ?? SessionContext.Username, 82);
        Content = BuildRoot();
        Validate();
    }

    private Control BuildRoot()
    {
        var back = Ui.BackButton();
        back.Margin = new Thickness(16, 16, 0, 0);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.VerticalAlignment = VerticalAlignment.Top;
        back.IsVisible = NavigationService.CanGoBack;
        back.Click += (_, _) => NavigationService.GoBack();

        var changeAvatar = Ui.GhostButton("Alterar foto");
        changeAvatar.HorizontalAlignment = HorizontalAlignment.Center;
        changeAvatar.Click += async (_, _) => await PickAvatarAsync();

        var save = Ui.PrimaryButton("Guardar");
        _saveButton = save;
        var cancel = Ui.GhostButton("Cancelar");
        save.Click += async (_, _) => await SaveAsync();
        cancel.Click += (_, _) => NavigationService.GoBack();

        var body = Ui.V(12,
            Ui.TextBlock("Editar perfil", 28, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Dados públicos visíveis para outros utilizadores.", 13, Ui.Muted),
            Ui.H(14, _avatarPreview, changeAvatar),
            _fullName,
            _validationError,
            LockedUsernameField(),
            _bio,
            _bioCounter,
            Ui.H(10, save, cancel),
            _status);

        var root = new Grid
        {
            Background = Ui.Bg,
            Children =
            {
                back,
                new Border
                {
                    MaxWidth = 560,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Ui.Card(body, new Thickness(30), 22)
                }
            }
        };
        Ui.SoftFadeIn(root);
        return root;
    }

    private async Task PickAvatarAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Escolher avatar",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Imagens")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" },
                    MimeTypes = new[] { "image/png", "image/jpeg", "image/webp" }
                }
            }
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        if (memory.Length > 512 * 1024)
        {
            _status.Text = "Avatar demasiado grande. Máximo: 512KB.";
            _status.Foreground = Ui.Danger;
            return;
        }
        var bytes = memory.ToArray();
        _avatarBase64 = Convert.ToBase64String(bytes);
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        _avatarMime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        try
        {
            _avatarPreview.Child = new Image
            {
                Source = new Bitmap(new MemoryStream(bytes)),
                Width = 82,
                Height = 82,
                Stretch = Stretch.UniformToFill
            };
        }
        catch { }
    }

    private Control LockedUsernameField()
    {
        var username = _user?.username ?? SessionContext.Username;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Ui.Surface2,
            MinHeight = 44,
            Margin = new Thickness(0),
            Children =
            {
                new TextBlock { Text = "@" + username, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14,0,8,0) },
                Ui.Icon("lock", 15, Ui.Muted2)
            }
        };
        Grid.SetColumn(row.Children[1], 1);
        row.IsHitTestVisible = false;
        ToolTip.SetTip(row, "O teu @ não pode ser alterado.");
        return new Border
        {
            IsHitTestVisible = false,
            Background = Ui.Surface2,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = row
        };
    }

    private bool Validate()
    {
        var display = (_fullName.Text ?? string.Empty).Trim();
        var bio = _bio.Text ?? string.Empty;
        _bioCounter.Text = $"{bio.Length}/160";
        if (display.Length < 3 || display.Length > 32 || display.Any(char.IsWhiteSpace))
        {
            _fullName.BorderBrush = Ui.Danger;
            _validationError.Text = "Nome de display inválido. Usa 3–32 caracteres sem espaços.";
            return false;
        }
        if (bio.Length > 160)
        {
            _bio.BorderBrush = Ui.Danger;
            _validationError.Text = "Bio demasiado longa.";
            return false;
        }
        _fullName.BorderBrush = Ui.Border;
        _bio.BorderBrush = Ui.Border;
        _validationError.Text = string.Empty;
        return true;
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        if (!Validate()) return;
        try
        {
            _saving = true;
            if (_saveButton != null) Ui.SetButtonLoading(_saveButton, true);
            var resp = await ApiService.Chat.UpdateProfileAsync(
                string.Empty,
                (_fullName.Text ?? string.Empty).Trim(),
                (_bio.Text ?? string.Empty).Trim(),
                _avatarBase64,
                _avatarMime);
            if (resp?.success == true)
            {
                _status.Text = "Perfil atualizado.";
                _status.Foreground = Ui.Success;
                if (resp.user != null)
                {
                    SessionContext.UpdateProfile(resp.user.username, resp.user.DisplayName, resp.user.bio, resp.user.avatar_base64, resp.user.avatar_mime);
                    LocalUserProfileStore.Save(new LocalUserProfile { Email = SessionContext.Email, Username = resp.user.username, FullName = resp.user.full_name, Bio = resp.user.bio, AvatarBase64 = resp.user.avatar_base64, AvatarMime = resp.user.avatar_mime });
                }
                NavigationService.GoBack();
            }
            else
            {
                _status.Text = resp?.message ?? "Não foi possível atualizar.";
                _status.Foreground = Ui.Danger;
            }
        }
        finally
        {
            _saving = false;
            if (_saveButton != null) Ui.SetButtonLoading(_saveButton, false);
        }
    }
}
