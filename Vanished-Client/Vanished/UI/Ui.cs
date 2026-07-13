using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vanished.UI;

public enum VanishedThemeMode
{
    Dark,
    Light,
    System
}

public static class Ui
{
    public static SolidColorBrush Bg = Brush.Parse("#0E1621") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#0E1621"));
    public static SolidColorBrush Surface = Brush.Parse("#17212B") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#17212B"));
    public static SolidColorBrush Surface2 = Brush.Parse("#1E2C3A") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#1E2C3A"));
    public static SolidColorBrush Panel = Surface;
    public static SolidColorBrush Panel2 = Surface2;
    public static SolidColorBrush Panel3 = Brush.Parse("#223246") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#223246"));
    public static SolidColorBrush Border = Brush.Parse("#26384A") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#26384A"));
    public static SolidColorBrush BorderSoft = Brush.Parse("#213143") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#213143"));
    public static SolidColorBrush Text = Brush.Parse("#F5F7FA") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#F5F7FA"));
    public static SolidColorBrush Muted = Brush.Parse("#9CAFC2") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#9CAFC2"));
    public static SolidColorBrush Muted2 = Brush.Parse("#6B7F94") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#6B7F94"));
    public static SolidColorBrush Accent = Brush.Parse("#2AABEE") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#2AABEE"));
    public static SolidColorBrush AccentStrong = Accent;
    public static SolidColorBrush AccentSoft = Brush.Parse("#12374D") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#12374D"));
    public static SolidColorBrush AccentHover = Brush.Parse("#229ED9") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#229ED9"));
    public static SolidColorBrush Success = Brush.Parse("#35D07F") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#35D07F"));
    public static SolidColorBrush Warning = Brush.Parse("#F3C969") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#F3C969"));
    public static SolidColorBrush Danger = Brush.Parse("#FF5C77") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#FF5C77"));
    public static SolidColorBrush AccentForeground = new SolidColorBrush(Colors.White);
    public static SolidColorBrush MessageStatusPending = Brush.Parse("#B8C7D6") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#B8C7D6"));
    public static SolidColorBrush MessageStatusSent = Brush.Parse("#EAF6FF") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#EAF6FF"));
    public static SolidColorBrush MessageStatusDelivered = Brush.Parse("#FFFFFF") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static SolidColorBrush MessageStatusRead = Brush.Parse("#7CFFB2") as SolidColorBrush ?? new SolidColorBrush(Color.Parse("#7CFFB2"));
    public static event Action? ThemeChanged;
    public static LinearGradientBrush PrimaryGradient = BuildGradient("#2AABEE", "#229ED9");

    public static VanishedThemeMode CurrentTheme { get; private set; } = VanishedThemeMode.Dark;

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanished");

    private static string SettingsFile => Path.Combine(SettingsDirectory, "appsettings.json");

    public static void ApplySavedTheme()
    {
        try
        {
            if (!File.Exists(SettingsFile))
            {
                ApplyTheme(VanishedThemeMode.Dark, false);
                return;
            }

            var json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppUiSettings>(json);
            if (settings != null && Enum.TryParse<VanishedThemeMode>(settings.Theme, true, out var mode))
                ApplyTheme(mode, false);
            else
                ApplyTheme(VanishedThemeMode.Dark, false);
        }
        catch
        {
            ApplyTheme(VanishedThemeMode.Dark, false);
        }
    }

    public static void ApplyTheme(VanishedThemeMode mode, bool persist = true)
    {
        CurrentTheme = mode;

        var app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = mode switch
            {
                VanishedThemeMode.Light => ThemeVariant.Light,
                VanishedThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        var effective = mode == VanishedThemeMode.System ? ResolveSystemTheme() : mode;

        if (effective == VanishedThemeMode.Light)
        {
            SetPalette("#F4F7FB", "#FFFFFF", "#EDF3F9", "#E4ECF5", "#C9D6E2", "#DCE5EE", "#111827", "#536879", "#7A8B9A", "#168BC5", "#0E78AF", "#DDF2FF", "#168A50", "#9A6A00", "#C8324A");
            MessageStatusPending.Color = Color.Parse("#8B97A3");
            MessageStatusSent.Color = Color.Parse("#667788");
            MessageStatusDelivered.Color = Color.Parse("#4C6376");
            MessageStatusRead.Color = Color.Parse("#168BC5");
            PrimaryGradient = BuildGradient("#229ED9", "#168BC5");
        }
        else
        {
            SetPalette("#0E1621", "#17212B", "#1E2C3A", "#223246", "#26384A", "#213143", "#F5F7FA", "#9CAFC2", "#6B7F94", "#2AABEE", "#229ED9", "#12374D", "#35D07F", "#F3C969", "#FF5C77");
            MessageStatusPending.Color = Color.Parse("#888888");
            MessageStatusSent.Color = Color.Parse("#CCFFFFFF");
            MessageStatusDelivered.Color = Color.Parse("#E6FFFFFF");
            MessageStatusRead.Color = Color.Parse("#29B6F6");
            PrimaryGradient = BuildGradient("#2AABEE", "#229ED9");
        }

        SyncApplicationResources();
        if (persist) SaveSettings();
        ThemeChanged?.Invoke();
    }

    public static void RefreshSystemThemeIfNeeded()
    {
        if (CurrentTheme == VanishedThemeMode.System)
            ApplyTheme(VanishedThemeMode.System, false);
    }

    private static void SetPalette(string bg, string surface, string surface2, string panel3, string border, string borderSoft, string text, string muted, string muted2, string accent, string accentHover, string accentSoft, string success, string warning, string danger)
    {
        Bg.Color = Color.Parse(bg);
        Surface.Color = Color.Parse(surface);
        Surface2.Color = Color.Parse(surface2);
        Panel = Surface;
        Panel2 = Surface2;
        Panel3.Color = Color.Parse(panel3);
        Border.Color = Color.Parse(border);
        BorderSoft.Color = Color.Parse(borderSoft);
        Text.Color = Color.Parse(text);
        Muted.Color = Color.Parse(muted);
        Muted2.Color = Color.Parse(muted2);
        Accent.Color = Color.Parse(accent);
        AccentStrong = Accent;
        AccentHover.Color = Color.Parse(accentHover);
        AccentSoft.Color = Color.Parse(accentSoft);
        Success.Color = Color.Parse(success);
        Warning.Color = Color.Parse(warning);
        Danger.Color = Color.Parse(danger);
    }

    public static void SyncApplicationResources()
    {
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;
        resources["WindowBackgroundBrush"] = Bg;
        resources["PanelBrush"] = Surface;
        resources["PanelElevatedBrush"] = Surface2;
        resources["BorderBrush"] = Border;
        resources["AccentBrush"] = Accent;
        resources["AccentStrongBrush"] = AccentHover;
        resources["TextPrimaryBrush"] = Text;
        resources["TextMutedBrush"] = Muted;
        resources["DangerBrush"] = Danger;
        resources["SuccessBrush"] = Success;
        resources["WarningBrush"] = Warning;

        resources["VanishedBackgroundBrush"] = Bg;
        resources["VanishedSurfaceBrush"] = Surface;
        resources["VanishedSurface2Brush"] = Surface2;
        resources["VanishedBorderBrush"] = Border;
        resources["VanishedBorderSoftBrush"] = BorderSoft;
        resources["VanishedInputBackgroundBrush"] = Surface2;
        resources["VanishedInputBorderBrush"] = Border;
        resources["VanishedInputFocusBorderBrush"] = Accent;
        resources["VanishedTextBrush"] = Text;
        resources["VanishedMutedBrush"] = Muted;
        resources["VanishedScrollbarThumbBrush"] = Panel3;
        resources["VanishedScrollbarThumbHoverBrush"] = Border;
    }

    private static VanishedThemeMode ResolveSystemTheme()
    {
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        return variant == ThemeVariant.Light ? VanishedThemeMode.Light : VanishedThemeMode.Dark;
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(new AppUiSettings { Theme = CurrentTheme.ToString() }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    private sealed class AppUiSettings
    {
        public string Theme { get; set; } = VanishedThemeMode.Dark.ToString();
    }

    private static LinearGradientBrush BuildGradient(string a, string b) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) }
    };

    public static TextBlock TextBlock(string text, double size = 14, IBrush? foreground = null, FontWeight weight = FontWeight.Normal)
        => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = foreground ?? Text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static TextBox TextBox(string watermark)
    {
        var box = new TextBox
        {
            Watermark = watermark,
            MinHeight = 44,
            MinWidth = 0,
            Padding = new Thickness(14, 9),
            Background = Surface2,
            Foreground = Text,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            CaretBrush = Accent,
            FocusAdorner = null
        };

        box.Classes.Add("vanished-textbox");
        box.GotFocus += (_, _) =>
        {
            box.Background = Surface2;
            box.BorderBrush = Accent;
        };
        box.LostFocus += (_, _) =>
        {
            box.Background = Surface2;
            box.BorderBrush = Border;
        };
        box.PointerEntered += (_, _) => box.Background = Surface2;
        box.PointerExited += (_, _) => box.Background = Surface2;
        return box;
    }

    public static void ApplyInnerTextBoxChromeFix(TextBox box)
    {
        box.FocusAdorner = null;
        box.Background = Brushes.Transparent;
        box.BorderBrush = Brushes.Transparent;
        box.BorderThickness = new Thickness(0);
        box.CornerRadius = new CornerRadius(0);
        box.Classes.Add("vanished-inner-input");
    }

    public static TextBox PasswordBox(string watermark)
    {
        var box = TextBox(watermark);
        box.PasswordChar = '●';
        return box;
    }

    public static Border PasswordFieldWithToggle(string watermark, out TextBox inputRef)
    {
        var revealed = false;
        var box = TextBox(watermark);
        box.PasswordChar = '●';
        ApplyInnerTextBoxChromeFix(box);
        box.MinHeight = 40;
        box.Padding = new Thickness(0, 8);
        inputRef = box;

        var iconHost = new ContentControl
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = Icon("eye_off", 16, Muted2)
        };

        var toggle = new Button
        {
            Content = iconHost,
            Width = 34,
            Height = 34,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(toggle, "Mostrar password");
        toggle.Click += (_, e) =>
        {
            e.Handled = true;
            revealed = !revealed;
            box.PasswordChar = revealed ? '\0' : '●';
            iconHost.Content = Icon(revealed ? "eye" : "eye_off", 16, Muted2);
            ToolTip.SetTip(toggle, revealed ? "Ocultar password" : "Mostrar password");
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { box, toggle }
        };
        Grid.SetColumn(toggle, 1);

        var outer = new Border
        {
            MinHeight = 44,
            Padding = new Thickness(14, 0, 6, 0),
            Background = Surface2,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = grid
        };

        box.GotFocus += (_, _) => { outer.Background = Surface2; outer.BorderBrush = Accent; };
        box.LostFocus += (_, _) => { outer.Background = Surface2; outer.BorderBrush = Border; };
        return outer;
    }

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Content = text,
            Tag = text,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(18, 8)
        };
        b.Classes.Add("loading-button");
        return b;
    }

    public static Button SecondaryButton(string text)
        => new()
        {
            Content = text,
            Tag = text,
            MinHeight = 40,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Surface2,
            Foreground = Text,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 7)
        };

    public static Button GhostButton(string text)
        => new()
        {
            Content = text,
            Tag = text,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = Muted,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7)
        };


    public static Button LinkButton(string text)
        => new()
        {
            Content = text,
            Tag = text,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = Muted,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            FontSize = 13,
            Opacity = 0.72
        };

    public static Button BackButton(string text = "Voltar")
        => new()
        {
            Content = H(6, Icon("back", 14, Muted), TextBlock(text, 13, Muted)),
            Tag = text,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = Muted,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            Opacity = 0.78
        };

    public static CheckBox Toggle(string text)
        => new()
        {
            Content = text,
            Foreground = Text,
            FontSize = 13,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };

    public static TextBlock AuthLink(string text)
        => new()
        {
            Text = text,
            FontSize = 13,
            Foreground = Accent,
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 1.0
        };

    public static Border LoadingSpinner(double size = 20, double stroke = 2.5, IBrush? color = null)
    {
        var spinner = new Border
        {
            Width = size,
            Height = size,
            BorderBrush = color ?? AccentForeground,
            BorderThickness = new Thickness(stroke, stroke, stroke, 0),
            CornerRadius = new CornerRadius(size / 2),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new RotateTransform(0)
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (spinner.Parent == null) { timer.Stop(); return; }
            if (spinner.RenderTransform is RotateTransform rt)
                rt.Angle = (rt.Angle + 8) % 360;
        };
        spinner.AttachedToVisualTree += (_, _) => timer.Start();
        spinner.DetachedFromVisualTree += (_, _) => timer.Stop();
        return spinner;
    }

    public static void SetButtonLoading(Button button, bool loading)
    {
        if (loading)
        {
            if (button.Tag == null) button.Tag = button.Content?.ToString() ?? string.Empty;
            button.Content = LoadingSpinner(18, 2.5, AccentForeground);
            button.IsEnabled = false;
            button.Opacity = 0.75;
        }
        else
        {
            if (button.Tag is string text) button.Content = text;
            button.IsEnabled = true;
            button.Opacity = 1;
        }
    }

    public static Button DangerButton(string text)
    {
        var btn = GhostButton(text);
        btn.Foreground = Danger;
        btn.BorderBrush = Danger;
        return btn;
    }

    public static Border Card(Control child, Thickness? padding = null, double radius = 18)
        => new()
        {
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = padding ?? new Thickness(28),
            Child = child
        };

    public static StackPanel V(double spacing = 12, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children)
            AddChildDefensive(panel, child);
        return panel;
    }

    public static StackPanel H(double spacing = 10, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children)
            AddChildDefensive(panel, child);
        return panel;
    }

    public static void AddChildDefensive(Panel panel, Control child)
    {
        if (child.Parent is Panel oldPanel)
            oldPanel.Children.Remove(child);
        else if (child.Parent is ContentControl oldContent && ReferenceEquals(oldContent.Content, child))
            oldContent.Content = null;
        else if (child.Parent is Decorator oldDecorator && ReferenceEquals(oldDecorator.Child, child))
            oldDecorator.Child = null;
        panel.Children.Add(child);
    }

    public static Border Avatar(string text, double size = 42)
        => new()
        {
            Width = size,
            Height = size,
            Background = Surface2,
            CornerRadius = new CornerRadius(size / 2),
            ClipToBounds = true,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "V" : text.Trim()[0].ToString().ToUpperInvariant(),
                Foreground = Text,
                FontSize = size * 0.38,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

    public static Border AvatarImage(string? avatarBase64, string fallbackText, double size = 42)
    {
        if (string.IsNullOrWhiteSpace(avatarBase64))
            return Avatar(fallbackText, size);

        try
        {
            var comma = avatarBase64.IndexOf(',');
            var raw = comma >= 0 ? avatarBase64[(comma + 1)..] : avatarBase64;
            var bytes = Convert.FromBase64String(raw);
            return new Border
            {
                Width = size,
                Height = size,
                Background = Surface2,
                CornerRadius = new CornerRadius(size / 2),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = new Bitmap(new MemoryStream(bytes)),
                    Width = size,
                    Height = size,
                    Stretch = Stretch.UniformToFill
                }
            };
        }
        catch
        {
            return Avatar(fallbackText, size);
        }
    }

    public static TextBlock StatusBlock()
        => new()
        {
            Foreground = Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 22
        };

    public static void BindEnterToCommand(Control input, Func<Task> submit)
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

    public static void BindEnterToAction(Control input, Action submit)
    {
        input.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    e.Handled = true;
                    submit();
                }
            },
            routes: RoutingStrategies.Tunnel,
            handledEventsToo: false);
    }


    public static Button IconButton(string text)
        => new()
        {
            Content = text,
            Width = 36,
            Height = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = Muted,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(0)
        };

    public static Button CircleButton(string text, IBrush foreground, IBrush? background = null)
        => new()
        {
            Content = new TextBlock { Text = text, Foreground = foreground, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold },
            Width = 32,
            Height = 32,
            MinWidth = 32,
            MinHeight = 32,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = background ?? Surface2,
            Foreground = foreground,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(0)
        };

    public static Border Pill(Control content, IBrush? background = null, IBrush? border = null)
        => new()
        {
            Background = background ?? Surface2,
            BorderBrush = border ?? Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 5),
            Child = content
        };

    public static Border Dot(IBrush brush, double size = 8)
        => new()
        {
            Width = size,
            Height = size,
            Background = brush,
            CornerRadius = new CornerRadius(size / 2)
        };

    public static Grid SplitColumns(string columns, params Control[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        for (var i = 0; i < children.Length; i++)
        {
            Grid.SetColumn(children[i], i);
            grid.Children.Add(children[i]);
        }
        return grid;
    }

    public static Grid SplitRows(string rows, params Control[] children)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions(rows) };
        for (var i = 0; i < children.Length; i++)
        {
            Grid.SetRow(children[i], i);
            grid.Children.Add(children[i]);
        }
        return grid;
    }

    public static Border Divider()
        => new() { Height = 1, Background = Border, Margin = new Thickness(0, 4), IsHitTestVisible = false };

    public static Border MenuPanel(params Control[] children)
        => new()
        {
            MinWidth = 200,
            Background = Surface,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8),
            Child = V(3, children)
        };


    private static readonly Dictionary<string, string> IconPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profile"] = @"M150 850Q170 850 187.5 840.0Q205 830 215.0 812.5Q225 795 225.0 775.0Q225 755 215.0 737.5Q205 720 187.5 710.0Q170 700 150.0 700.0Q130 700 112.5 710.0Q95 720 85.0 737.5Q75 755 75.0 775.0Q75 795 85.0 812.5Q95 830 112.5 840.0Q130 850 150 850ZM150 725Q171 725 185.5 739.5Q200 754 200.0 775.0Q200 796 185.5 810.5Q171 825 150.0 825.0Q129 825 114.5 810.5Q100 796 100.0 775.0Q100 754 114.5 739.5Q129 725 150 725ZM150 875Q119 875 93.5 890.0Q68 905 53.0 931.0Q38 957 37 988Q38 993 41.5 996.5Q45 1000 50.0 1000.0Q55 1000 58.5 996.5Q62 993 62 988Q63 964 74.5 944.0Q86 924 106.0 912.0Q126 900 150.0 900.0Q174 900 194.0 912.0Q214 924 226.0 944.0Q238 964 238 988Q238 993 241.5 996.5Q245 1000 250.0 1000.0Q255 1000 259.0 996.5Q263 993 263 988Q262 957 247.0 931.0Q232 905 206.5 890.0Q181 875 150 875Z",
        ["edit"] = @"M233 712 81 864Q72 873 67.0 884.5Q62 896 63 908V925Q63 930 66.5 933.5Q70 937 75 937H92Q104 938 115.5 933.0Q127 928 136 919L288 767Q300 755 300.0 739.0Q300 723 288.5 712.0Q277 701 261.0 701.0Q245 701 233 712ZM271 749 118 902Q107 912 92 912H88V908Q88 893 98 882L251 729Q255 725 261.0 725.0Q267 725 271.0 729.0Q275 733 275.0 739.0Q275 745 271 749ZM288 812Q282 812 278.5 816.0Q275 820 275 825V887H225Q209 887 198.5 898.0Q188 909 188 925V975H63Q47 975 36.0 964.0Q25 953 25 938V762Q25 747 36.0 736.0Q47 725 62 725H176Q181 725 184.5 721.5Q188 718 188.0 712.5Q188 707 184.5 703.5Q181 700 176 700H63Q46 700 31.5 708.5Q17 717 8.5 731.5Q0 746 0 762V937Q0 954 8.5 968.5Q17 983 31.5 991.5Q46 1000 62 1000H204Q217 1000 228.5 995.5Q240 991 248 982L282 948Q291 940 295.5 928.5Q300 917 300 904V825Q300 820 296.5 816.0Q293 812 288 812ZM231 964Q223 972 212 974V925Q213 920 216.5 916.5Q220 913 225 913H274Q272 923 264 931Z",
        ["settings"] = @"M150 800Q129 800 114.5 814.5Q100 829 100.0 850.0Q100 871 114.5 885.5Q129 900 150.0 900.0Q171 900 185.5 885.5Q200 871 200.0 850.0Q200 829 185.5 814.5Q171 800 150 800ZM150 875Q140 875 132.5 867.5Q125 860 125.0 850.0Q125 840 132.5 832.5Q140 825 150.0 825.0Q160 825 167.5 832.5Q175 840 175.0 850.0Q175 860 167.5 867.5Q160 875 150 875ZM266 874 261 871Q264 850 261 829L266 826Q275 821 280.0 812.5Q285 804 285.0 794.0Q285 784 280.0 775.0Q275 766 266.0 761.0Q257 756 247.0 756.0Q237 756 229 761L223 764Q207 751 188 744V737Q188 722 177.0 711.0Q166 700 150.0 700.0Q134 700 123.0 711.0Q112 722 113 738V744Q93 751 77 764L71 761Q58 753 43.0 757.5Q28 762 20.0 775.0Q12 788 16.0 803.0Q20 818 34 826L39 829Q36 850 39 871L34 874Q20 882 16.0 897.0Q12 912 20.0 925.0Q28 938 43.0 942.5Q58 947 71 939L77 936Q93 949 112 956V962Q113 978 123.5 989.0Q134 1000 150.0 1000.0Q166 1000 176.5 989.0Q187 978 188 963V956Q207 949 223 936L229 939Q242 947 257.0 942.5Q272 938 280.0 925.0Q288 912 284.0 897.0Q280 882 266 874ZM234 827Q241 850 234 873Q233 878 234.5 881.5Q236 885 240 888L254 895Q258 898 259.5 903.0Q261 908 258.5 912.5Q256 917 251.0 918.5Q246 920 241 917L228 909Q224 907 219.5 907.5Q215 908 212 911Q195 929 172 935Q168 936 165.0 939.5Q162 943 162 947V962Q162 968 158.5 971.5Q155 975 150.0 975.0Q145 975 141.0 971.5Q137 968 137 962V947Q137 943 134.5 939.5Q132 936 128 935Q105 929 88 911Q85 908 80.5 907.5Q76 907 72 909L59 917Q53 921 46.5 917.0Q40 913 40.0 906.0Q40 899 46 895L60 888Q64 885 65.5 881.5Q67 878 66 873Q59 850 66 827Q67 822 65.5 818.5Q64 815 60 812L46 805Q40 801 40.0 794.0Q40 787 46.5 783.0Q53 779 59 783L72 791Q76 793 80.5 792.5Q85 792 88 789Q105 771 128 765Q132 764 135.0 760.5Q138 757 138 753V737Q137 732 141.0 728.5Q145 725 150.0 725.0Q155 725 159.0 728.5Q163 732 163 737V753Q162 757 165.0 760.5Q168 764 172 765Q195 771 212 789Q215 792 219.5 792.5Q224 793 228 791L241 783Q247 779 253.5 783.0Q260 787 260.0 794.0Q260 801 254 805L240 812Q236 815 234.5 818.5Q233 822 234 827Z",
        ["logout"] = @"M143 887Q138 887 134.5 891.0Q131 895 131 900V937Q131 953 120.0 964.0Q109 975 93 975H63Q47 975 36.0 964.0Q25 953 25 938V762Q25 747 36.0 736.0Q47 725 62 725H93Q109 725 120.0 736.0Q131 747 131 763V800Q131 805 134.5 809.0Q138 813 143.5 812.5Q149 812 152.5 808.5Q156 805 156 800V762Q156 746 147.5 731.5Q139 717 124.5 708.5Q110 700 93 700H63Q46 700 31.5 708.5Q17 717 8.5 731.5Q0 746 0 762V937Q0 954 8.5 968.5Q17 983 31.5 991.5Q46 1000 62 1000H93Q110 1000 124.5 991.5Q139 983 147.5 968.5Q156 954 156 937V900Q156 895 152.5 891.5Q149 888 143 888ZM286 823 229 766Q223 761 216.0 763.0Q209 765 207.5 772.0Q206 779 211 784L264 837H75Q70 837 66.5 841.0Q63 845 63.0 850.0Q63 855 66.5 859.0Q70 863 75 863L265 862L211 916Q206 921 207.5 928.0Q209 935 216.0 937.0Q223 939 228 934L286 877Q297 866 297.0 850.0Q297 834 286 823Z",
        ["check"] = @"M279 755 106 928Q103 932 97.5 932.0Q92 932 89 928L22 861Q18 858 13.0 858.0Q8 858 4 861Q0 865 0.0 870.0Q0 875 4 879L71 946Q82 957 97.5 957.0Q113 957 124 946L297 773Q300 769 300.0 764.0Q300 759 297 755Q293 752 288.0 752.0Q283 752 279 755Z",
        ["close"] = @"M296 704Q293 700 287.5 700.0Q282 700 279 704L150 832L21 704Q18 700 12.5 700.0Q7 700 4 704Q0 707 0.0 712.5Q0 718 4 721L132 850L4 979Q0 982 0.0 987.5Q0 993 4 996Q7 1000 12.5 1000.0Q18 1000 21 996L150 868L279 996Q282 1000 287.5 1000.0Q293 1000 296 996Q300 993 300.0 987.5Q300 982 296 979L168 850L296 721Q300 718 300.0 712.5Q300 707 296 704Z",
        ["back"] = @"M215 1000Q209 1000 206 996L104 894Q95 885 90.0 873.5Q85 862 85.0 850.0Q85 838 90.0 826.5Q95 815 104 806L206 704Q209 700 214.5 700.0Q220 700 223.5 703.5Q227 707 227.0 712.5Q227 718 223 721L121 823Q110 834 110.0 850.0Q110 866 121 877L224 979Q227 982 227.0 987.5Q227 993 223.5 996.5Q220 1000 215 1000Z",
        ["add"] = @"M288 837H163V712Q163 707 159.0 703.5Q155 700 150 700Q145 700 141.0 703.5Q137 707 137 712V837H13Q7 838 3.5 841.5Q0 845 0 850Q0 855 3.5 859.0Q7 863 12 863H138V987Q138 993 141.5 996.5Q145 1000 150 1000Q155 1000 158.5 996.5Q162 993 162 987V862H288Q293 863 296.5 859.0Q300 855 300 850Q300 845 296.5 841.0Q293 837 288 837Z",
        ["search"] = @"M296 979 222 904Q243 879 248.5 847.0Q254 815 243.5 784.5Q233 754 208.5 732.0Q184 710 153.0 703.0Q122 696 90.5 704.5Q59 713 36.0 736.0Q13 759 4.5 790.5Q-4 822 3.0 853.0Q10 884 32.0 908.5Q54 933 84.5 943.5Q115 954 147.0 948.5Q179 943 204 922L279 996Q282 1000 287.5 1000.0Q293 1000 296.5 996.5Q300 993 300.0 987.5Q300 982 296 979ZM125 925Q98 925 75.0 911.5Q52 898 38.5 875.0Q25 852 25.0 825.0Q25 798 38.5 775.0Q52 752 75.0 738.5Q98 725 125.0 725.0Q152 725 175.0 738.5Q198 752 211.5 775.0Q225 798 225.0 825.0Q225 852 211.5 875.0Q198 898 175.0 911.5Q152 925 125 925Z",
        ["lock"] = @"M238 805V787Q237 764 225.5 744.0Q214 724 194.0 712.0Q174 700 150.0 700.0Q126 700 106.0 712.0Q86 724 74.0 744.0Q62 764 62 787V805Q45 813 35.0 828.5Q25 844 25 862V937Q25 954 33.5 968.5Q42 983 56.5 991.5Q71 1000 87 1000H213Q229 1000 243.5 991.5Q258 983 266.5 968.5Q275 954 275 938V862Q275 844 265.0 828.5Q255 813 238 805ZM88 787Q88 771 96.0 756.5Q104 742 118.5 733.5Q133 725 150.0 725.0Q167 725 181.5 733.5Q196 742 204.0 756.5Q212 771 212 788V800H88ZM250 937Q250 953 239.0 964.0Q228 975 212 975H88Q72 975 61.0 964.0Q50 953 50 938V862Q50 847 61.0 836.0Q72 825 87 825H213Q228 825 239.0 836.0Q250 847 250 862ZM150 875Q145 875 141.0 878.5Q137 882 137 888V912Q138 918 141.5 921.5Q145 925 150.0 925.0Q155 925 159.0 921.5Q163 918 163 912V887Q163 882 159.0 878.5Q155 875 150 875Z",
        ["security"] = @"M232 727 154 701Q150 699 146 701L68 727Q49 733 37.0 749.5Q25 766 25 786V850Q25 892 55 930Q77 958 112 980Q130 992 145 999Q150 1001 155 999Q170 992 188 980Q223 958 245 930Q275 892 275 850V786Q275 766 263.0 749.5Q251 733 232 727ZM250 850Q250 881 229 910Q213 932 185 952Q168 964 150 974Q132 964 115 952Q87 932 71 910Q50 881 50 850V786Q50 774 57.0 764.0Q64 754 76 750L150 726L224 750Q236 754 243.0 764.0Q250 774 250 786ZM191 804 139 856 111 827Q106 822 99.0 823.5Q92 825 90.0 832.0Q88 839 93 844L122 874Q128 882 138 882H139Q149 882 156 875L209 822Q213 818 213.0 812.5Q213 807 209.0 803.5Q205 800 200.0 800.0Q195 800 191 804Z",
        ["notification"] = @"M282 871 258 785Q251 760 234.0 740.5Q217 721 193.0 710.0Q169 699 142.5 700.0Q116 701 93.0 712.5Q70 724 54.0 744.5Q38 765 32 791L14 874Q11 888 14.0 902.0Q17 916 26.0 927.0Q35 938 48.0 944.0Q61 950 75 950H89Q93 972 110.5 986.0Q128 1000 150.0 1000.0Q172 1000 189.5 986.0Q207 972 211 950H222Q236 950 249.5 943.5Q263 937 271.5 925.5Q280 914 283.0 899.5Q286 885 282 871ZM150 975Q138 975 128.5 968.0Q119 961 115 950H185Q181 961 171.5 968.0Q162 975 150 975ZM252 910Q246 917 238.5 921.0Q231 925 222 925H75Q66 925 58.5 921.5Q51 918 45.5 911.0Q40 904 38.0 896.0Q36 888 38 879L57 797Q63 766 87.5 746.0Q112 726 143.5 725.0Q175 724 200.5 743.0Q226 762 234 792L258 877Q260 886 258.5 894.5Q257 903 252 910Z",
        ["info"] = @"M150 700Q109 700 74.5 720.0Q40 740 20.0 774.5Q0 809 0.0 850.0Q0 891 20.0 925.5Q40 960 74.5 980.0Q109 1000 150.0 1000.0Q191 1000 225.5 980.0Q260 960 280.0 925.5Q300 891 300.0 850.0Q300 809 280.0 774.5Q260 740 225.5 720.0Q191 700 150 700ZM150 975Q116 975 87.5 958.0Q59 941 42.0 912.5Q25 884 25.0 850.0Q25 816 42.0 787.5Q59 759 87.5 742.0Q116 725 150.0 725.0Q184 725 212.5 742.0Q241 759 258.0 787.5Q275 816 275.0 850.0Q275 884 258.0 912.5Q241 941 212.5 958.0Q184 975 150 975ZM150 825H138Q132 825 128.5 828.5Q125 832 125.0 837.5Q125 843 128.5 846.5Q132 850 138 850H150V925Q150 930 153.5 933.5Q157 937 162.5 937.0Q168 937 171.5 933.5Q175 930 175 925V850Q175 840 167.5 832.5Q160 825 150 825ZM131 781Q131 789 136.5 794.5Q142 800 150.0 800.0Q158 800 163.5 794.5Q169 789 169.0 781.0Q169 773 163.5 767.5Q158 762 150.0 762.5Q142 763 136.5 768.0Q131 773 131 781Z",
        ["chat"] = @"M250 700H50Q29 700 14.5 714.5Q0 729 0 750V900Q0 921 14.5 935.5Q29 950 50 950H86L142 997Q145 1000 150.0 1000.0Q155 1000 158 997L214 950H250Q271 950 285.5 935.5Q300 921 300 900V750Q300 729 285.5 714.5Q271 700 250 700ZM275 900Q275 910 267.5 917.5Q260 925 250 925H214Q205 925 198 931L150 971L102 931Q95 925 86 925H50Q40 925 32.5 917.5Q25 910 25 900V750Q25 740 32.5 732.5Q40 725 50 725H250Q260 725 267.5 732.5Q275 740 275 750ZM88 787H150Q155 787 159.0 783.5Q163 780 163.0 775.0Q163 770 159.0 766.0Q155 762 150 762H88Q82 763 78.5 766.5Q75 770 75.0 775.0Q75 780 78.5 784.0Q82 788 88 788ZM213 812H88Q82 813 78.5 816.5Q75 820 75.0 825.0Q75 830 78.5 834.0Q82 838 88 838H213Q218 837 221.5 833.5Q225 830 225.0 825.0Q225 820 221.5 816.0Q218 812 213 812ZM213 862H88Q82 862 78.5 866.0Q75 870 75.0 875.0Q75 880 78.5 883.5Q82 887 88 887H213Q218 887 221.5 883.5Q225 880 225.0 875.0Q225 870 221.5 866.0Q218 862 213 862Z",
        ["fingerprint"] = @"M150 700Q111 700 83 728Q55 756 55 795Q55 801 59 805Q63 809 69 809Q75 809 79 805Q83 801 83 795Q83 768 102 749Q122 730 150 730Q178 730 198 750Q217 769 217 797Q217 803 221 807Q225 811 231 811Q237 811 241 807Q245 803 245 797Q245 757 217 729Q189 700 150 700ZM150 755Q132 755 119 768Q106 781 106 799V842Q106 884 83 912Q79 917 79 923Q79 929 84 933Q89 937 95 936Q101 935 105 930Q134 895 134 842V799Q134 793 139 788Q144 783 150 783Q156 783 161 788Q166 793 166 799V848Q166 913 127 958Q123 963 124 969Q125 975 130 979Q135 983 141 982Q147 981 151 976Q194 924 194 848V799Q194 781 181 768Q168 755 150 755ZM60 843Q54 843 50 847Q46 851 46 857Q46 887 32 907Q28 912 29 918Q30 924 35 928Q40 932 46 931Q52 930 56 925Q74 899 74 857Q74 851 70 847Q66 843 60 843ZM240 843Q234 843 230 847Q226 851 226 857Q226 913 196 951Q192 956 193 962Q194 968 199 972Q204 976 210 975Q216 974 220 969Q254 924 254 857Q254 851 250 847Q246 843 240 843Z",
        ["key"] = @"M104 850Q104 817 127 794Q150 771 183 771Q216 771 239 794Q262 817 262 850Q262 883 239 906Q216 929 183 929Q161 929 143 918L98 963H64V929H30V895L75 850Q104 850 104 850ZM183 799Q162 799 147 814Q132 829 132 850Q132 871 147 886Q162 901 183 901Q204 901 219 886Q234 871 234 850Q234 829 219 814Q204 799 183 799ZM196 828Q205 828 211 834Q217 840 217 849Q217 858 211 864Q205 870 196 870Q187 870 181 864Q175 858 175 849Q175 840 181 834Q187 828 196 828Z",
        ["eye"] = @"M150 745Q93 745 48 775Q18 795 0 850Q18 905 48 925Q93 955 150 955Q207 955 252 925Q282 905 300 850Q282 795 252 775Q207 745 150 745ZM150 780Q194 780 230 802Q250 814 264 850Q250 886 230 898Q194 920 150 920Q106 920 70 898Q50 886 36 850Q50 814 70 802Q106 780 150 780ZM150 805Q131 805 118 818Q105 831 105 850Q105 869 118 882Q131 895 150 895Q169 895 182 882Q195 869 195 850Q195 831 182 818Q169 805 150 805ZM150 830Q158 830 164 836Q170 842 170 850Q170 858 164 864Q158 870 150 870Q142 870 136 864Q130 858 130 850Q130 842 136 836Q142 830 150 830Z",
        ["eye_off"] = @"M25 710Q20 705 13 705Q6 705 3 710Q-2 716 3 722L278 997Q284 1002 291 997Q298 992 297 985Q297 980 292 976L248 932Q278 909 300 850Q282 795 252 775Q207 745 150 745Q118 745 89 755L25 710ZM150 780Q194 780 230 802Q250 814 264 850Q250 884 232 897L202 867Q205 858 205 850Q205 827 189 811Q173 795 150 795Q142 795 133 798L111 776Q130 780 150 780ZM36 850Q47 879 65 893Q99 920 150 920Q170 920 189 915L213 939Q184 955 150 955Q93 955 48 925Q18 905 0 850Q10 819 28 798L53 823Q43 834 36 850Z",
        ["chevron_down"] = @"M43 797Q47 793 53 793Q58 793 62 797L150 884L238 797Q242 793 247 793Q253 793 257 797Q261 801 261 806Q261 812 257 816L159 914Q155 918 150 918Q145 918 141 914L43 816Q39 812 39 806Q39 801 43 797Z",
        ["request"] = @"M238 712H63Q46 713 31.5 721.0Q17 729 8.5 743.5Q0 758 0 775V925Q0 942 8.5 956.5Q17 971 31.5 979.0Q46 987 62 988H238Q254 987 268.5 979.0Q283 971 291.5 956.5Q300 942 300 925V775Q300 758 291.5 743.5Q283 729 268.5 721.0Q254 713 238 712ZM63 737H238Q249 738 258.5 744.0Q268 750 272 761L177 857Q166 868 150.0 868.0Q134 868 123 857L28 761Q32 750 41.5 744.0Q51 738 62 738ZM238 962H63Q47 962 36.0 951.5Q25 941 25 925V794L106 874Q118 886 134.0 890.5Q150 895 166.0 890.5Q182 886 194 874L275 794V925Q275 941 264.0 951.5Q253 962 237 962Z",
        ["send"] = @"M289 711Q282 704 272.5 701.5Q263 699 254 701L54 743Q36 746 22.5 757.0Q9 768 3.5 785.0Q-2 802 2.0 819.5Q6 837 18 849L40 871Q43 874 43 879V919Q44 928 47 935V936Q53 947 64 952L65 953Q72 956 81 956H121Q126 956 129 960L151 982Q160 990 171.0 995.0Q182 1000 193.5 1000.0Q205 1000 215 997Q232 991 243.0 977.5Q254 964 257 947L299 746Q301 737 298.5 727.5Q296 718 289 711ZM58 853 36 832Q28 824 25.5 813.5Q23 803 26.5 793.0Q30 783 38.5 776.0Q47 769 58 768L256 726L68 914V879Q68 864 57 853ZM232 943Q231 953 224.0 961.5Q217 970 207.0 973.0Q197 976 186.5 974.0Q176 972 169 964L147 942Q136 931 121 932H86L274 744Z",
        ["trash"] = @"M263 750H224Q219 728 202.0 714.0Q185 700 162 700H138Q115 700 98.0 714.0Q81 728 76 750H38Q32 750 28.5 753.5Q25 757 25.0 762.5Q25 768 28.5 771.5Q32 775 38 775H50V937Q50 954 58.5 968.5Q67 983 81.5 991.5Q96 1000 112 1000H188Q204 1000 218.5 991.5Q233 983 241.5 968.5Q250 954 250 938V775H263Q268 775 271.5 771.5Q275 768 275.0 762.5Q275 757 271.5 753.5Q268 750 263 750ZM138 725H163Q174 725 184.0 732.0Q194 739 198 750H102Q106 739 116.0 732.0Q126 725 138 725ZM225 937Q225 953 214.0 964.0Q203 975 187 975H113Q97 975 86.0 964.0Q75 953 75 938V775H225ZM125 925Q130 925 134.0 921.5Q138 918 138 912V837Q138 832 134.0 828.5Q130 825 125.0 825.0Q120 825 116.5 828.5Q113 832 113 837V912Q112 918 116.0 921.5Q120 925 125 925ZM175 925Q180 925 184.0 921.5Q188 918 188 912V837Q188 832 184.0 828.5Q180 825 175.0 825.0Q170 825 166.0 828.5Q162 832 162 837V912Q163 918 166.5 921.5Q170 925 175 925Z",
        ["external_link"] = @"M250 837V937Q250 954 241.5 968.5Q233 983 218.5 991.5Q204 1000 187 1000H63Q46 1000 31.5 991.5Q17 983 8.5 968.5Q0 954 0 937V812Q0 796 8.5 781.5Q17 767 31.5 758.5Q46 750 63 750H163Q168 750 171.5 753.5Q175 757 175.0 762.5Q175 768 171.5 771.5Q168 775 162 775H63Q47 775 36.0 786.0Q25 797 25 812V937Q25 953 36.0 964.0Q47 975 63 975H188Q203 975 214.0 964.0Q225 953 225 937V837Q225 832 228.5 828.5Q232 825 237.5 825.0Q243 825 246.5 828.5Q250 832 250 838ZM263 700H175Q170 700 166.5 703.5Q163 707 163.0 712.5Q163 718 166.5 721.5Q170 725 175 725H257L104 879Q100 882 100.0 887.5Q100 893 103.5 896.5Q107 900 112.5 900.0Q118 900 121 896L275 743V825Q275 830 278.5 833.5Q282 837 287.5 837.0Q293 837 296.5 833.5Q300 830 300 825V737Q300 722 289.0 711.0Q278 700 262 700Z",
        ["more"] = @"M0 850Q0 860 7.5 867.5Q15 875 25.0 875.0Q35 875 42.5 867.5Q50 860 50.0 850.0Q50 840 42.5 832.5Q35 825 25.0 825.0Q15 825 7.5 832.5Q0 840 0 850ZM125 850Q125 860 132.5 867.5Q140 875 150.0 875.0Q160 875 167.5 867.5Q175 860 175.0 850.0Q175 840 167.5 832.5Q160 825 150.0 825.0Q140 825 132.5 832.5Q125 840 125 850ZM250 850Q250 860 257.5 867.5Q265 875 275.0 875.0Q285 875 292.5 867.5Q300 860 300.0 850.0Q300 840 292.5 832.5Q285 825 275.0 825.0Q265 825 257.5 832.5Q250 840 250 850Z",
        ["block"] = @"M150 700Q109 700 74.5 720.0Q40 740 20.0 774.5Q0 809 0.0 850.0Q0 891 20.0 925.5Q40 960 74.5 980.0Q109 1000 150.0 1000.0Q191 1000 225.5 980.0Q260 960 280.0 925.5Q300 891 300.0 850.0Q300 809 280.0 774.5Q260 740 225.5 720.0Q191 700 150 700ZM150 725Q172 725 192.0 732.5Q212 740 229 753L53 929Q30 901 26.0 865.0Q22 829 37.5 796.5Q53 764 83.5 744.5Q114 725 150 725ZM150 975Q128 975 108.0 967.5Q88 960 71 947L247 771Q270 799 274.0 835.0Q278 871 262.5 903.5Q247 936 216.5 955.5Q186 975 150 975Z",
        ["unlock"] = @"M213 800H88V787Q87 766 100.5 749.0Q114 732 134.5 727.0Q155 722 174.5 730.5Q194 739 205 757Q207 762 212.0 763.5Q217 765 221.5 762.5Q226 760 227.5 755.0Q229 750 227 745Q212 719 184.5 707.0Q157 695 128.0 702.5Q99 710 80.5 734.0Q62 758 63 788V805Q45 813 35.0 828.5Q25 844 25 862V937Q25 954 33.5 968.5Q42 983 56.5 991.5Q71 1000 87 1000H213Q229 1000 243.5 991.5Q258 983 266.5 968.5Q275 954 275 938V862Q275 846 266.5 831.5Q258 817 243.5 808.5Q229 800 213 800ZM250 937Q250 953 239.0 964.0Q228 975 212 975H88Q72 975 61.0 964.0Q50 953 50 938V862Q50 847 61.0 836.0Q72 825 87 825H213Q228 825 239.0 836.0Q250 847 250 862ZM150 875Q145 875 141.0 878.5Q137 882 137 888V912Q138 918 141.5 921.5Q145 925 150.0 925.0Q155 925 159.0 921.5Q163 918 163 912V887Q163 882 159.0 878.5Q155 875 150 875Z",
    };

    public static Control Icon(string name, double size = 18, IBrush? foreground = null)
    {
        if (IconPaths.TryGetValue(name, out var data))
        {
            return new Avalonia.Controls.Shapes.Path
            {
                Width = size,
                Height = size,
                Data = Geometry.Parse(data),
                Fill = foreground ?? Text,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };
        }

        return new TextBlock
        {
            Text = name,
            FontSize = size,
            Foreground = foreground ?? Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    public static Button IconButtonName(string iconName)
    {
        var content = iconName.Equals("more", StringComparison.OrdinalIgnoreCase)
            ? new TextBlock
            {
                Text = "•••",
                FontSize = 17,
                FontWeight = FontWeight.Bold,
                Foreground = Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                LineHeight = 17
            }
            : Icon(iconName, 17, Muted);

        return new Button
        {
            Content = content,
            Width = 36,
            Height = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = Muted,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0)
        };
    }

    public static Button MenuOption(string icon, string label, IBrush? foreground = null)
        => new()
        {
            MinHeight = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = foreground ?? Text,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 0),
            Content = H(10, Icon(icon, 16, foreground ?? Muted), TextBlock(label, 13, foreground ?? Text))
        };

    public static Border Centered(Control child, double maxWidth = 460)
        => new()
        {
            Background = Bg,
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    MaxWidth = maxWidth,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 24),
                    Child = child
                }
            }
        };

    public static void SoftFadeIn(Control control)
    {
        control.Opacity = 0;
        var steps = 10;
        var current = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(18) };
        timer.Tick += (_, _) =>
        {
            current++;
            control.Opacity = Math.Min(1, current / (double)steps);
            if (current >= steps)
                timer.Stop();
        };
        timer.Start();
    }

    public static void SoftSlideIn(Control control, double fromX = 28)
    {
        control.Opacity = 0;
        var transform = new TranslateTransform(fromX, 0);
        control.RenderTransform = transform;
        var steps = 12;
        var current = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            current++;
            var t = current / (double)steps;
            t = 1 - Math.Pow(1 - t, 3);
            control.Opacity = Math.Min(1, t);
            transform.X = fromX * (1 - t);
            if (current >= steps)
            {
                transform.X = 0;
                control.Opacity = 1;
                timer.Stop();
            }
        };
        timer.Start();
    }


    public static void SoftSlideOut(Control control, Action? completed = null, double toX = -18)
    {
        var transform = control.RenderTransform as TranslateTransform ?? new TranslateTransform(0, 0);
        control.RenderTransform = transform;
        control.Opacity = Math.Clamp(control.Opacity, 0, 1);
        var steps = 10;
        var current = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            current++;
            var t = current / (double)steps;
            t = 1 - Math.Pow(1 - t, 3);
            control.Opacity = Math.Max(0, 1 - t);
            transform.X = toX * t;
            if (current >= steps)
            {
                timer.Stop();
                completed?.Invoke();
            }
        };
        timer.Start();
    }
}
