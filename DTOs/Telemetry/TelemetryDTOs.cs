// DTOs/Telemetry/TelemetryDTOs.cs
namespace MDMServer.DTOs.Telemetry;

public record TelemetryReportRequest(
    int? BatteryLevel,
    bool BatteryCharging,
    long? StorageAvailableMB,
    long? TotalStorageMB,
    double? Latitude,
    double? Longitude,
    float? LocationAccuracy,
    long? LocationAgeSeconds,
    string? ConnectionType,
    string? Ssid,
    int? SignalStrength,
    string? IpAddress,
    bool KioskModeEnabled,
    bool CameraDisabled,
    bool ScreenOn,
    long UptimeHours,
    long? RamUsedMB,
    float? CpuTemp
);

public class TelemetrySnapshotDto
{
    public int Id { get; init; }
    public string DeviceId { get; init; } = "";
    public int? BatteryLevel { get; init; }
    public bool BatteryCharging { get; init; }
    public long? StorageAvailableMB { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public float? LocationAccuracy { get; init; }
    public string? ConnectionType { get; init; }
    public string? Ssid { get; init; }
    public int? SignalStrength { get; init; }
    public string? IpAddress { get; init; }
    public bool KioskModeEnabled { get; init; }
    public bool ScreenOn { get; init; }
    public long? RamUsedMB { get; init; }
    public float? CpuTemp { get; init; }
    public DateTime RecordedAt { get; init; }
}

public class LocationPointDto
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public float? Accuracy { get; init; }
    public string? IpAddress { get; init; }
    public DateTime RecordedAt { get; init; }
}

public record DeviceEventDto(
    int Id,
    string DeviceId,
    string EventType,
    string Severity,
    string? Title,
    string? Details,
    int? CommandId,
    DateTime OccurredAt
);

public record ScreenshotDto(
    int Id,
    string DeviceId,
    int? CommandId,
    string ImageBase64,
    int? FileSizeKB,
    DateTime TakenAt
);

// Agregar al final de DTOs/Telemetry/TelemetryDTOs.cs
public class TelemetrySnapshotRow
{
    public int Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int? BatteryLevel { get; set; }
    public bool BatteryCharging { get; set; }
    public long? StorageAvailableMB { get; set; }
    public long? TotalStorageMB { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public float? LocationAccuracy { get; set; }
    public string? ConnectionType { get; set; }
    public string? Ssid { get; set; }
    public int? SignalStrength { get; set; }
    public string? IpAddress { get; set; }
    public bool KioskModeEnabled { get; set; }
    public bool CameraDisabled { get; set; }
    public bool ScreenOn { get; set; }
    public long UptimeHours { get; set; }
    public long? RamUsedMB { get; set; }
    public float? CpuTemp { get; set; }
    public DateTime RecordedAt { get; set; }
}