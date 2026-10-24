using FluentValidation;

namespace BakeryPOS.Application.Sync.Commands;

public sealed class SyncSalesValidator : AbstractValidator<SyncSalesCommand>
{
    public SyncSalesValidator()
    {
        RuleFor(x => x.Sales)
            .NotEmpty().WithMessage("El lote de sincronización debe contener al menos una venta.");
    }
}
