using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Pagination;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Products.Queries;

public sealed class GetProductsHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductListDto>>>
{
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public GetProductsHandler(IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _dapper = dapper;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<ProductListDto>>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var conditions = "p.organization_id = @OrganizationId AND p.is_active = true AND p.deleted_at IS NULL";

        if (request.CategoryId.HasValue)
            conditions += " AND p.category_id = @CategoryId";

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            conditions += " AND (p.name ILIKE @Search OR p.sku ILIKE @Search OR pb.barcode ILIKE @Search)";

        var countSql = $"""
            SELECT COUNT(DISTINCT p.id)
            FROM products p
            LEFT JOIN product_barcodes pb ON p.id = pb.product_id AND pb.deleted_at IS NULL
            WHERE {conditions}
            """;

        var totalCount = await _dapper.QueryFirstOrDefaultAsync<int>(countSql, new
        {
            _currentUser.OrganizationId,
            request.CategoryId,
            Search = $"%{request.SearchTerm}%"
        }, ct);

        var dataSql = $"""
            SELECT DISTINCT p.id, p.sku, p.name, pc.name as category_name,
                   COALESCE(pr.unit_price, 0) as unit_price,
                   COALESCE(t.rate, 0) as tax_rate,
                   p.image_url,
                   (SELECT pb2.barcode FROM product_barcodes pb2 WHERE pb2.product_id = p.id AND pb2.deleted_at IS NULL LIMIT 1) as barcode
            FROM products p
            JOIN product_categories pc ON p.category_id = pc.id
            LEFT JOIN product_barcodes pb ON p.id = pb.product_id AND pb.deleted_at IS NULL
            LEFT JOIN prices pr ON p.id = pr.product_id AND pr.deleted_at IS NULL
                AND pr.price_list_id = COALESCE(@PriceListId,
                    (SELECT pl.id FROM price_lists pl WHERE pl.organization_id = @OrganizationId AND pl.is_default = true LIMIT 1))
            LEFT JOIN taxes t ON p.tax_id = t.id
            WHERE {conditions}
            ORDER BY pc.display_order, p.name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var items = await _dapper.QueryAsync<ProductListDto>(dataSql, new
        {
            _currentUser.OrganizationId,
            request.CategoryId,
            request.PriceListId,
            Search = $"%{request.SearchTerm}%",
            Offset = (request.Page - 1) * request.PageSize,
            request.PageSize
        }, ct);

        return Result.Success(new PagedResult<ProductListDto>(items, request.Page, request.PageSize, totalCount));
    }
}
