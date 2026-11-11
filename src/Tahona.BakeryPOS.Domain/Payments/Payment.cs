using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Approved,
    Declined,
    Cancelled,
    Refunded,
    PartiallyRefunded,
}

public enum PaymentPurpose
{
    /// <summary>Payment of a sale (the sale arrives in phase 8).</summary>
    Sale,
    /// <summary>Integration test charge, without a sale (I-23).</summary>
    Test,
}

/// <summary>
/// Logical payment (of a sale or a test). Its status is its own and is not mixed with the sale's. A
/// gateway payment stays pending until its signed webhook or an audited manual resolution (I-23).
/// </summary>
public sealed class Payment : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public PaymentPurpose Purpose { get; private set; }
    /// <summary>Paid sale; null on test charges. No FK until the sales module exists.</summary>
    public Guid? SaleId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public PaymentMethodKind MethodKind { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>Cash received (cash only); <see cref="ChangeAmount"/> = received − amount.</summary>
    public decimal? AmountTendered { get; private set; }
    public decimal? ChangeAmount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    /// <summary>Reference of the method (transfer, external card terminal) or approval number.</summary>
    public string? Reference { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    /// <summary>Who resolved it by hand; null if the gateway resolved it.</summary>
    public Guid? ResolvedBy { get; private set; }
    public bool ResolvedManually { get; private set; }
    public string? ResolutionNote { get; private set; }
    /// <summary>What was returned to the customer through a void or the sale's returns (phase 9).</summary>
    public decimal RefundedAmount { get; private set; }

    public bool IsPending => Status == PaymentStatus.Pending;

    private Payment() { }

    public static Payment StartGateway(Guid companyId, PaymentPurpose purpose, Guid? saleId, Guid cashSessionId, PaymentMethod method, decimal amount)
    {
        if (method.Kind != PaymentMethodKind.Gateway)
            throw new DomainException("VALIDACION", "El medio de pago no es de la pasarela.");
        if (purpose == PaymentPurpose.Sale && saleId is null)
            throw new DomainException("VALIDACION", "El pago de una venta debe indicar la venta.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("VALIDACION", "El monto debe ser mayor que 0 y con máximo 2 decimales.");
        if (method.CompanyId != companyId || !method.IsActive)
            throw new DomainException("VALIDACION", "El medio de pago no está activo en la empresa.");
        return new Payment
        {
            CompanyId = companyId, Purpose = purpose, SaleId = saleId, CashSessionId = cashSessionId,
            PaymentMethodId = method.Id, MethodKind = method.Kind, Amount = amount,
        };
    }

    /// <summary>
    /// A payment that does not go through the gateway and is approved when recorded: cash (with change), transfer
    /// or external card terminal (with a mandatory reference; they are reconciled by hand).
    /// </summary>
    public static Payment RecordImmediate(Guid companyId, Guid saleId, Guid cashSessionId, PaymentMethod method, decimal amount,
        decimal? amountTendered, string? reference, DateTime at)
    {
        if (method.Kind == PaymentMethodKind.Gateway)
            throw new DomainException("VALIDACION", "Los medios de la pasarela se cobran en el datáfono.");
        if (method.CompanyId != companyId || !method.IsActive)
            throw new DomainException("VALIDACION", "El medio de pago no está activo en la empresa.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("VALIDACION", "El monto debe ser mayor que 0 y con máximo 2 decimales.");
        if (method.RequiresReference && string.IsNullOrWhiteSpace(reference))
            throw new DomainException("VALIDACION", $"{method.Name} exige referencia.");
        decimal? change = null;
        if (method.Kind == PaymentMethodKind.Cash)
        {
            var tendered = amountTendered ?? amount;
            if (tendered < amount)
                throw new DomainException("VALIDACION", "El efectivo recibido es menor que el monto.");
            amountTendered = tendered;
            change = tendered - amount;
        }
        else if (amountTendered is not null)
            throw new DomainException("VALIDACION", "Solo el efectivo admite excedente (cambio).");

        return new Payment
        {
            CompanyId = companyId, Purpose = PaymentPurpose.Sale, SaleId = saleId, CashSessionId = cashSessionId, PaymentMethodId = method.Id,
            MethodKind = method.Kind, Amount = amount, AmountTendered = amountTendered, ChangeAmount = change,
            Status = PaymentStatus.Approved, ResolvedAt = at, Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
        };
    }

    /// <summary>Approved by the gateway (signed webhook and matching amount).</summary>
    public void ApproveByGateway(DateTime at, string? approvalNumber)
    {
        EnsurePending();
        Status = PaymentStatus.Approved;
        ResolvedAt = at;
        Reference = approvalNumber ?? Reference;
    }

    /// <summary>The gateway rejected the request: the charge never reached the card terminal and it can be tried again.</summary>
    public void CancelNotSent(DateTime at, string reason)
    {
        EnsurePending();
        Status = PaymentStatus.Cancelled;
        ResolvedAt = at;
        ResolutionNote = reason.Length > 300 ? reason[..300] : reason;
    }

    public void DeclineByGateway(DateTime at)
    {
        EnsurePending();
        Status = PaymentStatus.Declined;
        ResolvedAt = at;
    }

    /// <summary>
    /// Manual resolution (I-23) when the webhook did not arrive: approved requires the approval number from the
    /// receipt; cancelled only after checking on the card terminal that it was not charged. It is audited.
    /// </summary>
    public void ResolveManually(PaymentStatus resolution, Guid by, DateTime at, string? approvalNumber, string note)
    {
        EnsurePending();
        if (resolution is not (PaymentStatus.Approved or PaymentStatus.Declined or PaymentStatus.Cancelled))
            throw new DomainException("VALIDACION", "Resolución: aprobado, rechazado o cancelado.");
        if (resolution == PaymentStatus.Approved && string.IsNullOrWhiteSpace(approvalNumber))
            throw new DomainException("VALIDACION", "Para aprobar a mano escribe el número de aprobación del recibo del datáfono.");
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("VALIDACION", "Explica por qué se resuelve a mano.");
        Status = resolution;
        ResolvedAt = at;
        ResolvedBy = by;
        ResolvedManually = true;
        ResolutionNote = note.Trim();
        if (resolution == PaymentStatus.Approved) Reference = approvalNumber!.Trim();
    }

    /// <summary>Deducts a refund from the approved payment (partial or full).</summary>
    public void ApplyRefund(decimal amount)
    {
        if (Status is not (PaymentStatus.Approved or PaymentStatus.PartiallyRefunded))
            throw new DomainException("ESTADO_INVALIDO", $"Solo se reembolsa un pago aprobado (está {Status}).");
        if (amount <= 0 || RefundedAmount + amount > Amount)
            throw new DomainException("VALIDACION", "El reembolso supera lo pagado.");
        RefundedAmount += amount;
        Status = RefundedAmount == Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new DomainException("ESTADO_INVALIDO", $"El pago ya está resuelto ({Status}).");
    }
}

public enum AttemptStatus
{
    Created,
    Pending,
    Approved,
    Declined,
    Voided,
    Error,
    /// <summary>It is unknown whether the gateway received the charge (timeout): it is not resent blindly.</summary>
    Unknown,
    Expired,
}

/// <summary>
/// Charge attempt at the gateway. <see cref="Reference"/> is the attempt's id and travels as
/// <c>reference</c> to Bold, which returns it in the webhook. States only move forward.
/// </summary>
public sealed class PaymentAttempt : AuditableEntity, ITenantEntity
{
    public const string Bold = "bold";

    public Guid CompanyId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string Provider { get; private set; } = Bold;
    /// <summary>Environment of the account it was created with: the webhook is only validated with that environment's key.</summary>
    public GatewayEnvironment Environment { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    /// <summary>The webhook's <c>data.payment_id</c>.</summary>
    public string? ExternalId { get; private set; }
    /// <summary>The creation's <c>payload.integration_id</c> (for Bold support).</summary>
    public string? IntegrationId { get; private set; }
    public AttemptStatus Status { get; private set; } = AttemptStatus.Created;
    public string? RawStatus { get; private set; }
    public decimal Amount { get; private set; }
    public string GatewayMethod { get; private set; } = string.Empty;
    public string? TerminalSerial { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? LastEventAt { get; private set; }

    public bool IsOpen => Status is AttemptStatus.Created or AttemptStatus.Pending or AttemptStatus.Unknown;

    private PaymentAttempt() { }

    public static PaymentAttempt Create(Payment payment, GatewayEnvironment environment, string gatewayMethod, string terminalSerial)
    {
        var attempt = new PaymentAttempt
        {
            CompanyId = payment.CompanyId, PaymentId = payment.Id, Amount = payment.Amount, Environment = environment,
            GatewayMethod = gatewayMethod, TerminalSerial = terminalSerial,
        };
        attempt.Reference = attempt.Id.ToString("N");
        return attempt;
    }

    /// <summary>The gateway received the charge (201): it stays waiting for the webhook.</summary>
    public void MarkSent(string? integrationId)
    {
        EnsureOpen();
        Status = AttemptStatus.Pending;
        IntegrationId = integrationId;
    }

    /// <summary>The gateway rejected the request (4xx with a code): the charge never reached the card terminal.</summary>
    public void MarkRejectedBeforeSending(string code, string message)
    {
        EnsureOpen();
        Status = AttemptStatus.Error;
        ErrorCode = code;
        ErrorMessage = message;
    }

    /// <summary>Timeout or network error: it is unknown whether it arrived.</summary>
    public void MarkUnknown(string message)
    {
        EnsureOpen();
        Status = AttemptStatus.Unknown;
        ErrorMessage = message;
    }

    public void ApplyResult(AttemptStatus status, string rawStatus, string externalId, DateTime at)
    {
        EnsureOpen();
        if (status is not (AttemptStatus.Approved or AttemptStatus.Declined or AttemptStatus.Voided or AttemptStatus.Error))
            throw new DomainException("VALIDACION", "Resultado de pasarela no válido.");
        Status = status;
        RawStatus = rawStatus;
        ExternalId = externalId;
        LastEventAt = at;
    }

    /// <summary>Closes the attempt when the payment was resolved by hand (I-23).</summary>
    public void CloseManually(AttemptStatus status)
    {
        if (!IsOpen) return;
        Status = status;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException("ESTADO_INVALIDO", $"El intento ya terminó ({Status}).");
    }
}

public enum RefundMethod
{
    /// <summary>Cash from the open register (D-11: every return is paid in cash, also gateway ones).</summary>
    Cash,
    Gateway,
    Manual,
}

/// <summary>
/// Refund of a payment for a void or return (payment-integration.md §8). Its status is its own; with
/// cash it is completed when recorded. Unique per (payment, source).
/// </summary>
public sealed class Refund : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid SaleId { get; private set; }
    /// <summary><c>void</c> o <c>return</c>.</summary>
    public string SourceType { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public decimal Amount { get; private set; }
    public RefundMethod Method { get; private set; }
    /// <summary><c>completed</c> (cash); <c>pending</c>/<c>failed</c> are left for gateway refunds.</summary>
    public string Status { get; private set; } = "completed";
    public DateTime OccurredAt { get; private set; }

    private Refund() { }

    public static Refund Cash(Payment payment, string sourceType, Guid sourceId, decimal amount, DateTime at)
    {
        payment.ApplyRefund(amount);
        return new Refund
        {
            CompanyId = payment.CompanyId, PaymentId = payment.Id, SaleId = payment.SaleId!.Value, SourceType = sourceType, SourceId = sourceId,
            Amount = amount, Method = RefundMethod.Cash, OccurredAt = at,
        };
    }
}
