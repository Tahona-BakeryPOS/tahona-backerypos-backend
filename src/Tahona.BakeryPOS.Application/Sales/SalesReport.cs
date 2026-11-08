using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Sales;

/// <summary>
/// Informe de ventas (HU-10, CU-08 de la v1) por días de Colombia (UTC−5) entre <paramref name="From"/> y <paramref name="To"/>
/// (inclusive). Vendido = ventas confirmadas (no anuladas); las anuladas y las devoluciones van aparte; neto = vendido −
/// devuelto. Lo cobrado por medio de pago es neto de reembolsos.
/// </summary>
public sealed record GetSalesReportQuery(DateOnly From, DateOnly To, Guid? BranchId) : IRequest<Result<SalesReportDto>>;

public sealed record SalesReportDayDto(DateOnly Date, int SalesCount, decimal TotalSold, int VoidedCount, decimal TotalVoided, decimal TotalReturned);

public sealed record SalesReportBranchDto(Guid BranchId, string BranchName, int SalesCount, decimal TotalSold, int VoidedCount, decimal TotalVoided,
    decimal TotalReturned);

public sealed record SalesReportProductDto(Guid ProductId, string Name, decimal Quantity, decimal Total);

public sealed record SalesReportDto(DateOnly From, DateOnly To, int SalesCount, decimal TotalSold, int VoidedCount, decimal TotalVoided,
    int ReturnsCount, decimal TotalReturned, decimal NetSold, decimal TaxTotal, IReadOnlyList<SalesReportDayDto> Days,
    IReadOnlyList<SalesReportBranchDto> BranchBreakdown, IReadOnlyList<PaymentMethodTotal> PaymentMethods, IReadOnlyList<SalesReportProductDto> TopProducts);

public sealed class GetSalesReportValidator : AbstractValidator<GetSalesReportQuery>
{
    public GetSalesReportValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("'Hasta' no puede ser anterior a 'desde'.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 366).WithMessage("El rango máximo es un año.");
    }
}

public sealed class SalesReportHandler : IRequestHandler<GetSalesReportQuery, Result<SalesReportDto>>
{
    /// <summary>Colombia no tiene horario de verano.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISalePayments _payments;
    private readonly IBranchDirectory _branches;

    public SalesReportHandler(IAppDbContext db, ITenantContext tenant, ISalePayments payments, IBranchDirectory branches)
    {
        _db = db;
        _tenant = tenant;
        _payments = payments;
        _branches = branches;
    }

    public async Task<Result<SalesReportDto>> Handle(GetSalesReportQuery request, CancellationToken ct)
    {
        if (request.BranchId is { } requested && !_tenant.CanAccessBranch(requested))
            return Result.Failure<SalesReportDto>(Error.SinAsignacionVigente);
        var from = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - ColombiaOffset;
        var to = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - ColombiaOffset;

        var salesQuery = _db.Sales.AsNoTracking()
            .Where(s => (s.Status == SaleStatus.Confirmed || s.Status == SaleStatus.Voided) && s.ConfirmedAt >= from && s.ConfirmedAt < to);
        if (request.BranchId is { } branchId) salesQuery = salesQuery.Where(s => s.BranchId == branchId);
        var sales = (await salesQuery.Select(s => new { s.Id, s.BranchId, s.Status, s.Total, s.TaxTotal, ConfirmedAt = s.ConfirmedAt!.Value }).ToListAsync(ct))
            .Where(s => _tenant.CanAccessBranch(s.BranchId)).ToList();

        var returnsQuery = _db.SaleReturns.AsNoTracking().Where(r => r.OccurredAt >= from && r.OccurredAt < to);
        if (request.BranchId is { } returnBranch) returnsQuery = returnsQuery.Where(r => r.BranchId == returnBranch);
        var returns = (await returnsQuery.Select(r => new { r.BranchId, r.Total, r.OccurredAt }).ToListAsync(ct))
            .Where(r => _tenant.CanAccessBranch(r.BranchId)).ToList();

        var confirmed = sales.Where(s => s.Status == SaleStatus.Confirmed).ToList();
        var voided = sales.Where(s => s.Status == SaleStatus.Voided).ToList();
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc + ColombiaOffset);

        var days = Enumerable.Range(0, request.To.DayNumber - request.From.DayNumber + 1).Select(request.From.AddDays)
            .Select(d => new SalesReportDayDto(d,
                confirmed.Count(s => Day(s.ConfirmedAt) == d), confirmed.Where(s => Day(s.ConfirmedAt) == d).Sum(s => s.Total),
                voided.Count(s => Day(s.ConfirmedAt) == d), voided.Where(s => Day(s.ConfirmedAt) == d).Sum(s => s.Total),
                returns.Where(r => Day(r.OccurredAt) == d).Sum(r => r.Total)))
            .ToList();

        var branchIds = sales.Select(s => s.BranchId).Concat(returns.Select(r => r.BranchId)).Distinct().ToList();
        var branches = new List<SalesReportBranchDto>();
        foreach (var id in branchIds)
        {
            var name = (await _branches.FindAsync(id, ct))?.Name ?? "";
            branches.Add(new SalesReportBranchDto(id, name, confirmed.Count(s => s.BranchId == id), confirmed.Where(s => s.BranchId == id).Sum(s => s.Total),
                voided.Count(s => s.BranchId == id), voided.Where(s => s.BranchId == id).Sum(s => s.Total),
                returns.Where(r => r.BranchId == id).Sum(r => r.Total)));
        }

        var confirmedIds = confirmed.Select(s => s.Id).ToList();
        // Agrupación con tipos anónimos: el DTO se arma en memoria porque EF no traduce la proyección a un record.
        var topRows = await _db.SaleLines.AsNoTracking().Where(l => confirmedIds.Contains(l.SaleId))
            .GroupBy(l => new { l.ProductId, l.Description })
            .Select(g => new { g.Key.ProductId, g.Key.Description, Quantity = g.Sum(l => l.Quantity), Total = g.Sum(l => l.LineTotal) })
            .OrderByDescending(p => p.Total).Take(10).ToListAsync(ct);
        var top = topRows.Select(p => new SalesReportProductDto(p.ProductId, p.Description, p.Quantity, p.Total)).ToList();
        var methods = await _payments.TotalsByMethodAsync(confirmedIds, ct);

        var totalSold = confirmed.Sum(s => s.Total);
        var totalReturned = returns.Sum(r => r.Total);
        return Result.Success(new SalesReportDto(request.From, request.To, confirmed.Count, totalSold, voided.Count, voided.Sum(s => s.Total),
            returns.Count, totalReturned, totalSold - totalReturned, confirmed.Sum(s => s.TaxTotal), days,
            branches.OrderByDescending(b => b.TotalSold).ToList(), methods, top));
    }
}
