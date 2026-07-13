using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Services;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class SettingsPage : UserControl
{
    private readonly TextBlock _status = Ui.StatusBlock();
    private readonly List<AccordionItem> _sections = new();

    public SettingsPage()
    {
        Content = BuildRoot();
    }

    private Control BuildRoot()
    {
        var back = Ui.BackButton();
        back.Margin = new Thickness(16, 16, 0, 0);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.VerticalAlignment = VerticalAlignment.Top;
        back.IsVisible = NavigationService.CanGoBack;
        back.Click += (_, _) => NavigationService.GoBack();

        var appearance = Section("Aparência", BuildAppearance(), true);
        var notifications = Section("Notificações", BuildNotifications());
        var privacy = Section("Privacidade & Segurança", BuildSecurity());
        var about = Section("Sobre", BuildAbout());

        var panel = Ui.V(14,
            Ui.TextBlock("Definições", 32, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Preferências deste dispositivo.", 14, Ui.Muted),
            appearance.Root,
            notifications.Root,
            privacy.Root,
            about.Root,
            _status);

        var root = new Grid
        {
            Background = Ui.Bg,
            Children =
            {
                back,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 56, 0, 0),
                    Content = new Border
                    {
                        MaxWidth = 600,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Padding = new Thickness(24, 0, 24, 30),
                        Child = panel
                    }
                }
            }
        };
        Ui.SoftFadeIn(root);
        return root;
    }

    private AccordionItem Section(string title, Control content, bool open = false)
    {
        var item = new AccordionItem(title, content, () => OpenExclusive(title));
        _sections.Add(item);
        item.SetOpen(open, false);
        return item;
    }

    private void OpenExclusive(string title)
    {
        var target = _sections.Find(x => x.Title == title);
        if (target == null) return;
        var next = !target.IsOpen;
        foreach (var section in _sections)
            section.SetOpen(section == target && next, true);
    }

    private Control BuildAppearance()
    {
        var dark = ThemeButton("🌑", "Escuro", VanishedThemeMode.Dark);
        var light = ThemeButton("☀️", "Claro", VanishedThemeMode.Light);
        var system = ThemeButton("🌓", "Sistema", VanishedThemeMode.System);
        return Ui.V(10,
            Ui.TextBlock("Escolhe o tema da app.", 13, Ui.Muted),
            dark,
            light,
            system);
    }

    private Control BuildNotifications()
    {
        var prefs = NotificationPreferences.Load();

        var masterToggle = Ui.Toggle("Ativar notificações");
        var soundToggle = Ui.Toggle("Sons de notificação");
        var requestToggle = Ui.Toggle("Pedidos de mensagem");

        masterToggle.IsChecked = prefs.EnableNotifications;
        soundToggle.IsChecked = prefs.SoundEnabled;
        requestToggle.IsChecked = prefs.RequestNotifications;

        void ApplyEnabledState()
        {
            var enabled = masterToggle.IsChecked == true;
            soundToggle.IsEnabled = enabled;
            requestToggle.IsEnabled = enabled;
            soundToggle.Opacity = enabled ? 1 : 0.55;
            requestToggle.Opacity = enabled ? 1 : 0.55;
        }

        async Task SaveAsync()
        {
            prefs.EnableNotifications = masterToggle.IsChecked == true;
            prefs.SoundEnabled = soundToggle.IsChecked == true;
            prefs.RequestNotifications = requestToggle.IsChecked == true;
            ApplyEnabledState();
            await NotificationService.UpdateAsync(prefs);
            _status.Text = "Preferências de notificações atualizadas.";
            _status.Foreground = Ui.Success;
        }

        masterToggle.PropertyChanged += async (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) await SaveAsync(); };
        soundToggle.PropertyChanged += async (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) await SaveAsync(); };
        requestToggle.PropertyChanged += async (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) await SaveAsync(); };

        ApplyEnabledState();

        return Ui.V(8,
            Ui.TextBlock("Controla as notificações deste dispositivo.", 13, Ui.Muted),
            masterToggle,
            soundToggle,
            requestToggle);
    }

    private Control BuildSecurity()
    {
        var securityStatus = Ui.StatusBlock();
        var rotateIdentity = SecurityActionButton("security", "Rodar identity + recovery key", "Regenera a identidade criptográfica e a recovery key.");
        var rotateDevice = SecurityActionButton("lock", "Rodar chaves deste dispositivo", "Regenera apenas as chaves locais deste dispositivo.");
        var export = SecurityActionButton("send", "Criar exportação cifrada", "Cria um backup cifrado.", neutral: true);
        var vanish = SecurityActionButton("trash", "Apagar conta Vanished", "Apaga definitivamente a conta e todos os dados.", danger: true);

        var latestRecoveryKey = new TextBox
        {
            IsVisible = false,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 74,
            Background = Ui.Surface2,
            Foreground = Ui.Text,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14)
        };
        var copyLatestRecoveryKey = Ui.SecondaryButton("Copiar nova recovery key");
        copyLatestRecoveryKey.IsVisible = false;
        copyLatestRecoveryKey.Click += async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip != null && !string.IsNullOrWhiteSpace(latestRecoveryKey.Text))
            {
                await clip.SetTextAsync(latestRecoveryKey.Text);
                securityStatus.Text = "Recovery key copiada.";
                securityStatus.Foreground = Ui.Success;
            }
        };

        rotateIdentity.Click += async (_, _) =>
        {
            string generatedRecoveryKey = string.Empty;
            var result = await ModalService.ShowSensitiveActionAsync(new SensitiveActionRequest
            {
                Title = "Rodar chaves de identidade",
                Description = "Esta operação é irreversível. Confirma a tua identidade para continuar.",
                OnConfirm = async (password, mfa) =>
                {
                    if (!ValidateLocalTotp(password, mfa))
                        return SensitiveActionResult.Fail("Password ou código MFA incorretos.");
                    var (apiResult, recoveryKey) = await ApiService.Auth.RotateIdentityAsync(password);
                    if (!apiResult.success)
                        return SensitiveActionResult.Fail(apiResult.message);
                    generatedRecoveryKey = recoveryKey;
                    return SensitiveActionResult.Ok("Identidade rodada. Copia e guarda a nova recovery key.");
                }
            });
            ApplySensitiveResult(result, securityStatus);
            if (result?.IsSuccess == true && !string.IsNullOrWhiteSpace(generatedRecoveryKey))
            {
                latestRecoveryKey.Text = generatedRecoveryKey;
                latestRecoveryKey.IsVisible = true;
                copyLatestRecoveryKey.IsVisible = true;
            }
        };

        rotateDevice.Click += async (_, _) =>
        {
            var result = await ModalService.ShowSensitiveActionAsync(new SensitiveActionRequest
            {
                Title = "Rodar chaves do dispositivo",
                Description = "As chaves deste dispositivo serão regeneradas.",
                OnConfirm = async (password, mfa) =>
                {
                    if (!ValidateLocalTotp(password, mfa))
                        return SensitiveActionResult.Fail("Password ou código MFA incorretos.");
                    var apiResult = await ApiService.Auth.RotateDeviceKeysAsync(password);
                    return apiResult.success
                        ? SensitiveActionResult.Ok(apiResult.message)
                        : SensitiveActionResult.Fail(apiResult.message);
                }
            });
            ApplySensitiveResult(result, securityStatus);
        };


        export.Click += async (_, _) =>
        {
            var result = await ModalService.ShowSensitiveActionAsync(new SensitiveActionRequest
            {
                Title = "Criar exportação cifrada",
                Description = "A exportação será protegida com as tuas credenciais.",
                OnConfirm = async (password, mfa) =>
                {
                    if (!ValidateLocalTotp(password, mfa))
                        return SensitiveActionResult.Fail("Password ou código MFA incorretos.");
                    var file = await ApiService.Auth.CreateEncryptedExportFileAsync(password, mfa);
                    if (!file.success || file.Bytes == null || file.Bytes.Length == 0)
                        return SensitiveActionResult.Fail(file.message);

                    var top = TopLevel.GetTopLevel(this);
                    if (top == null)
                        return SensitiveActionResult.Fail("Janela principal indisponível.");

                    var target = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Guardar exportação cifrada",
                        SuggestedFileName = string.IsNullOrWhiteSpace(file.FileName) ? $"vanished_export_{DateTime.Now:yyyyMMdd}.vne" : file.FileName,
                        FileTypeChoices = new[]
                        {
                            new FilePickerFileType("Vanished Export") { Patterns = new[] { "*.vne" } },
                            FilePickerFileTypes.All
                        }
                    });
                    if (target == null)
                        return SensitiveActionResult.Fail("Exportação cancelada.");

                    await using var stream = await target.OpenWriteAsync();
                    await stream.WriteAsync(file.Bytes);
                    return SensitiveActionResult.Ok("Exportação guardada com sucesso.");
                }
            });
            ApplySensitiveResult(result, securityStatus);
        };

        vanish.Click += async (_, _) =>
        {
            var result = await ModalService.ShowSensitiveActionAsync(new SensitiveActionRequest
            {
                Title = "Apagar conta Vanished",
                Description = "Confirma password, Vanished PIN e código MFA para apagar a conta.",
                ExtraWarning = "Esta ação é irreversível. Vai apagar por completo a tua conta e todos os dados.",
                ConfirmText = "Apagar conta",
                RequireAccountPin = true,
                IsDangerous = true,
                OnConfirmWithPin = async (password, accountPin, mfa) =>
                {
                    if (!ValidateLocalPassword(password))
                        return SensitiveActionResult.Fail("Password local incorreta.");
                    if (!ValidateLocalTotp(password, mfa))
                        return SensitiveActionResult.Fail("Código MFA incorreto.");

                    var response = await ApiService.Auth.VanishAccountAsync(accountPin);
                    if (!response.success)
                        return SensitiveActionResult.Fail(response.message ?? "Não foi possível apagar a conta.");

                    try { await ApiService.Connection.StopAsync(); } catch { }
                    try { await ApiService.WebSocket.DisconnectAsync(); } catch { }

                    LocalDataCleaner.DeleteAllLocalData();

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ToastService.Show("Conta apagada definitivamente.", "check", ToastType.Success);
                        AppShellWindow.Instance?.ResetToAuthAfterAccountDeletion();
                    });

                    return SensitiveActionResult.Ok("Conta apagada definitivamente.");
                }
            });
            ApplySensitiveResult(result, securityStatus);
        };

        var rotationGroup = Ui.V(8,
            Ui.TextBlock("Chaves e recuperação", 15, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("Gere identity key, recovery key e device keys deste dispositivo.", 12, Ui.Muted),
            rotateIdentity,
            rotateDevice,
            latestRecoveryKey,
            copyLatestRecoveryKey);
        var exportGroup = Ui.V(8,
            Ui.TextBlock("Exportação", 15, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("Cria uma cópia cifrada da tua conta.", 12, Ui.Muted),
            export);
        var dangerGroup = Ui.V(8,
            Ui.TextBlock("Zona de perigo", 15, Ui.Danger, FontWeight.SemiBold),
            Ui.TextBlock("Apagar permanente a conta Vanished e todos os seus dados.", 12, Ui.Muted),
            vanish);

        return new Border
        {
            Padding = new Thickness(16),
            Child = Ui.V(14, rotationGroup, Ui.Divider(), exportGroup, Ui.Divider(), dangerGroup, securityStatus)
        };
    }

    private static Button SecurityActionButton(string icon, string title, string subtitle, bool neutral = false, bool danger = false)
    {
        var btn = danger ? Ui.DangerButton(string.Empty) : (neutral ? Ui.GhostButton(string.Empty) : Ui.SecondaryButton(string.Empty));
        btn.Height = 44;
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        btn.CornerRadius = new CornerRadius(8);
        btn.Background = neutral ? Brushes.Transparent : (danger ? Brush.Parse("#2A131A") : Ui.Surface2);
        btn.BorderBrush = neutral ? Ui.BorderSoft : (danger ? Ui.Danger : Ui.Border);
        btn.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Children =
            {
                Ui.Icon(icon, 16, danger ? Ui.Danger : Ui.Muted),
                Ui.V(1, Ui.TextBlock(title, 13, danger ? Ui.Danger : Ui.Text, FontWeight.SemiBold), Ui.TextBlock(subtitle, 11, Ui.Muted2))
            }
        };
        if (btn.Content is Grid g && g.Children.Count > 1)
            Grid.SetColumn(g.Children[1], 1);
        return btn;
    }

    private static bool ValidateLocalPassword(string password)
    {
        using var key = KeyManager.LoadPrivateKey(SessionContext.Email, password ?? string.Empty);
        return key != null;
    }

    private static bool ValidateLocalTotp(string password, string mfa)
    {
        var secret = LocalTotpManager.Load(SessionContext.Email, password ?? string.Empty);
        return !string.IsNullOrWhiteSpace(secret) && LocalTotpManager.Verify(secret, mfa ?? string.Empty);
    }

    private static void ApplySensitiveResult(SensitiveActionResult? result, TextBlock status)
    {
        if (result == null) return;
        status.Text = result.Message;
        status.Foreground = result.IsSuccess ? Ui.Success : Ui.Danger;
    }

    private Control BuildAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
        var website = Ui.H(8, Ui.Icon("external_link", 14, Ui.Accent), Ui.AuthLink("www.vanished.pt"));
        website.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        website.PointerPressed += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.vanished.pt") { UseShellExecute = true });
            }
            catch { }
        };

        return Ui.V(8,
            Ui.TextBlock($"Vanished {version}", 18, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("Cliente Zero-Knowledge cross-platform.", 13, Ui.Muted),
            website);
    }

    private Button ThemeButton(string icon, string label, VanishedThemeMode mode)
    {
        var active = Ui.CurrentTheme == mode;
        var btn = Ui.SecondaryButton(active ? $"{icon} {label}  ✓" : $"{icon} {label}");
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.Background = active ? Ui.AccentSoft : Ui.Surface2;
        btn.Click += (_, _) =>
        {
            Ui.ApplyTheme(mode);
            _status.Text = "Tema atualizado.";
            _status.Foreground = Ui.Success;
            if (TopLevel.GetTopLevel(this) is AppShellWindow shell)
                shell.RefreshThemeChrome();
            NavigationService.Navigate(new SettingsPage(), false);
        };
        return btn;
    }

    private sealed class AccordionItem
    {
        private readonly Border _contentHost;
        private readonly TextBlock _arrow;
        private readonly Action _toggle;
        public string Title { get; }
        public bool IsOpen { get; private set; }
        public Control Root { get; }

        public AccordionItem(string title, Control content, Action toggle)
        {
            Title = title;
            _toggle = toggle;
            _arrow = Ui.TextBlock("›", 20, Ui.Muted, FontWeight.Bold);
            _contentHost = new Border
            {
                Padding = new Thickness(0, 12, 0, 0),
                Child = content,
                IsVisible = false,
                Opacity = 0,
                MaxHeight = 0,
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { Ui.TextBlock(title, 18, Ui.Text, FontWeight.SemiBold), _arrow }
            };
            Grid.SetColumn(_arrow, 1);

            var header = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = headerGrid
            };
            var headerHost = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                Background = Brushes.Transparent,
                Child = header
            };
            headerHost.PointerEntered += (_, _) => headerHost.Background = Ui.Surface2;
            headerHost.PointerExited += (_, _) => headerHost.Background = Brushes.Transparent;
            header.Click += (_, _) => _toggle();

            Root = Ui.Card(Ui.V(0, headerHost, _contentHost), new Thickness(10), 15);
        }

        public void SetOpen(bool open, bool animate)
        {
            IsOpen = open;
            _arrow.Text = open ? "⌄" : "›";
            if (!animate)
            {
                _contentHost.IsVisible = open;
                _contentHost.Opacity = open ? 1 : 0;
                _contentHost.MaxHeight = open ? 900 : 0;
                return;
            }

            _contentHost.IsVisible = true;
            var startHeight = _contentHost.MaxHeight;
            var targetHeight = open ? 900 : 0;
            var startOpacity = _contentHost.Opacity;
            var targetOpacity = open ? 1.0 : 0.0;
            var steps = 16;
            var step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                step++;
                var t = step / (double)steps;
                t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                _contentHost.MaxHeight = startHeight + (targetHeight - startHeight) * t;
                _contentHost.Opacity = startOpacity + (targetOpacity - startOpacity) * t;
                if (step >= steps)
                {
                    timer.Stop();
                    _contentHost.MaxHeight = targetHeight;
                    _contentHost.Opacity = targetOpacity;
                    if (!open)
                        _contentHost.IsVisible = false;
                }
            };
            timer.Start();
        }
    }
}
