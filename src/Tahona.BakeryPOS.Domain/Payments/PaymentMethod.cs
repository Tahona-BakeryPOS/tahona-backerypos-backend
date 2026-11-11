using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Payments;

public enum PaymentMethodKind
{
    /// <summary>Cash: it goes into the drawer (cash movements), with change.</summary>
    Cash,
    /// <summary>Charged through the gateway (Bold card terminal): only approved by its webhook or audited by hand (I-23).</summary>
    Gateway,
    /// <summary>Card terminal of another provider, not integrated: mandatory reference and manual reconciliation.</summary>
    ExternalCard,
    /// <summary>Transferencia verificada a mano: referencia obligatoria.</summary>
    TransferManual,
}

/// <summary>Values of <c>payment_method</c> in Bold's card-terminal API (payment-integration.md §11).</summary>
public static class BoldMethods
{
    public const string Card = "POS";
    public const string Nequi = "NEQUI";
    public const string Qr = "PAY_BY_QR_BOLD";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Card, Nequi, Qr };
}

/// <summary>The company's payment method (Cash, Bold card, Nequi, QR, Transfer…).</summary>
public sealed class PaymentMethod : AuditableEntity, ITenantEntity
{
    public const string CashCode = "CASH";

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PaymentMethodKind Kind { get; private set; }
    /// <summary>Only with <see cref="PaymentMethodKind.Gateway"/>: the gateway method (<see cref="BoldMethods"/>).</summary>
    public string? GatewayMethod { get; private set; }
    public bool RequiresReference { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private PaymentMethod() { }

    public static PaymentMethod Create(Guid companyId, string code, string name, PaymentMethodKind kind, string? gatewayMethod,
        bool requiresReference, int displayOrder)
    {
        if (companyId == Guid.Empty || string.IsNullOrWhiteSpace(code))
            throw new DomainException("VALIDACION", "El medio de pago necesita empresa y código.");
        var method = new PaymentMethod { CompanyId = companyId, Code = code.Trim().ToUpperInvariant(), Kind = kind };
        method.SetGatewayMethod(gatewayMethod);
        method.Update(name, requiresReference, displayOrder, isActive: true);
        return method;
    }

    public void Update(string name, bool requiresReference, int displayOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El nombre del medio de pago es obligatorio.");
        if (Kind == PaymentMethodKind.Cash && !isActive)
            throw new DomainException("VALIDACION", "El efectivo no se puede desactivar: la caja lo necesita.");
        Name = name.Trim();
        // External card terminal and transfer always carry a reference; cash and gateway do not ask for one.
        RequiresReference = Kind is PaymentMethodKind.ExternalCard or PaymentMethodKind.TransferManual || requiresReference && Kind != PaymentMethodKind.Cash;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    private void SetGatewayMethod(string? gatewayMethod)
    {
        if (Kind == PaymentMethodKind.Gateway)
        {
            if (gatewayMethod is null || !BoldMethods.All.Contains(gatewayMethod))
                throw new DomainException("VALIDACION", $"Método de la pasarela: {string.Join(", ", BoldMethods.All)}.");
            GatewayMethod = gatewayMethod;
        }
        else if (gatewayMethod is not null)
            throw new DomainException("VALIDACION", "Solo los medios de la pasarela llevan método de pasarela.");
    }
}

/// <summary>Methods each company is born with (I-23). The Bold ones stay inactive until the integration is configured.</summary>
public static class DefaultPaymentMethods
{
    public static readonly IReadOnlyList<(string Code, string Name, PaymentMethodKind Kind, string? GatewayMethod, bool Active)> All =
    [
        (PaymentMethod.CashCode, "Efectivo", PaymentMethodKind.Cash, null, true),
        ("BOLD_CARD", "Tarjeta (datáfono)", PaymentMethodKind.Gateway, BoldMethods.Card, false),
        ("BOLD_NEQUI", "Nequi", PaymentMethodKind.Gateway, BoldMethods.Nequi, false),
        ("BOLD_QR", "QR", PaymentMethodKind.Gateway, BoldMethods.Qr, false),
        ("TRANSFER", "Transferencia", PaymentMethodKind.TransferManual, null, true),
    ];
}
