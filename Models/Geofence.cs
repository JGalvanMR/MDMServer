namespace MDMServer.Models;

public class Geofence
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int RadiusMeters { get; set; } = 100;
    public bool IsEntry { get; set; } = true;
    public bool IsExit { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GeofenceEvent
{
    public int Id { get; set; }
    public int GeofenceId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // "ENTER" o "EXIT"
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTime RecordedAt { get; set; }
}

// Estado calculado para el frontend
public class GeofenceStatus
{
    public int GeofenceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double DistanceMeters { get; set; }
    public bool IsInside { get; set; }
    public bool IsEntry { get; set; }
    public bool IsExit { get; set; }
}