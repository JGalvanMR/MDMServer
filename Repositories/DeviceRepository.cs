using Dapper;
using MDMServer.Data;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<DeviceRepository> _logger;

    public DeviceRepository(IDbConnectionFactory factory, ILogger<DeviceRepository> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task<Device?> GetByDeviceIdAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<Device>(
            "SELECT * FROM dbo.Devices WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId }
        );
    }

    public async Task<Device?> GetByTokenAsync(string token)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<Device>(
            "SELECT * FROM dbo.Devices WHERE Token = @Token AND IsActive = 1",
            new { Token = token }
        );
    }

    public async Task<Device> CreateAsync(Device device)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<int>(@"
            INSERT INTO dbo.Devices
                (DeviceId, DeviceName, Model, Manufacturer, AndroidVersion,
                 ApiLevel, Token, TokenCreatedAt, RegisteredAt, UpdatedAt, IsActive)
            VALUES
                (@DeviceId, @DeviceName, @Model, @Manufacturer, @AndroidVersion,
                 @ApiLevel, @Token, GETUTCDATE(), GETUTCDATE(), GETUTCDATE(), 1);
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new {
                device.DeviceId, device.DeviceName, device.Model,
                device.Manufacturer, device.AndroidVersion,
                device.ApiLevel, device.Token
            }
        );
        device.Id = id;
        _logger.LogInformation("Dispositivo creado: {DeviceId} (DbId={Id})", device.DeviceId, id);
        return device;
    }

    public async Task UpdateLastSeenAsync(string deviceId, int? battery,
        long? storageMB, string? ip, bool? kioskMode, bool? cameraDisabled)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Devices SET
                LastSeen             = GETUTCDATE(),
                UpdatedAt            = GETUTCDATE(),
                PollCount            = PollCount + 1,
                BatteryLevel         = COALESCE(@Battery, BatteryLevel),
                StorageAvailableMB   = COALESCE(@StorageMB, StorageAvailableMB),
                IpAddress            = COALESCE(@Ip, IpAddress),
                KioskModeEnabled     = COALESCE(@KioskMode, KioskModeEnabled),
                CameraDisabled       = COALESCE(@CameraDisabled, CameraDisabled)
            WHERE DeviceId = @DeviceId",
            new {
                DeviceId       = deviceId,
                Battery        = battery,
                StorageMB      = storageMB,
                Ip             = ip,
                KioskMode      = kioskMode,
                CameraDisabled = cameraDisabled
            }
        );
    }

    public async Task UpdateNotesAsync(string deviceId, string notes)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE dbo.Devices SET Notes = @Notes, UpdatedAt = GETUTCDATE() WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId, Notes = notes }
        );
    }

    public async Task DeactivateAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE dbo.Devices SET IsActive = 0, UpdatedAt = GETUTCDATE() WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId }
        );
        _logger.LogWarning("Dispositivo desactivado: {DeviceId}", deviceId);
    }

    public async Task<List<Device>> GetAllAsync(bool? onlyActive = true)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var sql = onlyActive == true
            ? "SELECT * FROM dbo.Devices WHERE IsActive = 1 ORDER BY LastSeen DESC"
            : "SELECT * FROM dbo.Devices ORDER BY RegisteredAt DESC";

        var result = await conn.QueryAsync<Device>(sql);
        return result.ToList();
    }

    public async Task<int> GetTotalCountAsync(bool? onlyActive = true)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            onlyActive == true
                ? "SELECT COUNT(*) FROM dbo.Devices WHERE IsActive = 1"
                : "SELECT COUNT(*) FROM dbo.Devices"
        );
    }

    public async Task<bool> ExistsAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM dbo.Devices WHERE DeviceId = @DeviceId",
            new { DeviceId = deviceId }
        ) > 0;
    }
}