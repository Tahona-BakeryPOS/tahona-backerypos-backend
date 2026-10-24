using BakeryPOS.Domain.Common;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: user_assignment / asignacion_usuario
/// Links a user to a role at a specific branch.
/// </summary>
public class UserAssignment : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid BranchId { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public AppUser User { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
