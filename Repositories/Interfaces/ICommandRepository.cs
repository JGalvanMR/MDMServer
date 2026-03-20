using MDMServer.Models;

namespace MDMServer.Repositories.Interfaces;

public interface ICommandRepository
{
    Task<Command>        CreateAsync(Command command);
    Task<Command?>       GetByIdAsync(int id);
    Task<List<Command>>  GetPendingByDeviceIdAsync(string deviceId, int maxCount = 10);
    Task<List<Command>>  GetByDeviceIdAsync(string deviceId, int page = 1, int pageSize = 20);
    Task<int>            GetPendingCountByDeviceIdAsync(string deviceId);
    Task<int>            GetTotalCountByDeviceIdAsync(string deviceId);
    Task                 MarkAsSentAsync(int id);
    Task                 MarkAsExecutingAsync(int id);
    Task                 MarkAsExecutedAsync(int id, string? resultJson);
    Task                 MarkAsFailedAsync(int id, string errorMessage);
    Task                 CancelAsync(int id, string reason);
    Task<List<Command>>  GetByDeviceIdAndStatusAsync(string deviceId, string status);
    Task<int>            CancelAllPendingByDeviceIdAsync(string deviceId);
}