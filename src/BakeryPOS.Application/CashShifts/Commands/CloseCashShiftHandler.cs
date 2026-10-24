using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.CashShifts.Commands;

public sealed class CloseCashShiftHandler : IRequestHandler<CloseCashShiftCommand, Result<CloseCashShiftResponse>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CloseCashShiftHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<CloseCashShiftResponse>> Handle(CloseCashShiftCommand request, CancellationToken ct)
    {
        var shift = await _db.CashShifts
            .Include(s => s.CashMovements)
            .FirstOrDefaultAsync(s =>
                s.Id == request.CashShiftId &&
                s.OrganizationId == _currentUser.OrganizationId &&
                s.DeletedAt == null, ct);

        if (shift is null)
            return Result.Failure<CloseCashShiftResponse>(Error.NotFound("Turno", request.CashShiftId));

        try
        {
            shift.Close(request.ClosingAmount, _currentUser.UserId, request.Notes);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<CloseCashShiftResponse>(Error.TurnoCerrado);
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(new CloseCashShiftResponse(
            shift.Id,
            shift.OpeningAmount,
            shift.ClosingAmount!.Value,
            shift.ExpectedAmount,
            shift.Difference));
    }
}
