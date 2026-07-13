using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using Vanished.Shell;

namespace Vanished.UI;

public enum ToastType
{
    Info,
    Success,
    Error,
    Warning
}

public static class ToastService
{
    public static void Show(string message, string icon = "info", ToastType type = ToastType.Info, int duration = 3000)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        Dispatcher.UIThread.Post(async () =>
        {
            var shell = AppShellWindow.Instance;
            if (shell == null) return;

            var foreground = type switch
            {
                ToastType.Success => Ui.Success,
                ToastType.Error => Ui.Danger,
                ToastType.Warning => Ui.Warning,
                _ => Ui.Accent
            };

            var background = type switch
            {
                ToastType.Success => new SolidColorBrush(Color.FromArgb(42, Ui.Success.Color.R, Ui.Success.Color.G, Ui.Success.Color.B)),
                ToastType.Error => new SolidColorBrush(Color.FromArgb(42, Ui.Danger.Color.R, Ui.Danger.Color.G, Ui.Danger.Color.B)),
                ToastType.Warning => new SolidColorBrush(Color.FromArgb(42, Ui.Warning.Color.R, Ui.Warning.Color.G, Ui.Warning.Color.B)),
                _ => Ui.Surface2
            };

            var toast = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Background = background,
                BorderBrush = Ui.Border,
                BorderThickness = new Thickness(1),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(70, 0, 0, 0), OffsetY = 6 }),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        Ui.Icon(icon, 16, foreground),
                        new TextBlock
                        {
                            Text = message,
                            FontSize = 13,
                            Foreground = Ui.Text,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 320,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            shell.ToastLayer.Children.Add(toast);
            AnimateToastIn(toast);
            await Task.Delay(duration);
            AnimateToastOut(toast, () => shell.ToastLayer.Children.Remove(toast));
        });
    }

    private static void AnimateToastIn(Control toast)
    {
        toast.Opacity = 0;
        var transform = new TranslateTransform(0, 16);
        toast.RenderTransform = transform;
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            step++;
            var t = Math.Min(1, step / 12.0);
            t = 1 - Math.Pow(1 - t, 3);
            toast.Opacity = t;
            transform.Y = 16 * (1 - t);
            if (step >= 12) timer.Stop();
        };
        timer.Start();
    }

    private static void AnimateToastOut(Control toast, Action completed)
    {
        var transform = toast.RenderTransform as TranslateTransform ?? new TranslateTransform(0, 0);
        toast.RenderTransform = transform;
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            step++;
            var t = Math.Min(1, step / 10.0);
            t = 1 - Math.Pow(1 - t, 3);
            toast.Opacity = 1 - t;
            transform.Y = -10 * t;
            if (step >= 10)
            {
                timer.Stop();
                completed();
            }
        };
        timer.Start();
    }
}
