using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MDMServer.Services;

public class StreamingConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _viewerSockets = new();
    private readonly ConcurrentDictionary<string, string> _viewerToDevice = new();

    public void AddViewer(string viewerId, WebSocket ws) => _viewerSockets[viewerId] = ws;
    public void RemoveViewer(string viewerId) => _viewerSockets.TryRemove(viewerId, out _);
    public void MapViewerToDevice(string viewerId, string deviceId) => _viewerToDevice[viewerId] = deviceId;
    public string? GetDeviceForViewer(string viewerId) => _viewerToDevice.TryGetValue(viewerId, out var deviceId) ? deviceId : null;
    public WebSocket? GetViewerForDevice(string deviceId)
    {
        var entry = _viewerToDevice.FirstOrDefault(x => x.Value == deviceId);
        return entry.Key != null && _viewerSockets.TryGetValue(entry.Key, out var ws) ? ws : null;
    }
    public void RemoveViewerByDevice(string deviceId)
    {
        var entry = _viewerToDevice.FirstOrDefault(x => x.Value == deviceId);
        if (entry.Key != null)
        {
            _viewerToDevice.TryRemove(entry.Key, out _);
            _viewerSockets.TryRemove(entry.Key, out _);
        }
    }
}