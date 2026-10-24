using FluentValidation;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateCashRegisterValidator : AbstractValidator<CreateCashRegisterCommand>
{
    public CreateCashRegisterValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
