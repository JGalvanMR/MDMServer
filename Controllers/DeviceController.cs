using Microsoft.AspNetCore.Mvc;
using MDMServer.Core;
using MDMServer.Core.Exceptions;
using MDMServer.DTOs.Device;
using MDMServer.DTOs.Poll;
using MDMServer.Services;

namespace MDMServer.Controllers;

[ApiController]
[Route("api/device")]
[Produces("application/json")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService  _deviceService;
    private readonly ICommandService _commandService;
    private readonly ILogger<DeviceController> _logger;

    public DeviceController(
        IDeviceService deviceService,
        ICommandService commandService,
        ILogger<DeviceController> logger)
    {
        _deviceService  = deviceService;
        _commandService = commandService;
        _logger         = logger;
    }

    // ── POST /api/device/register ──────────────────────────────────────────────
    /// <summary>Bootstrap: registra el dispositivo y obtiene token de autenticación.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterDeviceResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request)
    {
        var ip = GetClientIp();
        var response = await _deviceService.RegisterAsync(request, ip);
        return Ok(ApiResponse<RegisterDeviceResponse>.Ok(response, requestId: GetRequestId()));
    }

    // ── POST /api/device/poll ──────────────────────────────────────────────────
    /// <summary>Obtiene comandos pendientes. Llamar cada N segundos.</summary>
    [HttpPost("poll")]
    [ProducesResponseType(typeof(ApiResponse<PollResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Poll([FromBody] PollRequest request)
    {
        var (device, _) = await AuthorizeDeviceAsync();
        var response    = await _commandService.PollAsync(device.DeviceId, request);
        return Ok(ApiResponse<PollResponse>.Ok(response, requestId: GetRequestId()));
    }

    // ── POST /api/device/command-result ───────────────────────────────────────
    /// <summary>Reporta el resultado de la ejecución de un comando.</summary>
    [HttpPost("command-result")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> CommandResult([FromBody] CommandResultRequest request)
    {
        var (device, _) = await AuthorizeDeviceAsync();
        await _commandService.ReportResultAsync(device.DeviceId, request);
        return Ok(ApiResponse.OkEmpty("Resultado registrado.", GetRequestId()));
    }

    // ── POST /api/device/heartbeat ─────────────────────────────────────────────
    /// <summary>Actualiza estado del dispositivo. Llamar independientemente del poll.</summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request)
    {
        var (device, _) = await AuthorizeDeviceAsync();
        await _deviceService.UpdateHeartbeatAsync(device.DeviceId, request, GetClientIp());
        return Ok(ApiResponse.OkEmpty("Heartbeat OK.", GetRequestId()));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private async Task<(Models.Device Device, bool IsNew)> AuthorizeDeviceAsync()
    {
        if (!HttpContext.Request.Headers.TryGetValue(
                MdmConstants.Headers.DeviceToken, out var token))
            throw new UnauthorizedException("Header 'Device-Token' no encontrado.");

        return await _deviceService.AuthenticateOrThrowAsync(token!);
    }

    private string GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string GetRequestId()
        => HttpContext.Request.Headers[MdmConstants.Headers.RequestId].ToString();
}