using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: organization / organizacion
/// Tenant root. Row-level isolation via organization_id.
/// </summary>
public class Organization : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Nit { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Branch> Branches { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
    public ICollection<Role> Roles { get; set; } = [];
}
