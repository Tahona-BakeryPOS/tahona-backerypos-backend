using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.CashMovements.Commands;

public sealed class RegisterCashMovementHandler : IRequestHandler<RegisterCashMovementCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public RegisterCashMovementHandler(IApplicationDbContext db, IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _db = db;
        _dapper = dapper;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RegisterCashMovementCommand request, CancellationToken ct)
    {
        var shift = await _db.CashShifts
            .FirstOrDefaultAsync(s =>
                s.Id == request.CashShiftId &&
                s.OrganizationId == _currentUser.OrganizationId &&
                s.DeletedAt == null, ct);

        if (shift is null)
            return Result.Failure<Guid>(Error.NotFound("Turno", request.CashShiftId));

        if (shift.Status == CashShiftStatus.Closed)
            return Result.Failure<Guid>(Error.TurnoCerrado);

        // Retiros requieren motivo (autorización del propietario se valida vía permiso en el controller)
        if (request.Type == CashMovementType.Expense && request.OperationReasonId is null)
            return Result.Failure<Guid>(Error.Validation("Los retiros de efectivo requieren un motivo."));

        if (request.Type == CashMovementType.Expense)
        {
            var available = await GetAvailableCashAsync(shift.Id, shift.OpeningAmount, ct);
            if (request.Amount > available)
                return Result.Failure<Guid>(Error.RetiroExcedeSaldo);
        }

        var movement = new CashMovement
        {
            OrganizationId = _currentUser.OrganizationId,
            CashShiftId = shift.Id,
            OperationReasonId = request.OperationReasonId,
            Type = request.Type,
            Amount = request.Amount,
            Description = request.Description,
            OccurredAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.CashMovements.AddAsync(movement, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(movement.Id);
    }

    /// <summary>
    /// Efectivo esperado = apertura + ventas en efectivo + ingresos − retiros (sin contar el movimiento en curso).
    /// </summary>
    private async Task<decimal> GetAvailableCashAsync(Guid cashShiftId, decimal openingAmount, CancellationToken ct)
    {
        const string sql = """
            SELECT
                COALESCE(SUM(CASE WHEN cm.type = 'Income' THEN cm.amount ELSE 0 END), 0) -
                COALESCE(SUM(CASE WHEN cm.type = 'Expense' THEN cm.amount ELSE 0 END), 0)
            FROM cash_movements cm
            WHERE cm.cash_shift_id = @CashShiftId AND cm.deleted_at IS NULL
            """;

        var movementsNet = await _dapper.QueryFirstOrDefaultAsync<decimal>(sql, new { CashShiftId = cashShiftId }, ct);

        const string salesSql = """
            SELECT COALESCE(SUM(pay.amount), 0)
            FROM payments pay
            JOIN sales s ON pay.sale_id = s.id
            JOIN payment_methods pm ON pay.payment_method_id = pm.id
            WHERE s.cash_shift_id = @CashShiftId
              AND s.status = 'Completed'
              AND pm.code = 'EFECTIVO'
              AND pay.deleted_at IS NULL AND s.deleted_at IS NULL
            """;

        var cashSales = await _dapper.QueryFirstOrDefaultAsync<decimal>(salesSql, new { CashShiftId = cashShiftId }, ct);

        return openingAmount + movementsNet + cashSales;
    }
}
