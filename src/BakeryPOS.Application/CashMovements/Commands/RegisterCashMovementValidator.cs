using FluentValidation;

namespace BakeryPOS.Application.CashMovements.Commands;

public sealed class RegisterCashMovementValidator : AbstractValidator<RegisterCashMovementCommand>
{
    public RegisterCashMovementValidator()
    {
        RuleFor(x => x.CashShiftId)
            .NotEmpty().WithMessage("El turno de caja es obligatorio.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("El monto debe ser mayor a cero.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("El tipo de movimiento no es válido.");
    }
}
