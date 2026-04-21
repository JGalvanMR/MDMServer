// ══════════════════════════════════════════════════════════════════════════════
// Services/ViewerHub.cs
// ══════════════════════════════════════════════════════════════════════════════
// Manages viewer WebSocket connections, auth, watch registration,
// and frame forwarding from devices to viewers.
// ══════════════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MDMServer.Services;

/// <summary>
/// Represents a single viewer connection that has completed auth + watch.
/// </summary>
public sealed class ViewerConnection
{
    public string ViewerId { get; init; } = "";
    public WebSocket WebSocket { get; init; } = null!;
    public string DeviceId { get; set; } = "";
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    public bool IsAuthenticated { get; set; }
    public bool IsWatching { get; set; }
}

/// <summary>
/// Singleton service that tracks viewer connections per device
/// and forwards video frames / config from devices to viewers.
/// </summary>
public class ViewerHub
{
    private readonly ILogger<ViewerHub> _logger;

    /// <summary>
    /// deviceId → list of viewers watching that device
    /// </summary>
    private readonly ConcurrentDictionary<string, List<ViewerConnection>> _viewersByDevice = new();

    /// <summary>
    /// viewerId → connection (for cleanup on disconnect)
    /// </summary>
    private readonly ConcurrentDictionary<string, ViewerConnection> _allViewers = new();

    /// <summary>
    /// The admin key that viewers must present to authenticate.
    /// </summary>
    private readonly string _adminKey;

    public ViewerHub(ILogger<ViewerHub> logger, IConfiguration config)
    {
        _logger = logger;
        // Match whatever key your AuthContext uses
        _adminKey = config["AdminAuth:Key"]
                 ?? config["ADMIN_KEY"]
                 ?? "DEV-ADMIN-KEY-SOLO-PARA-DESARROLLO-NO-USAR-EN-PROD";
    }

    // ── Auth ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validate adminKey from viewer auth message.
    /// Returns true if valid.
    /// </summary>
    public bool ValidateAuth(string? adminKey)
    {
        if (string.IsNullOrEmpty(adminKey))
        {
            _logger.LogWarning("[ViewerHub] Auth failed: empty key");
            return false;
        }

        // Constant-time comparison to prevent timing attacks
        var valid = adminKey == _adminKey;
        if (!valid)
        {
            _logger.LogWarning("[ViewerHub] Auth failed: invalid key (length={Length})", adminKey.Length);
        }
        return valid;
    }

    // ── Registration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Register a viewer for a specific device after successful watch.
    /// </summary>
    public void RegisterViewer(ViewerConnection viewer, string deviceId)
    {
        viewer.DeviceId = deviceId;
        viewer.IsWatching = true;

        var list = _viewersByDevice.GetOrAdd(deviceId, _ => new List<ViewerConnection>());
        lock (list)
        {
            list.Add(viewer);
        }

        _logger.LogInformation(
            "[ViewerHub] Viewer {ViewerId} now watching device {DeviceId} (total viewers for device: {Count})",
            viewer.ViewerId, deviceId, list.Count);
    }

    /// <summary>
    /// Remove a viewer from all tracking structures. Call on WS disconnect.
    /// </summary>
    public void UnregisterViewer(string viewerId)
    {
        if (_allViewers.TryRemove(viewerId, out var viewer))
        {
            if (!string.IsNullOrEmpty(viewer.DeviceId) &&
                _viewersByDevice.TryGetValue(viewer.DeviceId, out var list))
            {
                lock (list)
                {
                    list.Remove(viewer);
                    if (list.Count == 0)
                    {
                        _viewersByDevice.TryRemove(viewer.DeviceId, out _);
                    }
                }
            }

            _logger.LogInformation(
                "[ViewerHub] Viewer {ViewerId} unregistered from device {DeviceId}",
                viewerId, viewer.DeviceId);
        }
    }

    /// <summary>
    /// Create a ViewerConnection and track it (before auth).
    /// </summary>
    public ViewerConnection CreateConnection(string viewerId, WebSocket ws)
    {
        var conn = new ViewerConnection { ViewerId = viewerId, WebSocket = ws };
        _allViewers[viewerId] = conn;
        return conn;
    }

    // ── Forwarding (called from device handler) ──────────────────────────────

    /// <summary>
    /// Forward a text message (video_config, etc.) to all viewers of a device.
    /// Call this from your device WebSocket handler when you receive video_config.
    /// </summary>
    public async Task ForwardTextToViewersAsync(string deviceId, string jsonMessage)
    {
        if (!_viewersByDevice.TryGetValue(deviceId, out var list))
            return;

        List<ViewerConnection> snapshot;
        lock (list) { snapshot = list.ToList(); }

        var dead = new List<ViewerConnection>();

        foreach (var viewer in snapshot)
        {
            try
            {
                if (viewer.WebSocket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(jsonMessage);
                    await viewer.WebSocket.SendAsync(
                        bytes,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None);
                }
                else
                {
                    dead.Add(viewer);
                }
            }
            catch (WebSocketException)
            {
                dead.Add(viewer);
            }
        }

        // Cleanup dead connections
        foreach (var d in dead) UnregisterViewer(d.ViewerId);
    }

    /// <summary>
    /// Forward binary video frame data to all viewers of a device.
    /// Call this from your device WebSocket handler when you receive frame bytes.
    /// </summary>
    public async Task ForwardBinaryToViewersAsync(string deviceId, byte[] frameData)
    {
        if (!_viewersByDevice.TryGetValue(deviceId, out var list))
            return;

        List<ViewerConnection> snapshot;
        lock (list) { snapshot = list.ToList(); }

        var dead = new List<ViewerConnection>();

        foreach (var viewer in snapshot)
        {
            try
            {
                if (viewer.WebSocket.State == WebSocketState.Open)
                {
                    await viewer.WebSocket.SendAsync(
                        frameData,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        CancellationToken.None);
                }
                else
                {
                    dead.Add(viewer);
                }
            }
            catch (WebSocketException)
            {
                dead.Add(viewer);
            }
        }

        foreach (var d in dead) UnregisterViewer(d.ViewerId);
    }

    /// <summary>
    /// Forward a keyframe request to the device WS (if connected).
    /// </summary>
    public async Task<bool> RequestKeyframeAsync(string deviceId, WebSocket? deviceWebSocket)
    {
        if (deviceWebSocket == null || deviceWebSocket.State != WebSocketState.Open)
        {
            _logger.LogWarning("[ViewerHub] Cannot request keyframe: device {DeviceId} WS not open", deviceId);
            return false;
        }

        try
        {
            var msg = Encoding.UTF8.GetBytes("{\"type\":\"request_keyframe\"}");
            await deviceWebSocket.SendAsync(msg, WebSocketMessageType.Text, true, CancellationToken.None);
            _logger.LogInformation("[ViewerHub] Keyframe request sent to device {DeviceId}", deviceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ViewerHub] Failed to send keyframe request to device {DeviceId}", deviceId);
            return false;
        }
    }

    /// <summary>
    /// Forward a touch input event to the device WS.
    /// </summary>
    public async Task ForwardInputToDeviceAsync(string deviceId, string inputJson, WebSocket? deviceWebSocket)
    {
        if (deviceWebSocket == null || deviceWebSocket.State != WebSocketState.Open)
            return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(inputJson);
            await deviceWebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ViewerHub] Failed to forward input to device {DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// Get viewer count for a device (for logging / status).
    /// </summary>
    public int GetViewerCount(string deviceId)
    {
        if (_viewersByDevice.TryGetValue(deviceId, out var list))
        {
            lock (list) return list.Count;
        }
        return 0;
    }
}