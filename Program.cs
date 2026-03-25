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

var builder = WebApplication.CreateBuilder(args);

// ── CORRECCIÓN: logs duplicados ───────────────────────────────────────────────
// builder.Host.UseSerilog() reemplaza el ILoggerFactory de Serilog pero NO
// elimina los proveedores por defecto de ASP.NET Core (Console, Debug).
// Sin ClearProviders(), ambos proveedores escriben cada mensaje → duplicados.
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
        Title       = "MDM Server API",
        Version     = "v1",
        Description = "API de administración remota de dispositivos Android"
    });
    c.AddSecurityDefinition("DeviceToken", new()
    {
        Type        = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In          = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name        = "Device-Token",
        Description = "Token del dispositivo (obtenido al registrar)"
    });
    c.AddSecurityDefinition("AdminKey", new()
    {
        Type        = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In          = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name        = "X-Admin-Key",
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
builder.Services.AddSingleton<IWebSocketHub, WebSocketHub>();
builder.Services.AddHostedService<CommandExpiryService>();
builder.Services.AddScoped<ITelemetryRepository, TelemetryRepository>();

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

// ── Endpoint WebSocket ────────────────────────────────────────────────────────
app.MapGet("/ws/device", async (
    HttpContext ctx,
    IDeviceService deviceService,
    ICommandService commandService,
    IWebSocketHub hub,
    ILogger<Program> logger) =>
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
        PropertyNamingPolicy    = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ── CORRECCIÓN: fuga de event handlers ────────────────────────────────────
    //
    // PROBLEMA ORIGINAL:
    //   hub.OnMessageReceived += async (deviceId, json) => { ... };
    //   await hub.HandleConnectionAsync(...);
    //   // El handler NUNCA se desuscribía.
    //
    // Consecuencia: con cada reconexión del mismo dispositivo se acumulaba un
    // handler adicional. Tras N reconexiones, N handlers estaban activos.
    // N-1 de ellos ejecutaban el `if (deviceId != device.DeviceId) return`
    // pero aún así el GC no podía recolectar las closures ni los objetos
    // capturados (device, commandService, deviceService). En ejecución continua
    // esto degradaba el servidor progresivamente.
    //
    // CORRECCIÓN:
    //   Guardar referencia explícita al delegate y desuscribirse en el finally.
    //   El bloque try/finally garantiza la desuscripción incluso si la conexión
    //   cierra por error, cancelación o excepción no controlada.
    Func<string, string, Task> messageHandler = async (deviceId, json) =>
    {
        if (deviceId != device.DeviceId) return;
        await ProcessWsMessageAsync(json, deviceId, commandService, deviceService, jsonOpts);
    };

    hub.OnMessageReceived += messageHandler;
    try
    {
        // Bloquea hasta que la conexión WS cierre (el hub gestiona el receive loop)
        await hub.HandleConnectionAsync(device.DeviceId, ws, ctx.RequestAborted);
    }
    finally
    {
        // Siempre se ejecuta: conexión cerrada limpiamente, por timeout,
        // por CancellationToken o por excepción.
        hub.OnMessageReceived -= messageHandler;
        logger.LogDebug(
            "Handler WS desuscrito para {DeviceId}. Conexiones activas: {Count}",
            device.DeviceId, hub.OnlineCount);
    }
});

// ── Helper local ──────────────────────────────────────────────────────────────
static async Task ProcessWsMessageAsync(
    string json,
    string deviceId,
    ICommandService cmdSvc,
    IDeviceService devSvc,
    JsonSerializerOptions opts)
{
    try
    {
        using var doc  = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString()?.ToUpper();

        switch (type)
        {
            case "RESULT":
                var commandId    = doc.RootElement.GetProperty("commandId").GetInt32();
                var success      = doc.RootElement.GetProperty("success").GetBoolean();
                var resultJson   = doc.RootElement.TryGetProperty("resultJson",   out var rj) ? rj.GetString() : null;
                var errorMessage = doc.RootElement.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;
                await cmdSvc.ReportResultAsync(deviceId,
                    new MDMServer.DTOs.Poll.CommandResultRequest(
                        commandId, success, resultJson, errorMessage));
                break;

            case "STATUS":
                var battery = doc.RootElement.TryGetProperty("batteryLevel",      out var bat) ? (int?)bat.GetInt32()   : null;
                var storage = doc.RootElement.TryGetProperty("storageAvailableMB", out var sto) ? (long?)sto.GetInt64()  : null;
                var kiosk   = doc.RootElement.TryGetProperty("kioskModeEnabled",  out var ki)  ? (bool?)ki.GetBoolean() : null;
                var camOff  = doc.RootElement.TryGetProperty("cameraDisabled",    out var cam) ? (bool?)cam.GetBoolean(): null;
                var ip      = doc.RootElement.TryGetProperty("ipAddress",         out var ipad)? ipad.GetString()       : null;
                await devSvc.UpdateHeartbeatAsync(deviceId,
                    new MDMServer.DTOs.Poll.HeartbeatRequest(
                        battery, storage, kiosk ?? false, camOff ?? false, ip), ip);
                break;

            // PONG y otros tipos conocidos: ignorar silenciosamente
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

Log.Information("MDM Server listo en {Urls}", string.Join(", ", app.Urls));
app.Run();
return 0;
