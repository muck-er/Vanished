namespace Vanished.API.Helpers;

public sealed class DisplayResolutionProfile
{
    public double ScreenWidth { get; init; } = 1280;
    public double ScreenHeight { get; init; } = 800;
    public double WorkAreaWidth { get; init; } = 1280;
    public double WorkAreaHeight { get; init; } = 800;
    public bool IsVerySmallScreen { get; init; }
    public bool IsSmallScreen { get; init; }
    public bool IsCompactScreen { get; init; }
    public bool PreferMaximizedMainWindow { get; init; }
    public double MainSidebarWidth { get; init; } = 72;
    public double ChatSidebarWidth { get; init; } = 330;
    public double BubbleMaxWidth { get; init; } = 620;
    public double UiScale { get; init; } = 1;
    public double ContentPadding { get; init; } = 20;
}

public static class DisplayResolutionHelper
{
    public static DisplayResolutionProfile GetProfile() => new();
}
