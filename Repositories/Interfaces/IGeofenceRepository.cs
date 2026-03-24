using MDMServer.DTOs.Geofence;
using MDMServer.Models;

namespace MDMServer.Repositories.Interfaces;

public interface IGeofenceRepository
{
    Task<List<Geofence>> GetByDeviceIdAsync(string deviceId);
    Task<Geofence?> GetByIdAsync(int id);
    Task<Geofence> CreateAsync(Geofence geofence);
    Task UpdateAsync(Geofence geofence);
    Task DeleteAsync(int id);
    Task<List<GeofenceStatus>> CheckLocationAsync(string deviceId, decimal lat, decimal lng, float? accuracy);
    Task RecordEventAsync(GeofenceEvent evt);
    Task<List<GeofenceEventDto>> GetEventsAsync(string deviceId, int? geofenceId = null, int limit = 50);
    Task<string?> GetLastEventTypeAsync(int geofenceId, string deviceId);
}