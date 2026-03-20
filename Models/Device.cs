namespace MDMServer.Models;

public class Device
{
    public int       Id                 { get; set; }
    public string    DeviceId           { get; set; } = string.Empty;
    public string?   DeviceName         { get; set; }
    public string?   Model              { get; set; }
    public string?   Manufacturer       { get; set; }
    public string?   AndroidVersion     { get; set; }
    public int?      ApiLevel           { get; set; }
    public string    Token              { get; set; } = string.Empty;
    public DateTime  TokenCreatedAt     { get; set; }
    public bool      IsActive           { get; set; } = true;
    public DateTime? LastSeen           { get; set; }
    public DateTime  RegisteredAt       { get; set; }
    public DateTime  UpdatedAt          { get; set; }
    public bool      KioskModeEnabled   { get; set; }
    public bool      CameraDisabled     { get; set; }
    public int?      BatteryLevel       { get; set; }
    public long?     StorageAvailableMB { get; set; }
    public long?     TotalStorageMB     { get; set; }
    public string?   IpAddress          { get; set; }
    public long      PollCount          { get; set; }
    public string?   Notes              { get; set; }

    public bool IsOnline =>
        LastSeen.HasValue &&
        (DateTime.UtcNow - LastSeen.Value).TotalMinutes < 2;
}