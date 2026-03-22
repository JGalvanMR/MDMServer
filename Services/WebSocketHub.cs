using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MDMServer.Core;

namespace MDMServer.Services;

/// <summary>
/// Gestiona todas las conexiones WebSocket activas de los dispositivos.
/// Thread-safe. Permite push directo de comandos.
/// </summary>
public interface IWebSocketHub
{
    Task HandleConnectionAsync(string deviceId, WebSocket ws, CancellationToken ct);
    Task<bool> PushCommandAsync(string deviceId, WsCommandMessage message);
    bool IsOnline(string deviceId);
    int OnlineCount { get; }
    IReadOnlyList<string> OnlineDeviceIds { get; }
    event Func<string, string, Task>? OnMessageReceived;
}

public class WebSocketHub : IWebSocketHub
{
    public event Func<string, string, Task>? OnMessageReceived;
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();
    private readonly ILogger<WebSocketHub> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Ping cada 30s para mantener la conexión viva
    private const int PingIntervalSeconds = 30;

    public WebSocketHub(ILogger<WebSocketHub> logger) => _logger = logger;

    public int OnlineCount => _connections.Count(c => c.Value.IsAlive);

    public IReadOnlyList<string> OnlineDeviceIds =>
        _connections.Where(c => c.Value.IsAlive).Select(c => c.Key).ToList();

    public bool IsOnline(string deviceId) =>
        _connections.TryGetValue(deviceId, out var conn) && conn.IsAlive;

    /// <summary>
    /// Maneja el ciclo de vida completo de una conexión WS de un dispositivo.
    /// Bloquea hasta que la conexión cierra.
    /// </summary>
    public async Task HandleConnectionAsync(
        string deviceId, WebSocket ws, CancellationToken ct)
    {
        var conn = new WebSocketConnection(ws, deviceId);
        _connections[deviceId] = conn;
        _logger.LogInformation("WS conectado: {DeviceId}", deviceId);

        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Ping loop en background
        var pingTask = PingLoopAsync(conn, pingCts.Token);

        try
        {
            await ReceiveLoopAsync(conn, ct);
        }
        finally
        {
            pingCts.Cancel();
            try { await pingTask; } catch { /* ignore */ }

            _connections.TryRemove(deviceId, out _);
            _logger.LogInformation("WS desconectado: {DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// Envía un comando al dispositivo por WebSocket.
    /// Retorna false si el dispositivo no está conectado.
    /// </summary>
    public async Task<bool> PushCommandAsync(string deviceId, WsCommandMessage message)
    {
        if (!_connections.TryGetValue(deviceId, out var conn) || !conn.IsAlive)
            return false;

        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            await conn.SendAsync(bytes, WebSocketMessageType.Text, true);
            _logger.LogInformation(
                "WS push: DeviceId={DeviceId} CommandId={Id} Type={Type}",
                deviceId, message.CommandId, message.CommandType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "WS push fallido para {DeviceId}: {Error}", deviceId, ex.Message);
            return false;
        }
    }

    private async Task ReceiveLoopAsync(WebSocketConnection conn, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (conn.IsAlive && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await conn.Ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await conn.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando", ct);
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // Disparar evento en lugar de conn.OnMessageReceived
                    if (OnMessageReceived != null)
                        await OnMessageReceived.Invoke(conn.DeviceId, json);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException ex)
            {
                _logger.LogDebug("WS error {DeviceId}: {Msg}", conn.DeviceId, ex.Message);
                break;
            }
        }
    }

    private async Task PingLoopAsync(WebSocketConnection conn, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && conn.IsAlive)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct);
                if (!conn.IsAlive) break;

                var ping = JsonSerializer.Serialize(new { type = "PING" });
                var bytes = Encoding.UTF8.GetBytes(ping);
                await conn.SendAsync(bytes, WebSocketMessageType.Text, true);
            }
            catch { break; }
        }
    }
}

// ── Abstracción de una conexión individual ────────────────────────────────────
public class WebSocketConnection
{
    public WebSocket Ws { get; }
    public string DeviceId { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    // Suscriptores al evento de mensaje entrante
    public event Action<string>? MessageReceived;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketConnection(WebSocket ws, string deviceId)
    {
        Ws = ws;
        DeviceId = deviceId;
    }

    public bool IsAlive =>
        Ws.State == WebSocketState.Open;

    public async Task SendAsync(byte[] data, WebSocketMessageType type, bool endOfMessage)
    {
        await _sendLock.WaitAsync();
        try
        {
            await Ws.SendAsync(data, type, endOfMessage, CancellationToken.None);
        }
        finally { _sendLock.Release(); }
    }

    public void OnMessageReceived(string json) => MessageReceived?.Invoke(json);
}