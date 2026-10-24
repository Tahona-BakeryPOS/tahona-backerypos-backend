using FluentValidation;

namespace BakeryPOS.Application.Products.Commands;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("La categoría es obligatoria.");
    }
}
