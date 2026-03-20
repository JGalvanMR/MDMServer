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
}

public class DeviceService : IDeviceService
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly ICommandRepository _commandRepo;
    private readonly ITokenService _tokenService;
    private readonly ILogger<DeviceService> _logger;

    // En DeviceService.cs — agregar en constructor
    private readonly IDbConnectionFactory _dbFactory;

    public DeviceService(
        IDeviceRepository deviceRepo,
        ICommandRepository commandRepo,
        ITokenService tokenService,
        IDbConnectionFactory dbFactory,        // ← agregar
        ILogger<DeviceService> logger)
    {
        _deviceRepo = deviceRepo;
        _commandRepo = commandRepo;
        _tokenService = tokenService;
        _dbFactory = dbFactory;             // ← agregar
        _logger = logger;
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

    public async Task<RegisterDeviceResponse> RegisterAsync(
        RegisterDeviceRequest request, string clientIp)
    {
        // Re-registro: devolver token existente
        var existing = await _deviceRepo.GetByDeviceIdAsync(request.DeviceId);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Re-registro: DeviceId={DeviceId} IP={Ip}", request.DeviceId, clientIp);

            await _deviceRepo.UpdateLastSeenAsync(
                request.DeviceId, null, null, clientIp, null, null);

            return new RegisterDeviceResponse(
                existing.DeviceId, existing.Token,
                "Dispositivo ya registrado. Token existente devuelto.", false
            );
        }

        // Nuevo registro
        var device = new Device
        {
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName?.Trim(),
            Model = request.Model?.Trim(),
            Manufacturer = request.Manufacturer?.Trim(),
            AndroidVersion = request.AndroidVersion,
            ApiLevel = request.ApiLevel,
            Token = _tokenService.GenerateDeviceToken(),
            IpAddress = clientIp
        };

        await _deviceRepo.CreateAsync(device);
        _logger.LogInformation(
            "Nuevo dispositivo registrado: {DeviceId} Modelo={Model} IP={Ip}",
            device.DeviceId, device.Model, clientIp);

        return new RegisterDeviceResponse(
            device.DeviceId, device.Token,
            "Dispositivo registrado correctamente.", true
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