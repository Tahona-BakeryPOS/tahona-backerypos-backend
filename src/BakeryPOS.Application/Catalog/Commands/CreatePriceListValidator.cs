using FluentValidation;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreatePriceListValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
