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
    event Func<string, string, Task>? OnMessageText;
    event Func<string, byte[], Task>? OnMessageBinary;
    Task<bool> SendTextAsync(string deviceId, string text);
    Task<bool> SendBinaryToViewer(string deviceId, byte[] data);
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

    private const int PingIntervalSeconds = 30;

    // Tamaño del buffer por lectura individual (no limita el mensaje total)
    private const int ReadBufferSize = 65536; // 64KB por chunk

    // Límite máximo de un mensaje ensamblado
    private const int MaxMessageSize = 5 * 1024 * 1024; // 5MB

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

    /// <summary>
    /// Loop principal de recepción con soporte correcto de fragmentación WebSocket.
    ///
    /// FIX: La versión anterior hacía un único ReceiveAsync y emitía el evento
    /// inmediatamente. Los mensajes WebSocket (especialmente frames H264 grandes
    /// o JSON con SPS/PPS) pueden llegar fragmentados en múltiples chunks.
    /// Sin el loop do...while(!EndOfMessage) el payload llega truncado o
    /// completamente ignorado en el caso de fragmentos intermedios.
    /// </summary>
    private async Task ReceiveLoopAsync(WebSocketConnection conn, CancellationToken ct)
    {
        var buffer = new byte[ReadBufferSize];

        while (conn.IsAlive && !ct.IsCancellationRequested)
        {
            try
            {
                // ── Ensamblar el mensaje completo antes de procesarlo ──────────
                // Un mensaje WebSocket puede llegar en N frames (EndOfMessage=false
                // en los N-1 primeros). Debemos acumular todos los chunks antes
                // de emitir el evento para garantizar integridad del payload.
                using var msgStream = new MemoryStream();
                WebSocketReceiveResult result;
                bool oversized = false;
                WebSocketMessageType msgType = WebSocketMessageType.Text;

                do
                {
                    result = await conn.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Completar handshake de cierre si el socket aún lo permite
                        if (conn.Ws.State == WebSocketState.Open ||
                            conn.Ws.State == WebSocketState.CloseReceived)
                        {
                            await conn.Ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure, "Cerrando", ct);
                        }
                        return;
                    }

                    msgType = result.MessageType;

                    if (!oversized)
                    {
                        msgStream.Write(buffer, 0, result.Count);

                        if (msgStream.Length > MaxMessageSize)
                        {
                            oversized = true;
                            _logger.LogError(
                                "Mensaje WS de {DeviceId} excede {Max}MB — descartado",
                                conn.DeviceId, MaxMessageSize / 1024 / 1024);
                            // Seguimos leyendo hasta EndOfMessage para no desincronizar
                            // el stream, pero descartamos el contenido.
                        }
                    }
                }
                while (!result.EndOfMessage);

                // Mensaje descartado por tamaño excesivo
                if (oversized) continue;

                var payload = msgStream.ToArray();
                if (payload.Length == 0) continue;

                // ── Despachar por tipo ────────────────────────────────────────
                if (msgType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(payload);
                    if (OnMessageText != null)
                        await OnMessageText.Invoke(conn.DeviceId, text);
                }
                else if (msgType == WebSocketMessageType.Binary)
                {
                    if (OnMessageBinary != null)
                        await OnMessageBinary.Invoke(conn.DeviceId, payload);
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
        if (viewers.Count == 0) return false;

        var tasks = viewers.Select(async ws =>
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SendBinaryToViewer error: {Error}", ex.Message);
            }
        });

        await Task.WhenAll(tasks);
        return true;
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

    public event Action<string>? MessageReceived;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WebSocketConnection(WebSocket ws, string deviceId)
    {
        Ws = ws;
        DeviceId = deviceId;
    }

    public bool IsAlive => Ws.State == WebSocketState.Open;

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