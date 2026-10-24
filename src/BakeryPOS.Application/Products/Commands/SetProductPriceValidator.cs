using FluentValidation;

namespace BakeryPOS.Application.Products.Commands;

public sealed class SetProductPriceValidator : AbstractValidator<SetProductPriceCommand>
{
    public SetProductPriceValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.PriceListId).NotEmpty();
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
    }
}
