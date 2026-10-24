using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

/// <summary>
/// CU-10: Crear caja registradora en una sucursal.
/// </summary>
public sealed record CreateCashRegisterCommand(Guid BranchId, string Name) : IRequest<Result<Guid>>;
