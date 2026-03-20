using MDMServer.Models;

namespace MDMServer.Repositories.Interfaces;

public interface IDeviceRepository
{
    Task<Device?>       GetByDeviceIdAsync(string deviceId);
    Task<Device?>       GetByTokenAsync(string token);
    Task<Device>        CreateAsync(Device device);
    Task                UpdateLastSeenAsync(string deviceId, int? battery,
                            long? storageMB, string? ip, bool? kioskMode, bool? cameraDisabled);
    Task                UpdateNotesAsync(string deviceId, string notes);
    Task                DeactivateAsync(string deviceId);
    Task<List<Device>>  GetAllAsync(bool? onlyActive = true);
    Task<int>           GetTotalCountAsync(bool? onlyActive = true);
    Task<bool>          ExistsAsync(string deviceId);
}