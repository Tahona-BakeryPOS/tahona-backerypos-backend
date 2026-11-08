using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Users;

/// <summary>Usuario visto desde una empresa: datos globales + su pertenencia y asignaciones en ella.</summary>
/// <param name="Id">Id del usuario (global); otra empresa no lo ve aunque exista.</param>
/// <param name="Version">ETag de la pertenencia para If-Match.</param>
public sealed record UserDto(
    Guid Id,
    string FullName,
    string? Email,
    bool IsActive,
    bool HasPin,
    bool CanUseBackoffice,
    IReadOnlyList<AssignmentDto> Assignments,
    string Version);

public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, Guid? BranchId, DateTime ValidFrom, DateTime? ValidTo, bool IsCurrent);

public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record GetUsersQuery : IRequest<Result<IReadOnlyList<UserDto>>>;

public sealed record GetUserByIdQuery(Guid Id) : IRequest<Result<UserDto>>;

public sealed record GetRolesQuery : IRequest<Result<IReadOnlyList<RoleDto>>>;

public sealed class UserQueriesHandler :
    IRequestHandler<GetUsersQuery, Result<IReadOnlyList<UserDto>>>,
    IRequestHandler<GetUserByIdQuery, Result<UserDto>>,
    IRequestHandler<GetRolesQuery, Result<IReadOnlyList<RoleDto>>>
{
    private readonly IAppDbContext _db;
    private readonly TimeProvider _clock;

    public UserQueriesHandler(IAppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<UserDto>>> Handle(GetUsersQuery request, CancellationToken ct) =>
        Result.Success(await UserReader.ReadAsync(_db, null, _clock.GetUtcNow().UtcDateTime, ct));

    public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var users = await UserReader.ReadAsync(_db, request.Id, _clock.GetUtcNow().UtcDateTime, ct);
        return users.Count == 0 ? Result.Failure<UserDto>(Error.NotFound("Usuario", request.Id)) : Result.Success(users[0]);
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Code, r.Name, r.IsSystem, PermissionIds = r.Permissions.Select(p => p.PermissionId).ToList() })
            .ToListAsync(ct);
        var codes = await _db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        IReadOnlyList<RoleDto> result = roles
            .Select(r => new RoleDto(r.Id, r.Code, r.Name, r.IsSystem, r.PermissionIds.Select(id => codes[id]).Order(StringComparer.Ordinal).ToList()))
            .ToList();
        return Result.Success(result);
    }
}

/// <summary>Lectura de usuarios de la empresa en sesión (el filtro por empresa aplica a pertenencias y asignaciones).</summary>
internal static class UserReader
{
    public static async Task<IReadOnlyList<UserDto>> ReadAsync(IAppDbContext db, Guid? userId, DateTime now, CancellationToken ct)
    {
        var members = await db.Memberships.AsNoTracking()
            .Where(m => userId == null || m.UserId == userId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new
            {
                MembershipId = m.Id, u.Id, u.FullName, u.Email, m.IsActive, HasPin = m.PinHash != null,
                CanUseBackoffice = u.Email != null && u.PasswordHash != null, m.RowVersion,
            })
            .ToListAsync(ct);
        if (members.Count == 0) return [];

        var membershipIds = members.Select(m => m.MembershipId).ToList();
        var assignments = await db.RoleAssignments.AsNoTracking()
            .Where(a => membershipIds.Contains(a.MembershipId))
            .Join(db.Roles, a => a.RoleId, r => r.Id, (a, r) => new { Assignment = a, r.Code, r.Name })
            .ToListAsync(ct);
        var byMembership = assignments.ToLookup(x => x.Assignment.MembershipId);

        return members
            .OrderBy(m => m.FullName)
            .Select(m => new UserDto(m.Id, m.FullName, m.Email, m.IsActive, m.HasPin, m.CanUseBackoffice,
                byMembership[m.MembershipId]
                    .OrderBy(x => x.Assignment.ValidFrom)
                    .Select(x => new AssignmentDto(x.Assignment.Id, x.Assignment.RoleId, x.Code, x.Name, x.Assignment.BranchId,
                        x.Assignment.ValidFrom, x.Assignment.ValidTo, x.Assignment.IsValidAt(now)))
                    .ToList(),
                RowVersions.Encode(m.RowVersion)))
            .ToList();
    }
}
