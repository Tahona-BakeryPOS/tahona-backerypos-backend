using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>
/// Equipo vinculado a una caja. Se autentica con un token opaco que solo conoce el equipo; aquí se
/// guarda su hash. Desactivarlo revoca el token.
/// </summary>
public sealed class Device : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTime? LastSeenAt { get; private set; }

    private Device() { }

    public static Device Create(Guid companyId, Guid cashRegisterId, string name, string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El nombre del dispositivo es obligatorio.");
        return new Device { CompanyId = companyId, CashRegisterId = cashRegisterId, Name = name.Trim(), TokenHash = tokenHash };
    }

    public void Deactivate() => IsActive = false;

    public void Touch(DateTime now) => LastSeenAt = now;
}

/// <summary>Código de un solo uso, de vida corta, para vincular un equipo a una caja.</summary>
public sealed class DevicePairingCode : AuditableEntity, ITenantEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Guid CompanyId { get; private set; }
    public Guid CashRegisterId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public Guid? DeviceId { get; private set; }

    private DevicePairingCode() { }

    public static DevicePairingCode Create(Guid companyId, Guid cashRegisterId, string codeHash, DateTime now) =>
        new() { CompanyId = companyId, CashRegisterId = cashRegisterId, CodeHash = codeHash, ExpiresAt = now + Lifetime };

    public bool IsUsable(DateTime now) => ConsumedAt is null && ExpiresAt > now;

    public void Consume(Guid deviceId, DateTime now)
    {
        if (!IsUsable(now))
            throw new DomainException("CODIGO_VINCULACION_INVALIDO", "El código de vinculación ya se usó o venció.");
        ConsumedAt = now;
        DeviceId = deviceId;
    }
}
