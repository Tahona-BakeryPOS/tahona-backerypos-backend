using FluentValidation;

namespace BakeryPOS.Application.Auth.Commands;

public sealed class LoginByPinValidator : AbstractValidator<LoginByPinCommand>
{
    public LoginByPinValidator()
    {
        RuleFor(x => x.Pin)
            .NotEmpty().WithMessage("El PIN es obligatorio.")
            .MinimumLength(4).WithMessage("El PIN debe tener al menos 4 dígitos.")
            .MaximumLength(6).WithMessage("El PIN no puede tener más de 6 dígitos.")
            .Matches(@"^\d+$").WithMessage("El PIN solo puede contener dígitos.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("La sucursal es obligatoria.");
    }
}
