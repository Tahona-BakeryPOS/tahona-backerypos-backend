using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: role_permission / rol_permiso (join table)
/// </summary>
public class RolePermission : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
