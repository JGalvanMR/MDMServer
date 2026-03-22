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

public class SystemStatsDto
{
    public int      TotalDevices        { get; set; }
    public int      OnlineDevices       { get; set; }
    public int      PendingCommands     { get; set; }
    public int      ExecutedLast24h     { get; set; }
    public int      FailedLast24h       { get; set; }
    public double   AverageBatteryLevel { get; set; }
    public DateTime ServerTime          { get; set; }
}