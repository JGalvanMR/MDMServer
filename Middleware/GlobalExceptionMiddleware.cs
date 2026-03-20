using System.Text.Json;
using MDMServer.Core;
using MDMServer.Core.Exceptions;

namespace MDMServer.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (MdmException ex)
        {
            _logger.LogWarning("MdmException [{Code}]: {Message}", ex.ErrorCode, ex.Message);
            await WriteErrorResponse(context, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            // Generar un ID de correlación para rastrear en logs
            var correlationId = context.TraceIdentifier;
            _logger.LogError(ex, "Excepción no controlada. CorrelationId={CorrelationId}", correlationId);

            await WriteErrorResponse(context, 500,
                $"Error interno del servidor. Referencia: {correlationId}");
        }
    }

    private static async Task WriteErrorResponse(HttpContext ctx, int statusCode, string message)
    {
        ctx.Response.StatusCode  = statusCode;
        ctx.Response.ContentType = "application/json";

        var response = ApiResponse.Fail(message, ctx.TraceIdentifier);
        var json     = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await ctx.Response.WriteAsync(json);
    }
}