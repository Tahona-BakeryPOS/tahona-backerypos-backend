using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Payments;

public enum GatewayEnvironment
{
    Sandbox,
    Production,
}

/// <summary>
/// Gateway credentials per company and environment. The keys are stored encrypted (infrastructure
/// encrypts them); the domain never sees the clear value. Only one active environment per company.
/// </summary>
public sealed class GatewayAccount : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public string Provider { get; private set; } = PaymentAttempt.Bold;
    public GatewayEnvironment Environment { get; private set; }
    /// <summary>Identity key (<c>Authorization: x-api-key</c>), encrypted.</summary>
    public string EncryptedIdentityKey { get; private set; } = string.Empty;
    /// <summary>Secret key (webhook signature), encrypted; null = empty (in tests Bold signs with an empty key).</summary>
    public string? EncryptedSecretKey { get; private set; }
    /// <summary>The seller's email at Bold (the charge's <c>user_email</c>).</summary>
    public string SellerEmail { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    private GatewayAccount() { }

    public static GatewayAccount Create(Guid companyId, GatewayEnvironment environment) =>
        new() { CompanyId = companyId, Environment = environment };

    public void SetCredentials(string encryptedIdentityKey, string? encryptedSecretKey, string sellerEmail)
    {
        if (string.IsNullOrWhiteSpace(encryptedIdentityKey))
            throw new DomainException("VALIDACION", "La llave de identidad es obligatoria.");
        if (Environment == GatewayEnvironment.Production && string.IsNullOrWhiteSpace(encryptedSecretKey))
            throw new DomainException("VALIDACION", "En producción la llave secreta es obligatoria: sin ella no se puede validar el webhook.");
        if (string.IsNullOrWhiteSpace(sellerEmail))
            throw new DomainException("VALIDACION", "El correo del vendedor en Bold es obligatorio.");
        EncryptedIdentityKey = encryptedIdentityKey;
        EncryptedSecretKey = encryptedSecretKey;
        SellerEmail = sellerEmail.Trim().ToLowerInvariant();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

/// <summary>Bold card terminal assigned to a register: that register's charges are sent to it.</summary>
public sealed class RegisterTerminal : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public string Provider { get; private set; } = PaymentAttempt.Bold;
    public string TerminalModel { get; private set; } = string.Empty;
    public string TerminalSerial { get; private set; } = string.Empty;

    private RegisterTerminal() { }

    public static RegisterTerminal Create(Guid companyId, Guid cashRegisterId, string model, string serial)
    {
        var terminal = new RegisterTerminal { CompanyId = companyId, CashRegisterId = cashRegisterId };
        terminal.Set(model, serial);
        return terminal;
    }

    public void Set(string model, string serial)
    {
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(serial))
            throw new DomainException("VALIDACION", "Modelo y serial del datáfono son obligatorios.");
        TerminalModel = model.Trim();
        TerminalSerial = serial.Trim();
    }
}
