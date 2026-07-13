using Avalonia.Controls;

namespace Vanished.API.Helpers;

public static class ResponsiveWindowHelper
{
    public static void Apply(
        Window window,
        double widthRatio = 0.30,
        double heightRatio = 0.88,
        double minWidth = 400,
        double maxWidth = 560,
        double minHeight = 540,
        double maxHeight = 960,
        bool allowAutoMaximize = false)
    {
        window.MinWidth = minWidth;
        window.MinHeight = minHeight;
        window.Width = maxWidth;
        window.Height = maxHeight;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
