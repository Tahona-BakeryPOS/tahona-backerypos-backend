using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Sales;

public sealed record SaleLineDto(Guid Id, int LineNo, Guid ProductId, string Sku, string Description, string UnitCode, decimal Quantity,
    decimal UnitPrice, bool PriceIncludesTax, decimal DiscountAmount, string? DiscountReason, decimal TaxRate, decimal TaxableBase,
    decimal TaxAmount, decimal LineTotal);

/// <param name="Status"><c>draft</c>, <c>pending_payment</c>, <c>confirmed</c>, <c>cancelled</c>, <c>voided</c>.</param>
/// <param name="Remaining">Lo que falta por pagar con pagos aprobados.</param>
/// <param name="Fiscal">Factura electrónica (estado ante la DIAN vía Siigo); null si no se emitió.</param>
/// <param name="RefundedTotal">Devuelto al cliente por anulación o devoluciones (fase 9).</param>
/// <param name="ReturnedQuantities">Cantidad ya devuelta por línea de venta (para limitar nuevas devoluciones).</param>
public sealed record SaleDto(Guid Id, long? Number, Guid BranchId, Guid CashRegisterId, Guid CashSessionId, Guid CashierId, string? CashierName,
    string? CustomerIdentification, string Status, string PaymentStatus, string InventoryStatus, string FiscalStatus, decimal Subtotal,
    decimal DiscountTotal, decimal TaxTotal, decimal Total, decimal Remaining, decimal Change, bool StockAuthorized, DateTime CreatedAt,
    DateTime? ConfirmedAt, IReadOnlyList<SaleLineDto> Lines, IReadOnlyList<SalePaymentInfo> Payments, FiscalDocumentSummary? Fiscal,
    decimal RefundedTotal, DateTime? VoidedAt, string? VoidReason, string? VoidedByName, IReadOnlyDictionary<Guid, decimal> ReturnedQuantities,
    IReadOnlyList<SaleReturnDto> Returns, IReadOnlyList<CreditNoteSummary> CreditNotes);

public sealed record SaleReturnLineDto(Guid SaleLineId, string Description, decimal Quantity, decimal Amount, bool Restock);

public sealed record SaleReturnDto(Guid Id, DateTime OccurredAt, string Reason, string? ApprovedByName, decimal Total, IReadOnlyList<SaleReturnLineDto> Lines);

public sealed record SaleListItemDto(Guid Id, long? Number, Guid BranchId, string? CashierName, string? CustomerIdentification, string Status,
    string PaymentStatus, string FiscalStatus, decimal Total, int LineCount, DateTime CreatedAt, DateTime? ConfirmedAt, decimal RefundedTotal);

public sealed record GetSaleQuery(Guid Id) : IRequest<Result<SaleDto>>;

/// <summary>Ventas por fecha de creación, sucursal, sesión de caja y estado; de la más reciente a la más antigua.</summary>
public sealed record GetSalesQuery(DateTime? From, DateTime? To, Guid? BranchId, Guid? CashSessionId, string? Status, int Page = 1, int PageSize = 50,
    long? Number = null)
    : IRequest<Result<PagedResult<SaleListItemDto>>>;

public sealed class GetSalesValidator : AbstractValidator<GetSalesQuery>
{
    public GetSalesValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class SalesQueriesHandler :
    IRequestHandler<GetSaleQuery, Result<SaleDto>>,
    IRequestHandler<GetSalesQuery, Result<PagedResult<SaleListItemDto>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISalePayments _payments;
    private readonly IFiscalDocumentIssuer _fiscal;
    private readonly IUserDirectory _users;

    public SalesQueriesHandler(IAppDbContext db, ITenantContext tenant, ISalePayments payments, IFiscalDocumentIssuer fiscal, IUserDirectory users)
    {
        _db = db;
        _tenant = tenant;
        _payments = payments;
        _fiscal = fiscal;
        _users = users;
    }

    public async Task<Result<SaleDto>> Handle(GetSaleQuery request, CancellationToken ct)
    {
        var sale = await _db.Sales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (sale is null || !_tenant.CanAccessBranch(sale.BranchId))
            return Result.Failure<SaleDto>(Error.NotFound("Venta", request.Id));

        var payments = await _payments.ForSaleAsync(sale.Id, ct);
        var fiscal = (await _fiscal.ForSalesAsync([sale.Id], ct)).GetValueOrDefault(sale.Id);
        var creditNotes = await _fiscal.CreditNotesForSalesAsync([sale.Id], ct);
        var returns = await _db.SaleReturns.AsNoTracking().Include(r => r.Lines).Where(r => r.SaleId == sale.Id).OrderBy(r => r.OccurredAt).ToListAsync(ct);
        var users = await _users.NamesAsync([sale.CashierId, .. returns.Select(r => r.ApprovedBy), .. sale.VoidedBy is { } v ? [v] : Array.Empty<Guid>()], ct);
        // Lo cobrado incluye lo que después se reembolsó: no vuelve a quedar "por pagar".
        var collected = payments.Where(p => p.Status is "approved" or "refunded" or "partially_refunded").ToList();
        var lineNames = sale.Lines.ToDictionary(l => l.Id, l => l.Description);
        return Result.Success(new SaleDto(sale.Id, sale.Number, sale.BranchId, sale.CashRegisterId, sale.CashSessionId, sale.CashierId,
            users.GetValueOrDefault(sale.CashierId),
            sale.CustomerIdentification, Name(sale.Status), Name(sale.PaymentStatus), Name(sale.InventoryStatus), FiscalName(sale, fiscal),
            sale.Subtotal, sale.DiscountTotal, sale.TaxTotal, sale.Total, sale.Total - collected.Sum(p => p.Amount), collected.Sum(p => p.ChangeAmount ?? 0),
            sale.StockAuthorizedBy is not null, sale.CreatedAt, sale.ConfirmedAt,
            sale.Lines.OrderBy(l => l.LineNo).Select(l => new SaleLineDto(l.Id, l.LineNo, l.ProductId, l.Sku, l.Description, l.UnitCode, l.Quantity,
                l.UnitPrice, l.PriceIncludesTax, l.DiscountAmount, l.DiscountReason, l.TaxRate, l.TaxableBase, l.TaxAmount, l.LineTotal)).ToList(),
            payments, fiscal, sale.RefundedTotal, sale.VoidedAt, sale.VoidReason, sale.VoidedBy is { } by ? users.GetValueOrDefault(by) : null,
            returns.SelectMany(r => r.Lines).GroupBy(l => l.SaleLineId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)),
            returns.Select(r => new SaleReturnDto(r.Id, r.OccurredAt, r.Reason, users.GetValueOrDefault(r.ApprovedBy), r.Total,
                r.Lines.Select(l => new SaleReturnLineDto(l.SaleLineId, lineNames.GetValueOrDefault(l.SaleLineId, ""), l.Quantity, l.Amount, l.Restock))
                    .ToList())).ToList(),
            creditNotes));
    }

    public async Task<Result<PagedResult<SaleListItemDto>>> Handle(GetSalesQuery request, CancellationToken ct)
    {
        var query = _db.Sales.AsNoTracking();
        if (request.From is { } from) query = query.Where(s => s.CreatedAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(s => s.CreatedAt <= to.ToUniversalTime());
        if (request.BranchId is { } branchId) query = query.Where(s => s.BranchId == branchId);
        if (request.Number is { } number) query = query.Where(s => s.Number == number);
        if (request.CashSessionId is { } sessionId) query = query.Where(s => s.CashSessionId == sessionId);
        if (request.Status is not null && Enum.TryParse<SaleStatus>(request.Status.Replace("_", ""), true, out var status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(s => new { Sale = s, LineCount = s.Lines.Count })
            .ToListAsync(ct);
        rows = rows.Where(r => _tenant.CanAccessBranch(r.Sale.BranchId)).ToList();
        var fiscal = await _fiscal.ForSalesAsync(rows.Select(r => r.Sale.Id).ToList(), ct);
        var users = await _users.NamesAsync(rows.Select(r => r.Sale.CashierId).ToList(), ct);
        var items = rows.Select(r => new SaleListItemDto(r.Sale.Id, r.Sale.Number, r.Sale.BranchId, users.GetValueOrDefault(r.Sale.CashierId),
            r.Sale.CustomerIdentification, Name(r.Sale.Status), Name(r.Sale.PaymentStatus), FiscalName(r.Sale, fiscal.GetValueOrDefault(r.Sale.Id)),
            r.Sale.Total, r.LineCount, r.Sale.CreatedAt, r.Sale.ConfirmedAt, r.Sale.RefundedTotal)).ToList();
        return Result.Success(new PagedResult<SaleListItemDto>(items, request.Page, request.PageSize, total));
    }

    /// <summary>El estado fiscal vivo lo da el documento; la venta solo sabe si se pidió.</summary>
    private static string FiscalName(Sale sale, FiscalDocumentSummary? doc) => doc?.Status switch
    {
        null => Name(sale.FiscalStatus),
        "accepted" => "accepted",
        "rejected" => "rejected",
        "unknown" or "temporary_error" => "in_review",
        "cancelled" => "cancelled",
        _ => "pending",
    };

    private static string Name<TEnum>(TEnum value) where TEnum : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
