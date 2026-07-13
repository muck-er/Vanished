using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vanished.API.Helpers;
using Vanished.API.Services;
using Vanished.Shell;
using Vanished.UI;

namespace Vanished.Pages;

public sealed class AuthPage : UserControl
{
    private readonly Border _cardHost = new();
    private TextBlock _status = Ui.StatusBlock();
    private PendingLoginSession? _pendingLogin;
    private bool _busy;
    private int _failedLoginAttempts;
    private bool _loginThrottleRunning;
    private TextBlock? _loginRecoveryLink;
    private Button? _loginContinueButton;
    private int _emailResendCooldownVersion;
    private const int RegistrationEmailResendCooldownSeconds = 60;
    private readonly string? _prefilledEmail;

    public AuthPage(string? prefilledEmail = null)
    {
        _prefilledEmail = string.IsNullOrWhiteSpace(prefilledEmail) ? null : prefilledEmail.Trim().ToLowerInvariant();
        Content = BuildRoot();
        var trusted = TrustedSessionStore.Load();
        if (trusted != null && string.IsNullOrWhiteSpace(_prefilledEmail))
            ShowTrustedUnlock(trusted.Email);
        else
            ShowLogin();
    }

    private Control BuildRoot()
    {
        Control logo;
        try
        {
            var uri = new Uri("avares://Vanished/Resources/Logo/LogoWithoutText.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            logo = new Image { Source = new Avalonia.Media.Imaging.Bitmap(stream), Width = 150, Height = 150, Stretch = Stretch.Uniform };
        }
        catch { logo = Ui.Avatar("Vanished", 150); }

        var brand = new Border
        {
            Background = Ui.Bg,
            Padding = new Thickness(56),
            Child = new Grid
            {
                Children =
                {
                    Ui.V(16,
                        logo,
                        Center("Vanished", 28, Ui.Text, FontWeight.Bold),
                        Center("Cliente Zero-Knowledge cross-platform.", 13, Ui.Muted, FontWeight.Normal))
                }
            }
        };
        ((StackPanel)((Grid)brand.Child!).Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
        ((StackPanel)((Grid)brand.Child!).Children[0]).VerticalAlignment = VerticalAlignment.Center;

        var right = new Border
        {
            Background = Ui.Bg,
            Padding = new Thickness(36),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Width = 380,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = _cardHost
                }
            }
        };

        var divider = new Border
        {
            Width = 1,
            Background = Ui.BorderSoft,
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 40)
        };

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,Auto,0.85*"),
            Background = Ui.Bg,
            Children = { brand, divider, right }
        };
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);
        return root;
    }

    private static TextBlock Center(string text, double size, IBrush brush, FontWeight weight)
    {
        var tb = Ui.TextBlock(text, size, brush, weight);
        tb.TextAlignment = TextAlignment.Center;
        tb.HorizontalAlignment = HorizontalAlignment.Center;
        return tb;
    }

    private TextBlock NewStatus()
    {
        _status = Ui.StatusBlock();
        return _status;
    }

    private void SetCard(Control content, double direction = 28)
    {
        var card = Ui.Card(content, new Thickness(30), 22);
        _cardHost.Child = card;
        Ui.SoftSlideIn(card, direction);
    }

    private void ShowLogin()
    {
        _pendingLogin = null;
        AuthFlowState.Clear();

        var email = Ui.TextBox("E-mail");
        if (!string.IsNullOrWhiteSpace(_prefilledEmail))
        {
            email.Text = _prefilledEmail;
            AuthFlowState.PendingEmail = _prefilledEmail;
        }

        _failedLoginAttempts = 0;
        _loginThrottleRunning = false;
        var passwordField = Ui.PasswordFieldWithToggle("Password local", out var password);
        var continueBtn = Ui.PrimaryButton("Continuar");
        _loginContinueButton = continueBtn;
        var recoveryBtn = BuildAuthOptionButton("key", "Recuperar ou importar conta", true);
        var status = NewStatus();
        var recoveryLink = Ui.AuthLink("Não consegues entrar? Recupera a tua conta.");
        recoveryLink.IsVisible = false;
        recoveryLink.Opacity = 0;
        recoveryLink.HorizontalAlignment = HorizontalAlignment.Center;
        recoveryLink.PointerPressed += (_, _) => ShowRecoveryHub(email.Text);
        _loginRecoveryLink = recoveryLink;

        var registerLink = Ui.AuthLink("Registe-se");
        registerLink.PointerPressed += (_, _) => ShowRegister();
        var registerRow = Ui.H(4, Ui.TextBlock("Ainda não tens conta?", 13, Ui.Muted), registerLink);
        registerRow.HorizontalAlignment = HorizontalAlignment.Center;

        BindEnter(email, () => BeginLoginAsync(email.Text, password.Text));
        BindEnter(password, () => BeginLoginAsync(email.Text, password.Text));
        continueBtn.Click += async (_, _) => await BeginLoginAsync(email.Text, password.Text);
        recoveryBtn.Click += (_, _) => ShowRecoveryHub(email.Text);

        SetCard(Ui.V(16,
            Ui.TextBlock("Bem-vindo de volta!", 22, Ui.Text, FontWeight.SemiBold),
            Ui.TextBlock("Inicia sessão com a conta Vanished.", 13, Ui.Muted),
            email,
            passwordField,
            continueBtn,
            BuildAuthDivider(),
            recoveryBtn,
            recoveryLink,
            status,
            registerRow));
        email.Focus();
    }

    private static Control BuildAuthDivider()
    {
        var left = new Border { Height = 1, Background = Ui.BorderSoft, VerticalAlignment = VerticalAlignment.Center };
        var label = Ui.TextBlock("ou", 12, Ui.Muted2);
        label.Margin = new Thickness(12, 0);
        var right = new Border { Height = 1, Background = Ui.BorderSoft, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Margin = new Thickness(0, 2),
            Children = { left, label, right }
        };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(right, 2);
        return grid;
    }

    private static Button BuildAuthOptionButton(string icon, string text, bool muted)
    {
        var btn = muted ? Ui.GhostButton(string.Empty) : Ui.SecondaryButton(string.Empty);
        btn.Height = 44;
        btn.HorizontalAlignment = HorizontalAlignment.Stretch;
        btn.HorizontalContentAlignment = HorizontalAlignment.Center;
        btn.CornerRadius = new CornerRadius(8);
        btn.Content = Ui.H(8, Ui.Icon(icon, 16, muted ? Ui.Muted : Ui.Text), Ui.TextBlock(text, 14, muted ? Ui.Muted : Ui.Text, FontWeight.SemiBold));
        ((StackPanel)btn.Content).HorizontalAlignment = HorizontalAlignment.Center;
        return btn;
    }


    private void ShowRecoveryHub(string? prefilledEmail = null)
    {
        var email = (prefilledEmail ?? AuthFlowState.PendingEmail ?? string.Empty).Trim().ToLowerInvariant();
        var recovery = Ui.SecondaryButton("Adicionar dispositivo com recovery key");
        var import = Ui.SecondaryButton("Importar exportação cifrada .vne");
        var back = Ui.AuthLink("Voltar ao início de sessão");
        var status = NewStatus();

        recovery.Click += (_, _) => ShowRecovery(email);
        import.Click += (_, _) => ShowEncryptedExportImport();
        back.PointerPressed += (_, _) => ShowLogin();

        SetCard(Ui.V(14,
            Ui.TextBlock("Recuperação e importação", 30, Ui.Text, FontWeight.Bold),
            Ui.Divider(),
            recovery,
            Ui.TextBlock("Usa a recovery key para adicionar este dispositivo à tua conta.", 12, Ui.Muted2),
            import,
            Ui.TextBlock("Usa um ficheiro .vne (Vanished Export).", 12, Ui.Muted2),
            status,
            back));
    }


    private void ShowTrustedUnlock(string email)
    {
        AuthFlowState.PendingEmail = email;
        var passwordField = Ui.PasswordFieldWithToggle("Password local", out var password);
        var pinField = Ui.PasswordFieldWithToggle("Vanished PIN", out var pin);
        var unlock = Ui.PrimaryButton("Desbloquear sessão");
        unlock.Classes.Add("loading-button");
        var switchAccount = Ui.AuthLink("Terminar sessão local / usar outra conta");
        var status = NewStatus();

        async Task unlockAsync()
        {
            if (_busy) return;
            if (string.IsNullOrWhiteSpace(password.Text) || string.IsNullOrWhiteSpace(pin.Text))
            {
                ShowStatus("Preenche password local e Vanished PIN.", Ui.Warning);
                return;
            }
            try
            {
                SetBusy(true);
                await RenderBusyAsync();
                ShowStatus("A revalidar este dispositivo sem MFA...", Ui.Muted);
                var result = await ApiService.Auth.UnlockTrustedDeviceAsync(email, password.Text ?? string.Empty, pin.Text ?? string.Empty);
                if (result?.success != true || result.IdentityPrivateKey == null || result.Device == null)
                {
                    ShowStatus(AuthErrorMapper.GenericIdentity, Ui.Danger);
                    return;
                }

                var me = await ApiService.Chat.GetMeAsync();
                if (me?.success != true || me.user == null || me.user.id <= 0)
                {
                    ShowStatus("Sessão validada, mas não foi possível obter o perfil.", Ui.Danger);
                    return;
                }

                SessionContext.Set(me.user.id, email, me.user.username, result.IdentityPrivateKey, me.user.key_version, result.Device.DeviceId, result.Device.SigningPrivateKey, result.Device.EncryptionPrivateKey);
                SessionContext.UpdateProfile(me.user.username, me.user.DisplayName, me.user.bio, me.user.avatar_base64, me.user.avatar_mime);
                LocalUserProfileStore.Save(new LocalUserProfile { Email = email, Username = me.user.username, FullName = me.user.full_name, Bio = me.user.bio, AvatarBase64 = me.user.avatar_base64, AvatarMime = me.user.avatar_mime });
                TrustedSessionStore.Save(email, result.Device.DeviceId);
                AuthFlowState.Clear();
                NavigationService.Navigate(new ChatPage());
            }
            catch (Exception ex)
            {
                ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro ao desbloquear sessão." : ex.Message, Ui.Danger);
            }
            finally { SetBusy(false); }
        }

        BindEnter(password, unlockAsync);
        BindEnter(pin, unlockAsync);
        unlock.Click += async (_, _) => await unlockAsync();
        switchAccount.PointerPressed += (_, _) =>
        {
            TrustedSessionStore.Clear();
            TokenHelper.ClearToken();
            SessionContext.Clear();
            ShowLogin();
        };

        SetCard(Ui.V(14,
            Ui.TextBlock("Desbloquear Vanished", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock($"Sessão local guardada para {MaskEmail(email)}.", 13, Ui.Muted),
            Ui.Divider(),
            passwordField,
            pinField,
            unlock,
            status,
            switchAccount));
        password.Focus();
    }

    private async Task BeginLoginAsync(string? emailRaw, string? passwordRaw)
    {
        if (_busy) return;
        var email = (emailRaw ?? string.Empty).Trim().ToLowerInvariant();
        var password = passwordRaw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowStatus(AuthErrorMapper.GenericCredentials, Ui.Warning);
            return;
        }
        AuthFlowState.PendingEmail = email;
        try
        {
            SetBusy(true);
            await RenderBusyAsync();
            ShowStatus("A validar credenciais locais e dispositivo...", Ui.Muted);
            var begin = await ApiService.Auth.BeginLoginAsync(email, password);
            if (begin?.success != true || begin.Pending == null)
            {
                RegisterLoginFailure();
                return;
            }
            _pendingLogin = begin.Pending;
            ShowLoginMfa();
        }
        catch
        {
            RegisterLoginFailure();
        }
        finally { SetBusy(false); }
    }

    private void RegisterLoginFailure()
    {
        _failedLoginAttempts++;
        ShowStatus(AuthErrorMapper.GenericCredentials, Ui.Danger);

        if (_loginRecoveryLink != null && !_loginRecoveryLink.IsVisible)
        {
            _loginRecoveryLink.IsVisible = true;
            Ui.SoftFadeIn(_loginRecoveryLink);
        }

        if (_failedLoginAttempts >= 5 && !_loginThrottleRunning)
            _ = ThrottleLoginButtonAsync(30);
    }

    private async Task ThrottleLoginButtonAsync(int seconds)
    {
        if (_loginContinueButton == null) return;
        _loginThrottleRunning = true;
        var original = _loginContinueButton.Tag as string ?? "Continuar";
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            _loginContinueButton.IsEnabled = false;
            _loginContinueButton.Content = $"Aguarda {remaining}s";
            await Task.Delay(1000);
        }
        _loginContinueButton.Content = original;
        _loginContinueButton.IsEnabled = true;
        _failedLoginAttempts = 0;
        _loginThrottleRunning = false;
    }


    private void ShowLoginMfa()
    {
        var requiresPin = _pendingLogin?.RequiresPin != false;
        var pinField = Ui.PasswordFieldWithToggle("Vanished PIN", out var pin);
        pinField.IsVisible = requiresPin;
        var code = Ui.TextBox("Código MFA de 6 dígitos");
        var enter = Ui.PrimaryButton("Entrar");
        var back = Ui.AuthLink("Cancelar e voltar ao início de sessão");
        var status = NewStatus();
        BindEnter(pin, () => FinishLoginAsync(code.Text, pin.Text));
        BindEnter(code, () => FinishLoginAsync(code.Text, pin.Text));
        code.TextChanged += async (_, _) => { if (!_busy && (code.Text ?? string.Empty).Trim().Length == 6 && (!requiresPin || !string.IsNullOrWhiteSpace(pin.Text))) await FinishLoginAsync(code.Text, pin.Text); };
        enter.Click += async (_, _) => await FinishLoginAsync(code.Text, pin.Text);
        back.PointerPressed += (_, _) => ShowLogin();
        SetCard(Ui.V(14,
            Ui.TextBlock("Verificação MFA", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock($"Conta: {MaskEmail(_pendingLogin?.Email ?? string.Empty)}", 13, Ui.Muted),
            Ui.TextBlock("Como terminaste sessão ou estás a trocar de conta, confirma Vanished PIN + MFA.", 13, Ui.Muted),
            Ui.Divider(),
            pinField,
            code,
            enter,
            status,
            back));
        if (requiresPin) pin.Focus(); else code.Focus();
    }

    private async Task FinishLoginAsync(string? codeRaw, string? pinRaw)
    {
        if (_busy) return;
        if (_pendingLogin == null)
        {
            ShowLogin();
            ShowStatus("Sessão de login expirada.", Ui.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(codeRaw) || (_pendingLogin.RequiresPin && string.IsNullOrWhiteSpace(pinRaw)))
        {
            ShowStatus(_pendingLogin.RequiresPin ? "Introduz o Vanished PIN e o código MFA." : "Introduz o código MFA.", Ui.Warning);
            return;
        }
        try
        {
            SetBusy(true);
            await RenderBusyAsync();
            var result = await ApiService.Auth.FinishLoginAsync(_pendingLogin, codeRaw, pinRaw);
            if (result?.success != true)
            {
                ShowStatus(AuthErrorMapper.GenericIdentity, Ui.Danger);
                return;
            }
            var me = await ApiService.Chat.GetMeAsync();
            if (me?.success != true || me.user == null || me.user.id <= 0)
            {
                ShowStatus("Não foi possível obter o perfil.", Ui.Danger);
                return;
            }
            SessionContext.Set(me.user.id, _pendingLogin.Email, me.user.username, _pendingLogin.IdentityPrivateKey, me.user.key_version, _pendingLogin.Device.DeviceId, _pendingLogin.Device.SigningPrivateKey, _pendingLogin.Device.EncryptionPrivateKey);
            SessionContext.UpdateProfile(me.user.username, me.user.DisplayName, me.user.bio, me.user.avatar_base64, me.user.avatar_mime);
            LocalUserProfileStore.Save(new LocalUserProfile { Email = _pendingLogin.Email, Username = me.user.username, FullName = me.user.full_name, Bio = me.user.bio, AvatarBase64 = me.user.avatar_base64, AvatarMime = me.user.avatar_mime });
            AuthFlowState.Clear();
            NavigationService.Navigate(new ChatPage());
        }
        catch { ShowStatus(AuthErrorMapper.GenericIdentity, Ui.Danger); }
        finally { SetBusy(false); }
    }

    private void ShowRegister()
    {
        var fullName = Ui.TextBox("Nome");
        var handle = Ui.TextBox("Handle público (@handle)");
        var email = Ui.TextBox("E-mail");
        var passwordField = Ui.PasswordFieldWithToggle("Password local", out var password);
        var confirmField = Ui.PasswordFieldWithToggle("Confirmar password local", out var confirm);
        var accept = new CheckBox { Content = "Entendo que sem a recovery key posso perder acesso à conta.", Foreground = Ui.Muted };
        var acceptPrivacy = new CheckBox { Content = "Li e aceito os Termos e Condições do Vanished.", Foreground = Ui.Muted };
        var privacyLink = Ui.AuthLink("Ver Termos e Condições");
        privacyLink.PointerPressed += (_, _) => TermsAndConditionsWindow.ShowOrActivate(AppShellWindow.Instance);
        var privacyRow = Ui.H(6, Ui.Icon("lock", 14, Ui.Muted2), privacyLink);
        privacyRow.HorizontalAlignment = HorizontalAlignment.Center;

        var handleStatus = Ui.TextBlock("Escolhe o @handle que os outros utilizadores vão usar para te encontrar.", 12, Ui.Muted2);
        var strength = Ui.TextBlock("A password fica só no cliente e serve para cifrar as tuas chaves locais.", 13, Ui.Muted);
        var reqLength = BuildRequirementLine("Mínimo de 8 caracteres");
        var reqCase = BuildRequirementLine("Maiúsculas e minúsculas");
        var reqDigit = BuildRequirementLine("Pelo menos 1 número");
        var reqSymbol = BuildRequirementLine("Pelo menos 1 carácter especial");
        var reqSpaces = BuildRequirementLine("Sem espaços");
        var reqMatch = BuildRequirementLine("Confirmação igual à password");
        var passwordChecklist = Ui.V(4, reqLength, reqCase, reqDigit, reqSymbol, reqSpaces, reqMatch);
        passwordChecklist.Margin = new Thickness(2, -4, 0, 2);

        var next = Ui.PrimaryButton("Validar email");
        var back = Ui.AuthLink("Já tens conta? Inicia sessão");
        var status = NewStatus();

        var handleEditedByUser = false;
        var internalHandleUpdate = false;
        var handleCheckVersion = 0;
        var lastAvailableHandle = string.Empty;

        async Task<bool> ensureHandleAvailableAsync(string username)
        {
            var localError = ValidateHandleForClient(username);
            if (localError != null)
            {
                lastAvailableHandle = string.Empty;
                handleStatus.Text = localError;
                handleStatus.Foreground = Ui.Warning;
                return false;
            }

            if (string.Equals(lastAvailableHandle, username, StringComparison.Ordinal))
                return true;

            handleStatus.Text = $"A verificar @{username}...";
            handleStatus.Foreground = Ui.Muted;
            var result = await ApiService.Auth.CheckUsernameAvailabilityAsync(username);
            if (result?.success == true && result.available)
            {
                lastAvailableHandle = result.username?.Trim() == username ? result.username.Trim() : username;
                handleStatus.Text = $"@{lastAvailableHandle} disponível.";
                handleStatus.Foreground = Ui.Success;
                return true;
            }

            lastAvailableHandle = string.Empty;
            handleStatus.Text = result?.message ?? $"@{username} não está disponível.";
            handleStatus.Foreground = Ui.Danger;
            return false;
        }

        async Task checkHandleAvailabilityDebouncedAsync()
        {
            var version = ++handleCheckVersion;
            var username = NormalizeHandleInput(handle.Text);
            lastAvailableHandle = string.Empty;

            var localError = ValidateHandleForClient(username);
            if (localError != null)
            {
                handleStatus.Text = string.IsNullOrWhiteSpace(username)
                    ? "Escolhe o @handle que os outros utilizadores vão usar para te encontrar."
                    : localError;
                handleStatus.Foreground = string.IsNullOrWhiteSpace(username) ? Ui.Muted2 : Ui.Warning;
                return;
            }

            handleStatus.Text = $"A verificar @{username}...";
            handleStatus.Foreground = Ui.Muted;
            await Task.Delay(450);
            if (version != handleCheckVersion || _busy)
                return;

            var result = await ApiService.Auth.CheckUsernameAvailabilityAsync(username);
            if (version != handleCheckVersion)
                return;

            if (result?.success == true && result.available)
            {
                lastAvailableHandle = result.username?.Trim() == username ? result.username.Trim() : username;
                handleStatus.Text = $"@{lastAvailableHandle} disponível.";
                handleStatus.Foreground = Ui.Success;
            }
            else
            {
                lastAvailableHandle = string.Empty;
                handleStatus.Text = result?.message ?? $"@{username} não está disponível.";
                handleStatus.Foreground = Ui.Danger;
            }
        }

        void updateHandleFromNameIfNeeded()
        {
            if (handleEditedByUser)
                return;

            var preview = BuildUsernamePreview(fullName.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(preview))
                return;

            internalHandleUpdate = true;
            handle.Text = preview;
            internalHandleUpdate = false;
            _ = checkHandleAvailabilityDebouncedAsync();
        }

        void updatePasswordIndicators()
        {
            var p = password.Text ?? string.Empty;
            var c = confirm.Text ?? string.Empty;
            var r = PasswordStrengthHelper.Evaluate(p);
            var hasPassword = !string.IsNullOrWhiteSpace(p);
            var passwordsMatch = hasPassword && p == c;

            SetRequirementLine(reqLength, r.HasMinLength, "Mínimo de 8 caracteres", hasPassword);
            SetRequirementLine(reqCase, r.HasMixedCase, "Maiúsculas e minúsculas", hasPassword);
            SetRequirementLine(reqDigit, r.HasDigit, "Pelo menos 1 número", hasPassword);
            SetRequirementLine(reqSymbol, r.HasSymbol, "Pelo menos 1 carácter especial", hasPassword);
            SetRequirementLine(reqSpaces, hasPassword && r.HasNoSpaces, "Sem espaços", hasPassword && !r.HasNoSpaces);
            SetRequirementLine(reqMatch, passwordsMatch, "Confirmação igual à password", !string.IsNullOrWhiteSpace(c));

            if (!hasPassword)
            {
                strength.Text = "A password fica só no cliente e serve para cifrar as tuas chaves locais.";
                strength.Foreground = Ui.Muted;
                return;
            }

            strength.Text = $"Força: {r.Label} ({r.Score}/100).";
            strength.Foreground = r.IsAcceptable && passwordsMatch ? Ui.Success : Ui.Muted;
        }

        async Task submitRegisterAsync()
        {
            var f = (fullName.Text ?? string.Empty).Trim();
            var h = NormalizeHandleInput(handle.Text);
            var e = (email.Text ?? string.Empty).Trim().ToLowerInvariant();
            var p = password.Text ?? string.Empty;
            var c = confirm.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(f) || string.IsNullOrWhiteSpace(h) || string.IsNullOrWhiteSpace(e) || string.IsNullOrWhiteSpace(p))
            {
                ShowStatus("Preenche nome, @handle, e-mail e password.", Ui.Warning);
                return;
            }

            var handleError = ValidateHandleForClient(h);
            if (handleError != null)
            {
                ShowStatus(handleError, Ui.Warning);
                handle.Focus();
                return;
            }

            var r = PasswordStrengthHelper.Evaluate(p);
            if (!r.IsAcceptable)
            {
                ShowStatus("Password fraca. Cumpre todos os indicadores: 8+ caracteres, maiúsculas, minúsculas, número, símbolo e sem espaços.", Ui.Warning);
                return;
            }
            if (p != c) { ShowStatus("As passwords não coincidem.", Ui.Danger); return; }
            if (!await ensureHandleAvailableAsync(h))
            {
                ShowStatus($"O @{h} não está disponível. Insere outro @handle.", Ui.Danger);
                handle.Focus();
                return;
            }
            if (acceptPrivacy.IsChecked != true) { ShowStatus("Confirma que leste e aceitas os Termos e Condições.", Ui.Warning); return; }
            if (accept.IsChecked != true) { ShowStatus("Confirma que entendeste a responsabilidade da recovery key.", Ui.Warning); return; }
            await StartRegistrationEmailAndShowCodeAsync(f, h, e, p);
        }

        fullName.TextChanged += (_, _) => updateHandleFromNameIfNeeded();
        handle.TextChanged += (_, _) =>
        {
            if (!internalHandleUpdate)
                handleEditedByUser = true;
            _ = checkHandleAvailabilityDebouncedAsync();
        };
        password.TextChanged += (_, _) => updatePasswordIndicators();
        confirm.TextChanged += (_, _) => updatePasswordIndicators();
        BindEnter(fullName, submitRegisterAsync);
        BindEnter(handle, submitRegisterAsync);
        BindEnter(email, submitRegisterAsync);
        BindEnter(password, submitRegisterAsync);
        BindEnter(confirm, submitRegisterAsync);
        next.Click += async (_, _) => await submitRegisterAsync();
        back.PointerPressed += (_, _) => ShowLogin();

        SetCard(Ui.V(14,
            Ui.TextBlock("Criar conta", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("A conta só pode ser criada depois de validares o email.", 13, Ui.Muted),
            Ui.Divider(),
            fullName,
            handle,
            handleStatus,
            email,
            passwordField,
            confirmField,
            strength,
            passwordChecklist,
            acceptPrivacy,
            privacyRow,
            accept,
            next,
            status,
            back));
        updatePasswordIndicators();
        fullName.Focus();
    }


    private async Task StartRegistrationEmailAndShowCodeAsync(string fullName, string username, string email, string password)
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            await RenderBusyAsync();
            var result = await ApiService.Auth.StartRegistrationEmailVerificationAsync(email);
            var cooldownSeconds = Math.Max(result?.resend_available_in_seconds ?? 0, result?.cooldown_seconds ?? 0);

            if (result?.success == true)
            {
                ShowRegisterEmailCode(fullName, username, email, password, cooldownSeconds > 0 ? cooldownSeconds : RegistrationEmailResendCooldownSeconds);
                ShowStatus(result.message ?? "Enviámos um código de validação para o teu email.", Ui.Success);
                return;
            }

            if (cooldownSeconds > 0)
            {
                ShowRegisterEmailCode(fullName, username, email, password, cooldownSeconds);
                ShowStatus(result?.message ?? $"Aguarda {cooldownSeconds}s antes de reenviar o código.", Ui.Warning);
                return;
            }

            ShowStatus(result?.message ?? "Não foi possível enviar o código de validação.", Ui.Danger);
        }
        catch (Exception ex)
        {
            ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro ao enviar código de validação." : ex.Message, Ui.Danger);
        }
        finally
        {
            SetBusy(false);
        }
    }


    private void ShowRegisterEmailCode(string fullName, string username, string email, string password, int initialCooldownSeconds = RegistrationEmailResendCooldownSeconds)
    {
        var code = Ui.TextBox("Código de 6 dígitos");
        var verify = Ui.PrimaryButton("Validar email");
        var resend = Ui.AuthLink("Reenviar código");
        var back = Ui.AuthLink("Voltar");
        var status = NewStatus();

        async Task verifyAsync()
        {
            if (_busy) return;
            var normalizedCode = NormalizeRegistrationCode(code.Text);
            if (normalizedCode.Length != 6)
            {
                ShowStatus("Introduz o código de 6 dígitos enviado para o teu email.", Ui.Warning);
                return;
            }

            try
            {
                SetBusy(true);
                await RenderBusyAsync();
                var result = await ApiService.Auth.VerifyRegistrationEmailAsync(email, normalizedCode);
                if (result?.success == true)
                {
                    var verificationToken = (result.email_verification_token ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(verificationToken))
                    {
                        ShowStatus("O servidor validou o código, mas não devolveu o token de registo. Atualiza o backend e tenta novamente.", Ui.Danger);
                        return;
                    }

                    ShowRegisterPin(fullName, username, email, password, verificationToken);
                    return;
                }

                ShowStatus(result?.message ?? "Código de validação inválido ou expirado.", Ui.Danger);
            }
            catch (Exception ex)
            {
                ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro ao validar email." : ex.Message, Ui.Danger);
            }
            finally
            {
                SetBusy(false);
            }
        }

        BindEnter(code, verifyAsync);
        code.TextChanged += async (_, _) =>
        {
            if (!_busy && NormalizeRegistrationCode(code.Text).Length == 6)
                await verifyAsync();
        };
        verify.Click += async (_, _) => await verifyAsync();
        resend.PointerPressed += async (_, _) =>
        {
            if (_busy) return;
            if (!resend.IsEnabled)
            {
                ShowStatus("Ainda tens de aguardar pelo fim do cooldown para reenviar o código.", Ui.Warning);
                return;
            }

            await StartRegistrationEmailAndShowCodeAsync(fullName, username, email, password);
        };
        back.PointerPressed += (_, _) => ShowRegister();

        SetCard(Ui.V(14,
            Ui.TextBlock("Valida o teu email", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock($"Enviámos um código para {email}. Introduz o código para continuares.", 13, Ui.Muted),
            Ui.TextBlock("Emails de teste ou endereços que não recebam o código não podem criar conta.", 12, Ui.Muted2),
            Ui.Divider(),
            code,
            verify,
            status,
            resend,
            back));

        StartRegistrationResendCooldown(resend, initialCooldownSeconds);
        code.Focus();
    }


    private void StartRegistrationResendCooldown(TextBlock resend, int seconds)
    {
        var version = ++_emailResendCooldownVersion;
        _ = RunRegistrationResendCooldownAsync(resend, Math.Max(0, seconds), version);
    }

    private async Task RunRegistrationResendCooldownAsync(TextBlock resend, int seconds, int version)
    {
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            if (version != _emailResendCooldownVersion)
                return;

            resend.IsEnabled = false;
            resend.Text = $"Reenviar código disponível em {remaining}s";
            resend.Foreground = Ui.Muted2;
            resend.TextDecorations = null;
            resend.Cursor = new Cursor(StandardCursorType.Arrow);

            await Task.Delay(1000);
        }

        if (version != _emailResendCooldownVersion)
            return;

        resend.IsEnabled = true;
        resend.Text = "Reenviar código";
        resend.Foreground = Ui.Accent;
        resend.TextDecorations = TextDecorations.Underline;
        resend.Cursor = new Cursor(StandardCursorType.Hand);
    }


    private void ShowRegisterPin(string fullName, string username, string email, string password, string emailVerificationToken)
    {
        var pinField = Ui.PasswordFieldWithToggle("Vanished PIN", out var pin);
        var confirmField = Ui.PasswordFieldWithToggle("Confirmar Vanished PIN", out var confirm);
        var next = Ui.PrimaryButton("Continuar para MFA");
        var back = Ui.AuthLink("Voltar");
        var status = NewStatus();

        void submit()
        {
            var p = pin.Text ?? string.Empty;
            var c = confirm.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(c))
            {
                ShowStatus("Preenche e confirma o Vanished PIN.", Ui.Warning);
                return;
            }
            if (p.Length < 6 || p.Length > 64 || p.Any(char.IsWhiteSpace))
            {
                ShowStatus("O Vanished PIN deve ter 6 a 64 caracteres e não pode conter espaços.", Ui.Warning);
                return;
            }
            if (p != c)
            {
                ShowStatus("Os PINs não coincidem.", Ui.Danger);
                return;
            }
            ShowRegisterMfa(fullName, username, email, password, p, emailVerificationToken);
        }

        BindEnter(pin, () => { submit(); return Task.CompletedTask; });
        BindEnter(confirm, () => { submit(); return Task.CompletedTask; });
        next.Click += (_, _) => submit();
        back.PointerPressed += (_, _) => ShowRegisterEmailCode(fullName, username, email, password, 0);

        SetCard(Ui.V(14,
            Ui.TextBlock("Criar Vanished PIN", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Este PIN protege a conta em dispositivos já autorizados.", 13, Ui.Muted),
            Ui.Divider(),
            pinField,
            confirmField,
            Ui.TextBlock("Usa no mínimo 6 caracteres. Pode ser numérico ou alfanumérico.", 12, Ui.Muted2),
            next,
            status,
            back));
        pin.Focus();
    }


    private void ShowRegisterMfa(string fullName, string username, string email, string password, string accountPin, string emailVerificationToken)
    {
        var secret = LocalTotpManager.GenerateSecret();
        var otpUri = LocalTotpManager.BuildOtpAuthUri(email, secret);
        var qr = TotpQrCodeHelper.CreateImage(otpUri, 220);
        var code = Ui.TextBox("Código atual de 6 dígitos");
        var create = Ui.PrimaryButton("Criar conta");
        var back = Ui.AuthLink("Voltar ao PIN");
        var status = NewStatus();
        var manual = new TextBox { Text = LocalTotpManager.FormatSecret(secret), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, Background = Ui.Surface2, Foreground = Ui.Muted, BorderBrush = Ui.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14) };
        var qrBox = new Border { Width = 246, Height = 246, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.White, CornerRadius = new CornerRadius(20), Padding = new Thickness(13), Child = qr };
        BindEnter(code, () => VerifyMfaAndCreateAsync(fullName, username, email, password, accountPin, emailVerificationToken, secret, code.Text));
        code.TextChanged += async (_, _) => { if (!_busy && (code.Text ?? string.Empty).Trim().Length == 6) await VerifyMfaAndCreateAsync(fullName, username, email, password, accountPin, emailVerificationToken, secret, code.Text); };
        create.Click += async (_, _) => await VerifyMfaAndCreateAsync(fullName, username, email, password, accountPin, emailVerificationToken, secret, code.Text);
        back.PointerPressed += (_, _) => ShowRegisterPin(fullName, username, email, password, emailVerificationToken);
        SetCard(Ui.V(14,
            Ui.TextBlock("MFA obrigatório", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Lê o QR code com uma app autenticadora.", 13, Ui.Muted),
            qrBox,
            Ui.TextBlock("Código manual, só se o QR falhar:", 12, Ui.Muted),
            manual,
            code,
            create,
            status,
            back));
        code.Focus();
    }

    private async Task VerifyMfaAndCreateAsync(string fullName, string username, string email, string password, string accountPin, string emailVerificationToken, string secret, string? code)
    {
        if (_busy) return;
        if (!LocalTotpManager.Verify(secret, code ?? string.Empty)) { ShowStatus("Código TOTP inválido.", Ui.Danger); return; }
        try
        {
            SetBusy(true);
            await RenderBusyAsync();
            LocalTotpManager.Save(email, password, secret);
            var (result, recoveryKey) = await ApiService.Auth.RegisterAsync(fullName, username, email, password, accountPin, emailVerificationToken, localMfaEnabled: true);
            if (result?.success == true)
            {
                TokenHelper.ClearToken();
                SessionContext.Clear();
                ShowRecoveryKey(recoveryKey);
                return;
            }
            LocalTotpManager.Disable(email);
            ShowStatus(result?.message ?? "Não foi possível criar a conta.", Ui.Danger);
        }
        catch (Exception ex) { LocalTotpManager.Disable(email); ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro ao criar conta." : ex.Message, Ui.Danger); }
        finally { SetBusy(false); }
    }


    private static string NormalizeRegistrationCode(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
    }


    private void ShowRecovery(string? prefilledEmail = null)
    {
        var inheritedEmail = string.IsNullOrWhiteSpace(prefilledEmail) ? AuthFlowState.PendingEmail : prefilledEmail;
        var lockedEmail = !string.IsNullOrWhiteSpace(inheritedEmail);

        var email = Ui.TextBox("E-mail");
        email.Text = (inheritedEmail ?? string.Empty).Trim().ToLowerInvariant();
        email.IsReadOnly = lockedEmail;
        email.Background = Ui.Surface2;
        email.Padding = lockedEmail ? new Thickness(14, 9, 40, 9) : new Thickness(14, 9);
        if (lockedEmail)
            ToolTip.SetTip(email, "Email herdado do início de sessão");

        var lockIcon = Ui.Icon("lock", 14, Ui.Muted2);
        lockIcon.HorizontalAlignment = HorizontalAlignment.Right;
        lockIcon.VerticalAlignment = VerticalAlignment.Center;
        lockIcon.Margin = new Thickness(0, 0, 14, 0);
        lockIcon.IsVisible = lockedEmail;

        var emailHost = new Grid { Children = { email, lockIcon } };

        var useDifferent = Ui.AuthLink("Usar um email diferente");
        useDifferent.IsVisible = lockedEmail;
        useDifferent.PointerPressed += (_, _) =>
        {
            email.IsReadOnly = false;
            email.Background = Ui.Surface2;
            email.Padding = new Thickness(14, 9);
            lockIcon.IsVisible = false;
            useDifferent.IsVisible = false;
            ToolTip.SetTip(email, null);
            email.Focus();
        };

        var recovery = Ui.TextBox("Recovery key");
        recovery.AcceptsReturn = true;
        recovery.MinHeight = 92;
        var passField = Ui.PasswordFieldWithToggle("Nova password local deste dispositivo", out var pass);
        var add = Ui.PrimaryButton("Adicionar dispositivo");
        var importLink = Ui.AuthLink("Importar .vne (Vanished Export)");
        var back = Ui.AuthLink("Voltar às opções");
        var status = NewStatus();

        BindEnter(email, () => RecoverDeviceAsync(email.Text, recovery.Text, pass.Text));
        BindEnter(recovery, () => RecoverDeviceAsync(email.Text, recovery.Text, pass.Text));
        BindEnter(pass, () => RecoverDeviceAsync(email.Text, recovery.Text, pass.Text));
        add.Click += async (_, _) => await RecoverDeviceAsync(email.Text, recovery.Text, pass.Text);
        importLink.PointerPressed += (_, _) => ShowEncryptedExportImport();
        back.PointerPressed += (_, _) => ShowRecoveryHub(email.Text);

        SetCard(Ui.V(14,
            Ui.TextBlock("Adicionar dispositivo", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Usa a recovery key para recuperar a conta.", 13, Ui.Muted),
            Ui.Divider(),
            emailHost,
            useDifferent,
            recovery,
            passField,
            add,
            status,
            Ui.H(4, Ui.TextBlock("Tens uma exportação local?", 13, Ui.Muted), importLink),
            back));

        if (lockedEmail) recovery.Focus();
        else email.Focus();
    }

    private void ShowEncryptedExportImport()
    {
        var importPasswordField = Ui.PasswordFieldWithToggle("Password da exportação .vne", out var importPassword);
        var importMfa = Ui.TextBox("Código MFA da conta");
        importMfa.MaxLength = 6;
        var importBtn = Ui.PrimaryButton("Selecionar ficheiro .vne e importar");
        importBtn.Classes.Add("loading-button");
        var recoveryLink = Ui.AuthLink("Usar recovery key");
        var back = Ui.AuthLink("Voltar às opções");
        var status = NewStatus();

        BindEnter(importPassword, () => ImportExportAsync(importPassword.Text, importMfa.Text));
        BindEnter(importMfa, () => ImportExportAsync(importPassword.Text, importMfa.Text));
        importMfa.TextChanged += async (_, _) =>
        {
            if (!_busy && (importMfa.Text ?? string.Empty).Trim().Length == 6 && !string.IsNullOrWhiteSpace(importPassword.Text))
                await ImportExportAsync(importPassword.Text, importMfa.Text);
        };
        importBtn.Click += async (_, _) => await ImportExportAsync(importPassword.Text, importMfa.Text);
        recoveryLink.PointerPressed += (_, _) => ShowRecovery();
        back.PointerPressed += (_, _) => ShowRecoveryHub();

        SetCard(Ui.V(14,
            Ui.TextBlock("Importar exportação cifrada", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Valida um ficheiro .vne (Vanished Export) para importar a conta.", 13, Ui.Muted),
            Ui.Divider(),
            importPasswordField,
            importMfa,
            importBtn,
            status,
            Ui.H(4, Ui.TextBlock("Não tens uma exportação?", 13, Ui.Muted), recoveryLink),
            back));
        importPassword.Focus();
    }


    private async Task ImportExportAsync(string? passwordRaw, string? mfaRaw)
    {
        if (_busy) return;
        var password = passwordRaw ?? string.Empty;
        var mfa = mfaRaw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mfa))
        {
            ShowStatus("Preenche a password da exportação e o código MFA.", Ui.Warning);
            return;
        }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                ShowStatus("Janela principal indisponível.", Ui.Danger);
                return;
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecionar ficheiro de exportação",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Vanished Export") { Patterns = new[] { "*.vne" } },
                    FilePickerFileTypes.All
                }
            });
            var file = files.FirstOrDefault();
            if (file == null) return;

            SetBusy(true);
            await RenderBusyAsync();
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var result = await ApiService.Auth.ImportEncryptedExportAsync(ms.ToArray(), file.Name, password, mfa);
            if (result.success)
                ShowStatus(result.message, Ui.Success);
            else
                ShowStatus(result.message, Ui.Danger);
        }
        catch (Exception ex)
        {
            ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro ao importar exportação." : ex.Message, Ui.Danger);
        }
        finally { SetBusy(false); }
    }

    private async Task RecoverDeviceAsync(string? emailRaw, string? recoveryRaw, string? passRaw)
    {
        if (_busy) return;
        var email = (emailRaw ?? string.Empty).Trim().ToLowerInvariant();
        var recovery = (recoveryRaw ?? string.Empty).Trim();
        var pass = passRaw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(recovery) || string.IsNullOrWhiteSpace(pass)) { ShowStatus("Preenche e-mail, recovery key e nova password local.", Ui.Warning); return; }
        try
        {
            SetBusy(true);
            await RenderBusyAsync();
            var result = await ApiService.Auth.RecoverDeviceAsync(email, recovery, pass);
            if (result?.success != true) { ShowStatus(result?.message ?? "Recovery falhou.", Ui.Danger); return; }
            ShowRecoveryKey(
                result.recovery_key,
                "Recovery key antiga revogada. Copia a nova chave antes de configurar o MFA local.",
                "Continuar para MFA",
                () => ShowLocalMfaSetup(email, pass));
        }
        catch (Exception ex) { ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Erro de recovery." : ex.Message, Ui.Danger); }
        finally { SetBusy(false); }
    }

    private void ShowLocalMfaSetup(string email, string password)
    {
        var secret = LocalTotpManager.GenerateSecret();
        var qr = TotpQrCodeHelper.CreateImage(LocalTotpManager.BuildOtpAuthUri(email, secret), 220);
        var code = Ui.TextBox("Código atual de 6 dígitos");
        var finish = Ui.PrimaryButton("Ativar MFA neste dispositivo");
        var qrBox = new Border { Width = 246, Height = 246, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.White, CornerRadius = new CornerRadius(20), Padding = new Thickness(13), Child = qr };
        var status = NewStatus();
        void finishLocalMfa()
        {
            if (!LocalTotpManager.Verify(secret, code.Text ?? string.Empty)) { ShowStatus("Código TOTP inválido.", Ui.Danger); return; }
            LocalTotpManager.Save(email, password, secret);
            AuthFlowState.Clear();
            ShowLogin();
            ShowStatus("Dispositivo adicionado. Já podes iniciar sessão.", Ui.Success);
        }
        BindEnter(code, () => { finishLocalMfa(); return Task.CompletedTask; });
        code.TextChanged += (_, _) => { if ((code.Text ?? string.Empty).Trim().Length == 6) finishLocalMfa(); };
        finish.Click += (_, _) => finishLocalMfa();
        SetCard(Ui.V(14,
            Ui.TextBlock("Configurar MFA local", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock("Este dispositivo precisa de MFA antes de poder iniciar sessão.", 13, Ui.Muted),
            qrBox,
            code,
            finish,
            status));
    }

    private void ShowRecoveryKey(string recoveryKey, string? description = null, string primaryActionText = "Ir para login", Action? primaryAction = null)
    {
        var keyBox = new TextBox { Text = recoveryKey, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74, Background = Ui.Surface2, Foreground = Ui.Text, BorderBrush = Ui.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14) };
        var copy = Ui.SecondaryButton("Copiar recovery key");
        var primary = Ui.PrimaryButton(primaryActionText);
        var status = NewStatus();
        copy.Click += async (_, _) => { var clip = TopLevel.GetTopLevel(this)?.Clipboard; if (clip != null) await clip.SetTextAsync(recoveryKey); ShowStatus("Recovery key copiada.", Ui.Success); };
        primary.Click += (_, _) => (primaryAction ?? ShowLogin).Invoke();
        SetCard(Ui.V(14,
            Ui.TextBlock("Guarda a recovery key", 30, Ui.Text, FontWeight.Bold),
            Ui.TextBlock(description ?? "Só aparece uma vez. Sem ela não consegues recuperar a conta.", 13, Ui.Warning),
            keyBox,
            copy,
            primary,
            status));
    }

    private static void BindEnter(Control input, Func<Task> submit)
    {
        input.AddHandler(
            InputElement.KeyDownEvent,
            async (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    e.Handled = true;
                    await submit();
                }
            },
            routes: RoutingStrategies.Tunnel,
            handledEventsToo: false);
    }

    private static async Task RenderBusyAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(35);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SetEnabledRecursive(_cardHost, !busy);
    }

    private static void SetEnabledRecursive(Control control, bool enabled)
    {
        if (control is Button b)
        {
            if (b.Classes.Contains("loading-button")) Ui.SetButtonLoading(b, !enabled);
            else b.IsEnabled = enabled;
        }
        if (control is Panel p)
            foreach (var child in p.Children)
                SetEnabledRecursive(child, enabled);
        if (control is ContentControl c && c.Content is Control cc)
            SetEnabledRecursive(cc, enabled);
        if (control is Decorator d && d.Child is Control dc)
            SetEnabledRecursive(dc, enabled);
        if (control is TextBox tb)
            tb.IsEnabled = enabled;
    }

    private void ShowStatus(string message, IBrush brush)
    {
        _status.Text = message;
        _status.Foreground = brush;
    }


    private static TextBlock BuildRequirementLine(string text)
    {
        return Ui.TextBlock($"• {text}", 12, Ui.Muted2);
    }

    private static void SetRequirementLine(TextBlock block, bool ok, string text, bool active)
    {
        block.Text = $"{(ok ? "✓" : "•")} {text}";
        block.Foreground = ok ? Ui.Success : active ? Ui.Danger : Ui.Muted2;
    }

    private static string NormalizeHandleInput(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text.StartsWith("@", StringComparison.Ordinal))
            text = text[1..];
        return text;
    }

    private static string? ValidateHandleForClient(string username)
    {
        username = NormalizeHandleInput(username);
        if (string.IsNullOrWhiteSpace(username))
            return "Escolhe um @handle.";
        if (username.Length < 3)
            return "O @handle tem de ter pelo menos 3 caracteres.";
        if (username.Length > 32)
            return "O @handle não pode ter mais de 32 caracteres.";
        if (username[0] is '_' or '.' or '-' || username[^1] is '_' or '.' or '-')
            return "O @handle não pode começar nem acabar com ponto, hífen ou underscore.";
        if (username.Any(ch => !((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '.' || ch == '-')))
            return "O @handle só pode ter letras sem acentos, números, ponto, hífen ou underscore.";
        return null;
    }

    private static string BuildUsernamePreview(string displayName)
    {
        var chars = (displayName ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') ? ch : (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.' ? '_' : '\0'))
            .Where(ch => ch != '\0')
            .ToArray();
        var raw = new string(chars);
        while (raw.Contains("__")) raw = raw.Replace("__", "_");
        return new string(raw.Trim('_').Take(32).ToArray());
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : email[0] + new string('•', Math.Min(6, at - 1)) + email[at..];
    }
}
