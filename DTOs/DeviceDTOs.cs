using System.ComponentModel.DataAnnotations;

namespace MDMServer.DTOs;

// ── REGISTER ────────────────────────────────────────────────
public record RegisterDeviceRequest(
    string DeviceId,
    string? DeviceName,
    string? Model,
    string? Manufacturer,
    string? AndroidVersion,
    int? ApiLevel
);

public record RegisterDeviceResponse(
    bool Success,
    string DeviceId,
    string Token,
    string Message
);

// ── POLL ─────────────────────────────────────────────────────
public record PollRequest(
    int? BatteryLevel,
    long? StorageAvailableMB,
    string? IpAddress
);

public record PollCommandDto(
    int CommandId,
    string CommandType,
    string? Parameters
);

public record PollResponse(
    bool Success,
    DateTime ServerTime,
    List<PollCommandDto> Commands
);

// ── COMMAND RESULT ───────────────────────────────────────────
public record CommandResultRequest(
    int CommandId,
    bool Success,
[StringLength(5_000_000)]
    string? ResultJson,
    string? ErrorMessage
);

public record CommandResultResponse(
    bool Success,
    string Message
);

// ── HEARTBEAT ────────────────────────────────────────────────
public record HeartbeatRequest(
    int? BatteryLevel,
    long? StorageAvailableMB,
    bool KioskModeEnabled,
    bool CameraDisabled,
    string? IpAddress
);