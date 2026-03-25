using Dapper;
using MDMServer.Core;
using MDMServer.Core.Exceptions;
using MDMServer.Data;
using MDMServer.DTOs.Command;
using MDMServer.DTOs.Device;
using MDMServer.DTOs.Poll;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;
using MDMServer.Services.Interfaces;

namespace MDMServer.Services;

public interface IDeviceService
{
    Task<(Device Device, bool IsNew)> AuthenticateOrThrowAsync(string token);
    Task<RegisterDeviceResponse> RegisterAsync(RegisterDeviceRequest request, string clientIp);
    Task UpdateHeartbeatAsync(string deviceId, HeartbeatRequest request, string? ip);
    Task<List<DeviceListItemDto>> GetAllAsync();
    Task<DeviceDetailDto> GetDetailAsync(string deviceId);
    Task DeactivateAsync(string deviceId);
    Task UpdateNotesAsync(string deviceId, string notes);
    Task<SystemStatsDto> GetStatsAsync();
    Task<bool> ExistsAsync(string deviceId);
}

public class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly ICommandRepository _commandRepo;
    private readonly ITokenService _tokenService;
    private readonly ILogger<DeviceService> _logger;
    private readonly IDbConnectionFactory _dbFactory;

    // ── CORRECCIÓN: clase concreta para mapeo Dapper ──────────────────────────
    // Dapper no puede mapear ValueTuple<T1,T2,T3> por nombre de columna.
    // El QuerySingleAsync<(string, string, bool)> original lanzaba InvalidCastException
    // en runtime porque Dapper intenta asignar Item1/Item2/Item3, no los nombres reales.
    private sealed class UpsertDeviceResult
    {
        public string DeviceId  { get; set; } = string.Empty;
        public string Token     { get; set; } = string.Empty;
        public bool   IsNew     { get; set; }
    }

    public DeviceService(
        IDeviceRepository deviceRepo,
        ICommandRepository commandRepo,
        ITokenService tokenService,
        IDbConnectionFactory dbFactory,
        ILogger<DeviceService> logger)
    {
        _deviceRepo   = deviceRepo;
        _commandRepo  = commandRepo;
        _tokenService = tokenService;
        _dbFactory    = dbFactory;
        _logger       = logger;
    }

    public async Task<(Device Device, bool IsNew)> AuthenticateOrThrowAsync(string token)
    {
        if (!_tokenService.ValidateTokenFormat(token))
            throw new UnauthorizedException("Formato de token inválido.");

        var device = await _deviceRepo.GetByTokenAsync(token)
                     ?? throw new UnauthorizedException("Token no reconocido.");

        if (!device.IsActive)
            throw new DeviceInactiveException(device.DeviceId);

        return (device, false);
    }

    public async Task<bool> ExistsAsync(string deviceId)
    {
        return await _deviceRepo.ExistsAsync(deviceId);
    }

    public async Task<RegisterDeviceResponse> RegisterAsync(
        RegisterDeviceRequest request, string clientIp)
    {
        using var conn = await _dbFactory.CreateConnectionAsync();

        // ── CORRECCIÓN: usar clase concreta UpsertDeviceResult en lugar de ValueTuple ──
        // ValueTuple causaba crash silencioso: Dapper mapeaba por posición (Item1/Item2/Item3)
        // y los valores llegaban como default(string)/default(bool).
        var result = await conn.QuerySingleAsync<UpsertDeviceResult>(@"
            EXEC dbo.sp_UpsertDevice
                @DeviceId, @DeviceName, @Model, @Manufacturer,
                @AndroidVersion, @ApiLevel, @Token, @IpAddress",
            new
            {
                DeviceId     = request.DeviceId,
                DeviceName   = request.DeviceName?.Trim(),
                Model        = request.Model?.Trim(),
                Manufacturer = request.Manufacturer?.Trim(),
                AndroidVersion = request.AndroidVersion,
                ApiLevel     = request.ApiLevel,
                Token        = _tokenService.GenerateDeviceToken(),
                IpAddress    = clientIp
            }
        );

        _logger.LogInformation(
            "{Action} dispositivo: {DeviceId} IP={Ip}",
            result.IsNew ? "Nuevo" : "Re-registro", request.DeviceId, clientIp);

        return new RegisterDeviceResponse(
            result.DeviceId,
            result.Token,
            result.IsNew ? "Dispositivo registrado correctamente." : "Token existente devuelto.",
            result.IsNew
        );
    }

    public async Task UpdateHeartbeatAsync(
        string deviceId, HeartbeatRequest request, string? ip)
    {
        await _deviceRepo.UpdateLastSeenAsync(
            deviceId,
            request.BatteryLevel,
            request.StorageAvailableMB,
            ip ?? request.IpAddress,
            request.KioskModeEnabled,
            request.CameraDisabled
        );
    }

    public async Task<List<DeviceListItemDto>> GetAllAsync()
    {
        var devices = await _deviceRepo.GetAllAsync();
        return devices.Select(d => new DeviceListItemDto(
            d.Id, d.DeviceId, d.DeviceName, d.Model,
            d.IsActive, d.IsOnline, d.LastSeen,
            d.BatteryLevel, d.KioskModeEnabled, d.CameraDisabled
        )).ToList();
    }

    public async Task<DeviceDetailDto> GetDetailAsync(string deviceId)
    {
        var device = await _deviceRepo.GetByDeviceIdAsync(deviceId)
                     ?? throw new DeviceNotFoundException(deviceId);

        var pendingCount = await _commandRepo.GetPendingCountByDeviceIdAsync(deviceId);

        return new DeviceDetailDto(
            device.Id, device.DeviceId, device.DeviceName,
            device.Model, device.Manufacturer, device.AndroidVersion, device.ApiLevel,
            device.IsActive, device.IsOnline, device.LastSeen, device.RegisteredAt,
            device.KioskModeEnabled, device.CameraDisabled,
            device.BatteryLevel, device.StorageAvailableMB, device.TotalStorageMB,
            device.IpAddress, device.PollCount, device.Notes,
            pendingCount
        );
    }

    public async Task DeactivateAsync(string deviceId)
    {
        if (!await _deviceRepo.ExistsAsync(deviceId))
            throw new DeviceNotFoundException(deviceId);

        await _deviceRepo.DeactivateAsync(deviceId);
    }

    public async Task UpdateNotesAsync(string deviceId, string notes)
    {
        if (!await _deviceRepo.ExistsAsync(deviceId))
            throw new DeviceNotFoundException(deviceId);

        await _deviceRepo.UpdateNotesAsync(deviceId, notes);
    }

    public async Task<SystemStatsDto> GetStatsAsync()
    {
        using var conn = await _dbFactory.CreateConnectionAsync();
        return await conn.QuerySingleAsync<SystemStatsDto>("EXEC dbo.sp_GetStats");
    }
}
