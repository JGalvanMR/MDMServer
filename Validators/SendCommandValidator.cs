// Validators/SendCommandValidator.cs — reemplazar completo
using FluentValidation;
using MDMServer.Core;
using MDMServer.DTOs.Command;

namespace MDMServer.Validators;

public class SendCommandValidator : AbstractValidator<SendCommandRequest>
{
    // Solo estos dos requieren confirm:true
    private static readonly HashSet<string> RequireConfirm = new()
    {
        MdmConstants.CommandTypes.RebootDevice,
        MdmConstants.CommandTypes.WipeData
    };

    public SendCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("DeviceId es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.CommandType)
            .NotEmpty().WithMessage("CommandType es requerido.")
            .Must(t => MdmConstants.CommandTypes.All.Contains(t))
            .WithMessage(
                $"CommandType inválido. Valores válidos: " +
                $"{string.Join(", ", MdmConstants.CommandTypes.All)}");

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 10)
            .WithMessage("Priority debe estar entre 1 y 10.")
            .When(x => x.Priority.HasValue);

        RuleFor(x => x.ExpiresInMinutes)
            .GreaterThan(0).WithMessage("ExpiresInMinutes debe ser mayor a 0.")
            .LessThanOrEqualTo(10080).WithMessage("Máximo 7 días (10080 min).")
            .When(x => x.ExpiresInMinutes.HasValue);

        // Solo REBOOT_DEVICE y WIPE_DATA requieren {"confirm":true}
        RuleFor(x => x.Parameters)
            .Must(ContainsConfirmTrue)
            .WithMessage(
                "Los comandos REBOOT_DEVICE y WIPE_DATA requieren " +
                "{\"confirm\":true} en Parameters.")
            .When(x => RequireConfirm.Contains(x.CommandType));
    }

    private static bool ContainsConfirmTrue(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(parameters);
            return doc.RootElement.TryGetProperty("confirm", out var confirm)
                   && confirm.GetBoolean();
        }
        catch { return false; }
    }
}