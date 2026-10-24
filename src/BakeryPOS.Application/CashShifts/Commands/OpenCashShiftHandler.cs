using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.CashShifts.Commands;

public sealed class OpenCashShiftHandler : IRequestHandler<OpenCashShiftCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public OpenCashShiftHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(OpenCashShiftCommand request, CancellationToken ct)
    {
        // Check no open shift exists for this cash register
        var existingOpen = await _db.CashShifts
            .AnyAsync(s =>
                s.CashRegisterId == request.CashRegisterId &&
                s.OrganizationId == _currentUser.OrganizationId &&
                s.Status == CashShiftStatus.Open &&
                s.DeletedAt == null, ct);

        if (existingOpen)
            return Result.Failure<Guid>(Error.TurnoYaAbierto);

        var shift = new CashShift
        {
            OrganizationId = _currentUser.OrganizationId,
            CashRegisterId = request.CashRegisterId,
            CreatedBy = _currentUser.UserId.ToString()
        };

        shift.Open(request.OpeningAmount, _currentUser.UserId);

        await _db.CashShifts.AddAsync(shift, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(shift.Id);
    }
}
