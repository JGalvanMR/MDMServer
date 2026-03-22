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
        _next = next;
        _logger = logger;
        _options = new RateLimitOptions();
    }

    // En RateLimitMiddleware.cs — cambiar la key del contador
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var rule = _rules.FirstOrDefault(r =>
            path.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase));

        if (rule.Key != null)
        {
            // Para endpoints de dispositivo usar el token como key
            // Para admin usar IP
            string rateLimitKey;
            if (path.StartsWith("/api/device/", StringComparison.OrdinalIgnoreCase) &&
                context.Request.Headers.TryGetValue("Device-Token", out var token) &&
                !string.IsNullOrEmpty(token))
            {
                // Por token: cada dispositivo tiene su propia ventana
                rateLimitKey = $"token:{token.ToString()[..Math.Min(16, token.ToString().Length)]}:{rule.Key}";
            }
            else
            {
                // Por IP para admin y register (sin token)
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                rateLimitKey = $"ip:{ip}:{rule.Key}";
            }

            if (IsRateLimited(rateLimitKey, rule.Value.WindowSecs, rule.Value.MaxReqs))
            {
                _logger.LogWarning("Rate limit excedido: Key={Key} Path={Path}", rateLimitKey, path);
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = rule.Value.WindowSecs.ToString();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """{"success":false,"error":"Rate limit excedido."}""");
                return;
            }
        }
        await _next(context);
    }

    // Middleware/RateLimitMiddleware.cs — reemplazar IsRateLimited
    private static bool IsRateLimited(string key, int windowSecs, int maxRequests)
    {
        var now = DateTime.UtcNow;

        while (true)
        {
            var current = _counters.GetOrAdd(key, _ => (0, now));

            // Ventana expirada → resetear con CAS
            if ((now - current.WindowStart).TotalSeconds >= windowSecs)
            {
                var newEntry = (Count: 1, WindowStart: now);
                if (_counters.TryUpdate(key, newEntry, current))
                    return false;
                continue; // retry si otro hilo actualizó primero
            }

            // Incrementar con CAS
            var updated = (Count: current.Count + 1, current.WindowStart);
            if (_counters.TryUpdate(key, updated, current))
                return updated.Count > maxRequests;
            // Si TryUpdate falla, otro hilo modificó — retry
        }
    }
}

public class RateLimitOptions
{
    public int PollWindowSeconds { get; set; } = 10;
    public int PollMaxRequests { get; set; } = 3;
    public int RegisterWindowSeconds { get; set; } = 60;
    public int RegisterMaxRequests { get; set; } = 5;
}