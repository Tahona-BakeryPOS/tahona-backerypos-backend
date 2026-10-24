using FluentValidation;

namespace BakeryPOS.Application.Sales.Commands;

public sealed class CreateSaleValidator : AbstractValidator<CreateSaleCommand>
{
    public CreateSaleValidator()
    {
        RuleFor(x => x.SaleId)
            .NotEmpty().WithMessage("El ID de la venta es obligatorio.");

        RuleFor(x => x.CashShiftId)
            .NotEmpty().WithMessage("El turno de caja es obligatorio.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("La venta debe tener al menos una línea.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage("El producto es obligatorio.");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("La cantidad debe ser mayor a cero.");
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("El precio unitario no puede ser negativo.");
        });

        RuleFor(x => x.Payments)
            .NotEmpty().WithMessage("La venta debe tener al menos un pago.");

        RuleForEach(x => x.Payments).ChildRules(payment =>
        {
            payment.RuleFor(p => p.PaymentMethodId).NotEmpty().WithMessage("El medio de pago es obligatorio.");
            payment.RuleFor(p => p.Amount).GreaterThan(0).WithMessage("El monto del pago debe ser mayor a cero.");
        });
    }
}
