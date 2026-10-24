using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.CashShifts.Commands;

/// <summary>
/// CU-02: Open a cash shift.
/// </summary>
public sealed record OpenCashShiftCommand(
    Guid CashRegisterId,
    decimal OpeningAmount) : IRequest<Result<Guid>>;
