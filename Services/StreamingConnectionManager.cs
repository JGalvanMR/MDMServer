using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MDMServer.Services;

public class StreamingConnectionManager
{
    // viewerId → WebSocket del viewer
    private readonly ConcurrentDictionary<string, WebSocket> _viewers = new();

    // viewerId → deviceId
    private readonly ConcurrentDictionary<string, string> _viewerToDevice = new();

    // deviceId → set de viewerIds activos
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _deviceToViewers = new();

    // ── FIX: Cache del último video_config (SPS/PPS) por dispositivo ─────────
    // Un viewer que se conecta DESPUÉS de que el Android envió video_config
    // nunca recibirá ese mensaje. Sin él, Broadway.js no puede inicializar
    // el decodificador y todos los frames binarios subsiguientes se descartan.
    // La solución: cachear el último video_config recibido y enviarlo
    // inmediatamente al viewer al momento de hacer "watch".
    private readonly ConcurrentDictionary<string, string> _videoConfigCache = new();

    // ─────────────────────────────────────────────────────────────────────────

    public void AddViewer(string viewerId, WebSocket ws)
    {
        _viewers[viewerId] = ws;
    }

    public void MapViewerToDeviceV1(string viewerId, string deviceId)
    {
        _viewerToDevice[viewerId] = deviceId;

        var viewers = _deviceToViewers.GetOrAdd(
            deviceId, _ => new ConcurrentDictionary<string, byte>());
        viewers[viewerId] = 0;
    }

    public void MapViewerToDevice(string viewerId, string deviceId)
    {
        // Remover mapping anterior si existe
        if (_viewerToDevice.TryGetValue(viewerId, out var oldDeviceId))
        {
            if (_deviceToViewers.TryGetValue(oldDeviceId, out var oldViewers))
            {
                oldViewers.TryRemove(viewerId, out _);
                if (oldViewers.IsEmpty)
                    _deviceToViewers.TryRemove(oldDeviceId, out _);
            }
        }

        _viewerToDevice[viewerId] = deviceId;

        var viewers = _deviceToViewers.GetOrAdd(
            deviceId, _ => new ConcurrentDictionary<string, byte>());

        viewers[viewerId] = 0;
    }

    /// <summary>
    /// Retorna los WebSockets de viewers activos para un dispositivo.
    ///
    /// FIX: La versión anterior solo filtraba por WebSocketState.Open pero
    /// dejaba las entradas stale en los diccionarios. Con el tiempo, estos
    /// acumulan referencias a sockets muertos que nunca se limpian, causando:
    ///   1. Memory leak progresivo en sesiones largas
    ///   2. Intentos de envío a sockets cerrados con error silencioso
    ///   3. Ralentización de GetViewersForDevice al iterar entradas muertas
    ///
    /// Ahora se limpia inline durante la enumeración: thread-safe porque
    /// ConcurrentDictionary.TryRemove es atómico.
    /// </summary>
    public List<WebSocket> GetViewersForDevice(string deviceId)
    {
        if (!_deviceToViewers.TryGetValue(deviceId, out var viewers))
            return new List<WebSocket>();

        var result = new List<WebSocket>();
        var stale = new List<string>();

        foreach (var viewerId in viewers.Keys)
        {
            if (_viewers.TryGetValue(viewerId, out var ws) &&
                ws.State == WebSocketState.Open)
            {
                result.Add(ws);
            }
            else
            {
                // Viewer desconectado sin pasar por RemoveViewer (p.ej. caída de red)
                stale.Add(viewerId);
            }
        }

        // Limpiar entradas muertas para evitar acumulación
        foreach (var id in stale)
        {
            viewers.TryRemove(id, out _);
            _viewers.TryRemove(id, out _);
            _viewerToDevice.TryRemove(id, out _);
        }

        // Si el set quedó vacío, limpiar también la entrada del device
        if (viewers.IsEmpty)
            _deviceToViewers.TryRemove(deviceId, out _);

        return result;
    }

    public WebSocket? GetFirstViewerForDevice(string deviceId)
        => GetViewersForDevice(deviceId).FirstOrDefault();

    public string? GetDeviceForViewer(string viewerId)
        => _viewerToDevice.TryGetValue(viewerId, out var deviceId) ? deviceId : null;

    public void RemoveViewer(string viewerId)
    {
        if (_viewerToDevice.TryRemove(viewerId, out var deviceId))
        {
            if (_deviceToViewers.TryGetValue(deviceId, out var viewers))
            {
                viewers.TryRemove(viewerId, out _);

                if (viewers.IsEmpty)
                    _deviceToViewers.TryRemove(deviceId, out _);
            }
        }

        _viewers.TryRemove(viewerId, out _);
    }

    public void RemoveViewerByDevice(string deviceId)
    {
        if (_deviceToViewers.TryRemove(deviceId, out var viewers))
        {
            foreach (var viewerId in viewers.Keys)
            {
                _viewerToDevice.TryRemove(viewerId, out _);
                _viewers.TryRemove(viewerId, out _);
            }
        }
    }

    // ── Video config cache ────────────────────────────────────────────────────

    /// <summary>
    /// Persiste el último video_config recibido del dispositivo.
    /// Llamar cada vez que se recibe un mensaje de tipo "video_config".
    /// </summary>
    public void SetVideoConfig(string deviceId, string configJson)
    {
        _videoConfigCache[deviceId] = configJson;
    }

    /// <summary>
    /// Retorna el último video_config cacheado, o null si el dispositivo
    /// aún no ha enviado uno.
    /// </summary>
    public string? GetVideoConfig(string deviceId)
        => _videoConfigCache.TryGetValue(deviceId, out var cfg) ? cfg : null;

    /// <summary>
    /// Limpia el cache de video_config cuando el dispositivo se desconecta.
    /// Garantiza que un viewer que llegue después de la reconexión del
    /// dispositivo no reciba un config obsoleto.
    /// </summary>
    public void ClearVideoConfig(string deviceId)
    {
        _videoConfigCache.TryRemove(deviceId, out _);
    }
}