namespace MDMServer.DTOs.Device;

public record DeviceDetailDto(
    int      Id,
    string   DeviceId,
    string?  DeviceName,
    string?  Model,
    string?  Manufacturer,
    string?  AndroidVersion,
    int?     ApiLevel,
    bool     IsActive,
    bool     IsOnline,
    DateTime? LastSeen,
    DateTime RegisteredAt,
    bool     KioskModeEnabled,
    bool     CameraDisabled,
    int?     BatteryLevel,
    long?    StorageAvailableMB,
    long?    TotalStorageMB,
    string?  IpAddress,
    long     PollCount,
    string?  Notes,
    int      PendingCommandsCount
);

public record DeviceListItemDto(
    int      Id,
    string   DeviceId,
    string?  DeviceName,
    string?  Model,
    bool     IsActive,
    bool     IsOnline,
    DateTime? LastSeen,
    int?     BatteryLevel,
    bool     KioskModeEnabled,
    bool     CameraDisabled
);