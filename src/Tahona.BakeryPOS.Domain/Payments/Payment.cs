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
    /// <summary>Pago de una venta (la venta llega en la fase 8).</summary>
    Sale,
    /// <summary>Cobro de prueba de la integración, sin venta (I-23).</summary>
    Test,
}

/// <summary>
/// Pago lógico (de una venta o de prueba). Su estado es propio y no se mezcla con el de la venta. Un
/// pago por pasarela queda pendiente hasta su webhook firmado o una resolución manual auditada (I-23).
/// </summary>
public sealed class Payment : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public PaymentPurpose Purpose { get; private set; }
    /// <summary>Venta pagada; null en cobros de prueba. Sin FK hasta que exista el módulo de ventas.</summary>
    public Guid? SaleId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public PaymentMethodKind MethodKind { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>Efectivo recibido (solo efectivo); <see cref="ChangeAmount"/> = recibido − monto.</summary>
    public decimal? AmountTendered { get; private set; }
    public decimal? ChangeAmount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    /// <summary>Referencia del medio (transferencia, datáfono externo) o número de aprobación.</summary>
    public string? Reference { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    /// <summary>Quién lo resolvió a mano; null si lo resolvió la pasarela.</summary>
    public Guid? ResolvedBy { get; private set; }
    public bool ResolvedManually { get; private set; }
    public string? ResolutionNote { get; private set; }
    /// <summary>Lo devuelto al cliente por anulación o devoluciones de la venta (fase 9).</summary>
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
    /// Pago que no pasa por la pasarela y se aprueba al registrarlo: efectivo (con cambio), transferencia
    /// o datáfono externo (con referencia obligatoria; se concilian a mano).
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

    /// <summary>Aprobado por la pasarela (webhook firmado y monto coincidente).</summary>
    public void ApproveByGateway(DateTime at, string? approvalNumber)
    {
        EnsurePending();
        Status = PaymentStatus.Approved;
        ResolvedAt = at;
        Reference = approvalNumber ?? Reference;
    }

    /// <summary>La pasarela rechazó la petición: el cobro nunca llegó al datáfono y se puede intentar otra vez.</summary>
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
    /// Resolución manual (I-23) cuando el webhook no llegó: aprobado exige el número de aprobación del
    /// recibo; cancelado solo tras verificar en el datáfono que no se cobró. Queda auditado.
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

    /// <summary>Descuenta un reembolso del pago aprobado (parcial o total).</summary>
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
    /// <summary>No se supo si la pasarela recibió el cobro (timeout): no se reenvía a ciegas.</summary>
    Unknown,
    Expired,
}

/// <summary>
/// Intento de cobro en la pasarela. <see cref="Reference"/> es el id del intento y viaja como
/// <c>reference</c> a Bold, que lo devuelve en el webhook. Los estados solo avanzan.
/// </summary>
public sealed class PaymentAttempt : AuditableEntity, ITenantEntity
{
    public const string Bold = "bold";

    public Guid CompanyId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string Provider { get; private set; } = Bold;
    /// <summary>Ambiente de la cuenta con que se creó: el webhook se valida solo con la llave de ese ambiente.</summary>
    public GatewayEnvironment Environment { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    /// <summary><c>data.payment_id</c> del webhook.</summary>
    public string? ExternalId { get; private set; }
    /// <summary><c>payload.integration_id</c> de la creación (para soporte de Bold).</summary>
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

    /// <summary>La pasarela recibió el cobro (201): queda esperando el webhook.</summary>
    public void MarkSent(string? integrationId)
    {
        EnsureOpen();
        Status = AttemptStatus.Pending;
        IntegrationId = integrationId;
    }

    /// <summary>La pasarela rechazó la petición (4xx con código): el cobro nunca llegó al datáfono.</summary>
    public void MarkRejectedBeforeSending(string code, string message)
    {
        EnsureOpen();
        Status = AttemptStatus.Error;
        ErrorCode = code;
        ErrorMessage = message;
    }

    /// <summary>Timeout o error de red: no se sabe si llegó.</summary>
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

    /// <summary>Cierra el intento cuando el pago se resolvió a mano (I-23).</summary>
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
    /// <summary>Efectivo de la caja abierta (D-11: toda devolución se paga en efectivo, también las de pasarela).</summary>
    Cash,
    Gateway,
    Manual,
}

/// <summary>
/// Reembolso de un pago por una anulación o devolución (payment-integration.md §8). Su estado es propio; con
/// efectivo se completa al registrarse. Único por (pago, origen).
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
    /// <summary><c>completed</c> (efectivo); <c>pending</c>/<c>failed</c> quedan para reembolsos por pasarela.</summary>
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
