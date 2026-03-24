// En DTOs/Geofence/GeofenceCheckResult.cs
namespace MDMServer.DTOs.Geofence;

public class GeofenceCheckResult
{
    public List<string> TriggeredEvents { get; set; } = new();
    public bool IsInsideAny { get; set; }
    public List<int> TriggeredGeofenceIds { get; set; } = new();
}