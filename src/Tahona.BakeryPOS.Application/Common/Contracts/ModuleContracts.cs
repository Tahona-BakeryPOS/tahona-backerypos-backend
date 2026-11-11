namespace Tahona.BakeryPOS.Application.Common.Contracts;

// Contracts between modules: the only thing a module may ask of another. Each one is implemented by the
// module that owns the data; this way no module touches another's DbSets or entities (architecture tests).

/// <summary>Implemented by Companies.</summary>
public interface ICompanyDirectory
{
    /// <summary>Active companies among those given (without the session company filter: used by login).</summary>
    Task<IReadOnlyList<CompanySummary>> GetActiveAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken ct);
}

/// <summary>Implemented by Companies. Respects the session's company filter.</summary>
public interface IBranchDirectory
{
    /// <summary>Branch of the company in session; null if it does not exist or belongs to another company.</summary>
    Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken ct);
}

/// <summary>Implemented by Cash.</summary>
public interface ICashRegisterDirectory
{
    /// <summary>Cash register of the company in session; null if it does not exist or belongs to another company.</summary>
    Task<CashRegisterSummary?> FindAsync(Guid cashRegisterId, CancellationToken ct);

    /// <summary>Cash register of any company: only for device flows authenticated by a secret.</summary>
    Task<CashRegisterSummary?> FindForDeviceAsync(Guid cashRegisterId, CancellationToken ct);
}

/// <summary>Implemented by Identity: default roles and the owner when onboarding a company.</summary>
public interface IIdentityProvisioning
{
    /// <summary>
    /// Adds (without saving) the system roles and the owner's membership. If the email already
    /// exists the user is reused (D-08) and no password is generated.
    /// </summary>
    Task<ProvisionedOwner> ProvisionCompanyAsync(Guid companyId, string ownerFullName, string ownerEmail, CancellationToken ct);
}

/// <summary>Implemented by Catalog: the catalog data a company is born with.</summary>
public interface ICatalogProvisioning
{
    /// <summary>Adds (without saving) the default taxes and the "General" category.</summary>
    void ProvisionCompany(Guid companyId);
}

/// <summary>Implemented by Catalog: what Inventory (and later Sales) needs from products.</summary>
public interface IProductDirectory
{
    /// <summary>Products of the company in session among those given (absent = they do not exist or belong to another company).</summary>
    Task<IReadOnlyDictionary<Guid, StockProductInfo>> FindAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    /// <summary>The price in force (branch or base, D-07) and tax of each product, for selling or invoicing.</summary>
    Task<IReadOnlyDictionary<Guid, SaleProductInfo>> PricedAsync(IReadOnlyCollection<Guid> productIds, Guid? branchId, DateTime at, CancellationToken ct);

    /// <summary>Active products that track stock, by name, with optional search.</summary>
    Task<(IReadOnlyList<StockProductInfo> Items, int Total)> ListTrackedAsync(string? search, int page, int pageSize, CancellationToken ct);
}

/// <summary>Implemented by Inventory: locations and reasons that companies and branches are born with.</summary>
public interface IInventoryProvisioning
{
    /// <summary>Adds (without saving) the default adjustment reasons.</summary>
    void ProvisionCompany(Guid companyId);

    /// <summary>Adds (without saving) the branch's default location ("Mostrador").</summary>
    void ProvisionBranch(Guid companyId, Guid branchId);
}

/// <summary>Implemented by Payments: unresolved payments of a cash session (D-12: they block the close).</summary>
public interface IPendingPaymentsProbe
{
    Task<int> CountForCashSessionAsync(Guid cashSessionId, CancellationToken ct);
}

/// <summary>Implemented by Cash: cash sessions for modules that charge (Payments, Sales).</summary>
public interface ICashSessionDirectory
{
    /// <summary>Session of the company in session; null if it does not exist or belongs to another company.</summary>
    Task<CashSessionSummary?> FindAsync(Guid cashSessionId, CancellationToken ct);

    /// <summary>The register's open session; null if there is none.</summary>
    Task<CashSessionSummary?> FindOpenForRegisterAsync(Guid cashRegisterId, CancellationToken ct);
}

public sealed record CashSessionSummary(Guid Id, Guid BranchId, Guid CashRegisterId, bool IsOpen);

/// <summary>Implemented by Payments: payment methods a company is born with.</summary>
public interface IPaymentsProvisioning
{
    void ProvisionCompany(Guid companyId);
}

/// <summary>Implemented by Cash: cash reasons a company is born with.</summary>
public interface ICashProvisioning
{
    /// <summary>Adds (without saving) the default income and withdrawal reasons.</summary>
    void ProvisionCompany(Guid companyId);
}

/// <summary>
/// Implemented by Invoicing: the confirmed sale requests its electronic invoice (D-17). Adds (without saving)
/// the document and its outbox message in the caller's transaction; sending happens afterwards.
/// </summary>
public interface IFiscalDocumentIssuer
{
    /// <summary>Whether the company in session has electronic invoicing on.</summary>
    Task<bool> IsActiveAsync(CancellationToken ct);

    /// <summary>Id of the document created; fails if invoicing is not configured and active.</summary>
    Task<Guid> EnqueueSaleInvoiceAsync(SaleFiscalSnapshot sale, CancellationToken ct);

    /// <summary>Fiscal status of the invoices of these sales (for the receipt and the listings).</summary>
    Task<IReadOnlyDictionary<Guid, FiscalDocumentSummary>> ForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);

    /// <summary>
    /// Phase 9 (US-8.5): credit note for a void (reason 2, the whole invoice: <paramref name="lines"/> and
    /// <paramref name="payments"/> null) or a return (reason 1, what was returned). Adds without saving. Returns null if the
    /// sale has no valid invoice to correct; on a void, a rejected invoice or one pending correction is cancelled.
    /// </summary>
    Task<Guid?> EnqueueCreditNoteAsync(Guid saleId, Guid sourceId, bool isVoid, IReadOnlyList<SaleFiscalLine>? lines,
        IReadOnlyList<SaleFiscalPayment>? payments, decimal total, CancellationToken ct);

    /// <summary>Credit notes of these sales.</summary>
    Task<IReadOnlyList<CreditNoteSummary>> CreditNotesForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);
}

/// <param name="SourceId">Sale (void) or return that originated the note.</param>
public sealed record CreditNoteSummary(Guid Id, Guid SaleId, Guid SourceId, int Reason, string Status, string? Prefix, long? Number,
    string? Cufe, string? Errors, decimal Total);

/// <param name="Status"><c>pending</c>, <c>submitting</c>, <c>accepted</c>, <c>rejected</c>, <c>temporary_error</c>, <c>unknown</c>, <c>cancelled</c>.</param>
public sealed record FiscalDocumentSummary(Guid Id, Guid SaleId, string Status, string? Prefix, long? Number, string? Cufe, string? Errors);

/// <summary>Implemented by Inventory: what the sale moves in stock (the branch's default location).</summary>
public interface IInventoryPosting
{
    /// <summary>Products with a "block" policy (D-06) whose stock is not enough.</summary>
    Task<IReadOnlyList<StockShortage>> ShortagesAsync(Guid branchId, IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, CancellationToken ct);

    /// <summary>
    /// Deducts the sale (without saving; idempotent per line). With <paramref name="authorizedBy"/> it allows a negative balance
    /// on "block" products; it fails with EXISTENCIAS_INSUFICIENTES if there is none.
    /// </summary>
    Task<SharedKernel.Results.Result> PostSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid? authorizedBy, CancellationToken ct);

    /// <summary>Void: returns to stock everything the sale deducted (<c>void_reversal</c>, without saving; idempotent).</summary>
    Task<SharedKernel.Results.Result> ReverseSaleAsync(Guid branchId, Guid saleId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        CancellationToken ct);

    /// <summary>Return: restocks what goes back on sale (<c>return</c>, without saving; idempotent per return line).</summary>
    Task<SharedKernel.Results.Result> PostReturnAsync(Guid branchId, Guid returnId, IReadOnlyList<(Guid LineId, Guid ProductId, decimal Quantity)> lines,
        Guid authorizedBy, CancellationToken ct);
}

public sealed record StockShortage(Guid ProductId, string Name, decimal OnHand, decimal Requested);

/// <summary>Implemented by Cash: the cash that enters the drawer with a sale.</summary>
public interface ICashDrawer
{
    /// <summary>Adds (without saving; idempotent per sale) the cash received and the change given.</summary>
    Task RecordSaleCashAsync(Guid cashSessionId, Guid saleId, decimal received, decimal change, CancellationToken ct);

    /// <summary>
    /// D-11: the money of a void or return leaves in cash from the open session (without saving; idempotent per
    /// source). Fails with CAJA_CERRADA or if the expected cash is not enough.
    /// </summary>
    Task<SharedKernel.Results.Result> RecordRefundAsync(Guid cashSessionId, string sourceType, Guid sourceId, decimal amount, Guid authorizedBy,
        CancellationToken ct);
}

/// <summary>Origins of refunds (cash, payments, notes).</summary>
public static class RefundSources
{
    public const string Void = "void";
    public const string Return = "return";
}

/// <summary>Implemented by Payments: a sale's payments.</summary>
public interface ISalePayments
{
    /// <summary>Cash, transfer or external card terminal: it is left approved (saves).</summary>
    Task<SharedKernel.Results.Result<SalePaymentInfo>> RecordImmediateAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, decimal? amountTendered, string? reference, CancellationToken ct);

    /// <summary>Gateway method: charge on the register's card terminal; it stays pending until the webhook (saves).</summary>
    Task<SharedKernel.Results.Result<SalePaymentInfo>> StartGatewayAsync(Guid saleId, CashSessionSummary session, Guid paymentMethodId,
        decimal amount, string description, CancellationToken ct);

    Task<IReadOnlyList<SalePaymentInfo>> ForSaleAsync(Guid saleId, CancellationToken ct);

    /// <summary>Kind of the method (<c>cash</c>, <c>gateway</c>, <c>external_card</c>, <c>transfer_manual</c>) or null if it does not exist.</summary>
    Task<string?> MethodKindAsync(Guid paymentMethodId, CancellationToken ct);

    /// <summary>
    /// Spreads a cash refund across the sale's approved payments (the last first) and records it (without saving;
    /// idempotent per source). Returns what was refunded per payment method (for the credit note).
    /// </summary>
    Task<SharedKernel.Results.Result<IReadOnlyList<SaleFiscalPayment>>> RefundAsync(Guid saleId, string sourceType, Guid sourceId, decimal amount,
        CancellationToken ct);

    /// <summary>Net charged per payment method of these sales (reports).</summary>
    Task<IReadOnlyList<PaymentMethodTotal>> TotalsByMethodAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct);
}

/// <param name="Net">Aprobado menos reembolsado.</param>
public sealed record PaymentMethodTotal(Guid PaymentMethodId, string MethodName, decimal Approved, decimal Refunded, decimal Net);

/// <param name="Status"><c>pending</c>, <c>approved</c>, <c>declined</c>, <c>cancelled</c>…</param>
public sealed record SalePaymentInfo(Guid Id, Guid PaymentMethodId, string MethodName, string MethodKind, decimal Amount, decimal? AmountTendered,
    decimal? ChangeAmount, string Status, string? AttemptStatus, string? Reference);

/// <summary>Outbox messages towards Sales (published by Payments and Invoicing without knowing Sales).</summary>
public static class SaleMessages
{
    /// <summary>A sale payment was resolved (webhook or by hand): Sales tries to confirm the sale.</summary>
    public const string PaymentResolved = "sales.payment-resolved";
}

public sealed record SalePaymentResolved(Guid CompanyId, Guid SaleId);

/// <param name="CustomerIdentification">NIT or ID of the buyer; null = final consumer.</param>
/// <param name="Lines">Unit prices with tax included (D-07).</param>
public sealed record SaleFiscalSnapshot(Guid SaleId, DateTime OccurredAt, string? CustomerIdentification, IReadOnlyList<SaleFiscalLine> Lines,
    IReadOnlyList<SaleFiscalPayment> Payments, decimal Total);

/// <param name="Discount">The line's discount (with tax included).</param>
public sealed record SaleFiscalLine(Guid ProductId, string Sku, string Name, decimal Quantity, decimal UnitPriceWithTax, Guid TaxId, decimal Discount = 0);

public sealed record SaleFiscalPayment(Guid PaymentMethodId, decimal Amount);

/// <summary>Implemented by Identity: display names to show who recorded or authorized something.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

/// <param name="AllowNegative">The product's insufficient-stock policy (D-06): true = allow negative.</param>
public sealed record StockProductInfo(Guid Id, string Sku, string Name, string UnitCode, bool AllowsDecimals, bool TrackStock,
    bool AllowNegative, bool IsActive);

/// <param name="Price">The price in force; null = no current price.</param>
/// <param name="IsAvailable">Available at the queried branch (US-3.3).</param>
public sealed record SaleProductInfo(Guid Id, string Sku, string Name, string UnitCode, Guid TaxId, decimal TaxRate, decimal? Price, bool PriceIncludesTax,
    bool AllowsDecimals, bool TrackStock, bool IsActive, bool IsAvailable);

public sealed record CompanySummary(Guid Id, string Name);

public sealed record BranchSummary(Guid Id, string Code, string Name, bool IsActive);

public sealed record CashRegisterSummary(Guid Id, Guid CompanyId, Guid BranchId, string Code, string Name, bool IsActive);

/// <param name="TemporaryPassword">One-time password; null if the user already existed.</param>
public sealed record ProvisionedOwner(Guid UserId, string Email, string? TemporaryPassword);
