using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MDMServer.Core;

namespace MDMServer.Middleware;

/// <summary>
/// Rate limiter en memoria por IP.
/// Para producción con múltiples instancias usar Redis o ASP.NET Core Rate Limiting.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RateLimitOptions _options;

    // IP → (conteo, ventana inicio)
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _counters = new();

    // Reglas por endpoint (path prefix → (ventanaSegundos, maxRequests))
    private static readonly Dictionary<string, (int WindowSecs, int MaxReqs)> _rules = new()
    {
        { "/api/device/poll",      (10,  3)  },   // 3 polls cada 10s por IP
        { "/api/device/register",  (60,  5)  },   // 5 registros por minuto
        { "/api/admin/",           (60,  100)},   // 100 requests admin/min
    };

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next    = next;
        _logger  = logger;
        _options = new RateLimitOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var rule = _rules.FirstOrDefault(r => path.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase));

        if (rule.Key != null)
        {
            var ip  = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ip}:{rule.Key}";

            if (IsRateLimited(key, rule.Value.WindowSecs, rule.Value.MaxReqs))
            {
                _logger.LogWarning("Rate limit excedido: IP={Ip} Path={Path}", ip, path);

                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = rule.Value.WindowSecs.ToString();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """{"success":false,"error":"Rate limit excedido. Reduce la frecuencia de solicitudes."}"""
                );
                return;
            }
        }

        await _next(context);
    }

    private static bool IsRateLimited(string key, int windowSecs, int maxRequests)
    {
        var now = DateTime.UtcNow;

        var entry = _counters.GetOrAdd(key, _ => (0, now));

        // Ventana expirada → resetear
        if ((now - entry.WindowStart).TotalSeconds >= windowSecs)
        {
            _counters[key] = (1, now);
            return false;
        }

        // Incrementar contador
        var newCount = entry.Count + 1;
        _counters[key] = (newCount, entry.WindowStart);

        return newCount > maxRequests;
    }
}

public class RateLimitOptions
{
    public int PollWindowSeconds      { get; set; } = 10;
    public int PollMaxRequests        { get; set; } = 3;
    public int RegisterWindowSeconds  { get; set; } = 60;
    public int RegisterMaxRequests    { get; set; } = 5;
}