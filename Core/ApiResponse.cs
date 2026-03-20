using System.Text.Json.Serialization;

namespace MDMServer.Core;

/// <summary>
/// Envelope estándar para TODAS las respuestas de la API.
/// Garantiza estructura consistente para el cliente Android.
/// </summary>
public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null, string? requestId = null)
        => new() { Success = true,  Data = data, Message = message, RequestId = requestId };

    public static ApiResponse<T> Fail(string error, string? requestId = null)
        => new() { Success = false, Error = error, RequestId = requestId };
}

// Versión sin datos (para endpoints que solo retornan éxito/error)
public class ApiResponse : ApiResponse<object?>
{
    public static ApiResponse OkEmpty(string message, string? requestId = null)
        => new() { Success = true, Message = message, RequestId = requestId };

    public static new ApiResponse Fail(string error, string? requestId = null)
        => new() { Success = false, Error = error, RequestId = requestId };
}