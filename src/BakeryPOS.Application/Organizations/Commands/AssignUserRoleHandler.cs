using System.Text.Json;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class AssignUserRoleHandler : IRequestHandler<AssignUserRoleCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AssignUserRoleHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AssignUserRoleCommand request, CancellationToken ct)
    {
        var userExists = await _db.Users.AnyAsync(u =>
            u.Id == request.UserId && u.OrganizationId == _currentUser.OrganizationId && u.DeletedAt == null, ct);
        if (!userExists)
            return Result.Failure<Guid>(Error.NotFound("Usuario", request.UserId));

        var roleExists = await _db.Roles.AnyAsync(r =>
            r.Id == request.RoleId && r.OrganizationId == _currentUser.OrganizationId && r.DeletedAt == null, ct);
        if (!roleExists)
            return Result.Failure<Guid>(Error.NotFound("Rol", request.RoleId));

        var branchExists = await _db.Branches.AnyAsync(b =>
            b.Id == request.BranchId && b.OrganizationId == _currentUser.OrganizationId && b.DeletedAt == null, ct);
        if (!branchExists)
            return Result.Failure<Guid>(Error.NotFound("Sucursal", request.BranchId));

        var alreadyAssigned = await _db.UserAssignments.AnyAsync(ua =>
            ua.UserId == request.UserId &&
            ua.RoleId == request.RoleId &&
            ua.BranchId == request.BranchId &&
            ua.IsActive &&
            ua.DeletedAt == null, ct);
        if (alreadyAssigned)
            return Result.Failure<Guid>(Error.CodigoDuplicado);

        var assignment = new UserAssignment
        {
            OrganizationId = _currentUser.OrganizationId,
            UserId = request.UserId,
            RoleId = request.RoleId,
            BranchId = request.BranchId,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.UserAssignments.AddAsync(assignment, ct);

        await _db.AuditLogs.AddAsync(new AuditLog
        {
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId,
            EntityName = nameof(UserAssignment),
            EntityId = assignment.Id,
            Action = "CREATE",
            NewValues = JsonSerializer.Serialize(new { request.UserId, request.RoleId, request.BranchId }),
            CreatedBy = _currentUser.UserId.ToString()
        }, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(assignment.Id);
    }
}
