using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <summary>Sucursales de la empresa del usuario (el filtro por empresa lo aplica el DbContext).</summary>
public sealed record GetBranchesQuery : IRequest<Result<IReadOnlyList<BranchDto>>>;

/// <summary>Una sucursal; si es de otra empresa responde NO_ENCONTRADO, igual que si no existiera.</summary>
public sealed record GetBranchByIdQuery(Guid Id) : IRequest<Result<BranchDto>>;
