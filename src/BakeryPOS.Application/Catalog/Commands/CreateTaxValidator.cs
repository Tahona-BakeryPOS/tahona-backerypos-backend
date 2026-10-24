using FluentValidation;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreateTaxValidator : AbstractValidator<CreateTaxCommand>
{
    public CreateTaxValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rate).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100)
            .WithMessage("La tasa del impuesto debe estar entre 0 y 100.");
    }
}
