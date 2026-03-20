namespace MDMServer.DTOs.Command;

public record SendCommandRequest(
    string  DeviceId,
    string  CommandType,
    string? Parameters,   // JSON string opcional
    int?    Priority,     // 1–10, default 5
    int?    ExpiresInMinutes  // null = no expira
);

public record SendCommandResponse(
    int    CommandId,
    string Message
);

public record CommandStatusDto(
    int      Id,
    string   DeviceId,
    string   CommandType,
    string?  Parameters,
    string   Status,
    int      Priority,
    DateTime  CreatedAt,
    DateTime? SentAt,
    DateTime? ExecutedAt,
    DateTime? ExpiresAt,
    string?  Result,
    string?  ErrorMessage,
    int      RetryCount
);

public record CancelCommandRequest(string Reason);

public record SystemStatsDto(
    int      TotalDevices,
    int      OnlineDevices,
    int      PendingCommands,
    int      ExecutedLast24h,
    int      FailedLast24h,
    DateTime ServerTime
);