using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Reports.Queries;

/// <summary>
/// CU-08: Consultar reportes de ventas diarias, con corte opcional por sucursal.
/// Las ventas anuladas se muestran por separado del total vendido.
/// </summary>
public sealed record GetDailySalesReportQuery(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? BranchId) : IRequest<Result<DailySalesReportDto>>;

public sealed record DailySalesReportDto(
    IReadOnlyList<DailySalesRowDto> Days,
    IReadOnlyList<BranchSalesRowDto> BranchBreakdown,
    decimal TotalSold,
    decimal TotalVoided,
    int SalesCount,
    int VoidedCount);

public sealed record DailySalesRowDto(
    DateOnly Date,
    decimal TotalSold,
    decimal TotalVoided,
    int SalesCount,
    int VoidedCount);

public sealed record BranchSalesRowDto(
    Guid BranchId,
    string BranchName,
    decimal TotalSold,
    decimal TotalVoided,
    int SalesCount,
    int VoidedCount);
