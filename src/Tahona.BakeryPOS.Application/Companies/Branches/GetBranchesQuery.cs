using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <summary>Branches of the user's company (the company filter is applied by the DbContext).</summary>
public sealed record GetBranchesQuery : IRequest<Result<IReadOnlyList<BranchDto>>>;

/// <summary>One branch; if it belongs to another company it answers NOT_FOUND, as if it did not exist.</summary>
public sealed record GetBranchByIdQuery(Guid Id) : IRequest<Result<BranchDto>>;
