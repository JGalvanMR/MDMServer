using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MDMServer.Services;

public class StreamingConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _viewers = new();

    // viewerId -> deviceId
    private readonly ConcurrentDictionary<string, string> _viewerToDevice = new();

    // deviceId -> viewers (thread-safe)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _deviceToViewers = new();

    public void AddViewer(string viewerId, WebSocket ws)
    {
        _viewers[viewerId] = ws;
    }

    public void MapViewerToDevice(string viewerId, string deviceId)
    {
        _viewerToDevice[viewerId] = deviceId;

        var viewers = _deviceToViewers.GetOrAdd(deviceId, _ => new ConcurrentDictionary<string, byte>());
        viewers[viewerId] = 0;
    }

    // 🔥 PRODUCCIÓN: múltiples viewers
    public List<WebSocket> GetViewersForDevice(string deviceId)
    {
        if (!_deviceToViewers.TryGetValue(deviceId, out var viewers))
            return new List<WebSocket>();

        return viewers.Keys
            .Select(id => _viewers.TryGetValue(id, out var ws) ? ws : null)
            .Where(ws => ws != null && ws.State == WebSocketState.Open)
            .Cast<WebSocket>()
            .ToList();
    }

    // 🔥 OPCIONAL: mantener compatibilidad
    public WebSocket? GetFirstViewerForDevice(string deviceId)
    {
        return GetViewersForDevice(deviceId).FirstOrDefault();
    }

    public string? GetDeviceForViewer(string viewerId)
    {
        return _viewerToDevice.TryGetValue(viewerId, out var deviceId)
            ? deviceId
            : null;
    }

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
}