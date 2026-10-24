using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

/// <summary>
/// CU-10: Crear sucursal.
/// </summary>
public sealed record CreateBranchCommand(string Name, string? Address, string? Phone) : IRequest<Result<Guid>>;
