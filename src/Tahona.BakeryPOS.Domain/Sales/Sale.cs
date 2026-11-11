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
    /// <summary>The company has no active invoicing when confirming.</summary>
    NotRequired,
    Pending,
    Accepted,
    Rejected,
    InReview,
}

/// <summary>
/// Sale as a persistent process (architecture.md §4): draft with prices and taxes frozen by the
/// server → pending payment → confirmed when approved payments cover the total. Sale, payment,
/// inventory and fiscal document each have their own status.
/// </summary>
public sealed class Sale : AuditableEntity, ITenantEntity
{
    private readonly List<SaleLine> _lines = [];

    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public Guid CashierId { get; private set; }
    /// <summary>NIT or ID if the customer asks for an invoice with their data; null = final consumer.</summary>
    public string? CustomerIdentification { get; private set; }
    /// <summary>Internal consecutive number per company, assigned on confirmation.</summary>
    public long? Number { get; private set; }
    public SaleStatus Status { get; private set; } = SaleStatus.Draft;
    public SalePaymentStatus PaymentStatus { get; private set; } = SalePaymentStatus.Unpaid;
    public SaleInventoryStatus InventoryStatus { get; private set; } = SaleInventoryStatus.NotApplied;
    public SaleFiscalStatus FiscalStatus { get; private set; } = SaleFiscalStatus.NotRequired;
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    /// <summary>Who authorized selling products with a "block" policy without stock (D-06).</summary>
    public Guid? StockAuthorizedBy { get; private set; }
    public Guid? FiscalDocumentId { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }
    public Guid? VoidedBy { get; private set; }
    /// <summary>Money returned to the customer through a void or returns.</summary>
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

    /// <summary>There is at least one payment in progress or approved: lines can no longer be added.</summary>
    public void MarkPendingPayment(decimal approvedTotal)
    {
        EnsureEditable();
        Status = SaleStatus.PendingPayment;
        PaymentStatus = approvedTotal <= 0 ? SalePaymentStatus.Unpaid : approvedTotal < Total ? SalePaymentStatus.PartiallyPaid : SalePaymentStatus.Paid;
    }

    /// <summary>Confirms when what was approved covers the exact total. Idempotent for an already confirmed sale.</summary>
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
    /// D-11: a full void of a confirmed sale while its shift is still open (validated by the caller) and with no
    /// previous returns. Everything is refunded and the inventory is reversed.
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

    /// <summary>Records the money of a return; it cannot exceed what was charged.</summary>
    public void RegisterReturn(decimal amount)
    {
        if (Status != SaleStatus.Confirmed)
            throw new DomainException("ESTADO_INVALIDO", "Solo se devuelve sobre una venta confirmada.");
        if (amount <= 0 || RefundedTotal + amount > Total)
            throw new DomainException("VALIDACION", "La devolución supera lo cobrado en la venta.");
        RefundedTotal += amount;
        PaymentStatus = RefundedTotal == Total ? SalePaymentStatus.Refunded : SalePaymentStatus.PartiallyRefunded;
    }

    /// <summary>Cancels a sale that was never paid (no approved or in-progress payments; validated by the caller).</summary>
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
/// Line with a historical copy of name, price and tax. With a price that includes tax (D-07):
/// gross = quantity × price − discount; base = gross / (1 + rate); tax = gross − base. If it does not
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

    /// <summary>Unit price with tax included (what the fiscal provider receives).</summary>
    public decimal UnitPriceWithTax => PriceIncludesTax ? UnitPrice : decimal.Round(UnitPrice * (1 + TaxRate / 100m), 2);

    private SaleLine() { }

    internal static SaleLine Create(Sale sale, int lineNo, Guid productId, string sku, string description, string unitCode, decimal quantity,
        decimal unitPrice, bool priceIncludesTax, decimal discountAmount, string? discountReason, Guid taxId, decimal taxRate)
    {
        // At most 2 decimals: it is what the electronic invoice accepts (Siigo, items.quantity).
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

/// <summary>Internal consecutive number of sales per company (the row version prevents repeating numbers).</summary>
public sealed class SaleCounter : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public long LastNumber { get; private set; }

    private SaleCounter() { }

    public static SaleCounter Create(Guid companyId) => new() { CompanyId = companyId };

    public long Next() => ++LastNumber;
}
