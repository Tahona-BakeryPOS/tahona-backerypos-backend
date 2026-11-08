using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Payments;

/// <summary>
/// Pagos de una venta. Efectivo, transferencia y datáfono externo quedan aprobados al registrarse; los de
/// Bold se cobran en el datáfono y quedan pendientes hasta el webhook o la resolución manual (I-23).
/// </summary>
public sealed class SalePayments : ISalePayments
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly GatewayPaymentService _gateway;
    private readonly TimeProvider _clock;

    public SalePayments(IAppDbContext db, ITenantContext tenant, GatewayPaymentService gateway, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<SalePaymentInfo>> RecordImmediateAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, decimal? amountTendered, string? reference, CancellationToken ct)
    {
        if (!session.IsOpen)
            return Result.Failure<SalePaymentInfo>(Error.CajaCerrada);
        var method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId, ct);
        if (method is null)
            return Result.Failure<SalePaymentInfo>(Error.NotFound("Medio de pago", paymentMethodId));
        var payment = Payment.RecordImmediate(_tenant.RequiredCompanyId, saleId, session.Id, method, amount, amountTendered, reference,
            _clock.GetUtcNow().UtcDateTime);
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return Result.Success((await ForSaleAsync(saleId, ct)).First(p => p.Id == payment.Id));
    }

    public async Task<Result<SalePaymentInfo>> StartGatewayAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId, decimal amount,
        string description, CancellationToken ct)
    {
        var payment = await _gateway.StartAsync(PaymentPurpose.Sale, saleId, session, paymentMethodId, amount, [], description, ct);
        if (payment.IsFailure)
            return Result.Failure<SalePaymentInfo>(payment.Error);
        return Result.Success((await ForSaleAsync(saleId, ct)).First(p => p.Id == payment.Value.Id));
    }

    public async Task<IReadOnlyList<SalePaymentInfo>> ForSaleAsync(Guid saleId, CancellationToken ct)
    {
        var payments = await _db.Payments.AsNoTracking().Where(p => p.SaleId == saleId).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var ids = payments.Select(p => p.Id).ToList();
        var methodIds = payments.Select(p => p.PaymentMethodId).Distinct().ToList();
        var methods = await _db.PaymentMethods.AsNoTracking().Where(m => methodIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var attempts = (await _db.PaymentAttempts.AsNoTracking().Where(a => ids.Contains(a.PaymentId)).ToListAsync(ct))
            .GroupBy(a => a.PaymentId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First().Status.ToString().ToLowerInvariant());
        return payments.Select(p => new SalePaymentInfo(p.Id, p.PaymentMethodId, methods.GetValueOrDefault(p.PaymentMethodId, ""),
            PaymentKinds.Name(p.MethodKind), p.Amount, p.AmountTendered, p.ChangeAmount,
            p.Status == PaymentStatus.PartiallyRefunded ? "partially_refunded" : p.Status.ToString().ToLowerInvariant(),
            attempts.GetValueOrDefault(p.Id), p.Reference)).ToList();
    }

    public async Task<Result<IReadOnlyList<SaleFiscalPayment>>> RefundAsync(Guid saleId, string sourceType, Guid sourceId, decimal amount,
        CancellationToken ct)
    {
        var existing = await _db.Refunds.AsNoTracking().Where(r => r.SaleId == saleId && r.SourceType == sourceType && r.SourceId == sourceId).ToListAsync(ct);
        var payments = await _db.Payments.Where(p => p.SaleId == saleId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        if (existing.Count > 0)
            return Result.Success(Allocation(existing.Select(r => (r.PaymentId, r.Amount)), payments));

        var now = _clock.GetUtcNow().UtcDateTime;
        var left = amount;
        var refunds = new List<(Guid PaymentId, decimal Amount)>();
        foreach (var payment in payments.Where(p => p.Status is PaymentStatus.Approved or PaymentStatus.PartiallyRefunded))
        {
            if (left <= 0) break;
            var part = Math.Min(left, payment.Amount - payment.RefundedAmount);
            if (part <= 0) continue;
            _db.Refunds.Add(Refund.Cash(payment, sourceType, sourceId, part, now));
            refunds.Add((payment.Id, part));
            left -= part;
        }
        if (left > 0)
            return Result.Failure<IReadOnlyList<SaleFiscalPayment>>(Error.Validation("El reembolso supera lo pagado en la venta."));
        return Result.Success(Allocation(refunds, payments));
    }

    private static IReadOnlyList<SaleFiscalPayment> Allocation(IEnumerable<(Guid PaymentId, decimal Amount)> refunds, IReadOnlyList<Payment> payments) =>
        refunds.GroupBy(r => payments.First(p => p.Id == r.PaymentId).PaymentMethodId)
            .Select(g => new SaleFiscalPayment(g.Key, g.Sum(r => r.Amount))).ToList();

    public async Task<IReadOnlyList<PaymentMethodTotal>> TotalsByMethodAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.Distinct().ToList();
        var totals = await _db.Payments.AsNoTracking()
            .Where(p => p.SaleId != null && ids.Contains(p.SaleId.Value)
                && (p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.PartiallyRefunded || p.Status == PaymentStatus.Refunded))
            .GroupBy(p => p.PaymentMethodId)
            .Select(g => new { MethodId = g.Key, Approved = g.Sum(p => p.Amount), Refunded = g.Sum(p => p.RefundedAmount) })
            .ToListAsync(ct);
        var methodIds = totals.Select(t => t.MethodId).ToList();
        var names = await _db.PaymentMethods.AsNoTracking().Where(m => methodIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        return totals.Select(t => new PaymentMethodTotal(t.MethodId, names.GetValueOrDefault(t.MethodId, ""), t.Approved, t.Refunded, t.Approved - t.Refunded))
            .OrderByDescending(t => t.Net).ToList();
    }

    public async Task<string?> MethodKindAsync(Guid paymentMethodId, CancellationToken ct)
    {
        var method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.IsActive, ct);
        return method is null ? null : PaymentKinds.Name(method.Kind);
    }
}
