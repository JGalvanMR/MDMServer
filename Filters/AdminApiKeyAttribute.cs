using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MDMServer.Core;
using MDMServer.Core.Exceptions;

namespace MDMServer.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config      = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger      = context.HttpContext.RequestServices
                              .GetRequiredService<ILogger<AdminApiKeyAttribute>>();
        var expectedKey = config["Mdm:AdminApiKey"]
                          ?? throw new InvalidOperationException("Mdm:AdminApiKey no está configurada.");

        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!context.HttpContext.Request.Headers.TryGetValue(
                MdmConstants.Headers.AdminApiKey, out var providedKey)
            || providedKey.ToString() != expectedKey)
        {
            logger.LogWarning(
                "[SECURITY] Acceso admin rechazado. IP={Ip} Path={Path}",
                ip, context.HttpContext.Request.Path
            );

            context.Result = new ObjectResult(
                ApiResponse.Fail("API Key inválida o ausente."))
                { StatusCode = 401 };
            return;
        }

        await next();
    }
}