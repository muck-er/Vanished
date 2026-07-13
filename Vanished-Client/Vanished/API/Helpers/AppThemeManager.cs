using Avalonia.Media;

namespace Vanished.API.Helpers;

public sealed class AppThemeOption
{
    public string Key { get; init; } = "dark";
    public string DisplayName { get; init; } = "Escuro";
    public bool IsLight { get; init; }
}

public static class AppThemeManager
{
    public static void Initialize() { }

    public static IBrush GetBrush(string key, string fallback)
        => Brush.Parse(string.IsNullOrWhiteSpace(fallback) ? "#FFFFFF" : fallback);
}
