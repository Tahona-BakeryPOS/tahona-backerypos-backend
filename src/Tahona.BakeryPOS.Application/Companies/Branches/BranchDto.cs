using System.Linq.Expressions;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Companies;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <param name="Version">ETag for If-Match on PUT.</param>
public sealed record BranchDto(Guid Id, string Code, string Name, string? Address, string? CityCode, string? Phone, bool IsActive, string Version)
{
    public static readonly Expression<Func<Branch, BranchDto>> Projection = b =>
        new BranchDto(b.Id, b.Code, b.Name, b.Address, b.CityCode, b.Phone, b.IsActive, RowVersions.Encode(b.RowVersion));

    private static readonly Func<Branch, BranchDto> Map = Projection.Compile();

    public static BranchDto From(Branch b) => Map(b);
}
