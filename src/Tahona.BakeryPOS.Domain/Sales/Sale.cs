using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Sales;

public enum SaleStatus
{
    Draft,
    PendingPayment,
    Confirmed,
    Cancelled,
    Voided,
}

public enum SalePaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Refunded,
    PartiallyRefunded,
}

public enum SaleInventoryStatus
{
    NotApplied,
    Applied,
    Reversed,
}

public enum SaleFiscalStatus
{
    /// <summary>La empresa no tiene facturación activa al confirmar.</summary>
    NotRequired,
    Pending,
    Accepted,
    Rejected,
    InReview,
}

/// <summary>
/// Venta como proceso persistente (architecture.md §4): borrador con precios e impuestos congelados por el
/// servidor → pendiente de pago → confirmada cuando los pagos aprobados cubren el total. Venta, pago,
/// inventario y documento fiscal tienen cada uno su estado.
/// </summary>
public sealed class Sale : AuditableEntity, ITenantEntity
{
    private readonly List<SaleLine> _lines = [];

    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public Guid CashierId { get; private set; }
    /// <summary>NIT o cédula si el cliente pide factura con sus datos; null = consumidor final.</summary>
    public string? CustomerIdentification { get; private set; }
    /// <summary>Consecutivo interno por empresa, asignado al confirmar.</summary>
    public long? Number { get; private set; }
    public SaleStatus Status { get; private set; } = SaleStatus.Draft;
    public SalePaymentStatus PaymentStatus { get; private set; } = SalePaymentStatus.Unpaid;
    public SaleInventoryStatus InventoryStatus { get; private set; } = SaleInventoryStatus.NotApplied;
    public SaleFiscalStatus FiscalStatus { get; private set; } = SaleFiscalStatus.NotRequired;
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    /// <summary>Quien autorizó vender sin existencias productos con política "bloquear" (D-06).</summary>
    public Guid? StockAuthorizedBy { get; private set; }
    public Guid? FiscalDocumentId { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }
    public Guid? VoidedBy { get; private set; }
    /// <summary>Dinero devuelto al cliente por anulación o devoluciones.</summary>
    public decimal RefundedTotal { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines.AsReadOnly();

    public bool IsOpen => Status is SaleStatus.Draft or SaleStatus.PendingPayment;

    private Sale() { }

    public static Sale Start(Guid companyId, Guid branchId, Guid cashRegisterId, Guid cashSessionId, Guid cashierId, string? customerIdentification)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || cashRegisterId == Guid.Empty || cashSessionId == Guid.Empty || cashierId == Guid.Empty)
            throw new DomainException("VALIDACION", "La venta necesita empresa, sucursal, caja, sesión y cajero.");
        var sale = new Sale
        {
            CompanyId = companyId, BranchId = branchId, CashRegisterId = cashRegisterId, CashSessionId = cashSessionId, CashierId = cashierId,
        };
        sale.SetCustomer(customerIdentification);
        return sale;
    }

    public SaleLine AddLine(Guid productId, string sku, string description, string unitCode, decimal quantity, decimal unitPrice,
        bool priceIncludesTax, decimal discountAmount, string? discountReason, Guid taxId, decimal taxRate)
    {
        EnsureEditable();
        var line = SaleLine.Create(this, _lines.Count + 1, productId, sku, description, unitCode, quantity, unitPrice, priceIncludesTax,
            discountAmount, discountReason, taxId, taxRate);
        _lines.Add(line);
        Recalculate();
        return line;
    }

    public void SetCustomer(string? identification)
    {
        if (Status is SaleStatus.Confirmed or SaleStatus.Cancelled or SaleStatus.Voided)
            throw new DomainException("ESTADO_INVALIDO", "El cliente solo se cambia antes de confirmar la venta.");
        if (identification is not null && !System.Text.RegularExpressions.Regex.IsMatch(identification.Trim(), "^[0-9]{4,15}$"))
            throw new DomainException("VALIDACION", "La identificación del cliente tiene entre 4 y 15 dígitos (sin dígito de verificación).");
        CustomerIdentification = string.IsNullOrWhiteSpace(identification) ? null : identification.Trim();
    }

    public void AuthorizeStock(Guid authorizedBy)
    {
        EnsureEditable();
        StockAuthorizedBy = authorizedBy;
    }

    /// <summary>Hay al menos un pago en curso o aprobado: ya no se agregan líneas.</summary>
    public void MarkPendingPayment(decimal approvedTotal)
    {
        EnsureEditable();
        Status = SaleStatus.PendingPayment;
        PaymentStatus = approvedTotal <= 0 ? SalePaymentStatus.Unpaid : approvedTotal < Total ? SalePaymentStatus.PartiallyPaid : SalePaymentStatus.Paid;
    }

    /// <summary>Confirma cuando lo aprobado cubre el total exacto. Idempotente para una venta ya confirmada.</summary>
    public bool Confirm(decimal approvedTotal, long number, DateTime at)
    {
        if (Status == SaleStatus.Confirmed) return false;
        EnsureEditable();
        if (_lines.Count == 0)
            throw new DomainException("VALIDACION", "La venta no tiene líneas.");
        if (approvedTotal != Total)
            throw new DomainException("PAGOS_NO_CUBREN_TOTAL", $"Los pagos aprobados ({approvedTotal:N2}) no cubren el total ({Total:N2}).");
        Status = SaleStatus.Confirmed;
        PaymentStatus = SalePaymentStatus.Paid;
        Number = number;
        ConfirmedAt = at;
        return true;
    }

    public void MarkInventoryApplied() => InventoryStatus = SaleInventoryStatus.Applied;

    public void SetFiscal(SaleFiscalStatus status, Guid? fiscalDocumentId)
    {
        FiscalStatus = status;
        FiscalDocumentId = fiscalDocumentId ?? FiscalDocumentId;
    }

    /// <summary>
    /// D-11: anulación total de una venta confirmada mientras su turno sigue abierto (lo valida el llamador) y sin
    /// devoluciones previas. Se reembolsa todo y se reversa el inventario.
    /// </summary>
    public void Void(string reason, Guid by, DateTime at)
    {
        if (Status != SaleStatus.Confirmed)
            throw new DomainException("ESTADO_INVALIDO", "Solo se anula una venta confirmada.");
        if (RefundedTotal > 0)
            throw new DomainException("ANULACION_NO_PERMITIDA", "La venta ya tiene devoluciones: registra otra devolución en vez de anularla.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("VALIDACION", "La anulación exige motivo.");
        Status = SaleStatus.Voided;
        VoidReason = reason.Trim();
        VoidedBy = by;
        VoidedAt = at;
        RefundedTotal = Total;
        PaymentStatus = SalePaymentStatus.Refunded;
        if (InventoryStatus == SaleInventoryStatus.Applied)
            InventoryStatus = SaleInventoryStatus.Reversed;
    }

    /// <summary>Registra el dinero de una devolución; no puede superar lo cobrado.</summary>
    public void RegisterReturn(decimal amount)
    {
        if (Status != SaleStatus.Confirmed)
            throw new DomainException("ESTADO_INVALIDO", "Solo se devuelve sobre una venta confirmada.");
        if (amount <= 0 || RefundedTotal + amount > Total)
            throw new DomainException("VALIDACION", "La devolución supera lo cobrado en la venta.");
        RefundedTotal += amount;
        PaymentStatus = RefundedTotal == Total ? SalePaymentStatus.Refunded : SalePaymentStatus.PartiallyRefunded;
    }

    /// <summary>Cancela una venta que nunca se pagó (sin pagos aprobados ni en curso; lo valida el llamador).</summary>
    public void Cancel(DateTime at)
    {
        EnsureEditable();
        Status = SaleStatus.Cancelled;
        CancelledAt = at;
    }

    private void EnsureEditable()
    {
        if (!IsOpen)
            throw new DomainException("ESTADO_INVALIDO", $"La venta está {Status}: no se puede modificar.");
    }

    private void Recalculate()
    {
        Subtotal = _lines.Sum(l => l.TaxableBase);
        DiscountTotal = _lines.Sum(l => l.DiscountAmount);
        TaxTotal = _lines.Sum(l => l.TaxAmount);
        Total = _lines.Sum(l => l.LineTotal);
    }
}

/// <summary>
/// Línea con copia histórica de nombre, precio e impuesto. Con precio que incluye impuesto (D-07):
/// bruto = cantidad × precio − descuento; base = bruto / (1 + tarifa); impuesto = bruto − base. Si no lo
/// incluye: base = cantidad × precio − descuento; impuesto = base × tarifa. Redondeo a 2 decimales.
/// </summary>
public sealed class SaleLine : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid SaleId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string UnitCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public bool PriceIncludesTax { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public string? DiscountReason { get; private set; }
    public Guid TaxId { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal TaxableBase { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    /// <summary>Precio unitario con impuesto incluido (lo que recibe el proveedor fiscal).</summary>
    public decimal UnitPriceWithTax => PriceIncludesTax ? UnitPrice : decimal.Round(UnitPrice * (1 + TaxRate / 100m), 2);

    private SaleLine() { }

    internal static SaleLine Create(Sale sale, int lineNo, Guid productId, string sku, string description, string unitCode, decimal quantity,
        decimal unitPrice, bool priceIncludesTax, decimal discountAmount, string? discountReason, Guid taxId, decimal taxRate)
    {
        // Máximo 2 decimales: es lo que admite la factura electrónica (Siigo, items.quantity).
        if (quantity <= 0 || decimal.Round(quantity, 2) != quantity)
            throw new DomainException("VALIDACION", "La cantidad debe ser mayor que 0 con máximo 2 decimales.");
        if (unitPrice < 0 || discountAmount < 0 || decimal.Round(discountAmount, 2) != discountAmount)
            throw new DomainException("VALIDACION", "Precio y descuento no pueden ser negativos; el descuento admite 2 decimales.");
        if (discountAmount > 0 && string.IsNullOrWhiteSpace(discountReason))
            throw new DomainException("VALIDACION", "El descuento exige motivo.");
        var gross = decimal.Round(quantity * unitPrice, 2) - discountAmount;
        if (gross < 0)
            throw new DomainException("VALIDACION", "El descuento no puede superar el valor de la línea.");

        var line = new SaleLine
        {
            CompanyId = sale.CompanyId, SaleId = sale.Id, LineNo = lineNo, ProductId = productId, Sku = sku, Description = description,
            UnitCode = unitCode, Quantity = quantity, UnitPrice = unitPrice, PriceIncludesTax = priceIncludesTax, DiscountAmount = discountAmount,
            DiscountReason = discountAmount > 0 ? discountReason!.Trim() : null, TaxId = taxId, TaxRate = taxRate,
        };
        if (priceIncludesTax)
        {
            line.TaxableBase = decimal.Round(gross / (1 + taxRate / 100m), 2);
            line.TaxAmount = gross - line.TaxableBase;
            line.LineTotal = gross;
        }
        else
        {
            line.TaxableBase = gross;
            line.TaxAmount = decimal.Round(gross * taxRate / 100m, 2);
            line.LineTotal = gross + line.TaxAmount;
        }
        return line;
    }
}

/// <summary>Consecutivo interno de ventas por empresa (la versión de fila evita repetir números).</summary>
public sealed class SaleCounter : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public long LastNumber { get; private set; }

    private SaleCounter() { }

    public static SaleCounter Create(Guid companyId) => new() { CompanyId = companyId };

    public long Next() => ++LastNumber;
}
