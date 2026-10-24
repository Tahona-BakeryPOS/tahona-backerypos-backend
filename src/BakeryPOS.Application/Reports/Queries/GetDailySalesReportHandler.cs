using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Reports.Queries;

/// <summary>
/// CU-08: Reporte de solo lectura. Consolidado y desglosado por sucursal;
/// ventas anuladas separadas del total vendido.
/// </summary>
public sealed class GetDailySalesReportHandler : IRequestHandler<GetDailySalesReportQuery, Result<DailySalesReportDto>>
{
    private readonly IDapperQueryService _dapper;
    private readonly ICurrentUserService _currentUser;

    public GetDailySalesReportHandler(IDapperQueryService dapper, ICurrentUserService currentUser)
    {
        _dapper = dapper;
        _currentUser = currentUser;
    }

    public async Task<Result<DailySalesReportDto>> Handle(GetDailySalesReportQuery request, CancellationToken ct)
    {
        if (request.FromDate > request.ToDate)
            return Result.Failure<DailySalesReportDto>(Error.Validation("La fecha inicial no puede ser mayor a la fecha final."));

        var fromDateTime = request.FromDate.ToDateTime(TimeOnly.MinValue);
        var toDateTime = request.ToDate.ToDateTime(TimeOnly.MaxValue);

        const string dailySql = """
            SELECT
                CAST(s.sale_date AS date) as date,
                COALESCE(SUM(CASE WHEN s.status = 'Completed' THEN s.total ELSE 0 END), 0) as total_sold,
                COALESCE(SUM(CASE WHEN s.status = 'Voided' THEN s.total ELSE 0 END), 0) as total_voided,
                COUNT(*) FILTER (WHERE s.status = 'Completed') as sales_count,
                COUNT(*) FILTER (WHERE s.status = 'Voided') as voided_count
            FROM sales s
            WHERE s.organization_id = @OrganizationId
              AND s.sale_date BETWEEN @FromDateTime AND @ToDateTime
              AND (@BranchId IS NULL OR s.branch_id = @BranchId)
              AND s.deleted_at IS NULL
            GROUP BY CAST(s.sale_date AS date)
            ORDER BY date
            """;

        var days = await _dapper.QueryAsync<DailySalesRowDto>(dailySql, new
        {
            _currentUser.OrganizationId,
            FromDateTime = fromDateTime,
            ToDateTime = toDateTime,
            request.BranchId
        }, ct);

        const string branchSql = """
            SELECT
                b.id as branch_id,
                b.name as branch_name,
                COALESCE(SUM(CASE WHEN s.status = 'Completed' THEN s.total ELSE 0 END), 0) as total_sold,
                COALESCE(SUM(CASE WHEN s.status = 'Voided' THEN s.total ELSE 0 END), 0) as total_voided,
                COUNT(*) FILTER (WHERE s.status = 'Completed') as sales_count,
                COUNT(*) FILTER (WHERE s.status = 'Voided') as voided_count
            FROM branches b
            LEFT JOIN sales s ON s.branch_id = b.id
                AND s.sale_date BETWEEN @FromDateTime AND @ToDateTime
                AND s.deleted_at IS NULL
            WHERE b.organization_id = @OrganizationId
              AND (@BranchId IS NULL OR b.id = @BranchId)
              AND b.deleted_at IS NULL
            GROUP BY b.id, b.name
            ORDER BY b.name
            """;

        var branchBreakdown = await _dapper.QueryAsync<BranchSalesRowDto>(branchSql, new
        {
            _currentUser.OrganizationId,
            FromDateTime = fromDateTime,
            ToDateTime = toDateTime,
            request.BranchId
        }, ct);

        var totalSold = days.Sum(d => d.TotalSold);
        var totalVoided = days.Sum(d => d.TotalVoided);
        var salesCount = days.Sum(d => d.SalesCount);
        var voidedCount = days.Sum(d => d.VoidedCount);

        return Result.Success(new DailySalesReportDto(
            days, branchBreakdown, totalSold, totalVoided, salesCount, voidedCount));
    }
}
