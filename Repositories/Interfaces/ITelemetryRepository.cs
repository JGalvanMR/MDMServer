using MDMServer.DTOs.Telemetry;

namespace MDMServer.Repositories.Interfaces;

public interface ITelemetryRepository
{
    Task SaveTelemetryAsync(string deviceId, TelemetryReportRequest request);
    Task<List<TelemetrySnapshotRow>> GetTelemetryHistoryAsync(string deviceId, int hoursBack, int maxRows);
    Task<TelemetrySnapshotRow?> GetLatestTelemetryAsync(string deviceId);
    Task<List<object>> GetLocationHistoryAsync(string deviceId, int hoursBack);
    Task<object?> GetLatestScreenshotAsync(string deviceId);
    Task<List<object>> GetEventsAsync(string deviceId, int page, int pageSize);
    Task SaveScreenshotAsync(string deviceId, int commandId, string base64Image, int? width, int? height);
}