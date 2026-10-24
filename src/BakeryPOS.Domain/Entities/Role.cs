using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: role / rol
/// </summary>
public class Role : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Organization Organization { get; set; } = null!;
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<UserAssignment> UserAssignments { get; set; } = [];
}
