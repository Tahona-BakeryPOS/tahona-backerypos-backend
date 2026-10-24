using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.CashMovements.Commands;

/// <summary>
/// CU-06: Registrar ingreso o retiro de efectivo en un turno abierto.
/// </summary>
public sealed record RegisterCashMovementCommand(
    Guid CashShiftId,
    CashMovementType Type,
    decimal Amount,
    Guid? OperationReasonId,
    string? Description) : IRequest<Result<Guid>>;
