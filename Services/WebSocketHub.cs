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
    event Func<string, string, Task>? OnMessageText;      // rename (era OnMessageReceived)
    event Func<string, byte[], Task>? OnMessageBinary;   // nuevo
    Task<bool> SendTextAsync(string deviceId, string text);   // nuevo
    Task<bool> SendBinaryToViewer(string deviceId, byte[] data); // nuevo
}

public class WebSocketHub : IWebSocketHub
{
    public event Func<string, string, Task>? OnMessageText;
    public event Func<string, byte[], Task>? OnMessageBinary;
    public event Func<string, string, Task>? OnMessageReceived;
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();
    private readonly ILogger<WebSocketHub> _logger;
    private readonly StreamingConnectionManager _connMgr;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Ping cada 30s para mantener la conexión viva
    private const int PingIntervalSeconds = 30;

    // Aumentar a 512KB para manejar screenshots base64
    private const int BufferSize = 524288; // 512KB
    private const int MaxMessageSize = 5 * 1024 * 1024; // 5MB límite

    public WebSocketHub(ILogger<WebSocketHub> logger, StreamingConnectionManager connMgr)
    {
        _logger = logger;
        _connMgr = connMgr;
    }


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
        var buffer = new byte[524288]; // 512KB
        while (conn.IsAlive && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await conn.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await conn.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando", ct);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (OnMessageText != null)
                        await OnMessageText.Invoke(conn.DeviceId, text);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var data = new byte[result.Count];
                    Array.Copy(buffer, data, result.Count);
                    if (OnMessageBinary != null)
                        await OnMessageBinary.Invoke(conn.DeviceId, data);
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

    public async Task<bool> SendTextAsync(string deviceId, string text)
    {
        if (!_connections.TryGetValue(deviceId, out var conn) || !conn.IsAlive)
            return false;
        var bytes = Encoding.UTF8.GetBytes(text);
        await conn.SendAsync(bytes, WebSocketMessageType.Text, true);
        return true;
    }

    public async Task<bool> SendBinaryToViewer(string deviceId, byte[] data)
    {
        var viewers = _connMgr.GetViewersForDevice(deviceId);

        if (viewers.Count == 0)
            return false;

        var tasks = viewers.Select(async ws =>
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(
                        data,
                        WebSocketMessageType.Binary,
                        true,
                        CancellationToken.None
                    );
                }
            }
            catch
            {
                // aquí puedes loggear si quieres
            }
        });

        await Task.WhenAll(tasks);
        return true;
    }

    private async Task ReceiveLoopAsyncOG(WebSocketConnection conn, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        var messageBuilder = new StringBuilder();

        while (conn.IsAlive && !ct.IsCancellationRequested)
        {
            try
            {
                WebSocketReceiveResult result;
                messageBuilder.Clear();

                do
                {
                    result = await conn.Ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await conn.Ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure, "Cerrando", ct);
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuilder.Append(chunk);

                        // Protección contra mensajes enormes
                        if (messageBuilder.Length > MaxMessageSize)
                        {
                            _logger.LogError("Mensaje WS excede 5MB, descartando");
                            messageBuilder.Clear();
                            break;
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        var data = buffer.Take(result.Count).ToArray();
                        if (OnMessageBinary != null)
                            await OnMessageBinary.Invoke(conn.DeviceId, data);
                    }
                }
                while (!result.EndOfMessage);

                if (messageBuilder.Length > 0 && OnMessageReceived != null)
                {
                    var fullMessage = messageBuilder.ToString();
                    await OnMessageReceived.Invoke(conn.DeviceId, fullMessage);
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