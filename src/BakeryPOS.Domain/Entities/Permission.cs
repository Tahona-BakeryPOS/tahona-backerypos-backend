using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: permission / permiso
/// System-wide, not per-organization.
/// </summary>
public class Permission : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Module { get; set; }

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
