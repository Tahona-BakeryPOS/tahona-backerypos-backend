using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Cash;

public enum CashSessionStatus
{
    Open,
    /// <summary>Reserved for a two-step close; the MVP goes from open to closed.</summary>
    Closing,
    Closed,
}

/// <summary>
/// A cash register's session (shift) (US-5.2, US-5.4): only one open per register. The expected cash is the
/// signed sum of its movements (opening, cash sales, change, refunds, income and
/// withdrawals). On closing, the expected, counted and difference are stored; a closed session does not change.
/// </summary>
public sealed class CashSession : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public CashSessionStatus Status { get; private set; } = CashSessionStatus.Open;
    public Guid OpenedBy { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public decimal OpeningAmount { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public decimal? ExpectedAmount { get; private set; }
    public decimal? CountedAmount { get; private set; }
    /// <summary>Contado − esperado: positivo sobra, negativo falta.</summary>
    public decimal? Difference { get; private set; }
    public string? Notes { get; private set; }

    public bool IsOpen => Status != CashSessionStatus.Closed;

    private CashSession() { }

    public static CashSession Open(Guid companyId, Guid branchId, Guid cashRegisterId, Guid openedBy, decimal openingAmount, DateTime at)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || cashRegisterId == Guid.Empty || openedBy == Guid.Empty)
            throw new DomainException("VALIDACION", "La sesión necesita empresa, sucursal, caja y usuario.");
        CashMoney.Validate(openingAmount, allowZero: true, "El efectivo inicial");
        return new CashSession
        {
            CompanyId = companyId, BranchId = branchId, CashRegisterId = cashRegisterId, OpenedBy = openedBy,
            OpenedAt = at, OpeningAmount = openingAmount,
        };
    }

    /// <summary>Closes with the counted cash; the expected amount is computed by the caller from the movements.</summary>
    public void Close(decimal expected, decimal counted, Guid closedBy, DateTime at, string? notes)
    {
        EnsureOpen();
        CashMoney.Validate(counted, allowZero: true, "El efectivo contado");
        Status = CashSessionStatus.Closed;
        ExpectedAmount = expected;
        CountedAmount = counted;
        Difference = counted - expected;
        ClosedBy = closedBy;
        ClosedAt = at;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void EnsureOpen()
    {
        if (!IsOpen)
            throw new DomainException("CAJA_CERRADA", "La sesión de caja ya está cerrada.");
    }
}

public enum CashMovementKind
{
    Opening,
    SaleCash,
    ChangeGiven,
    RefundCash,
    Income,
    Expense,
}

/// <summary>
/// Cash movement of a session: insert-only, amount always positive; the kind gives the sign.
/// Those derived from sales (phase 8) carry a source and are unique per (kind, source).
/// </summary>
public sealed class CashMovement : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid CashSessionId { get; private set; }
    public CashMovementKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public Guid? ReasonId { get; private set; }
    public string? SourceType { get; private set; }
    public Guid? SourceId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? AuthorizedBy { get; private set; }
    public string? Note { get; private set; }
    public DateTime OccurredAt { get; private set; }

    /// <summary>Signed amount for the expected total: it enters the drawer (+) or leaves it (−).</summary>
    public decimal SignedAmount => IsInflow(Kind) ? Amount : -Amount;

    private CashMovement() { }

    public static CashMovement Create(CashSession session, CashMovementKind kind, decimal amount, DateTime at, Guid? userId,
        Guid? reasonId = null, Guid? authorizedBy = null, string? note = null, string? sourceType = null, Guid? sourceId = null)
    {
        session.EnsureOpen();
        CashMoney.Validate(amount, allowZero: false, "El monto");
        if (kind is CashMovementKind.Income or CashMovementKind.Expense && reasonId is null)
            throw new DomainException("VALIDACION", "Los ingresos y retiros requieren un motivo.");
        return new CashMovement
        {
            CompanyId = session.CompanyId, CashSessionId = session.Id, Kind = kind, Amount = amount, OccurredAt = at,
            UserId = userId, ReasonId = reasonId, AuthorizedBy = authorizedBy, SourceType = sourceType, SourceId = sourceId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    public static bool IsInflow(CashMovementKind kind) =>
        kind is CashMovementKind.Opening or CashMovementKind.SaleCash or CashMovementKind.Income;

    /// <summary>
    /// Expected in the drawer = opening + net cash from sales (received − change) − cash returned
    /// + income − withdrawals (US-5.4) = signed sum of the movements.
    /// </summary>
    public static decimal Expected(IEnumerable<CashMovement> movements) => movements.Sum(m => m.SignedAmount);
}

public enum CashReasonKind
{
    Income,
    Expense,
}

/// <summary>Reason for a cash income or withdrawal.</summary>
public sealed class CashReason : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public CashReasonKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private CashReason() { }

    public static CashReason Create(Guid companyId, CashReasonKind kind, string name)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El motivo necesita empresa y nombre.");
        return new CashReason { CompanyId = companyId, Kind = kind, Name = name.Trim() };
    }
}

/// <summary>Reasons each company is born with (those of the previous system). Assumption to confirm (I-22).</summary>
public static class DefaultCashReasons
{
    public static readonly IReadOnlyList<(CashReasonKind Kind, string Name)> All =
    [
        (CashReasonKind.Income, "Cambio adicional"),
        (CashReasonKind.Income, "Aporte del dueño"),
        (CashReasonKind.Expense, "Pago a proveedor"),
        (CashReasonKind.Expense, "Retiro de seguridad"),
        (CashReasonKind.Expense, "Gastos menores"),
    ];
}

internal static class CashMoney
{
    public static void Validate(decimal amount, bool allowZero, string what)
    {
        if (amount < 0 || (!allowZero && amount == 0))
            throw new DomainException("VALIDACION", $"{what} debe ser {(allowZero ? "mayor o igual a" : "mayor que")} 0.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("VALIDACION", $"{what} admite máximo 2 decimales.");
    }
}
