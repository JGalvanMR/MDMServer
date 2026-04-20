using FluentValidation;
using FluentValidation.AspNetCore;
using Serilog;
using MDMServer.Data;
using MDMServer.Filters;
using MDMServer.Middleware;
using MDMServer.Repositories;
using MDMServer.Repositories.Interfaces;
using MDMServer.Services;
using MDMServer.Services.Interfaces;
using MDMServer.Validators;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using MDMServer.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ── CORRECCIÓN: logs duplicados ───────────────────────────────────────────────
builder.Logging.ClearProviders();

// ── Serilog ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, services, config) =>
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
);

// ── Controllers y validación ──────────────────────────────────────────────────
builder.Services
    .AddControllers(opts => opts.Filters.Add<ValidateModelAttribute>())
    .ConfigureApiBehaviorOptions(opts =>
        opts.SuppressModelStateInvalidFilter = true);

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterDeviceValidator>();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "MDM Server API",
        Version = "v1",
        Description = "API de administración remota de dispositivos Android"
    });
    c.AddSecurityDefinition("DeviceToken", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "Device-Token",
        Description = "Token del dispositivo (obtenido al registrar)"
    });
    c.AddSecurityDefinition("AdminKey", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Admin-Key",
        Description = "Clave del administrador"
    });
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── DI ────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<ICommandRepository, CommandRepository>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<ICommandService, CommandService>();
builder.Services.AddScoped<IGeofenceRepository, GeofenceRepository>();
builder.Services.AddScoped<IGeofenceService, GeofenceService>();
builder.Services.AddHostedService<CommandExpiryService>();
builder.Services.AddScoped<ITelemetryRepository, TelemetryRepository>();
builder.Services.AddSingleton<StreamingConnectionManager>();
builder.Services.AddSingleton<IWebSocketHub, WebSocketHub>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(61210);
});

// ══════════════════════════════════════════════════════════════════════════════
var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MDM Server API v1");
        c.RoutePrefix = "swagger";
    });
}

app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Headers.ContainsKey(MDMServer.Core.MdmConstants.Headers.RequestId))
        ctx.Request.Headers[MDMServer.Core.MdmConstants.Headers.RequestId] =
            Guid.NewGuid().ToString("N")[..12];
    await next();
});

app.UseSerilogRequestLogging(opts =>
{
    opts.GetLevel = (ctx, _, _) =>
        ctx.Request.Path.StartsWithSegments("/health") ||
        ctx.Request.Path.StartsWithSegments("/api/device/heartbeat")
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
});

// ── WebSocket ─────────────────────────────────────────────────────────────────
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseCors();
app.MapHealthChecks("/health");
app.MapControllers();

// ── Verificar DB al arrancar ──────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
    if (factory is SqlConnectionFactory sqlFactory)
    {
        if (!await sqlFactory.TestConnectionAsync())
        {
            Log.Fatal("No se pudo conectar a SQL Server. Abortando.");
            return 1;
        }
    }
}

// ── Endpoint WebSocket para dispositivos ─────────────────────────────────────
app.MapGet("/ws/device", HandleDeviceWebSocket);

// ── Endpoint WebSocket para viewers ──────────────────────────────────────────
app.MapGet("/ws/viewer", HandleViewerWebSocket);

Log.Information("MDM Server listo en {Urls}", string.Join(", ", app.Urls));
await app.RunAsync();
return 0;

// ──────────────────────────────────────────────────────────────────────────────
// WEBSOCKET: DISPOSITIVO
// ──────────────────────────────────────────────────────────────────────────────

static async Task HandleDeviceWebSocket(
    HttpContext ctx,
    [FromServices] IDeviceService deviceService,
    [FromServices] ICommandService commandService,
    [FromServices] IWebSocketHub hub,
    [FromServices] StreamingConnectionManager connMgr,
    [FromServices] ILogger<Program> logger)
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    if (!ctx.Request.Headers.TryGetValue("Device-Token", out var token))
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    MDMServer.Models.Device device;
    try
    {
        using var scope = ctx.RequestServices.CreateScope();
        var devSvc = scope.ServiceProvider.GetRequiredService<IDeviceService>();
        (device, _) = await devSvc.AuthenticateOrThrowAsync(token!);
    }
    catch
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    var jsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ── Handler de mensajes de texto ─────────────────────────────────────────
    //
    // FIX CRÍTICO: la versión anterior enviaba TODOS los mensajes de texto a
    // ProcessWsMessageAsync, que solo maneja RESULT / STATUS / PONG.
    // Los mensajes de tipo "video_config" (con SPS/PPS para Broadway.js)
    // caían en el case default y se descartaban silenciosamente.
    // Sin video_config el viewer nunca puede inicializar el decodificador H264.
    //
    // Solución:
    //   - Mensajes de gestión del dispositivo (RESULT, STATUS, PONG, HELLO)
    //     → ProcessWsMessageAsync (sin cambios)
    //   - Cualquier otro tipo (video_config, etc.)
    //     → cachear + reenviar a todos los viewers conectados
    //
    Func<string, string, Task> messageHandler = async (deviceId, json) =>
    {
        if (deviceId != device.DeviceId) return;

        // Intentar identificar el tipo de mensaje antes de procesar
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                var msgType = typeEl.GetString()?.ToUpperInvariant();

                // Mensajes de gestión del dispositivo → procesador estándar
                if (msgType is "RESULT" or "STATUS" or "PONG" or "HELLO")
                {
                    await ProcessWsMessageAsync(json, deviceId, commandService, deviceService, jsonOpts);
                    return;
                }

                // Cualquier otro mensaje (video_config, etc.) → reenviar a viewers
                if (msgType == "VIDEO_CONFIG")
                {
                    // Cachear para late-join viewers (ver HandleViewerWebSocket)
                    connMgr.SetVideoConfig(deviceId, json);

                    logger.LogWarning(
                        "🔥 VIDEO_CONFIG recibido de {DeviceId}: {Json}",
                        deviceId,
                        json
                    );
                }

                var jsonBytes = Encoding.UTF8.GetBytes(json);
                var viewers = connMgr.GetViewersForDevice(deviceId);

                foreach (var viewer in viewers)
                {
                    if (viewer.State != WebSocketState.Open) continue;
                    try
                    {
                        // CancellationToken.None: el viewer tiene su propio ciclo de vida,
                        // independiente del contexto del dispositivo
                        await viewer.SendAsync(
                            jsonBytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug("Error reenviando {MsgType} a viewer: {Error}", msgType, ex.Message);
                    }
                }
                return;
            }
        }
        catch
        {
            // JSON malformado o sin campo "type": procesar como mensaje de dispositivo
        }

        await ProcessWsMessageAsync(json, deviceId, commandService, deviceService, jsonOpts);
    };

    // ── Handler de mensajes binarios (frames H264) ────────────────────────────
    //
    // FIX: La versión anterior usaba ctx.RequestAborted como CancellationToken.
    // Cuando el dispositivo se desconecta, ese token se cancela y los envíos
    // al viewer fallan con OperationCanceledException justo cuando el pipeline
    // de cleanup ya está activo. Los frames en vuelo se pierden innecesariamente.
    //
    // Solución: usar CancellationToken.None — el viewer gestiona su propio
    // ciclo de vida a través de HandleViewerWebSocket.
    //
    Func<string, byte[], Task> messageHandlerBinary = async (devId, data) =>
    {
        if (devId != device.DeviceId) return;

        logger.LogWarning(
        "🎥 FRAME recibido de {DeviceId}, size={Size}",
        devId,
        data.Length
        );

        var viewers = connMgr.GetViewersForDevice(devId);
        foreach (var viewer in viewers)
        {
            if (viewer.State != WebSocketState.Open) continue;
            try
            {
                await viewer.SendAsync(
                    data, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Error enviando frame binario a viewer: {Error}", ex.Message);
            }
        }
    };

    hub.OnMessageText += messageHandler;
    hub.OnMessageBinary += messageHandlerBinary;

    try
    {
        await hub.HandleConnectionAsync(device.DeviceId, ws, ctx.RequestAborted);
    }
    finally
    {
        hub.OnMessageText -= messageHandler;
        hub.OnMessageBinary -= messageHandlerBinary;

        // Limpiar video_config cacheado: si el dispositivo reconecta enviará
        // un nuevo video_config, y no queremos que viewers reciban uno obsoleto
        connMgr.ClearVideoConfig(device.DeviceId);

        logger.LogDebug(
            "Handler WS desuscrito para {DeviceId}. Conexiones activas: {Count}",
            device.DeviceId, hub.OnlineCount);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// WEBSOCKET: VIEWER
// ──────────────────────────────────────────────────────────────────────────────

static async Task HandleViewerWebSocket(
    HttpContext ctx,
    [FromServices] IDeviceService deviceService,
    [FromServices] IWebSocketHub deviceHub,
    [FromServices] StreamingConnectionManager connMgr,
    [FromServices] ILogger<Program> logger)
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var viewerId = Guid.NewGuid().ToString();
    connMgr.AddViewer(viewerId, ws);

    logger.LogInformation("Viewer {ViewerId} conectado desde {IP}",
        viewerId, ctx.Connection.RemoteIpAddress);

    var buffer = new byte[4096];
    bool isAuthenticated = false;

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ctx.RequestAborted);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                logger.LogInformation("Viewer {ViewerId} cerró conexión", viewerId);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            logger.LogDebug("Viewer {ViewerId} recibió: {Message}", viewerId, text);

            ViewerMessage? msg;
            try
            {
                msg = JsonSerializer.Deserialize<ViewerMessage>(text);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error deserializando mensaje de viewer: {Error}", ex.Message);
                await ws.SendAsync(
                    Encoding.UTF8.GetBytes("{\"error\":\"Invalid JSON\"}"),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                continue;
            }

            if (msg?.Type == "auth")
            {
                var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
                var expectedKey = config["Mdm:AdminApiKey"];

                logger.LogDebug(
                    "Auth attempt. Key received: {Key}, Expected: {Expected}",
                    msg.AdminKey?.Substring(0, Math.Min(10, msg.AdminKey?.Length ?? 0)) + "...",
                    expectedKey?.Substring(0, Math.Min(10, expectedKey?.Length ?? 0)) + "...");

                if (msg.AdminKey != expectedKey)
                {
                    logger.LogWarning("Viewer {ViewerId} auth fallido", viewerId);
                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes("{\"error\":\"Invalid admin key\"}"),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                    break;
                }

                isAuthenticated = true;
                logger.LogInformation("Viewer {ViewerId} autenticado exitosamente", viewerId);
                await ws.SendAsync(
                    Encoding.UTF8.GetBytes("{\"status\":\"authenticated\"}"),
                    WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else if (msg?.Type == "watch")
            {
                if (!isAuthenticated)
                {
                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes("{\"error\":\"Not authenticated\"}"),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                    continue;
                }

                var deviceId = msg.DeviceId;
                logger.LogInformation(
                    "Viewer {ViewerId} solicitó ver dispositivo {DeviceId}", viewerId, deviceId);

                if (string.IsNullOrEmpty(deviceId) || !await deviceService.ExistsAsync(deviceId))
                {
                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes("{\"error\":\"Device not found\"}"),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                    continue;
                }

                connMgr.MapViewerToDevice(viewerId, deviceId);

                // ── FIX: Late-join — enviar video_config cacheado ─────────────
                // Si el dispositivo ya está transmitiendo, el viewer que recién
                // se conecta jamás recibiría el video_config (SPS/PPS) que el
                // Android envió al inicio del stream. Broadway.js necesita ese
                // mensaje para inicializar el decodificador antes del primer frame.
                // Enviarlo aquí garantiza que cualquier viewer pueda unirse
                // mid-stream y decodificar correctamente.
                var cachedConfig = connMgr.GetVideoConfig(deviceId);
                if (cachedConfig != null)
                {
                    try
                    {
                        var configBytes = Encoding.UTF8.GetBytes(cachedConfig);
                        await ws.SendAsync(
                            configBytes, WebSocketMessageType.Text, true, CancellationToken.None);
                        logger.LogInformation(
                            "video_config cacheado enviado a viewer {ViewerId} (late-join)", viewerId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(
                            "Error enviando video_config cacheado a viewer {ViewerId}: {Error}",
                            viewerId, ex.Message);
                    }
                }

                await ws.SendAsync(
                    Encoding.UTF8.GetBytes("{\"status\":\"watching\"}"),
                    WebSocketMessageType.Text, true, CancellationToken.None);

                logger.LogInformation(
                    "Viewer {ViewerId} ahora observa dispositivo {DeviceId}", viewerId, deviceId);
            }
            else if (msg?.Type == "input")
            {
                if (!isAuthenticated) continue;

                var targetDeviceId = connMgr.GetDeviceForViewer(viewerId);
                if (targetDeviceId != null && deviceHub.IsOnline(targetDeviceId))
                {
                    var inputMsg = JsonSerializer.Serialize(new
                    {
                        type = "INPUT",
                        eventType = msg.EventType,
                        x = msg.X,
                        y = msg.Y,
                        keyCode = msg.KeyCode
                    });

                    await deviceHub.SendTextAsync(targetDeviceId, inputMsg);
                    logger.LogDebug(
                        "Input reenviado a dispositivo {DeviceId}: {Input}", targetDeviceId, inputMsg);
                }
            }
        }
    }
    finally
    {
        connMgr.RemoveViewer(viewerId);
        logger.LogInformation("Viewer {ViewerId} desconectado y limpiado", viewerId);

        try
        {
            if (ws.State == WebSocketState.Open ||
                ws.State == WebSocketState.CloseReceived)
            {
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closed",
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("Error cerrando WebSocket viewer: {Error}", ex.Message);
        }
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// PROCESADOR DE MENSAJES DE DISPOSITIVO (gestión, no streaming)
// ──────────────────────────────────────────────────────────────────────────────

static async Task ProcessWsMessageAsync(
    string json,
    string deviceId,
    ICommandService cmdSvc,
    IDeviceService devSvc,
    JsonSerializerOptions opts)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString()?.ToUpper();

        switch (type)
        {
            case "RESULT":
                var commandId = doc.RootElement.GetProperty("commandId").GetInt32();
                var success = doc.RootElement.GetProperty("success").GetBoolean();
                var resultJson = doc.RootElement.TryGetProperty("resultJson", out var rj) ? rj.GetString() : null;
                var errorMessage = doc.RootElement.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;
                await cmdSvc.ReportResultAsync(deviceId,
                    new MDMServer.DTOs.Poll.CommandResultRequest(
                        commandId, success, resultJson, errorMessage));
                break;

            case "STATUS":
                var battery = doc.RootElement.TryGetProperty("batteryLevel", out var bat) ? (int?)bat.GetInt32() : null;
                var storage = doc.RootElement.TryGetProperty("storageAvailableMB", out var sto) ? (long?)sto.GetInt64() : null;
                var kiosk = doc.RootElement.TryGetProperty("kioskModeEnabled", out var ki) ? (bool?)ki.GetBoolean() : null;
                var camOff = doc.RootElement.TryGetProperty("cameraDisabled", out var cam) ? (bool?)cam.GetBoolean() : null;
                var ip = doc.RootElement.TryGetProperty("ipAddress", out var ipad) ? ipad.GetString() : null;
                await devSvc.UpdateHeartbeatAsync(deviceId,
                    new MDMServer.DTOs.Poll.HeartbeatRequest(
                        battery, storage, kiosk ?? false, camOff ?? false, ip), ip);
                break;

            case "PONG":
                break;

            default:
                Serilog.Log.Debug(
                    "Mensaje WS desconocido tipo={Type} de {DeviceId}", type, deviceId);
                break;
        }
    }
    catch (Exception ex)
    {
        Serilog.Log.Debug(
            "Error procesando WS msg de {DeviceId}: {Err}", deviceId, ex.Message);
    }
}

// ── ViewerMessage (modelo del protocolo viewer → servidor) ────────────────────
public class ViewerMessage
{
    public string Type { get; set; } = "";
    public string? AdminKey { get; set; }
    public string? DeviceId { get; set; }
    public string? EventType { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? KeyCode { get; set; }
}