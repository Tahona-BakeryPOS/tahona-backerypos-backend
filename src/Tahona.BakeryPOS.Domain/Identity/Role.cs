using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Identity;

/// <summary>A company's role: a set of permissions. System roles are created at onboarding.</summary>
public sealed class Role : AuditableEntity, ITenantEntity
{
    private readonly List<RolePermission> _permissions = [];

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    private Role() { }

    public static Role Create(Guid companyId, string code, string name, bool isSystem, IEnumerable<Guid> permissionIds)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDACION", "El rol debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El código y el nombre del rol son obligatorios.");
        var role = new Role { CompanyId = companyId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), IsSystem = isSystem };
        role._permissions.AddRange(permissionIds.Distinct().Select(p => new RolePermission(role.Id, p)));
        return role;
    }
}

/// <summary>Permission granted to a role (junction table role_permissions).</summary>
public sealed class RolePermission
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    internal RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}

/// <summary>Global permission (catalog seeded by migration). It travels in the token as the "permission" claim.</summary>
public sealed class Permission
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
}

/// <summary>Codes of the roles created when a company is onboarded.</summary>
public static class SystemRoles
{
    public const string Owner = "OWNER";
    public const string BranchManager = "BRANCH_MANAGER";
    public const string Cashier = "CASHIER";
}
