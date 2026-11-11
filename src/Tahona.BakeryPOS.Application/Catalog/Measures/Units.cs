using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog.Measures;

/// <param name="Kind"><c>count</c>, <c>mass</c> o <c>volume</c>.</param>
/// <param name="AllowsDecimals">Mass and volume: the register asks for the quantity (weight) when selling.</param>
public sealed record UnitDto(Guid Id, string Code, string Name, string Kind, string DianCode, bool AllowsDecimals);

/// <summary>Units of measure: a global catalog, the same for all companies.</summary>
public sealed record GetUnitsQuery : IRequest<Result<IReadOnlyList<UnitDto>>>;

public sealed class GetUnitsHandler : IRequestHandler<GetUnitsQuery, Result<IReadOnlyList<UnitDto>>>
{
    private readonly IAppDbContext _db;

    public GetUnitsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<UnitDto>>> Handle(GetUnitsQuery request, CancellationToken ct)
    {
        var units = await _db.Units.AsNoTracking().OrderBy(u => u.Kind).ThenBy(u => u.Code).ToListAsync(ct);
        IReadOnlyList<UnitDto> list = units
            .Select(u => new UnitDto(u.Id, u.Code, u.Name, u.Kind.ToString().ToLowerInvariant(), u.DianCode, u.AllowsDecimals))
            .ToList();
        return Result.Success(list);
    }
}
