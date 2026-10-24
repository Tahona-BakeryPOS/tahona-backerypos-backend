using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.CashMovements.Queries;

public sealed class GetCashMovementsByShiftHandler
    : IRequestHandler<GetCashMovementsByShiftQuery, Result<IReadOnlyList<CashMovementDto>>>
{
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public GetCashMovementsByShiftHandler(IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _dapper = dapper;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CashMovementDto>>> Handle(GetCashMovementsByShiftQuery request, CancellationToken ct)
    {
        const string sql = """
            SELECT cm.id, cm.type::text as type, cm.amount,
                   orz.name as reason_name, cm.description, cm.occurred_at
            FROM cash_movements cm
            JOIN cash_shifts cs ON cm.cash_shift_id = cs.id
            LEFT JOIN operation_reasons orz ON cm.operation_reason_id = orz.id
            WHERE cm.cash_shift_id = @CashShiftId
              AND cs.organization_id = @OrganizationId
              AND cm.deleted_at IS NULL
            ORDER BY cm.occurred_at
            """;

        var movements = await _dapper.QueryAsync<CashMovementDto>(
            sql, new { request.CashShiftId, _currentUser.OrganizationId }, ct);

        return Result.Success(movements);
    }
}
