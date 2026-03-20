namespace MDMServer.Core;

// ── Mensajes Servidor → Dispositivo ──────────────────────────────────────────
public record WsCommandMessage(
    string Type,        // "COMMAND"
    int CommandId,
    string CommandType,
    string? Parameters,
    int Priority
);

public record WsPingMessage(string Type = "PING");

// ── Mensajes Dispositivo → Servidor ──────────────────────────────────────────
public record WsIncomingMessage(
    string Type,          // "PONG" | "RESULT" | "STATUS" | "HELLO"
                          // RESULT
    int? CommandId,
    bool? Success,
    string? ResultJson,
    string? ErrorMessage,
    // STATUS
    int? BatteryLevel,
    long? StorageAvailableMB,
    bool? KioskModeEnabled,
    bool? CameraDisabled,
    string? IpAddress,
    // HELLO
    string? DeviceId
);