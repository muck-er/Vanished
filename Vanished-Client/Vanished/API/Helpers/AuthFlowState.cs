namespace Vanished.API.Helpers;

public static class AuthFlowState
{
    public static string? PendingEmail { get; set; }

    public static void Clear() => PendingEmail = null;
}
