using MDMServer.DTOs.Geofence;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Services;

public interface IGeofenceService
{
    Task<List<Geofence>> GetGeofencesAsync(string deviceId);
    Task<Geofence> CreateGeofenceAsync(string deviceId, CreateGeofenceRequest request);
    Task DeleteGeofenceAsync(int id);
    Task<CheckLocationResponse> CheckLocationAsync(string deviceId, decimal? lat, decimal? lng, float? accuracy);
    Task<List<GeofenceEventDto>> GetEventsAsync(string deviceId, int? geofenceId);
}

public class GeofenceService : IGeofenceService
{
    private readonly IGeofenceRepository _repo;
    private readonly ILogger<GeofenceService> _logger;

    public GeofenceService(IGeofenceRepository repo, ILogger<GeofenceService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public Task<List<Geofence>> GetGeofencesAsync(string deviceId) =>
        _repo.GetByDeviceIdAsync(deviceId);

    public async Task<Geofence> CreateGeofenceAsync(string deviceId, CreateGeofenceRequest request)
    {
        var geofence = new Geofence
        {
            DeviceId = deviceId,
            Name = request.Name,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            IsEntry = request.IsEntry,
            IsExit = request.IsExit
        };
        return await _repo.CreateAsync(geofence);
    }

    public async Task DeleteGeofenceAsync(int id)
    {
        await _repo.DeleteAsync(id);
    }

    public async Task<CheckLocationResponse> CheckLocationAsync(string deviceId, decimal? lat, decimal? lng, float? accuracy)
    {
        if (!lat.HasValue || !lng.HasValue)
            return new CheckLocationResponse(new List<GeofenceStatusDto>(), new List<string>());

        // Extraer valores no-nullable para usar en el resto del método
        var latValue = lat.Value;
        var lngValue = lng.Value;

        var statuses = await _repo.CheckLocationAsync(deviceId, latValue, lngValue, accuracy);
        var triggeredEvents = new List<string>();
        var resultStatuses = new List<GeofenceStatusDto>();

        foreach (var status in statuses)
        {
            // Obtener último evento conocido para esta geofence
            var lastEvent = await _repo.GetLastEventTypeAsync(status.GeofenceId, deviceId);
            var lastWasInside = lastEvent == "ENTER";
            var currentlyInside = status.IsInside;

            // Detectar transiciones
            if (!lastWasInside && currentlyInside && status.IsEntry)
            {
                // Entrada detectada
                await _repo.RecordEventAsync(new GeofenceEvent
                {
                    GeofenceId = status.GeofenceId,
                    DeviceId = deviceId,
                    EventType = "ENTER",
                    Latitude = latValue,
                    Longitude = lngValue,
                    AccuracyMeters = accuracy
                });
                triggeredEvents.Add($"ENTER:{status.Name}");
                _logger.LogInformation("Geofence ENTER: {DeviceId} entró a {Geofence}", deviceId, status.Name);
            }
            else if (lastWasInside && !currentlyInside && status.IsExit)
            {
                // Salida detectada
                await _repo.RecordEventAsync(new GeofenceEvent
                {
                    GeofenceId = status.GeofenceId,
                    DeviceId = deviceId,
                    EventType = "EXIT",
                    Latitude = latValue,
                    Longitude = lngValue,
                    AccuracyMeters = accuracy
                });
                triggeredEvents.Add($"EXIT:{status.Name}");
                _logger.LogInformation("Geofence EXIT: {DeviceId} salió de {Geofence}", deviceId, status.Name);
            }

            resultStatuses.Add(new GeofenceStatusDto(
                status.GeofenceId,
                status.Name,
                status.DistanceMeters,
                status.IsInside,
                status.IsEntry,
                status.IsExit,
                lastEvent,
                null // LastEventTime se podría agregar si se modifica el SP
            ));
        }

        return new CheckLocationResponse(resultStatuses, triggeredEvents);
    }

    public Task<List<GeofenceEventDto>> GetEventsAsync(string deviceId, int? geofenceId = null) =>
        _repo.GetEventsAsync(deviceId, geofenceId);
}
