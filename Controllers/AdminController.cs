using Microsoft.AspNetCore.Mvc;
using MDMServer.Core;
using MDMServer.DTOs.Command;
using MDMServer.Filters;
using MDMServer.Services;

namespace MDMServer.Controllers;

[ApiController]
[Route("api/admin")]
[AdminApiKey]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IDeviceService  _deviceService;
    private readonly ICommandService _commandService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IDeviceService deviceService,
        ICommandService commandService,
        ILogger<AdminController> logger)
    {
        _deviceService  = deviceService;
        _commandService = commandService;
        _logger         = logger;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DEVICES
    // ════════════════════════════════════════════════════════════════════════════

    // ── GET /api/admin/devices ─────────────────────────────────────────────────
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _deviceService.GetAllAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            total   = devices.Count,
            online  = devices.Count(d => d.IsOnline),
            devices
        }, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId} ──────────────────────────────────────
    [HttpGet("devices/{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        var device = await _deviceService.GetDetailAsync(deviceId);
        return Ok(ApiResponse<object>.Ok(device, requestId: Rid()));
    }

    // ── DELETE /api/admin/devices/{deviceId} ───────────────────────────────────
    [HttpDelete("devices/{deviceId}")]
    public async Task<IActionResult> DeactivateDevice(string deviceId)
    {
        await _deviceService.DeactivateAsync(deviceId);
        return Ok(ApiResponse.OkEmpty($"Dispositivo {deviceId} desactivado.", Rid()));
    }

    // ── PATCH /api/admin/devices/{deviceId}/notes ──────────────────────────────
    [HttpPatch("devices/{deviceId}/notes")]
    public async Task<IActionResult> UpdateNotes(string deviceId,
        [FromBody] UpdateNotesRequest request)
    {
        await _deviceService.UpdateNotesAsync(deviceId, request.Notes ?? "");
        return Ok(ApiResponse.OkEmpty("Notas actualizadas.", Rid()));
    }

    // ── DELETE /api/admin/devices/{deviceId}/commands/pending ─────────────────
    /// <summary>Cancela TODOS los comandos pendientes de un dispositivo.</summary>
    [HttpDelete("devices/{deviceId}/commands/pending")]
    public async Task<IActionResult> CancelAllPending(string deviceId)
    {
        var count = await _commandService.CancelAllPendingAsync(deviceId);
        return Ok(ApiResponse.OkEmpty($"{count} comando(s) cancelado(s).", Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/commands ─────────────────────────────
    [HttpGet("devices/{deviceId}/commands")]
    public async Task<IActionResult> GetDeviceCommands(
        string deviceId,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var result = await _commandService.GetHistoryAsync(deviceId, page, pageSize);
        return Ok(ApiResponse<object>.Ok(result, requestId: Rid()));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    // ── POST /api/admin/commands ───────────────────────────────────────────────
    [HttpPost("commands")]
    public async Task<IActionResult> SendCommand([FromBody] SendCommandRequest request)
    {
        var ip       = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var response = await _commandService.SendAsync(request, ip);
        return Ok(ApiResponse<SendCommandResponse>.Ok(response, requestId: Rid()));
    }

    // ── GET /api/admin/commands/{id} ───────────────────────────────────────────
    [HttpGet("commands/{id:int}")]
    public async Task<IActionResult> GetCommandStatus(int id)
    {
        var cmd = await _commandService.GetStatusAsync(id);
        return Ok(ApiResponse<CommandStatusDto>.Ok(cmd, requestId: Rid()));
    }

    // ── DELETE /api/admin/commands/{id} ───────────────────────────────────────
    [HttpDelete("commands/{id:int}")]
    public async Task<IActionResult> CancelCommand(
        int id, [FromBody] CancelCommandRequest request)
    {
        await _commandService.CancelAsync(id, request.Reason ?? "Cancelado por administrador.");
        return Ok(ApiResponse.OkEmpty($"Comando {id} cancelado.", Rid()));
    }

    // ── POST /api/admin/commands/bulk ─────────────────────────────────────────
    /// <summary>Envía el mismo comando a múltiples dispositivos a la vez.</summary>
    [HttpPost("commands/bulk")]
    public async Task<IActionResult> SendBulkCommand([FromBody] BulkCommandRequest request)
    {
        if (request.DeviceIds is null || request.DeviceIds.Count == 0)
            return BadRequest(ApiResponse.Fail("DeviceIds es requerido y no puede estar vacío."));

        if (request.DeviceIds.Count > 100)
            return BadRequest(ApiResponse.Fail("Máximo 100 dispositivos por bulk command."));

        var ip      = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var results = new List<object>();

        foreach (var deviceId in request.DeviceIds)
        {
            try
            {
                var singleRequest = new SendCommandRequest(
                    deviceId, request.CommandType,
                    request.Parameters, request.Priority,
                    request.ExpiresInMinutes
                );
                var response = await _commandService.SendAsync(singleRequest, ip);
                results.Add(new { deviceId, success = true, commandId = response.CommandId });
            }
            catch (Exception ex)
            {
                results.Add(new { deviceId, success = false, error = ex.Message });
            }
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            total   = results.Count,
            results
        }, requestId: Rid()));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // STATS / HEALTH
    // ════════════════════════════════════════════════════════════════════════════

    // ── GET /api/admin/stats ───────────────────────────────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _deviceService.GetStatsAsync();
        return Ok(ApiResponse<SystemStatsDto>.Ok(stats, requestId: Rid()));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private string Rid()
        => HttpContext.Request.Headers[MdmConstants.Headers.RequestId].ToString();
}

// DTOs locales del admin
public record UpdateNotesRequest(string? Notes);

public record BulkCommandRequest(
    List<string>? DeviceIds,
    string  CommandType,
    string? Parameters,
    int?    Priority,
    int?    ExpiresInMinutes
);