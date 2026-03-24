using Dapper;
using MDMServer.Data;
using MDMServer.DTOs.Telemetry;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Repositories;

public class TelemetryRepository : ITelemetryRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<TelemetryRepository> _logger;

    public TelemetryRepository(IDbConnectionFactory factory,
        ILogger<TelemetryRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task SaveTelemetryAsync(string deviceId, TelemetryReportRequest r)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO dbo.DeviceTelemetry
                (DeviceId, BatteryLevel, BatteryCharging, StorageAvailableMB, TotalStorageMB,
                 Latitude, Longitude, LocationAccuracy, LocationAgeSeconds,
                 ConnectionType, Ssid, SignalStrength, IpAddress,
                 KioskModeEnabled, CameraDisabled, ScreenOn, UptimeHours,
                 RamUsedMB, CpuTemp, RecordedAt)
            VALUES
                (@DeviceId, @BatteryLevel, @BatteryCharging, @StorageAvailableMB, @TotalStorageMB,
                 @Latitude, @Longitude, @LocationAccuracy, @LocationAgeSeconds,
                 @ConnectionType, @Ssid, @SignalStrength, @IpAddress,
                 @KioskModeEnabled, @CameraDisabled, @ScreenOn, @UptimeHours,
                 @RamUsedMB, @CpuTemp, GETUTCDATE())",
            new
            {
                DeviceId = deviceId,
                r.BatteryLevel,
                r.BatteryCharging,
                r.StorageAvailableMB,
                r.TotalStorageMB,
                r.Latitude,
                r.Longitude,
                r.LocationAccuracy,
                r.LocationAgeSeconds,
                r.ConnectionType,
                r.Ssid,
                r.SignalStrength,
                r.IpAddress,
                r.KioskModeEnabled,
                r.CameraDisabled,
                r.ScreenOn,
                r.UptimeHours,
                r.RamUsedMB,
                r.CpuTemp
            }
        );
    }

    public async Task<List<TelemetrySnapshotRow>> GetTelemetryHistoryAsync(
        string deviceId, int hoursBack, int maxRows)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var rows = await conn.QueryAsync<TelemetrySnapshotRow>(
            "EXEC dbo.sp_GetTelemetryHistory @DeviceId, @HoursBack, @MaxRows",
            new { DeviceId = deviceId, HoursBack = hoursBack, MaxRows = maxRows }
        );
        return rows.ToList();
    }

    public async Task<TelemetrySnapshotRow?> GetLatestTelemetryAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<TelemetrySnapshotRow>(
            "EXEC dbo.sp_GetLatestTelemetry @DeviceId",
            new { DeviceId = deviceId }
        );
    }

    public async Task<List<object>> GetLocationHistoryAsync(string deviceId, int hoursBack)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var rows = await conn.QueryAsync<object>(@"
            SELECT Latitude, Longitude, LocationAccuracy, IpAddress, RecordedAt
            FROM dbo.DeviceTelemetry
            WHERE DeviceId  = @DeviceId
              AND Latitude   IS NOT NULL
              AND Longitude  IS NOT NULL
              AND RecordedAt >= DATEADD(HOUR, -@HoursBack, GETUTCDATE())
            ORDER BY RecordedAt DESC",
            new { DeviceId = deviceId, HoursBack = hoursBack }
        );
        return rows.ToList();
    }

    public async Task<object?> GetLatestScreenshotAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<object>(@"
            SELECT TOP 1 Id, DeviceId, Message AS Data, CreatedAt
            FROM dbo.DeviceLogs
            WHERE DeviceId = @DeviceId AND Category = 'SCREENSHOT'
            ORDER BY CreatedAt DESC",
            new { DeviceId = deviceId }
        );
    }

    public async Task<List<object>> GetEventsAsync(
        string deviceId, int page, int pageSize)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var offset = (page - 1) * pageSize;
        var rows = await conn.QueryAsync<object>(@"
            SELECT Id, DeviceId, Level, Category, Message, CreatedAt
            FROM dbo.DeviceLogs
            WHERE DeviceId = @DeviceId
            ORDER BY CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            new { DeviceId = deviceId, Offset = offset, PageSize = pageSize }
        );
        return rows.ToList();
    }

    public async Task SaveScreenshotAsync(
        string deviceId, int commandId, string base64Image, int? width, int? height)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandId,
            width,
            height,
            sizeKb = base64Image.Length * 3 / 4 / 1024,
            imageBase64 = base64Image
        });
        await conn.ExecuteAsync(@"
            INSERT INTO dbo.DeviceLogs (DeviceId, Level, Category, Message, CreatedAt)
            VALUES (@DeviceId, 'INFO', 'SCREENSHOT', @Message, GETUTCDATE())",
            new { DeviceId = deviceId, Message = json }
        );
        _logger.LogInformation(
            "Screenshot guardado DeviceId={DeviceId} CommandId={CommandId}",
            deviceId, commandId);
    }
}