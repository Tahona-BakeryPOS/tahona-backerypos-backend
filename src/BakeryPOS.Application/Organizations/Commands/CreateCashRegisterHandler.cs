using System.Text.Json;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateCashRegisterHandler : IRequestHandler<CreateCashRegisterCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateCashRegisterHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateCashRegisterCommand request, CancellationToken ct)
    {
        var branchExists = await _db.Branches.AnyAsync(b =>
            b.Id == request.BranchId && b.OrganizationId == _currentUser.OrganizationId && b.DeletedAt == null, ct);

        if (!branchExists)
            return Result.Failure<Guid>(Error.NotFound("Sucursal", request.BranchId));

        var cashRegister = new CashRegister
        {
            OrganizationId = _currentUser.OrganizationId,
            BranchId = request.BranchId,
            Name = request.Name,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.CashRegisters.AddAsync(cashRegister, ct);

        await _db.AuditLogs.AddAsync(new AuditLog
        {
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId,
            EntityName = nameof(CashRegister),
            EntityId = cashRegister.Id,
            Action = "CREATE",
            NewValues = JsonSerializer.Serialize(new { cashRegister.Name, cashRegister.BranchId }),
            CreatedBy = _currentUser.UserId.ToString()
        }, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(cashRegister.Id);
    }
}
