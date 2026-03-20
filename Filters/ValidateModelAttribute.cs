using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MDMServer.Core;

namespace MDMServer.Filters;

/// <summary>
/// Retorna errores de validación como ApiResponse estructurado
/// en lugar del ProblemDetails por defecto de ASP.NET.
/// </summary>
public class ValidateModelAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e =>
                    string.IsNullOrEmpty(e.ErrorMessage) ? "Campo inválido." : e.ErrorMessage))
                .ToList();

            context.Result = new BadRequestObjectResult(
                ApiResponse.Fail(string.Join(" | ", errors)));
        }
    }
}