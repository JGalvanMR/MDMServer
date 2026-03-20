using FluentValidation;
using MDMServer.DTOs.Device;

namespace MDMServer.Validators;

public class RegisterDeviceValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("DeviceId es requerido.")
            .MinimumLength(8).WithMessage("DeviceId debe tener al menos 8 caracteres.")
            .MaximumLength(100).WithMessage("DeviceId no puede superar 100 caracteres.")
            .Matches(@"^[a-zA-Z0-9_\-]+$")
            .WithMessage("DeviceId solo puede contener letras, números, guiones y guiones bajos.");

        RuleFor(x => x.DeviceName)
            .MaximumLength(200).WithMessage("DeviceName no puede superar 200 caracteres.")
            .When(x => x.DeviceName != null);

        RuleFor(x => x.Model)
            .MaximumLength(200).WithMessage("Model no puede superar 200 caracteres.")
            .When(x => x.Model != null);

        RuleFor(x => x.Manufacturer)
            .MaximumLength(200).WithMessage("Manufacturer no puede superar 200 caracteres.")
            .When(x => x.Manufacturer != null);

        RuleFor(x => x.AndroidVersion)
            .MaximumLength(50).WithMessage("AndroidVersion no puede superar 50 caracteres.")
            .When(x => x.AndroidVersion != null);

        RuleFor(x => x.ApiLevel)
            .InclusiveBetween(26, 99)
            .WithMessage("ApiLevel debe estar entre 26 (Android 8) y 99.")
            .When(x => x.ApiLevel.HasValue);
    }
}