using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Users;

public sealed record AssignmentInput(Guid RoleId, Guid? BranchId, DateTime? ValidFrom = null, DateTime? ValidTo = null);

/// <summary>
/// Creates a user in the company (US-2.1). With an email they have backoffice access; if the email already
/// exists on the platform that person is added to the company (D-08) without touching their password.
/// </summary>
public sealed record CreateUserCommand(string FullName, string? Email, string? Pin, IReadOnlyList<AssignmentInput> Assignments)
    : IRequest<Result<CreatedUserDto>>;

/// <param name="TemporaryPassword">One-time password; only in this response and only if the user is new.</param>
public sealed record CreatedUserDto(UserDto User, string? TemporaryPassword);

/// <summary>Edits the user's name, status and PIN in this company. Requires the version read (If-Match).</summary>
public sealed record UpdateUserCommand(Guid UserId, string? ExpectedVersion, string FullName, bool IsActive, string? Pin)
    : IRequest<Result<UserDto>>;

public sealed record AddAssignmentCommand(Guid UserId, Guid RoleId, Guid? BranchId, DateTime? ValidFrom, DateTime? ValidTo)
    : IRequest<Result<AssignmentDto>>;

/// <summary>Removes an assignment by closing its validity (it is not deleted).</summary>
public sealed record EndAssignmentCommand(Guid UserId, Guid AssignmentId) : IRequest<Result>;

internal static class UserRules
{
    public const string PinPattern = "^[0-9]{4,6}$";
    public const string PinMessage = "El PIN debe tener entre 4 y 6 dígitos.";
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Pin).Matches(UserRules.PinPattern).WithMessage(UserRules.PinMessage).When(x => x.Pin is not null);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Email) || x.Pin is not null)
            .WithName("email").WithMessage("Indica un correo (backoffice), un PIN (caja) o ambos.");
        RuleFor(x => x.Assignments).NotEmpty().WithMessage("Asigna al menos un rol.");
        RuleForEach(x => x.Assignments).ChildRules(a => a.RuleFor(x => x.RoleId).NotEmpty());
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Pin).Matches(UserRules.PinPattern).WithMessage(UserRules.PinMessage).When(x => x.Pin is not null);
    }
}

public sealed class AddAssignmentValidator : AbstractValidator<AddAssignmentCommand>
{
    public AddAssignmentValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom).When(x => x.ValidFrom is not null && x.ValidTo is not null);
    }
}

public sealed class UserCommandsHandler :
    IRequestHandler<CreateUserCommand, Result<CreatedUserDto>>,
    IRequestHandler<UpdateUserCommand, Result<UserDto>>,
    IRequestHandler<AddAssignmentCommand, Result<AssignmentDto>>,
    IRequestHandler<EndAssignmentCommand, Result>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly IPasswordHasher _hasher;
    private readonly SessionIssuer _sessions;
    private readonly TimeProvider _clock;

    public UserCommandsHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, IPasswordHasher hasher,
        SessionIssuer sessions, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _hasher = hasher;
        _sessions = sessions;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<Result<CreatedUserDto>> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var companyId = _tenant.RequiredCompanyId;
        var now = Now;

        foreach (var input in request.Assignments)
        {
            var target = await ValidateTargetAsync(input.RoleId, input.BranchId, ct);
            if (target.IsFailure)
                return Result.Failure<CreatedUserDto>(target.Error);
        }

        var email = User.NormalizeEmail(request.Email);
        string? temporaryPassword = null;
        var user = email is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is not null)
        {
            if (await _db.Memberships.AnyAsync(m => m.UserId == user.Id, ct))
                return Result.Failure<CreatedUserDto>(Error.CorreoEnUso);
        }
        else
        {
            temporaryPassword = email is null ? null : SecretTokens.NewTemporaryPassword();
            user = User.Create(request.FullName, email, temporaryPassword is null ? null : _hasher.Hash(temporaryPassword));
            _db.Users.Add(user);
        }

        var membership = Membership.Create(companyId, user.Id, request.Pin is null ? null : _hasher.Hash(request.Pin));
        _db.Memberships.Add(membership);
        foreach (var input in request.Assignments)
            _db.RoleAssignments.Add(RoleAssignment.Create(companyId, membership.Id, input.RoleId, input.BranchId, input.ValidFrom ?? now, input.ValidTo));
        await _db.SaveChangesAsync(ct);

        var created = await UserReader.ReadAsync(_db, user.Id, now, ct);
        return Result.Success(new CreatedUserDto(created[0], temporaryPassword));
    }

    public async Task<Result<UserDto>> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<UserDto>(version.Error);

        var membership = await _db.Memberships.FirstOrDefaultAsync(m => m.UserId == request.UserId, ct);
        if (membership is null)
            return Result.Failure<UserDto>(Error.NotFound("Usuario", request.UserId));
        _db.ExpectVersion(membership, version.Value);
        var user = await _db.Users.FirstAsync(u => u.Id == membership.UserId, ct);

        if (!request.IsActive && membership.IsActive)
        {
            if (request.UserId == _tenant.UserId)
                return Result.Failure<UserDto>(Error.AutoDesactivacion);
            if (!await HasOtherActiveOwnerAsync(excludeMembershipId: membership.Id, excludeAssignmentId: null, ct))
                return Result.Failure<UserDto>(Error.UltimoPropietario);
            membership.Deactivate();
            await _sessions.RevokeAllAsync(user.Id, membership.CompanyId, ct);
        }
        else if (request.IsActive)
        {
            membership.Activate();
        }

        if (!string.Equals(user.FullName, request.FullName.Trim(), StringComparison.Ordinal))
        {
            var shared = await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
                .AnyAsync(m => m.UserId == user.Id && m.Id != membership.Id, ct);
            if (shared || user.IsPlatformAdmin)
                return Result.Failure<UserDto>(Error.UsuarioCompartido);
            user.Rename(request.FullName);
        }

        if (request.Pin is not null)
            membership.SetPin(_hasher.Hash(request.Pin));

        await _db.SaveChangesAsync(ct);
        return Result.Success((await UserReader.ReadAsync(_db, user.Id, Now, ct))[0]);
    }

    public async Task<Result<AssignmentDto>> Handle(AddAssignmentCommand request, CancellationToken ct)
    {
        var now = Now;
        var membership = await _db.Memberships.FirstOrDefaultAsync(m => m.UserId == request.UserId, ct);
        if (membership is null)
            return Result.Failure<AssignmentDto>(Error.NotFound("Usuario", request.UserId));

        var target = await ValidateTargetAsync(request.RoleId, request.BranchId, ct);
        if (target.IsFailure)
            return Result.Failure<AssignmentDto>(target.Error);

        var duplicate = await _db.RoleAssignments.AnyAsync(a => a.MembershipId == membership.Id && a.RoleId == request.RoleId
            && a.BranchId == request.BranchId && (a.ValidTo == null || a.ValidTo > now), ct);
        if (duplicate)
            return Result.Failure<AssignmentDto>(Error.AsignacionDuplicada);

        var assignment = RoleAssignment.Create(membership.CompanyId, membership.Id, request.RoleId, request.BranchId,
            request.ValidFrom ?? now, request.ValidTo);
        _db.RoleAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);

        var role = target.Value;
        return Result.Success(new AssignmentDto(assignment.Id, role.Id, role.Code, role.Name, assignment.BranchId,
            assignment.ValidFrom, assignment.ValidTo, assignment.IsValidAt(now)));
    }

    public async Task<Result> Handle(EndAssignmentCommand request, CancellationToken ct)
    {
        var membership = await _db.Memberships.FirstOrDefaultAsync(m => m.UserId == request.UserId, ct);
        var assignment = membership is null
            ? null
            : await _db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == request.AssignmentId && a.MembershipId == membership.Id, ct);
        if (assignment is null)
            return Result.Failure(Error.NotFound("Asignación", request.AssignmentId));

        if (!await HasOtherActiveOwnerAsync(excludeMembershipId: null, excludeAssignmentId: assignment.Id, ct))
            return Result.Failure(Error.UltimoPropietario);

        assignment.End(Now);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Role of the company and, if given, an active branch of the company. Those of another company do not exist: 404.</summary>
    private async Task<Result<Role>> ValidateTargetAsync(Guid roleId, Guid? branchId, CancellationToken ct)
    {
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null)
            return Result.Failure<Role>(Error.NotFound("Rol", roleId));
        if (branchId is { } id)
        {
            var branch = await _branches.FindAsync(id, ct);
            if (branch is null)
                return Result.Failure<Role>(Error.NotFound("Sucursal", id));
            if (!branch.IsActive)
                return Result.Failure<Role>(Error.Validation("La sucursal está desactivada."));
        }
        return Result.Success(role);
    }

    /// <summary>
    /// Does another current, active owner remain if this membership or this assignment is removed? If what
    /// is removed is not an owner's, the answer is always yes.
    /// </summary>
    private async Task<bool> HasOtherActiveOwnerAsync(Guid? excludeMembershipId, Guid? excludeAssignmentId, CancellationToken ct)
    {
        var now = Now;
        var ownerRoleId = await _db.Roles.Where(r => r.Code == SystemRoles.Owner).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (ownerRoleId is null) return true;

        var owners = await _db.RoleAssignments
            .Where(a => a.RoleId == ownerRoleId && a.ValidFrom <= now && (a.ValidTo == null || a.ValidTo > now))
            .Join(_db.Memberships.Where(m => m.IsActive), a => a.MembershipId, m => m.Id, (a, m) => new { a.Id, a.MembershipId })
            .ToListAsync(ct);

        var removesAnOwner = owners.Any(o => o.MembershipId == excludeMembershipId || o.Id == excludeAssignmentId);
        if (!removesAnOwner) return true;
        return owners.Any(o => o.MembershipId != excludeMembershipId && o.Id != excludeAssignmentId);
    }
}
