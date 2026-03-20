namespace MDMServer.DTOs;

public record SendCommandRequest(
    string  DeviceId,
    string  CommandType,
    string? Parameters  // JSON string opcional, ej: {"enabled": true}
);

public record SendCommandResponse(
    bool   Success,
    int    CommandId,
    string Message
);

public record CommandStatusDto(
    int      Id,
    string   DeviceId,
    string   CommandType,
    string   Status,
    DateTime CreatedAt,
    DateTime? ExecutedAt,
    string?  Result,
    string?  ErrorMessage
);

public record DeviceDetailDto(
    int      Id,
    string   DeviceId,
    string?  DeviceName,
    string?  Model,
    string?  Manufacturer,
    string?  AndroidVersion,
    int?     ApiLevel,
    bool     IsActive,
    DateTime? LastSeen,
    DateTime RegisteredAt,
    bool     KioskModeEnabled,
    bool     CameraDisabled,
    int?     BatteryLevel,
    long?    StorageAvailableMB,
    string?  IpAddress,
    bool     IsOnline           // LastSeen < 2 minutos
);