namespace Tahona.BakeryPOS.Application.Common.Contracts;

// Contratos entre módulos: lo único que un módulo puede pedirle a otro. Cada uno lo implementa el
// módulo dueño de los datos; así ningún módulo toca DbSet ni entidades de otro (pruebas de arquitectura).

/// <summary>Implementado por Companies.</summary>
public interface ICompanyDirectory
{
    /// <summary>Empresas activas entre las indicadas (sin filtro por empresa en sesión: lo usa el login).</summary>
    Task<IReadOnlyList<CompanySummary>> GetActiveAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken ct);
}

/// <summary>Implementado por Companies. Respeta el filtro por empresa de la sesión.</summary>
public interface IBranchDirectory
{
    /// <summary>Sucursal de la empresa en sesión; null si no existe o es de otra empresa.</summary>
    Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken ct);
}

/// <summary>Implementado por Cash.</summary>
public interface ICashRegisterDirectory
{
    /// <summary>Caja de la empresa en sesión; null si no existe o es de otra empresa.</summary>
    Task<CashRegisterSummary?> FindAsync(Guid cashRegisterId, CancellationToken ct);

    /// <summary>Caja de cualquier empresa: solo para flujos de dispositivo autenticados por secreto.</summary>
    Task<CashRegisterSummary?> FindForDeviceAsync(Guid cashRegisterId, CancellationToken ct);
}

/// <summary>Implementado por Identity: roles por defecto y propietario al dar de alta una empresa.</summary>
public interface IIdentityProvisioning
{
    /// <summary>
    /// Agrega (sin guardar) los roles del sistema y la pertenencia del propietario. Si el correo ya
    /// existe se reutiliza el usuario (D-08) y no se genera contraseña.
    /// </summary>
    Task<ProvisionedOwner> ProvisionCompanyAsync(Guid companyId, string ownerFullName, string ownerEmail, CancellationToken ct);
}

/// <summary>Implementado por Catalog: datos de catálogo con que nace una empresa.</summary>
public interface ICatalogProvisioning
{
    /// <summary>Agrega (sin guardar) los impuestos por defecto y la categoría "General".</summary>
    void ProvisionCompany(Guid companyId);
}

/// <summary>Implementado por Catalog: lo que Inventory (y luego Sales) necesita de los productos.</summary>
public interface IProductDirectory
{
    /// <summary>Productos de la empresa en sesión entre los indicados (ausentes = no existen o son de otra empresa).</summary>
    Task<IReadOnlyDictionary<Guid, StockProductInfo>> FindAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    /// <summary>Precio que rige (sucursal o base, D-07) e impuesto de cada producto, para vender o facturar.</summary>
    Task<IReadOnlyDictionary<Guid, SaleProductInfo>> PricedAsync(IReadOnlyCollection<Guid> productIds, Guid? branchId, DateTime at, CancellationToken ct);

    /// <summary>Productos activos que controlan existencias, por nombre, con búsqueda opcional.</summary>
    Task<(IReadOnlyList<StockProductInfo> Items, int Total)> ListTrackedAsync(string? search, int page, int pageSize, CancellationToken ct);
}

/// <summary>Implementado por Inventory: ubicaciones y motivos con que nacen empresas y sucursales.</summary>
public interface IInventoryProvisioning
{
    /// <summary>Agrega (sin guardar) los motivos de ajuste por defecto.</summary>
    void ProvisionCompany(Guid companyId);

    /// <summary>Agrega (sin guardar) la ubicación por defecto ("Mostrador") de la sucursal.</summary>
    void ProvisionBranch(Guid companyId, Guid branchId);
}

/// <summary>Implementado por Payments: pagos sin resolver de una sesión de caja (D-12: bloquean el cierre).</summary>
public interface IPendingPaymentsProbe
{
    Task<int> CountForCashSessionAsync(Guid cashSessionId, CancellationToken ct);
}

/// <summary>Implementado por Cash: sesiones de caja para módulos que cobran (Payments, Sales).</summary>
public interface ICashSessionDirectory
{
    /// <summary>Sesión de la empresa en sesión; null si no existe o es de otra empresa.</summary>
    Task<CashSessionSummary?> FindAsync(Guid cashSessionId, CancellationToken ct);

    /// <summary>Sesión abierta de la caja; null si no hay.</summary>
    Task<CashSessionSummary?> FindOpenForRegisterAsync(Guid cashRegisterId, CancellationToken ct);
}

public sealed record CashSessionSummary(Guid Id, Guid BranchId, Guid CashRegisterId, bool IsOpen);

/// <summary>Implementado por Payments: medios de pago con que nace una empresa.</summary>
public interface IPaymentsProvisioning
{
    void ProvisionCompany(Guid companyId);
}

/// <summary>Implementado por Cash: motivos de caja con que nace una empresa.</summary>
public interface ICashProvisioning
{
    /// <summary>Agrega (sin guardar) los motivos de ingreso y retiro por defecto.</summary>
    void ProvisionCompany(Guid companyId);
}

/// <summary>
/// Implementado por Invoicing: la venta confirmada pide su factura electrónica (D-05). Agrega (sin guardar)
/// el documento y su mensaje de outbox en la transacción del llamador; el envío ocurre después.
/// </summary>
public interface IFiscalDocumentIssuer
{
    /// <summary>Si la empresa en sesión tiene la facturación electrónica activa.</summary>
    Task<bool> IsActiveAsync(CancellationToken ct);

    /// <summary>Id del documento creado; falla si la facturación no está configurada y activa.</summary>
    Task<Guid> EnqueueSaleInvoiceAsync(SaleFiscalSnapshot sale, CancellationToken ct);

    /// <summary>Estado fiscal de las facturas de estas ventas (para el comprobante y los listados).</summary>
    Task<IReadOnlyDictionary<Guid, FiscalDocumentSummary>> ForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);

    /// <summary>
    /// Fase 9 (HU-8.5): nota crédito por una anulación (motivo 2, toda la factura: <paramref name="lines"/> y
    /// <paramref name="payments"/> null) o una devolución (motivo 1, lo devuelto). Agrega sin guardar. Devuelve null si la
    /// venta no tiene factura válida que corregir; en una anulación, la factura rechazada o pendiente de corrección se cancela.
    /// </summary>
    Task<Guid?> EnqueueCreditNoteAsync(Guid saleId, Guid sourceId, bool isVoid, IReadOnlyList<SaleFiscalLine>? lines,
        IReadOnlyList<SaleFiscalPayment>? payments, decimal total, CancellationToken ct);

    /// <summary>Notas crédito de estas ventas.</summary>
    Task<IReadOnlyList<CreditNoteSummary>> CreditNotesForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);
}

/// <param name="SourceId">Venta (anulación) o devolución que originó la nota.</param>
public sealed record CreditNoteSummary(Guid Id, Guid SaleId, Guid SourceId, int Reason, string Status, string? Prefix, long? Number,
    string? Cufe, string? Errors, decimal Total);

/// <param name="Status"><c>pending</c>, <c>submitting</c>, <c>accepted</c>, <c>rejected</c>, <c>temporary_error</c>, <c>unknown</c>, <c>cancelled</c>.</param>
public sealed record FiscalDocumentSummary(Guid Id, Guid SaleId, string Status, string? Prefix, long? Number, string? Cufe, string? Errors);

/// <summary>Implementado por Inventory: lo que la venta mueve en existencias (ubicación por defecto de la sucursal).</summary>
public interface IInventoryPosting
{
    /// <summary>Productos con política "bloquear" (D-06) cuyas existencias no alcanzan.</summary>
    Task<IReadOnlyList<StockShortage>> ShortagesAsync(Guid branchId, IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, CancellationToken ct);

    /// <summary>
    /// Descuenta la venta (sin guardar; idempotente por línea). Con <paramref name="authorizedBy"/> permite saldo negativo
    /// en productos "bloquear"; falla con EXISTENCIAS_INSUFICIENTES si no lo hay.
    /// </summary>
    Task<SharedKernel.Results.Result> PostSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid? authorizedBy, CancellationToken ct);

    /// <summary>Anulación: devuelve a existencias todo lo que la venta descontó (<c>void_reversal</c>, sin guardar; idempotente).</summary>
    Task<SharedKernel.Results.Result> ReverseSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        CancellationToken ct);

    /// <summary>Devolución: reingresa lo que vuelve a la venta (<c>return</c>, sin guardar; idempotente por línea de devolución).</summary>
    Task<SharedKernel.Results.Result> PostReturnAsync(Guid branchId, Guid returnId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid authorizedBy, CancellationToken ct);
}

public sealed record StockShortage(Guid ProductId, string Name, decimal OnHand, decimal Requested);

/// <summary>Implementado por Cash: el efectivo que entra a la gaveta con una venta.</summary>
public interface ICashDrawer
{
    /// <summary>Agrega (sin guardar; idempotente por venta) el efectivo recibido y el cambio entregado.</summary>
    Task RecordSaleCashAsync(Guid cashSessionId, Guid saleId, decimal received, decimal change, CancellationToken ct);

    /// <summary>
    /// D-11: el dinero de una anulación o devolución sale en efectivo de la sesión abierta (sin guardar; idempotente por
    /// origen). Falla con CAJA_CERRADA o si el efectivo esperado no alcanza.
    /// </summary>
    Task<SharedKernel.Results.Result> RecordRefundAsync(Guid cashSessionId, string sourceType, Guid sourceId, decimal amount, Guid authorizedBy,
        CancellationToken ct);
}

/// <summary>Orígenes de reembolsos (caja, pagos, notas).</summary>
public static class RefundSources
{
    public const string Void = "void";
    public const string Return = "return";
}

/// <summary>Implementado por Payments: los pagos de una venta.</summary>
public interface ISalePayments
{
    /// <summary>Efectivo, transferencia o datáfono externo: queda aprobado (guarda).</summary>
    Task<SharedKernel.Results.Result<SalePaymentInfo>> RecordImmediateAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, decimal? amountTendered, string? reference, CancellationToken ct);

    /// <summary>Medio de la pasarela: cobro en el datáfono de la caja; queda pendiente hasta el webhook (guarda).</summary>
    Task<SharedKernel.Results.Result<SalePaymentInfo>> StartGatewayAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, string description, CancellationToken ct);

    Task<IReadOnlyList<SalePaymentInfo>> ForSaleAsync(Guid saleId, CancellationToken ct);

    /// <summary>Kind del medio (<c>cash</c>, <c>gateway</c>, <c>external_card</c>, <c>transfer_manual</c>) o null si no existe.</summary>
    Task<string?> MethodKindAsync(Guid paymentMethodId, CancellationToken ct);

    /// <summary>
    /// Reparte un reembolso en efectivo entre los pagos aprobados de la venta (el último primero) y lo registra (sin guardar;
    /// idempotente por origen). Devuelve lo reembolsado por medio de pago (para la nota crédito).
    /// </summary>
    Task<SharedKernel.Results.Result<IReadOnlyList<SaleFiscalPayment>>> RefundAsync(Guid saleId, string sourceType, Guid sourceId, decimal amount,
        CancellationToken ct);

    /// <summary>Cobrado neto por medio de pago de estas ventas (informes).</summary>
    Task<IReadOnlyList<PaymentMethodTotal>> TotalsByMethodAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);
}

/// <param name="Net">Aprobado menos reembolsado.</param>
public sealed record PaymentMethodTotal(Guid PaymentMethodId, string MethodName, decimal Approved, decimal Refunded, decimal Net);

/// <param name="Status"><c>pending</c>, <c>approved</c>, <c>declined</c>, <c>cancelled</c>…</param>
public sealed record SalePaymentInfo(Guid Id, Guid PaymentMethodId, string MethodName, string MethodKind, decimal Amount, decimal? AmountTendered,
    decimal? ChangeAmount, string Status, string? AttemptStatus, string? Reference);

/// <summary>Mensajes de outbox hacia Sales (los publican Payments e Invoicing sin conocer a Sales).</summary>
public static class SaleMessages
{
    /// <summary>Un pago de venta se resolvió (webhook o a mano): Sales intenta confirmar la venta.</summary>
    public const string PaymentResolved = "sales.payment-resolved";
}

public sealed record SalePaymentResolved(Guid CompanyId, Guid SaleId);

/// <param name="CustomerIdentification">NIT o cédula del adquirente; null = consumidor final.</param>
/// <param name="Lines">Precios unitarios con impuesto incluido (D-07).</param>
public sealed record SaleFiscalSnapshot(Guid SaleId, DateTime OccurredAt, string? CustomerIdentification, IReadOnlyList<SaleFiscalLine> Lines,
    IReadOnlyList<SaleFiscalPayment> Payments, decimal Total);

/// <param name="Discount">Descuento de la línea (con impuesto incluido).</param>
public sealed record SaleFiscalLine(Guid ProductId, string Sku, string Name, decimal Quantity, decimal UnitPriceWithTax, Guid TaxId, decimal Discount = 0);

public sealed record SaleFiscalPayment(Guid PaymentMethodId, decimal Amount);

/// <summary>Implementado por Identity: nombres para mostrar quién registró o autorizó algo.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

/// <param name="AllowNegative">Política de existencias insuficientes del producto (D-06): true = permitir negativo.</param>
public sealed record StockProductInfo(Guid Id, string Sku, string Name, string UnitCode, bool AllowsDecimals, bool TrackStock,
    bool AllowNegative, bool IsActive);

/// <param name="Price">Precio que rige; null = sin precio vigente.</param>
/// <param name="IsAvailable">Disponible en la sucursal consultada (HU-3.3).</param>
public sealed record SaleProductInfo(Guid Id, string Sku, string Name, string UnitCode, Guid TaxId, decimal TaxRate, decimal? Price, bool PriceIncludesTax,
    bool AllowsDecimals, bool TrackStock, bool IsActive, bool IsAvailable);

public sealed record CompanySummary(Guid Id, string Name);

public sealed record BranchSummary(Guid Id, string Code, string Name, bool IsActive);

public sealed record CashRegisterSummary(Guid Id, Guid CompanyId, Guid BranchId, string Code, string Name, bool IsActive);

/// <param name="TemporaryPassword">Contraseña de un solo uso; null si el usuario ya existía.</param>
public sealed record ProvisionedOwner(Guid UserId, string Email, string? TemporaryPassword);
