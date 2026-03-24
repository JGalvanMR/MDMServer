namespace MDMServer.DTOs.Geofence;

public record GeofenceDto(
    int Id,
    string DeviceId,
    string Name,
    decimal Latitude,
    decimal Longitude,
    int RadiusMeters,
    bool IsEntry,
    bool IsExit,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateGeofenceRequest(
    string Name,
    decimal Latitude,
    decimal Longitude,
    int RadiusMeters,
    bool IsEntry,
    bool IsExit
);

public record GeofenceEventDto(
    int Id,
    int GeofenceId,
    string GeofenceName,
    string EventType,
    decimal Latitude,
    decimal Longitude,
    double? AccuracyMeters,
    DateTime RecordedAt
);

public record GeofenceStatusDto(
    int GeofenceId,
    string Name,
    double DistanceMeters,
    bool IsInside,
    bool IsEntry,
    bool IsExit,
    string? LastEventType,
    DateTime? LastEventTime
);

public record CheckLocationRequest(
    decimal Latitude,
    decimal Longitude,
    float? Accuracy
);

public record CheckLocationResponse(
    List<GeofenceStatusDto> Statuses,
    List<string> TriggeredEvents // "ENTER:Oficina", "EXIT:Casa", etc.
);