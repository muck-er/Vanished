using Vanished.API.Services;

namespace Vanished;

public static class ApiService
{
    public static AuthService Auth { get; } = new();
    public static ChatService Chat { get; } = new();
    public static MessageService Messages { get; } = new();
    public static DeviceManagementService Devices { get; } = new();
    public static WebSocketService WebSocket { get; } = new();
    public static ConnectionMonitorService Connection { get; } = new(WebSocket);
}
