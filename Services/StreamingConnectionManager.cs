using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MDMServer.Services;

public class StreamingConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _viewers = new();
    private readonly ConcurrentDictionary<string, string> _viewerToDevice = new();
    private readonly ConcurrentDictionary<string, List<string>> _deviceToViewers = new();

    public void AddViewer(string viewerId, WebSocket ws) => _viewers[viewerId] = ws;
    
    public void MapViewerToDevice(string viewerId, string deviceId)
    {
        _viewerToDevice[viewerId] = deviceId;
        _deviceToViewers.AddOrUpdate(deviceId, 
            new List<string> { viewerId }, 
            (k, v) => { v.Add(viewerId); return v; });
    }

    // Este método faltaba (singular):
    public WebSocket? GetViewerForDevice(string deviceId)
    {
        if (_deviceToViewers.TryGetValue(deviceId, out var viewerIds) && viewerIds.Count > 0)
        {
            var firstViewerId = viewerIds.First();
            return _viewers.TryGetValue(firstViewerId, out var ws) ? ws : null;
        }
        return null;
    }

    public string? GetDeviceForViewer(string viewerId) => 
        _viewerToDevice.TryGetValue(viewerId, out var deviceId) ? deviceId : null;

    public List<WebSocket> GetViewersForDevice(string deviceId)
    {
        if (_deviceToViewers.TryGetValue(deviceId, out var viewerIds))
        {
            return viewerIds
                .Select(id => _viewers.TryGetValue(id, out var ws) ? ws : null)
                .Where(ws => ws != null && ws.State == WebSocketState.Open)
                .ToList()!;
        }
        return new List<WebSocket>();
    }

    public void RemoveViewer(string viewerId)
    {
        if (_viewerToDevice.TryRemove(viewerId, out var deviceId))
        {
            if (_deviceToViewers.TryGetValue(deviceId, out var list))
            {
                list.Remove(viewerId);
                if (list.Count == 0) _deviceToViewers.TryRemove(deviceId, out _);
            }
        }
        _viewers.TryRemove(viewerId, out _);
    }

    public void RemoveViewerByDevice(string deviceId)
    {
        if (_deviceToViewers.TryRemove(deviceId, out var viewerIds))
        {
            foreach (var vid in viewerIds)
            {
                _viewerToDevice.TryRemove(vid, out _);
                _viewers.TryRemove(vid, out _);
            }
        }
    }
}