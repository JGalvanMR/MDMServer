namespace MDMServer.DTOs.Poll;

public record PollRequest(
    int?   BatteryLevel,
    long?  StorageAvailableMB,
    string? IpAddress,
    bool?  KioskModeEnabled,
    bool?  CameraDisabled
);

public record PollCommandDto(
    int    CommandId,
    string CommandType,
    string? Parameters,
    int    Priority
);

public record PollResponse(
    DateTime           ServerTime,
    List<PollCommandDto> Commands,
    int                PendingAfter  // cuántos quedan aún pendientes
);

public record CommandResultRequest(
    int     CommandId,
    bool    Success,
    string? ResultJson,
    string? ErrorMessage
);

public record HeartbeatRequest(
    int?   BatteryLevel,
    long?  StorageAvailableMB,
    bool   KioskModeEnabled,
    bool   CameraDisabled,
    string? IpAddress
);