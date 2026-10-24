using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.ValueObjects;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: app_user / usuario
/// PIN-based login for cashiers.
/// </summary>
public class AppUser : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PinHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation
    public Organization Organization { get; set; } = null!;
    public ICollection<UserAssignment> UserAssignments { get; set; } = [];

    public void SetPin(string plainPin)
    {
        var pin = Pin.Create(plainPin);
        PinHash = pin.HashedValue;
    }

    public bool VerifyPin(string plainPin)
    {
        var pin = Pin.FromHash(PinHash);
        return pin.Verify(plainPin);
    }
}
