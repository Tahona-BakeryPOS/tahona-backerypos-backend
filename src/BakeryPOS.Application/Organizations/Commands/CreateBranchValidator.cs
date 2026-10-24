using FluentValidation;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
