using System.Diagnostics;
using MDMServer.Core;
using MDMServer.Data;
using MDMServer.Models;

namespace MDMServer.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    // Endpoints que NO se loguean en DB (demasiado frecuentes)
    private static readonly HashSet<string> _excludeFromDb = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/device/heartbeat",
        "/health",
        "/swagger"
    };

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Agregar X-Request-Id si no viene
        if (!context.Request.Headers.ContainsKey(MdmConstants.Headers.RequestId))
        {
            context.Request.Headers[MdmConstants.Headers.RequestId] =
                Guid.NewGuid().ToString("N")[..12];
        }

        var requestId = context.Request.Headers[MdmConstants.Headers.RequestId].ToString();
        var sw        = Stopwatch.StartNew();

        await _next(context);

        sw.Stop();

        var path       = context.Request.Path.Value ?? "";
        var method     = context.Request.Method;
        var statusCode = context.Response.StatusCode;
        var duration   = (int)sw.ElapsedMilliseconds;

        _logger.LogInformation(
            "{Method} {Path} → {StatusCode} ({Duration}ms) [ReqId={RequestId}]",
            method, path, statusCode, duration, requestId
        );

        // Log lento (más de 2 segundos)
        if (duration > 2000)
        {
            _logger.LogWarning("Solicitud lenta: {Method} {Path} tomó {Duration}ms", method, path, duration);
        }
    }
}