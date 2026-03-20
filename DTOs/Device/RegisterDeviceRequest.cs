namespace MDMServer.DTOs.Device;

public record RegisterDeviceRequest(
    string  DeviceId,
    string? DeviceName,
    string? Model,
    string? Manufacturer,
    string? AndroidVersion,
    int?    ApiLevel
);

public record RegisterDeviceResponse(
    string  DeviceId,
    string  Token,
    string  Message,
    bool    IsNewRegistration
);