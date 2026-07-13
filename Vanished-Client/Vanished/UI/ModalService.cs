using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using Vanished.Shell;

namespace Vanished.UI;

public sealed class SensitiveActionRequest
{
    public string Title { get; set; } = "Confirmar ação sensível";
    public string Description { get; set; } = "Confirma a tua identidade para continuar.";
    public string ExtraWarning { get; set; } = string.Empty;
    public string ConfirmText { get; set; } = "Confirmar";
    public bool RequireAccountPin { get; set; }
    public bool RequireLocalPassword { get; set; } = true;
    public bool IsDangerous { get; set; }
    public Func<string, string, Task<SensitiveActionResult>> OnConfirm { get; set; } = (_, _) => Task.FromResult(SensitiveActionResult.Fail("Ação não configurada."));
    public Func<string, string, string, Task<SensitiveActionResult>>? OnConfirmWithPin { get; set; }
}

public sealed class SensitiveActionResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;

    public static SensitiveActionResult Ok(string message = "Ação concluída.") => new() { IsSuccess = true, Message = message };
    public static SensitiveActionResult Fail(string message) => new() { IsSuccess = false, Message = string.IsNullOrWhiteSpace(message) ? "Password ou código MFA incorretos." : message };
}

public static class ModalService
{
    public static Task<SensitiveActionResult?> ShowSensitiveActionAsync(SensitiveActionRequest request)
    {
        var tcs = new TaskCompletionSource<SensitiveActionResult?>();
        var shell = AppShellWindow.Instance;
        if (shell == null)
        {
            tcs.SetResult(SensitiveActionResult.Fail("Janela principal indisponível."));
            return tcs.Task;
        }

        var passwordField = Ui.PasswordFieldWithToggle("Password local", out var password);
        passwordField.IsVisible = request.RequireLocalPassword;
        var accountPinField = Ui.PasswordFieldWithToggle("Vanished PIN", out var accountPin);
        accountPinField.IsVisible = request.RequireAccountPin;
        var mfa = Ui.TextBox("Código MFA");
        mfa.MaxLength = 6;
        var error = Ui.StatusBlock();
        var cancel = Ui.GhostButton("Cancelar");
        var confirm = request.IsDangerous ? Ui.DangerButton(request.ConfirmText) : Ui.PrimaryButton(request.ConfirmText);
        var cardScale = new ScaleTransform(0.92, 0.92);
        var cardTranslate = new TranslateTransform(0, 12);
        Border? overlay = null;
        var transforms = new TransformGroup();
        transforms.Children.Add(cardScale);
        transforms.Children.Add(cardTranslate);

        async Task SubmitAsync()
        {
            if (!confirm.IsEnabled) return;
            var requiresPassword = request.RequireLocalPassword;
            var missingPassword = requiresPassword && string.IsNullOrWhiteSpace(password.Text);
            var missingPin = request.RequireAccountPin && string.IsNullOrWhiteSpace(accountPin.Text);
            var missingMfa = string.IsNullOrWhiteSpace(mfa.Text);
            if (missingPassword || missingPin || missingMfa)
            {
                error.Text = BuildMissingFieldsMessage(requiresPassword, request.RequireAccountPin);
                error.Foreground = Ui.Warning;
                await ShakeAsync(cardTranslate);
                return;
            }

            Ui.SetButtonLoading(confirm, true);
            password.IsEnabled = false;
            accountPin.IsEnabled = false;
            mfa.IsEnabled = false;
            try
            {
                var passwordText = request.RequireLocalPassword ? password.Text ?? string.Empty : string.Empty;
                var result = request.RequireAccountPin && request.OnConfirmWithPin != null
                    ? await request.OnConfirmWithPin(passwordText, accountPin.Text ?? string.Empty, mfa.Text ?? string.Empty)
                    : await request.OnConfirm(passwordText, mfa.Text ?? string.Empty);
                if (result.IsSuccess)
                {
                    await AnimateOutAsync(cardScale, cardTranslate, overlay!);
                    shell.ClearOverlay();
                    tcs.TrySetResult(result);
                    return;
                }

                error.Text = result.Message;
                error.Foreground = Ui.Danger;
                await ShakeAsync(cardTranslate);
            }
            catch (Exception ex)
            {
                error.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Não foi possível executar a ação." : ex.Message;
                error.Foreground = Ui.Danger;
                await ShakeAsync(cardTranslate);
            }
            finally
            {
                Ui.SetButtonLoading(confirm, false);
                password.IsEnabled = true;
                accountPin.IsEnabled = true;
                mfa.IsEnabled = true;
                FocusFirstRequiredField(request, password, accountPin, mfa);
            }
        }

        cancel.Click += async (_, _) =>
        {
            await AnimateOutAsync(cardScale, cardTranslate, overlay!);
            shell.ClearOverlay();
            tcs.TrySetResult(null);
        };
        confirm.Click += async (_, _) => await SubmitAsync();
        mfa.TextChanged += async (_, _) =>
        {
            var digits = OnlyDigits(mfa.Text ?? string.Empty);
            if (digits != mfa.Text) mfa.Text = digits;
            if (digits.Length == 6) await SubmitAsync();
        };
        mfa.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SubmitAsync(); } };
        accountPin.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; mfa.Focus(); } };
        password.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (request.RequireAccountPin) accountPin.Focus(); else mfa.Focus(); } };

        var providerHint = Ui.TextBlock(BuildProviderHint(request), 11, Ui.Muted2);
        providerHint.Margin = new Thickness(0, -2, 0, 0);

        var card = new Border
        {
            Width = 400,
            MaxWidth = 440,
            Background = Ui.Surface,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = transforms,
            Child = Ui.V(14,
                Ui.H(10, Ui.Icon(request.IsDangerous ? "trash" : "lock", 20, request.IsDangerous ? Ui.Danger : Ui.Accent), Ui.TextBlock(request.Title, 22, Ui.Text, FontWeight.Bold)),
                Ui.TextBlock(request.Description, 13, Ui.Muted),
                string.IsNullOrWhiteSpace(request.ExtraWarning) ? new Border { IsVisible = false } : Ui.TextBlock(request.ExtraWarning, 13, Ui.Danger, FontWeight.SemiBold),
                passwordField,
                accountPinField,
                mfa,
                providerHint,
                error,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children = { new Border(), cancel, confirm }
                })
        };
        if (card.Child is StackPanel stack && stack.Children[stack.Children.Count - 1] is Grid buttons)
        {
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(confirm, 2);
        }

        overlay = new Border
        {
            Background = Brush.Parse("#99000000"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = card,
            Focusable = true
        };
        overlay.KeyDown += async (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; await AnimateOutAsync(cardScale, cardTranslate, overlay!); shell.ClearOverlay(); tcs.TrySetResult(null); } };

        shell.ShowOverlay(overlay);
        _ = AnimateInAsync(cardScale, cardTranslate, overlay);
        Dispatcher.UIThread.Post(() => FocusFirstRequiredField(request, password, accountPin, mfa));
        return tcs.Task;

        static string BuildMissingFieldsMessage(bool requiresPassword, bool requiresPin)
        {
            if (requiresPassword && requiresPin)
                return "Preenche password local, Vanished PIN e código MFA.";
            if (requiresPassword)
                return "Preenche a password local e o código MFA.";
            if (requiresPin)
                return "Preenche Vanished PIN e código MFA.";
            return "Preenche o código MFA.";
        }

        static string BuildProviderHint(SensitiveActionRequest request)
        {
            if (!request.RequireLocalPassword && request.RequireAccountPin)
                return "Re-autenticação atual: Vanished PIN + MFA.";
            if (request.RequireAccountPin)
                return "Re-autenticação atual: password local + Vanished PIN + MFA.";
            return "Re-autenticação atual: password local + MFA.";
        }

        static void FocusFirstRequiredField(SensitiveActionRequest request, TextBox password, TextBox accountPin, TextBox mfa)
        {
            if (request.RequireLocalPassword && string.IsNullOrWhiteSpace(password.Text))
            {
                password.Focus();
                return;
            }

            if (request.RequireAccountPin && string.IsNullOrWhiteSpace(accountPin.Text))
            {
                accountPin.Focus();
                return;
            }

            mfa.Focus();
        }

        static string OnlyDigits(string text)
        {
            Span<char> buffer = stackalloc char[Math.Min(text.Length, 6)];
            var count = 0;
            foreach (var ch in text)
            {
                if (char.IsDigit(ch) && count < 6) buffer[count++] = ch;
            }
            return new string(buffer[..count]);
        }
    }

    private static Border overlay = new();

    private static async Task AnimateInAsync(ScaleTransform scale, TranslateTransform translate, Control overlay)
    {
        overlay.Opacity = 0;
        for (var i = 1; i <= 12; i++)
        {
            var t = 1 - Math.Pow(1 - i / 12.0, 3);
            overlay.Opacity = t;
            scale.ScaleX = scale.ScaleY = 0.92 + (0.08 * t);
            translate.Y = 12 * (1 - t);
            await Task.Delay(16);
        }
        overlay.Opacity = 1;
        scale.ScaleX = scale.ScaleY = 1;
        translate.Y = 0;
    }

    private static async Task AnimateOutAsync(ScaleTransform scale, TranslateTransform translate, Control overlay)
    {
        for (var i = 1; i <= 10; i++)
        {
            var t = i / 10.0;
            overlay.Opacity = 1 - t;
            scale.ScaleX = scale.ScaleY = 1 - (0.05 * t);
            translate.Y = 8 * t;
            await Task.Delay(15);
        }
    }

    private static async Task ShakeAsync(TranslateTransform translate)
    {
        var original = translate.X;
        var points = new[] { -8, 8, -6, 6, -3, 3, 0 };
        foreach (var x in points)
        {
            translate.X = original + x;
            await Task.Delay(42);
        }
        translate.X = original;
    }
}
