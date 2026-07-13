using System.Threading.Tasks;
using Vanished.API.Models;

namespace Vanished.API.Services;

public sealed class DeviceManagementService : BaseService
{
    public async Task<MyDevicesResponse?> GetMyDevicesAsync()
        => await GetAsync<MyDevicesResponse>("api/user/devices");

    public async Task<ApiResponse?> RenameDeviceAsync(string deviceId, string name)
        => await PostAsync<object, ApiResponse>("api/user/devices/rename", new
        {
            DeviceId = deviceId ?? string.Empty,
            Name = name ?? string.Empty
        });

    public async Task<ApiResponse?> RevokeDeviceAsync(string deviceId)
        => await PostAsync<object, ApiResponse>("api/user/devices/revoke", new
        {
            DeviceId = deviceId ?? string.Empty
        });

    public async Task<RevokeDevicesResponse?> RevokeOtherDevicesAsync()
        => await PostAsync<object, RevokeDevicesResponse>("api/user/devices/revoke-others", new { });
}
