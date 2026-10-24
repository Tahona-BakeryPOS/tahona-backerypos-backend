using BakeryPOS.SharedKernel.Pagination;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Products.Queries;

/// <summary>
/// CU-04: List products with prices for the POS screen.
/// Uses Dapper for optimized read.
/// </summary>
public sealed record GetProductsQuery(
    Guid? CategoryId,
    string? SearchTerm,
    Guid? PriceListId,
    int Page = 1,
    int PageSize = 50) : IRequest<Result<PagedResult<ProductListDto>>>;

public sealed record ProductListDto(
    Guid Id,
    string Sku,
    string Name,
    string CategoryName,
    decimal UnitPrice,
    decimal TaxRate,
    string? ImageUrl,
    string? Barcode);
