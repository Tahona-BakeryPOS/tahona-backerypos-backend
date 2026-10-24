using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.CashShifts.Commands;

public sealed class CloseCashShiftHandler : IRequestHandler<CloseCashShiftCommand, Result<CloseCashShiftResponse>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public CloseCashShiftHandler(IApplicationDbContext db, IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _db = db;
        _dapper = dapper;
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

        if (shift.Status == CashShiftStatus.Closed)
            return Result.Failure<CloseCashShiftResponse>(Error.TurnoCerrado);

        const string cashSalesSql = """
            SELECT COALESCE(SUM(pay.amount), 0)
            FROM payments pay
            JOIN sales s ON pay.sale_id = s.id
            JOIN payment_methods pm ON pay.payment_method_id = pm.id
            WHERE s.cash_shift_id = @CashShiftId
              AND s.status = 'Completed'
              AND pm.code = 'EFECTIVO'
              AND pay.deleted_at IS NULL AND s.deleted_at IS NULL
            """;

        var cashSalesTotal = await _dapper.QueryFirstOrDefaultAsync<decimal>(
            cashSalesSql, new { request.CashShiftId }, ct);

        try
        {
            shift.Close(request.ClosingAmount, _currentUser.UserId, cashSalesTotal, request.Notes);
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
