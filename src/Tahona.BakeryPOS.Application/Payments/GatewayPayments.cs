using System.Text.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Application.Payments;

/// <param name="Status"><c>pending</c>, <c>approved</c>, <c>declined</c>, <c>cancelled</c>, <c>refunded</c>…</param>
/// <param name="AttemptStatus">Status of the last attempt at the gateway (<c>pending</c>, <c>unknown</c>, <c>error</c>…).</param>
public sealed record PaymentDto(Guid Id, string Purpose, Guid? SaleId, Guid CashSessionId, Guid PaymentMethodId, string PaymentMethodName,
    decimal Amount, string Status, string? AttemptStatus, string? AttemptError, string? Reference, bool ResolvedManually,
    string? ResolutionNote, DateTime CreatedAt, DateTime? ResolvedAt);

/// <summary>Integration test charge, without a sale (I-23): it goes to the indicated register's card terminal.</summary>
public sealed record StartTestChargeCommand(Guid CashRegisterId, Guid PaymentMethodId, decimal Amount) : IRequest<Result<PaymentDto>>;

/// <summary>Status of a payment (the POS queries it every few seconds while waiting for the card terminal).</summary>
public sealed record GetPaymentQuery(Guid Id) : IRequest<Result<PaymentDto>>;

/// <summary>Payments by creation date, status and cash session, newest to oldest.</summary>
public sealed record GetPaymentsQuery(DateTime? From, DateTime? To, string? Status, Guid? CashSessionId) : IRequest<Result<IReadOnlyList<PaymentDto>>>;

/// <summary>
/// Manual resolution (I-23) of a gateway payment that was left with no response: <c>approved</c> with the
/// approval number from the receipt, <c>declined</c> or <c>cancelled</c>; a note is mandatory. It is audited.
/// </summary>
public sealed record ResolvePaymentCommand(Guid Id, string Resolution, string? ApprovalNumber, string Note) : IRequest<Result<PaymentDto>>;

public enum WebhookOutcome
{
    Processed,
    Duplicate,
    Ignored,
    InvalidSignature,
}

/// <summary>Bold webhook: raw body (for the signature) and <c>x-bold-signature</c> header.</summary>
public sealed record ProcessBoldWebhookCommand(string RawBody, string? Signature) : IRequest<WebhookOutcome>;

public sealed class StartTestChargeValidator : AbstractValidator<StartTestChargeCommand>
{
    public StartTestChargeValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
    }
}

public sealed class ResolvePaymentValidator : AbstractValidator<ResolvePaymentCommand>
{
    public ResolvePaymentValidator()
    {
        RuleFor(x => x.Resolution).Must(r => r is "approved" or "declined" or "cancelled").WithMessage("Resolución: approved, declined o cancelled.");
        RuleFor(x => x.ApprovalNumber).NotEmpty().MaximumLength(100).When(x => x.Resolution == "approved")
            .WithMessage("Para aprobar a mano escribe el número de aprobación del recibo del datáfono.");
        RuleFor(x => x.Note).NotEmpty().MaximumLength(300).WithMessage("Explica por qué se resuelve a mano.");
    }
}

/// <summary>
/// Charge through the Bold card terminal (payment-integration.md §4 and §11): stores payment + attempt, calls Bold outside
/// the transaction and stores the result of the request. Approval only arrives through the signed webhook
/// or an audited manual resolution. It is used by the test charge and, in phase 8, by the sale.
/// </summary>
public sealed class GatewayPaymentService
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly IBoldGateway _bold;

    public GatewayPaymentService(IAppDbContext db, ITenantContext tenant, ISecretProtector secrets, IBoldGateway bold)
    {
        _db = db;
        _tenant = tenant;
        _secrets = secrets;
        _bold = bold;
    }

    public async Task<Result<Payment>> StartAsync(PaymentPurpose purpose, Guid? saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, IReadOnlyList<BoldTax> taxes, string? description, CancellationToken ct)
    {
        if (!session.IsOpen)
            return Result.Failure<Payment>(Error.CajaCerrada);
        var method = await _db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId, ct);
        if (method is null)
            return Result.Failure<Payment>(Error.NotFound("Medio de pago", paymentMethodId));
        if (method.Kind != PaymentMethodKind.Gateway || !method.IsActive)
            return Result.Failure<Payment>(Error.Validation("El medio de pago no es de la pasarela o está desactivado."));
        var account = await _db.GatewayAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        if (account is null)
            return Result.Failure<Payment>(Error.PasarelaNoConfigurada);
        var terminal = await _db.RegisterTerminals.AsNoTracking().FirstOrDefaultAsync(t => t.CashRegisterId == session.CashRegisterId, ct);
        if (terminal is null)
            return Result.Failure<Payment>(Error.DatafonoNoConfigurado);

        var payment = Payment.StartGateway(_tenant.RequiredCompanyId, purpose, saleId, session.Id, method, amount);
        var attempt = PaymentAttempt.Create(payment, account.Environment, method.GatewayMethod!, terminal.TerminalSerial);
        _db.Payments.Add(payment);
        _db.PaymentAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        // Outside the transaction: the charge reaches the card terminal.
        var result = await _bold.CreateCheckoutAsync(_secrets.Unprotect(account.EncryptedIdentityKey), new BoldCheckoutRequest(
            amount, taxes, method.GatewayMethod!, terminal.TerminalModel, terminal.TerminalSerial, attempt.Reference,
            account.SellerEmail, description), ct);

        switch (result.Outcome)
        {
            case BoldCheckoutOutcome.Sent:
                attempt.MarkSent(result.IntegrationId);
                break;
            case BoldCheckoutOutcome.Rejected:
                attempt.MarkRejectedBeforeSending(result.ErrorCode ?? "ERROR", result.ErrorMessage ?? "Bold rechazó el cobro.");
                payment.CancelNotSent(DateTime.UtcNow, $"Bold rechazó la petición: {result.ErrorCode} {result.ErrorMessage}");
                break;
            default:
                attempt.MarkUnknown(result.ErrorMessage ?? "Sin respuesta de Bold.");
                break;
        }
        await _db.SaveChangesAsync(ct);

        return result.Outcome == BoldCheckoutOutcome.Rejected
            ? Result.Failure<Payment>(Error.Validation($"Bold rechazó el cobro ({result.ErrorCode}): {result.ErrorMessage}"))
            : Result.Success(payment);
    }
}

public sealed class GatewayPaymentsHandler :
    IRequestHandler<StartTestChargeCommand, Result<PaymentDto>>,
    IRequestHandler<GetPaymentQuery, Result<PaymentDto>>,
    IRequestHandler<GetPaymentsQuery, Result<IReadOnlyList<PaymentDto>>>,
    IRequestHandler<ResolvePaymentCommand, Result<PaymentDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICashSessionDirectory _sessions;
    private readonly GatewayPaymentService _gateway;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _clock;

    public GatewayPaymentsHandler(IAppDbContext db, ITenantContext tenant, ICashSessionDirectory sessions, GatewayPaymentService gateway,
        IOutbox outbox, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _sessions = sessions;
        _gateway = gateway;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<Result<PaymentDto>> Handle(StartTestChargeCommand request, CancellationToken ct)
    {
        var session = await _sessions.FindOpenForRegisterAsync(request.CashRegisterId, ct);
        if (session is null)
            return Result.Failure<PaymentDto>(Error.Validation("La caja no tiene una sesión abierta: ábrela en el POS para hacer el cobro de prueba."));
        if (!_tenant.CanAccessBranch(session.BranchId))
            return Result.Failure<PaymentDto>(Error.SinAsignacionVigente);

        var payment = await _gateway.StartAsync(PaymentPurpose.Test, null, session, request.PaymentMethodId, request.Amount, [],
            "Cobro de prueba BakeryPOS", ct);
        return payment.IsFailure ? Result.Failure<PaymentDto>(payment.Error) : await Handle(new GetPaymentQuery(payment.Value.Id), ct);
    }

    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken ct)
    {
        var rows = await ToDtosAsync(_db.Payments.AsNoTracking().Where(p => p.Id == request.Id), ct);
        return rows.Count == 0 ? Result.Failure<PaymentDto>(Error.NotFound("Pago", request.Id)) : Result.Success(rows[0]);
    }

    public async Task<Result<IReadOnlyList<PaymentDto>>> Handle(GetPaymentsQuery request, CancellationToken ct)
    {
        var query = _db.Payments.AsNoTracking();
        if (request.From is { } from) query = query.Where(p => p.CreatedAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(p => p.CreatedAt <= to.ToUniversalTime());
        if (request.CashSessionId is { } sessionId) query = query.Where(p => p.CashSessionId == sessionId);
        if (request.Status is not null && Enum.TryParse<PaymentStatus>(request.Status.Replace("_", ""), true, out var status))
            query = query.Where(p => p.Status == status);
        return Result.Success(await ToDtosAsync(query.OrderByDescending(p => p.CreatedAt).Take(500), ct));
    }

    public async Task<Result<PaymentDto>> Handle(ResolvePaymentCommand request, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (payment is null)
            return Result.Failure<PaymentDto>(Error.NotFound("Pago", request.Id));
        var session = await _sessions.FindAsync(payment.CashSessionId, ct);
        if (session is not null && !_tenant.CanAccessBranch(session.BranchId))
            return Result.Failure<PaymentDto>(Error.SinAsignacionVigente);

        var resolution = request.Resolution switch
        {
            "approved" => PaymentStatus.Approved,
            "declined" => PaymentStatus.Declined,
            _ => PaymentStatus.Cancelled,
        };
        payment.ResolveManually(resolution, _tenant.UserId ?? throw new InvalidOperationException("Requiere un usuario en sesión."),
            _clock.GetUtcNow().UtcDateTime, request.ApprovalNumber, request.Note);
        var attempt = await _db.PaymentAttempts.FirstOrDefaultAsync(a => a.PaymentId == payment.Id && (a.Status == AttemptStatus.Created
            || a.Status == AttemptStatus.Pending || a.Status == AttemptStatus.Unknown), ct);
        attempt?.CloseManually(resolution == PaymentStatus.Approved ? AttemptStatus.Approved
            : resolution == PaymentStatus.Declined ? AttemptStatus.Declined : AttemptStatus.Expired);
        if (payment.SaleId is { } saleId)
            _outbox.Enqueue(SaleMessages.PaymentResolved, new SalePaymentResolved(payment.CompanyId, saleId));
        await _db.SaveChangesAsync(ct);
        return await Handle(new GetPaymentQuery(payment.Id), ct);
    }

    private async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IQueryable<Payment> query, CancellationToken ct)
    {
        var payments = await query.ToListAsync(ct);
        var ids = payments.Select(p => p.Id).ToList();
        var methodIds = payments.Select(p => p.PaymentMethodId).Distinct().ToList();
        var methods = await _db.PaymentMethods.AsNoTracking().Where(m => methodIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var attempts = (await _db.PaymentAttempts.AsNoTracking().Where(a => ids.Contains(a.PaymentId)).ToListAsync(ct))
            .GroupBy(a => a.PaymentId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.CreatedAt).First());
        return payments.Select(p =>
        {
            var a = attempts.GetValueOrDefault(p.Id);
            return new PaymentDto(p.Id, p.Purpose.ToString().ToLowerInvariant(), p.SaleId, p.CashSessionId, p.PaymentMethodId,
                methods.GetValueOrDefault(p.PaymentMethodId, ""), p.Amount, StatusName(p.Status), a?.Status.ToString().ToLowerInvariant(),
                a?.ErrorMessage, p.Reference, p.ResolvedManually, p.ResolutionNote, p.CreatedAt, p.ResolvedAt);
        }).ToList();
    }

    private static string StatusName(PaymentStatus s) => s == PaymentStatus.PartiallyRefunded ? "partially_refunded" : s.ToString().ToLowerInvariant();
}

/// <summary>
/// Bold webhook (payment-integration.md §11): finds the attempt by reference, validates the signature with that
/// company's secret key, deduplicates by notification id and applies the result. An amount different
/// from the attempt's leaves it in error without approving the payment.
/// </summary>
public sealed class BoldWebhookHandler : IRequestHandler<ProcessBoldWebhookCommand, WebhookOutcome>
{
    private readonly IAppDbContext _db;
    private readonly IInboundEventStore _events;
    private readonly ISecretProtector _secrets;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<BoldWebhookHandler> _logger;

    public BoldWebhookHandler(IAppDbContext db, IInboundEventStore events, ISecretProtector secrets, IOutbox outbox, TimeProvider clock,
        ILogger<BoldWebhookHandler> logger)
    {
        _db = db;
        _events = events;
        _secrets = secrets;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WebhookOutcome> Handle(ProcessBoldWebhookCommand request, CancellationToken ct)
    {
        BoldEvent? evt;
        try
        {
            evt = BoldEvent.Parse(request.RawBody);
        }
        catch (JsonException)
        {
            evt = null;
        }
        if (evt is null)
        {
            _logger.LogWarning("Webhook de Bold con cuerpo ilegible");
            return WebhookOutcome.InvalidSignature;
        }

        // No company session: the attempt is looked up by its reference and the company comes from it.
        var attempt = evt.Reference is null ? null : await _db.PaymentAttempts.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(a => a.Provider == PaymentAttempt.Bold && a.Reference == evt.Reference, ct);
        // Only the key of the attempt's environment: the test one is empty and cannot validate a production charge.
        var account = attempt is null ? null : await _db.GatewayAccounts.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == attempt.CompanyId && a.Environment == attempt.Environment, ct);
        var valid = account is not null && (account.Environment == GatewayEnvironment.Sandbox || account.EncryptedSecretKey is not null)
            && BoldSignature.IsValid(request.RawBody, account.EncryptedSecretKey is null ? null : _secrets.Unprotect(account.EncryptedSecretKey), request.Signature);

        var record = await _events.RecordAsync(PaymentAttempt.Bold, evt.Id, request.RawBody, valid, ct);
        if (!valid)
        {
            _logger.LogWarning("Webhook de Bold {EventId} rechazado: firma inválida o referencia desconocida", evt.Id);
            await _events.MarkAsync(record.Id, InboundEventStatus.Failed, "Firma inválida o referencia desconocida", ct);
            return WebhookOutcome.InvalidSignature;
        }
        if (record.IsDuplicate)
            return WebhookOutcome.Duplicate;

        var payment = await _db.Payments.IgnoreQueryFilters([QueryFilters.Tenant]).FirstAsync(p => p.Id == attempt!.PaymentId, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var outcome = Apply(evt, attempt!, payment, now, out var note);
        if (outcome == WebhookOutcome.Processed && payment.SaleId is { } saleId)
            _outbox.Enqueue(SaleMessages.PaymentResolved, new SalePaymentResolved(payment.CompanyId, saleId));
        await _db.SaveChangesAsync(ct);
        await _events.MarkAsync(record.Id, outcome == WebhookOutcome.Processed ? InboundEventStatus.Processed : InboundEventStatus.Ignored, note, ct);
        return outcome;
    }

    private WebhookOutcome Apply(BoldEvent evt, PaymentAttempt attempt, Payment payment, DateTime now, out string? note)
    {
        note = null;
        switch (evt.Type)
        {
            case "SALE_APPROVED" when attempt.IsOpen:
                if (evt.Total != attempt.Amount)
                {
                    // A different amount is never approved (payment-integration.md §4).
                    attempt.ApplyResult(AttemptStatus.Error, evt.Type, evt.PaymentId, now);
                    note = $"Monto distinto: Bold {evt.Total}, intento {attempt.Amount}";
                    _logger.LogError("Pago {PaymentId}: {Note}", payment.Id, note);
                    return WebhookOutcome.Processed;
                }
                attempt.ApplyResult(AttemptStatus.Approved, evt.Type, evt.PaymentId, now);
                if (payment.IsPending) payment.ApproveByGateway(now, evt.ApprovalNumber);
                return WebhookOutcome.Processed;
            case "SALE_REJECTED" when attempt.IsOpen:
                attempt.ApplyResult(AttemptStatus.Declined, evt.Type, evt.PaymentId, now);
                if (payment.IsPending) payment.DeclineByGateway(now);
                return WebhookOutcome.Processed;
            default:
                // Already resolved (e.g. by hand), voids or unknown types: the event is stored without changing anything.
                note = $"Sin cambios: {evt.Type} con intento {attempt.Status} y pago {payment.Status}";
                if (evt.Type == "SALE_APPROVED" && payment.Status != PaymentStatus.Approved)
                    _logger.LogError("Pago {PaymentId}: Bold aprobó después de resolverse como {Status}; revisar en la conciliación", payment.Id, payment.Status);
                return WebhookOutcome.Ignored;
        }
    }

    private sealed record BoldEvent(string Id, string Type, string PaymentId, string? Reference, decimal Total, string? ApprovalNumber)
    {
        public static BoldEvent? Parse(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id) || !root.TryGetProperty("type", out var type) || !root.TryGetProperty("data", out var data))
                return null;
            string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
            var reference = data.TryGetProperty("metadata", out var meta) ? Str(meta, "reference") : null;
            var total = data.TryGetProperty("amount", out var amount) && amount.TryGetProperty("total", out var t) && t.TryGetDecimal(out var d) ? d : -1m;
            return new BoldEvent(id.ToString(), type.GetString() ?? "", Str(data, "payment_id") ?? "", reference, total, Str(data, "approval_number"));
        }
    }
}

/// <summary>Unresolved payments of a cash session: they block its close (D-12).</summary>
public sealed class PendingPaymentsProbe : IPendingPaymentsProbe
{
    private readonly IAppDbContext _db;

    public PendingPaymentsProbe(IAppDbContext db)
    {
        _db = db;
    }

    public Task<int> CountForCashSessionAsync(Guid cashSessionId, CancellationToken ct) =>
        _db.Payments.CountAsync(p => p.CashSessionId == cashSessionId && p.Status == PaymentStatus.Pending, ct);
}
