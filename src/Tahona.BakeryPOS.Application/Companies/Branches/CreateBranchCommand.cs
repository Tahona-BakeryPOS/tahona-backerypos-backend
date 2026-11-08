using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <summary>Crea una sucursal en la empresa del usuario. La empresa nunca viaja en el cuerpo.</summary>
public sealed record CreateBranchCommand(string Code, string Name, string? Address, string? CityCode = null, string? Phone = null)
    : IRequest<Result<BranchDto>>;
