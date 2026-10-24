using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sales.Queries;

/// <summary>
/// Uses Dapper for optimized read (CQRS query side).
/// </summary>
public sealed class GetSaleByIdHandler : IRequestHandler<GetSaleByIdQuery, Result<SaleDto>>
{
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public GetSaleByIdHandler(IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _dapper = dapper;
        _currentUser = currentUser;
    }

    public async Task<Result<SaleDto>> Handle(GetSaleByIdQuery request, CancellationToken ct)
    {
        const string saleSql = """
            SELECT s.id, s.document_number, s.sale_date, s.subtotal,
                   s.tax_total, s.discount_total, s.total,
                   s.status::text as status,
                   u.full_name as sold_by,
                   p.full_name as customer_name
            FROM sales s
            JOIN app_users u ON s.sold_by_user_id = u.id
            LEFT JOIN parties p ON s.customer_id = p.id
            WHERE s.id = @SaleId AND s.organization_id = @OrganizationId
              AND s.deleted_at IS NULL
            """;

        var sale = await _dapper.QueryFirstOrDefaultAsync<SaleQueryResult>(
            saleSql,
            new { request.SaleId, _currentUser.OrganizationId },
            ct);

        if (sale is null)
            return Result.Failure<SaleDto>(Error.NotFound("Venta", request.SaleId));

        const string linesSql = """
            SELECT line_number, product_name, quantity, unit_price,
                   discount_percent, tax_rate, line_total
            FROM sale_lines
            WHERE sale_id = @SaleId AND deleted_at IS NULL
            ORDER BY line_number
            """;

        var lines = await _dapper.QueryAsync<SaleLineDto>(linesSql, new { request.SaleId }, ct);

        const string paymentsSql = """
            SELECT pm.name as payment_method_name, pay.amount, pay.reference
            FROM payments pay
            JOIN payment_methods pm ON pay.payment_method_id = pm.id
            WHERE pay.sale_id = @SaleId AND pay.deleted_at IS NULL
            """;

        var payments = await _dapper.QueryAsync<PaymentDto>(paymentsSql, new { request.SaleId }, ct);

        var dto = new SaleDto(
            sale.Id, sale.DocumentNumber, sale.SaleDate,
            sale.Subtotal, sale.TaxTotal, sale.DiscountTotal, sale.Total,
            sale.Status, sale.SoldBy, sale.CustomerName,
            lines, payments);

        return Result.Success(dto);
    }

    private sealed record SaleQueryResult(
        Guid Id, string? DocumentNumber, DateTime SaleDate,
        decimal Subtotal, decimal TaxTotal, decimal DiscountTotal, decimal Total,
        string Status, string SoldBy, string? CustomerName);
}
