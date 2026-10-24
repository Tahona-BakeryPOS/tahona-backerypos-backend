using FluentValidation;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreateProductCategoryValidator : AbstractValidator<CreateProductCategoryCommand>
{
    public CreateProductCategoryValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre de la categoría es obligatorio.")
            .MaximumLength(100);
    }
}
