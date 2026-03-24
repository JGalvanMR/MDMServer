using Dapper;
using MDMServer.Data;
using MDMServer.DTOs.Geofence;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Repositories;

public class GeofenceRepository : IGeofenceRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<GeofenceRepository> _logger;

    public GeofenceRepository(IDbConnectionFactory factory, ILogger<GeofenceRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<List<Geofence>> GetByDeviceIdAsync(string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var result = await conn.QueryAsync<Geofence>(
            "SELECT * FROM dbo.Geofences WHERE DeviceId = @DeviceId ORDER BY CreatedAt DESC",
            new { DeviceId = deviceId }
        );
        return result.ToList();
    }

    public async Task<Geofence?> GetByIdAsync(int id)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<Geofence>(
            "SELECT * FROM dbo.Geofences WHERE Id = @Id",
            new { Id = id }
        );
    }

    public async Task<Geofence> CreateAsync(Geofence geofence)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<int>(@"
            INSERT INTO dbo.Geofences 
                (DeviceId, Name, Latitude, Longitude, RadiusMeters, IsEntry, IsExit, IsActive, CreatedAt, UpdatedAt)
            VALUES 
                (@DeviceId, @Name, @Latitude, @Longitude, @RadiusMeters, @IsEntry, @IsExit, 1, GETUTCDATE(), GETUTCDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            geofence
        );
        geofence.Id = id;
        geofence.IsActive = true;
        geofence.CreatedAt = DateTime.UtcNow;
        _logger.LogInformation("Geofence creada: {Name} para {DeviceId}", geofence.Name, geofence.DeviceId);
        return geofence;
    }

    public async Task UpdateAsync(Geofence geofence)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            UPDATE dbo.Geofences SET
                Name = @Name,
                Latitude = @Latitude,
                Longitude = @Longitude,
                RadiusMeters = @RadiusMeters,
                IsEntry = @IsEntry,
                IsExit = @IsExit,
                IsActive = @IsActive,
                UpdatedAt = GETUTCDATE()
            WHERE Id = @Id",
            geofence
        );
    }

    public async Task DeleteAsync(int id)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM dbo.Geofences WHERE Id = @Id",
            new { Id = id }
        );
        _logger.LogInformation("Geofence {Id} eliminada", id);
    }

    public async Task<List<GeofenceStatus>> CheckLocationAsync(string deviceId, decimal lat, decimal lng, float? accuracy)
    {
        using var conn = await _factory.CreateConnectionAsync();
        // Usar el SP que calcula distancia con Haversine
        var result = await conn.QueryAsync<GeofenceStatus>(
            "EXEC dbo.sp_CheckGeofenceStatus @DeviceId, @Latitude, @Longitude, @AccuracyMeters",
            new { DeviceId = deviceId, Latitude = lat, Longitude = lng, AccuracyMeters = accuracy }
        );
        return result.ToList();
    }

    public async Task RecordEventAsync(GeofenceEvent evt)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO dbo.GeofenceEvents 
                (GeofenceId, DeviceId, EventType, Latitude, Longitude, AccuracyMeters, RecordedAt)
            VALUES 
                (@GeofenceId, @DeviceId, @EventType, @Latitude, @Longitude, @AccuracyMeters, GETUTCDATE())",
            evt
        );
    }

    public async Task<List<GeofenceEventDto>> GetEventsAsync(string deviceId, int? geofenceId = null, int limit = 50)
    {
        using var conn = await _factory.CreateConnectionAsync();
        var sql = @"
            SELECT TOP (@Limit) 
                e.Id, e.GeofenceId, g.Name as GeofenceName, e.EventType,
                e.Latitude, e.Longitude, e.AccuracyMeters, e.RecordedAt
            FROM dbo.GeofenceEvents e
            INNER JOIN dbo.Geofences g ON e.GeofenceId = g.Id
            WHERE e.DeviceId = @DeviceId
            " + (geofenceId.HasValue ? "AND e.GeofenceId = @GeofenceId" : "") + @"
            ORDER BY e.RecordedAt DESC";

        var result = await conn.QueryAsync<GeofenceEventDto>(
            sql,
            new { DeviceId = deviceId, GeofenceId = geofenceId, Limit = limit }
        );
        return result.ToList();
    }

    public async Task<string?> GetLastEventTypeAsync(int geofenceId, string deviceId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<string>(
            "EXEC dbo.sp_GetLastGeofenceEvent @GeofenceId, @DeviceId",
            new { GeofenceId = geofenceId, DeviceId = deviceId }
        );
    }
}