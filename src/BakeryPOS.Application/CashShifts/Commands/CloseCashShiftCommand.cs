using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.CashShifts.Commands;

/// <summary>
/// CU-03: Close a cash shift.
/// </summary>
public sealed record CloseCashShiftCommand(
    Guid CashShiftId,
    decimal ClosingAmount,
    string? Notes) : IRequest<Result<CloseCashShiftResponse>>;

public sealed record CloseCashShiftResponse(
    Guid CashShiftId,
    decimal OpeningAmount,
    decimal ClosingAmount,
    decimal? ExpectedAmount,
    decimal? Difference);
