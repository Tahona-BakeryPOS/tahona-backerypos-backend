using System.Text.Json;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateBranchHandler : IRequestHandler<CreateBranchCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateBranchHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var branch = new Branch
        {
            OrganizationId = _currentUser.OrganizationId,
            Name = request.Name,
            Address = request.Address,
            Phone = request.Phone,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.Branches.AddAsync(branch, ct);

        await _db.AuditLogs.AddAsync(new AuditLog
        {
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId,
            EntityName = nameof(Branch),
            EntityId = branch.Id,
            Action = "CREATE",
            NewValues = JsonSerializer.Serialize(new { branch.Name, branch.Address, branch.Phone }),
            CreatedBy = _currentUser.UserId.ToString()
        }, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(branch.Id);
    }
}
