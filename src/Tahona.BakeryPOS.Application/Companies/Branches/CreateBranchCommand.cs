using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <summary>Creates a branch in the user's company. The company never travels in the body.</summary>
public sealed record CreateBranchCommand(string Code, string Name, string? Address, string? CityCode = null, string? Phone = null)
    : IRequest<Result<BranchDto>>;
