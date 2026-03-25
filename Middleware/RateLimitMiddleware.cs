using System.Collections.Concurrent;
using MDMServer.Core;

namespace MDMServer.Middleware;

/// <summary>
/// Rate limiter en memoria por IP / token.
///
/// CORRECCIONES respecto a la versión anterior:
///  1. Las entradas con ventana expirada se eliminan periódicamente para evitar
///     que _counters crezca sin límite (memory leak en ejecución continua).
///  2. La limpieza se ejecuta en background cada CleanupIntervalSeconds para no
///     bloquear el pipeline de requests.
///
/// NOTA: para múltiples instancias (Docker, K8s) este rate limiter no es
/// efectivo porque cada instancia tiene su propio diccionario. Migrar a
/// ASP.NET Core Rate Limiting con Redis para entornos distribuidos.
/// </summary>
public class RateLimitMiddleware : IAsyncDisposable
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    // IP/token → (conteo, inicio de ventana)
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)>
        _counters = new();

    // ── CORRECCIÓN: rate limit admin demasiado agresivo ──────────────────────
    // La regla original "/api/admin/" con 100 req/60s es una ventana compartida
    // para TODAS las rutas admin. El panel en live mode de screenshots llama
    // GET /api/admin/commands/{id} cada 500ms = 120 req/min solo para ese
    // polling. MonitoringPage hace 4 fetches en paralelo al cargar. Con un
    // usuario legítimo activo en la UI se supera el límite en segundos.
    //
    // Solución: reglas granulares por tipo de ruta, con ventanas y límites
    // ajustados al patrón de uso real de cada endpoint.
    // El orden importa: el middleware usa FirstOrDefault, por lo que las rutas
    // más específicas deben ir antes que el prefijo genérico "/api/admin/".
    private static readonly Dictionary<string, (int WindowSecs, int MaxReqs)> _rules = new()
    {
        // Device endpoints (autenticados con Device-Token, rate limit por token)
        { "/api/device/poll",     (10,  3)  },
        { "/api/device/register", (60,  5)  },

        // Polling frecuente de comandos: live mode llama cada 500ms por comando activo.
        // 60 req/10s = 6 req/s por IP, suficiente para un admin con varios tabs abiertos.
        { "/api/admin/commands",  (10, 60)  },

        // Telemetría y ubicación: MonitoringPage hace polling cada 30s pero
        // puede abrir varios dispositivos en paralelo.
        { "/api/admin/devices",   (60, 600) },

        // Stats y ws-status: Header.tsx los llama cada 30s.
        { "/api/admin/stats",     (60, 60)  },
        { "/api/admin/ws-status", (60, 60)  },

        // Resto de endpoints admin (geofences, bulk commands, etc.)
        { "/api/admin/",          (60, 300) },
    };

    // ── Limpieza periódica de entradas expiradas ───────────────────────────────
    private const int CleanupIntervalSeconds = 120;
    private readonly Timer _cleanupTimer;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next   = next;
        _logger = logger;

        // CORRECCIÓN: timer que purga entradas cuya ventana ya expiró.
        // Sin esto el diccionario acumula una entrada por cada IP/token que
        // haya hecho al menos una request, y nunca libera memoria.
        _cleanupTimer = new Timer(
            callback: _ => CleanupExpiredEntries(),
            state:    null,
            dueTime:  TimeSpan.FromSeconds(CleanupIntervalSeconds),
            period:   TimeSpan.FromSeconds(CleanupIntervalSeconds)
        );
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var rule = _rules.FirstOrDefault(r =>
            path.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase));

        if (rule.Key != null)
        {
            string rateLimitKey;
            if (path.StartsWith("/api/device/", StringComparison.OrdinalIgnoreCase) &&
                context.Request.Headers.TryGetValue("Device-Token", out var token) &&
                !string.IsNullOrEmpty(token))
            {
                var tokenPrefix = token.ToString()[..Math.Min(16, token.ToString().Length)];
                rateLimitKey = $"token:{tokenPrefix}:{rule.Key}";
            }
            else
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                rateLimitKey = $"ip:{ip}:{rule.Key}";
            }

            if (IsRateLimited(rateLimitKey, rule.Value.WindowSecs, rule.Value.MaxReqs))
            {
                _logger.LogWarning(
                    "Rate limit excedido: Key={Key} Path={Path}", rateLimitKey, path);
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

    private static bool IsRateLimited(string key, int windowSecs, int maxRequests)
    {
        var now = DateTime.UtcNow;

        while (true)
        {
            var current = _counters.GetOrAdd(key, _ => (0, now));

            if ((now - current.WindowStart).TotalSeconds >= windowSecs)
            {
                var newEntry = (Count: 1, WindowStart: now);
                if (_counters.TryUpdate(key, newEntry, current))
                    return false;
                continue;
            }

            var updated = (Count: current.Count + 1, current.WindowStart);
            if (_counters.TryUpdate(key, updated, current))
                return updated.Count > maxRequests;
        }
    }

    // ── CORRECCIÓN: eliminar entradas con ventana expirada ────────────────────
    // Se ejecuta cada CleanupIntervalSeconds para que el diccionario no crezca
    // indefinidamente. Solo elimina entradas cuya ventana ya terminó; las activas
    // no se tocan, por lo que no interrumpe requests en curso.
    private static void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;
        // Calculamos el máximo windowSecs definido para no borrar entradas
        // que aún podrían estar activas bajo alguna regla.
        var maxWindow = _rules.Values.Max(r => r.WindowSecs);

        foreach (var key in _counters.Keys)
        {
            if (_counters.TryGetValue(key, out var entry) &&
                (now - entry.WindowStart).TotalSeconds > maxWindow)
            {
                // TryRemove es thread-safe; si entre el TryGetValue y el TryRemove
                // se actualizó la entrada, la dejamos — se limpiará en el próximo ciclo.
                _counters.TryRemove(key, out _);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cleanupTimer.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class RateLimitOptions
{
    public int PollWindowSeconds    { get; set; } = 10;
    public int PollMaxRequests      { get; set; } = 3;
    public int RegisterWindowSeconds { get; set; } = 60;
    public int RegisterMaxRequests  { get; set; } = 5;
}
