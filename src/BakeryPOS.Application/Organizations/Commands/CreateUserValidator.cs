using FluentValidation;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Pin)
            .NotEmpty().WithMessage("El PIN es obligatorio.")
            .MinimumLength(4).WithMessage("El PIN debe tener al menos 4 dígitos.")
            .MaximumLength(6).WithMessage("El PIN no puede tener más de 6 dígitos.")
            .Matches(@"^\d+$").WithMessage("El PIN solo puede contener dígitos.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("El correo electrónico no es válido.");
    }
}
