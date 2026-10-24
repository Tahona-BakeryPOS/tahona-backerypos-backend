using FluentValidation;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class AssignUserRoleValidator : AbstractValidator<AssignUserRoleCommand>
{
    public AssignUserRoleValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
    }
}
