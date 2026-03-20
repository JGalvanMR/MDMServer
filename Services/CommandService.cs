using MDMServer.Core;
using MDMServer.Core.Exceptions;
using MDMServer.DTOs.Command;
using MDMServer.DTOs.Poll;
using MDMServer.Models;
using MDMServer.Repositories.Interfaces;

namespace MDMServer.Services;

public interface ICommandService
{
    Task<SendCommandResponse>    SendAsync(SendCommandRequest request, string createdByIp);
    Task<PollResponse>           PollAsync(string deviceId, PollRequest request);
    Task<bool>                   ReportResultAsync(string deviceId, CommandResultRequest request);
    Task<CommandStatusDto>       GetStatusAsync(int commandId);
    Task<PagedResult<CommandStatusDto>> GetHistoryAsync(string deviceId, int page, int pageSize);
    Task                         CancelAsync(int commandId, string reason);
    Task<int>                    CancelAllPendingAsync(string deviceId);
}

public class CommandService : ICommandService
{
    private readonly ICommandRepository _commandRepo;
    private readonly IDeviceRepository  _deviceRepo;
    private readonly IConfiguration     _config;
    private readonly ILogger<CommandService> _logger;

    public CommandService(
        ICommandRepository commandRepo,
        IDeviceRepository deviceRepo,
        IConfiguration config,
        ILogger<CommandService> logger)
    {
        _commandRepo = commandRepo;
        _deviceRepo  = deviceRepo;
        _config      = config;
        _logger      = logger;
    }

    public async Task<SendCommandResponse> SendAsync(
        SendCommandRequest request, string createdByIp)
    {
        // Verificar que el dispositivo existe
        if (!await _deviceRepo.ExistsAsync(request.DeviceId))
            throw new DeviceNotFoundException(request.DeviceId);

        var command = new Command
        {
            DeviceId    = request.DeviceId,
            CommandType = request.CommandType,
            Parameters  = request.Parameters,
            Priority    = request.Priority ?? 5,
            CreatedByIp = createdByIp,
            ExpiresAt   = request.ExpiresInMinutes.HasValue
                ? DateTime.UtcNow.AddMinutes(request.ExpiresInMinutes.Value)
                : null
        };

        await _commandRepo.CreateAsync(command);

        _logger.LogInformation(
            "Comando creado: Id={Id} Tipo={Type} Dispositivo={DeviceId} Priority={Priority}",
            command.Id, command.CommandType, command.DeviceId, command.Priority
        );

        return new SendCommandResponse(
            command.Id,
            $"Comando {command.CommandType} encolado para {command.DeviceId}."
        );
    }

    public async Task<PollResponse> PollAsync(string deviceId, PollRequest request)
    {
        var maxCommands = _config.GetValue<int>("Mdm:MaxCommandsPerPoll", 10);

        // Actualizar last_seen con datos del poll
        await _deviceRepo.UpdateLastSeenAsync(
            deviceId,
            request.BatteryLevel,
            request.StorageAvailableMB,
            request.IpAddress,
            request.KioskModeEnabled,
            request.CameraDisabled
        );

        // Obtener comandos pendientes de forma atómica (SP marca como Sent)
        var pending = await _commandRepo.GetPendingByDeviceIdAsync(deviceId, maxCommands);

        // Contar cuántos quedan después de esta entrega
        var remainingAfter = await _commandRepo.GetPendingCountByDeviceIdAsync(deviceId);

        var dtos = pending.Select(c => new PollCommandDto(
            c.Id, c.CommandType, c.Parameters, c.Priority
        )).ToList();

        if (dtos.Count > 0)
        {
            _logger.LogInformation(
                "Poll DeviceId={DeviceId}: entregando {Count} comando(s). Restantes={Remaining}",
                deviceId, dtos.Count, remainingAfter
            );
        }

        return new PollResponse(DateTime.UtcNow, dtos, remainingAfter);
    }

    public async Task<bool> ReportResultAsync(string deviceId, CommandResultRequest request)
    {
        var command = await _commandRepo.GetByIdAsync(request.CommandId)
                      ?? throw new CommandNotFoundException(request.CommandId);

        if (command.DeviceId != deviceId)
        {
            _logger.LogWarning(
                "[SECURITY] Dispositivo {DeviceId} intentó reportar resultado del comando {Id} que pertenece a {Owner}",
                deviceId, request.CommandId, command.DeviceId
            );
            throw new UnauthorizedException(
                "El comando no pertenece a este dispositivo.");
        }

        if (request.Success)
        {
            await _commandRepo.MarkAsExecutedAsync(request.CommandId, request.ResultJson);
            _logger.LogInformation(
                "Comando {Id} ejecutado exitosamente por {DeviceId}",
                request.CommandId, deviceId);
        }
        else
        {
            var error = request.ErrorMessage ?? "Error desconocido reportado por el dispositivo.";
            await _commandRepo.MarkAsFailedAsync(request.CommandId, error);
            _logger.LogWarning(
                "Comando {Id} falló en {DeviceId}: {Error}",
                request.CommandId, deviceId, error);
        }

        return true;
    }

    public async Task<CommandStatusDto> GetStatusAsync(int commandId)
    {
        var cmd = await _commandRepo.GetByIdAsync(commandId)
                  ?? throw new CommandNotFoundException(commandId);
        return ToDto(cmd);
    }

    public async Task<PagedResult<CommandStatusDto>> GetHistoryAsync(
        string deviceId, int page, int pageSize)
    {
        if (!await _deviceRepo.ExistsAsync(deviceId))
            throw new DeviceNotFoundException(deviceId);

        var commands = await _commandRepo.GetByDeviceIdAsync(deviceId, page, pageSize);
        var total    = await _commandRepo.GetTotalCountByDeviceIdAsync(deviceId);
        var dtos     = commands.Select(ToDto).ToList();

        return PagedResult<CommandStatusDto>.Create(dtos, total, page, pageSize);
    }

    public async Task CancelAsync(int commandId, string reason)
    {
        var cmd = await _commandRepo.GetByIdAsync(commandId)
                  ?? throw new CommandNotFoundException(commandId);

        if (cmd.Status is MdmConstants.CommandStatuses.Executed
                       or MdmConstants.CommandStatuses.Failed
                       or MdmConstants.CommandStatuses.Cancelled)
        {
            throw new MdmException(
                $"No se puede cancelar un comando con estado '{cmd.Status}'.",
                400, "INVALID_STATUS_FOR_CANCEL"
            );
        }

        await _commandRepo.CancelAsync(commandId, reason);
        _logger.LogInformation("Comando {Id} cancelado. Razón: {Reason}", commandId, reason);
    }

    public async Task<int> CancelAllPendingAsync(string deviceId)
    {
        if (!await _deviceRepo.ExistsAsync(deviceId))
            throw new DeviceNotFoundException(deviceId);

        var count = await _commandRepo.CancelAllPendingByDeviceIdAsync(deviceId);
        _logger.LogInformation(
            "{Count} comandos pendientes cancelados para {DeviceId}", count, deviceId);
        return count;
    }

    private static CommandStatusDto ToDto(Command c) => new(
        c.Id, c.DeviceId, c.CommandType, c.Parameters,
        c.Status, c.Priority, c.CreatedAt, c.SentAt,
        c.ExecutedAt, c.ExpiresAt, c.Result, c.ErrorMessage, c.RetryCount
    );
}