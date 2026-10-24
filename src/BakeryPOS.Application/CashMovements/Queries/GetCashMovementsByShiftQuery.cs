using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.CashMovements.Queries;

/// <summary>
/// CU-06: Listar los movimientos de efectivo de un turno.
/// </summary>
public sealed record GetCashMovementsByShiftQuery(Guid CashShiftId) : IRequest<Result<IReadOnlyList<CashMovementDto>>>;

public sealed record CashMovementDto(
    Guid Id,
    string Type,
    decimal Amount,
    string? ReasonName,
    string? Description,
    DateTime OccurredAt);
