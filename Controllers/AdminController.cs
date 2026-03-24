using Microsoft.AspNetCore.Mvc;
using MDMServer.Core;
using MDMServer.DTOs.Command;
using MDMServer.Filters;
using MDMServer.Services;
using MDMServer.DTOs.Geofence;
using MDMServer.DTOs.Telemetry;
using MDMServer.Repositories.Interfaces;
using MDMServer.Models;

namespace MDMServer.Controllers;

// DTOs locales del admin
public record UpdateNotesRequest(string? Notes);

public record BulkCommandRequest(
    List<string>? DeviceIds,
    string CommandType,
    string? Parameters,
    int? Priority,
    int? ExpiresInMinutes
);

public record LocationTrackingRequest(
    int? IntervalSeconds,
    float? MinDistanceMeters
);

[ApiController]
[Route("api/admin")]
[AdminApiKey]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ICommandService _commandService;
    private readonly ILogger<AdminController> _logger;
    private readonly IGeofenceService _geofenceService;

    public AdminController(
        IDeviceService deviceService,
        ICommandService commandService,
        IGeofenceService geofenceService,
        ILogger<AdminController> logger)
    {
        _deviceService = deviceService;
        _commandService = commandService;
        _geofenceService = geofenceService;
        _logger = logger;
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
            total = devices.Count,
            online = devices.Count(d => d.IsOnline),
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
    public async Task<IActionResult> UpdateNotes(string deviceId, [FromBody] UpdateNotesRequest request)
    {
        await _deviceService.UpdateNotesAsync(deviceId, request.Notes ?? "");
        return Ok(ApiResponse.OkEmpty("Notas actualizadas.", Rid()));
    }

    // ── DELETE /api/admin/devices/{deviceId}/commands/pending ─────────────────
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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        var result = await _commandService.GetHistoryAsync(deviceId, page, pageSize);
        return Ok(ApiResponse<object>.Ok(result, requestId: Rid()));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // GEOFENCES
    // ════════════════════════════════════════════════════════════════════════════

    // ── GET /api/admin/devices/{deviceId}/geofences ─────────────────────────────
    [HttpGet("devices/{deviceId}/geofences")]
    public async Task<IActionResult> GetGeofences(string deviceId)
    {
        try
        {
            if (!await _deviceService.ExistsAsync(deviceId))
                return NotFound(ApiResponse.Fail("Dispositivo no encontrado.", Rid()));

            var geofences = await _geofenceService.GetGeofencesAsync(deviceId);
            return Ok(ApiResponse<object>.Ok(geofences ?? new List<Geofence>(), Rid()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo geofences para {DeviceId}", deviceId);
            return StatusCode(500, ApiResponse.Fail("Error interno al obtener geofences", Rid()));
        }
    }

    // ── POST /api/admin/devices/{deviceId}/geofences ────────────────────────────
    [HttpPost("devices/{deviceId}/geofences")]
    public async Task<IActionResult> CreateGeofence(string deviceId, [FromBody] CreateGeofenceRequest request)
    {
        if (!await _deviceService.ExistsAsync(deviceId))
            return NotFound(ApiResponse.Fail("Dispositivo no encontrado.", Rid()));

        var geofence = await _geofenceService.CreateGeofenceAsync(deviceId, request);
        var dto = new GeofenceDto(
            geofence.Id, geofence.DeviceId, geofence.Name, geofence.Latitude,
            geofence.Longitude, geofence.RadiusMeters, geofence.IsEntry,
            geofence.IsExit, geofence.IsActive, geofence.CreatedAt
        );
        return Ok(ApiResponse<GeofenceDto>.Ok(dto, requestId: Rid()));
    }

    // ── DELETE /api/admin/devices/{deviceId}/geofences/{id} ────────────────────
    [HttpDelete("devices/{deviceId}/geofences/{id:int}")]
    public async Task<IActionResult> DeleteGeofence(string deviceId, int id)
    {
        var geofences = await _geofenceService.GetGeofencesAsync(deviceId);
        if (!geofences.Any(g => g.Id == id))
            return NotFound(ApiResponse.Fail("Geofence no encontrada.", Rid()));

        await _geofenceService.DeleteGeofenceAsync(id);
        return Ok(ApiResponse.OkEmpty("Geofence eliminada.", Rid()));
    }

    // ── POST /api/admin/devices/{deviceId}/check-location ──────────────────────
    [HttpPost("devices/{deviceId}/check-location")]
    public async Task<IActionResult> CheckLocation(string deviceId, [FromBody] CheckLocationRequest request)
    {
        if (!await _deviceService.ExistsAsync(deviceId))
            return NotFound(ApiResponse.Fail("Dispositivo no encontrado.", Rid()));

        var result = await _geofenceService.CheckLocationAsync(
            deviceId, request.Latitude, request.Longitude, request.Accuracy);

        return Ok(ApiResponse<CheckLocationResponse>.Ok(result, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/geofence-events ───────────────────────
    [HttpGet("devices/{deviceId}/geofence-events")]
    public async Task<IActionResult> GetGeofenceEvents(
        string deviceId,
        [FromQuery] int? geofenceId = null,
        [FromQuery] int limit = 50)
    {
        var events = await _geofenceService.GetEventsAsync(deviceId, geofenceId);
        return Ok(ApiResponse<object>.Ok(new { count = events.Count, events }, requestId: Rid()));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    // ── POST /api/admin/commands ───────────────────────────────────────────────
    [HttpPost("commands")]
    public async Task<IActionResult> SendCommand([FromBody] SendCommandRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
    public async Task<IActionResult> CancelCommand(int id, [FromBody] CancelCommandRequest request)
    {
        await _commandService.CancelAsync(id, request.Reason ?? "Cancelado por administrador.");
        return Ok(ApiResponse.OkEmpty($"Comando {id} cancelado.", Rid()));
    }

    // ── POST /api/admin/commands/bulk ─────────────────────────────────────────
    [HttpPost("commands/bulk")]
    public async Task<IActionResult> SendBulkCommand([FromBody] BulkCommandRequest request)
    {
        if (request.DeviceIds is null || request.DeviceIds.Count == 0)
            return BadRequest(ApiResponse.Fail("DeviceIds es requerido y no puede estar vacío."));

        if (request.DeviceIds.Count > 100)
            return BadRequest(ApiResponse.Fail("Máximo 100 dispositivos por bulk command."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

        return Ok(ApiResponse<object>.Ok(new { total = results.Count, results }, requestId: Rid()));
    }

    // ── GET /api/admin/ws-status ─────────────────────────────────────────────
    [HttpGet("ws-status")]
    public IActionResult GetWsStatus([FromServices] IWebSocketHub hub)
    {
        return Ok(ApiResponse<object>.Ok(new
        {
            onlineViaWebSocket = hub.OnlineCount,
            connectedDeviceIds = hub.OnlineDeviceIds
        }, requestId: Rid()));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // LOCATION TRACKING
    // ════════════════════════════════════════════════════════════════════════════

    // ── POST /api/admin/devices/{deviceId}/location-tracking/start ─────────────
    [HttpPost("devices/{deviceId}/location-tracking/start")]
    public async Task<IActionResult> StartLocationTracking(string deviceId, [FromBody] LocationTrackingRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cmdRequest = new SendCommandRequest(
            deviceId,
            "START_LOCATION_TRACK",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                intervalSeconds = request.IntervalSeconds ?? 1,
                minDistanceMeters = request.MinDistanceMeters ?? 10
            }),
            Priority: 5,
            ExpiresInMinutes: null
        );

        var response = await _commandService.SendAsync(cmdRequest, ip);
        return Ok(ApiResponse<object>.Ok(new
        {
            commandId = response.CommandId,
            message = "Tracking de ubicación iniciado.",
            deviceId
        }, requestId: Rid()));
    }

    // ── POST /api/admin/devices/{deviceId}/location-tracking/stop ──────────────
    [HttpPost("devices/{deviceId}/location-tracking/stop")]
    public async Task<IActionResult> StopLocationTracking(string deviceId)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cmdRequest = new SendCommandRequest(
            deviceId,
            "STOP_LOCATION_TRACK",
            null,
            Priority: 5,
            ExpiresInMinutes: null
        );

        var response = await _commandService.SendAsync(cmdRequest, ip);
        return Ok(ApiResponse<object>.Ok(new
        {
            commandId = response.CommandId,
            message = "Tracking de ubicación detenido.",
            deviceId
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

    // ── GET /api/admin/devices/{deviceId}/telemetry ───────────────────────────
    [HttpGet("devices/{deviceId}/telemetry")]
    public async Task<IActionResult> GetTelemetry(
        string deviceId,
        [FromServices] ITelemetryRepository telemetryRepo,
        [FromQuery] int hoursBack = 24,
        [FromQuery] int maxRows = 200)
    {
        hoursBack = Math.Clamp(hoursBack, 1, 168);
        maxRows = Math.Clamp(maxRows, 1, 500);
        var rows = await telemetryRepo.GetTelemetryHistoryAsync(deviceId, hoursBack, maxRows);
        return Ok(ApiResponse<object>.Ok(new
        {
            deviceId,
            hoursBack,
            count = rows.Count,
            rows
        }, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/telemetry/latest ───────────────────
    [HttpGet("devices/{deviceId}/telemetry/latest")]
    public async Task<IActionResult> GetLatestTelemetry(
        string deviceId,
        [FromServices] ITelemetryRepository telemetryRepo)
    {
        var row = await telemetryRepo.GetLatestTelemetryAsync(deviceId);
        if (row is null)
            return NotFound(ApiResponse.Fail("Sin telemetría disponible para este dispositivo."));
        return Ok(ApiResponse<object>.Ok(row, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/location-history ───────────────────
    [HttpGet("devices/{deviceId}/location-history")]
    public async Task<IActionResult> GetLocationHistory(
        string deviceId,
        [FromServices] ITelemetryRepository telemetryRepo,
        [FromQuery] int hoursBack = 24)
    {
        var points = await telemetryRepo.GetLocationHistoryAsync(deviceId, hoursBack);
        return Ok(ApiResponse<object>.Ok(new
        {
            deviceId,
            hoursBack,
            count = points.Count,
            points
        }, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/screenshot ─────────────────────────
    [HttpGet("devices/{deviceId}/screenshot")]
    public async Task<IActionResult> GetLatestScreenshot(
        string deviceId,
        [FromServices] ITelemetryRepository telemetryRepo)
    {
        var screenshot = await telemetryRepo.GetLatestScreenshotAsync(deviceId);
        if (screenshot is null)
            return NotFound(ApiResponse.Fail("Sin capturas disponibles para este dispositivo."));

        return Ok(ApiResponse<object>.Ok(screenshot, requestId: Rid()));
    }

    // ── POST /api/admin/devices/{deviceId}/screenshot ────────────────────────
    [HttpPost("devices/{deviceId}/screenshot")]
    public async Task<IActionResult> RequestScreenshot(string deviceId)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cmdRequest = new SendCommandRequest(deviceId, "TAKE_SCREENSHOT", null, 3, 2);
        var response = await _commandService.SendAsync(cmdRequest, ip);

        return Ok(ApiResponse<object>.Ok(new
        {
            commandId = response.CommandId,
            message = "Captura solicitada. Consulta el resultado con GET /screenshot en ~5s.",
            deviceId
        }, requestId: Rid()));
    }

    // ── GET /api/admin/devices/{deviceId}/events ─────────────────────────────
    [HttpGet("devices/{deviceId}/events")]
    public async Task<IActionResult> GetDeviceEvents(
        string deviceId,
        [FromServices] ITelemetryRepository telemetryRepo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var events = await telemetryRepo.GetEventsAsync(deviceId, page, pageSize);
        return Ok(ApiResponse<object>.Ok(new { deviceId, page, pageSize, events }, requestId: Rid()));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private string Rid()
        => HttpContext.Request.Headers[MdmConstants.Headers.RequestId].ToString();
}
