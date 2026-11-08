using FluentValidation;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

public sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("El código es obligatorio: letras, números, guion o guion bajo (máx. 20).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre es obligatorio (máx. 120).");
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.CityCode).Matches("^[0-9]{5}$").When(x => x.CityCode is not null)
            .WithMessage("El código de ciudad es el código DANE de 5 dígitos.");
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}
