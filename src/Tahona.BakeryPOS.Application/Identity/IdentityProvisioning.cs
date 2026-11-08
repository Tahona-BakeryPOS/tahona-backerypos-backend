using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity;

/// <summary>
/// Roles que se crean con cada empresa. Matriz supuesta (decisión I-10): el propietario recibe todo; el
/// cajero, lo de la v1; el administrador de sucursal, la operación de su sucursal. Confirmar con el dueño.
/// </summary>
public static class DefaultRoles
{
    public static readonly IReadOnlyList<(string Code, string Name, IReadOnlyList<string> Permissions)> All =
    [
        (SystemRoles.Owner, "Propietario",
            Permissions.All.Select(p => p.Code).Where(c => c != Permissions.PlataformaAdmin).ToList()),
        (SystemRoles.BranchManager, "Administrador de sucursal",
        [
            Permissions.ProductosVer, Permissions.PreciosSucursal, Permissions.InventarioVer, Permissions.InventarioAjustar,
            Permissions.TurnosAbrir, Permissions.TurnosCerrar, Permissions.CajaMovimientos, Permissions.CajaRetiro,
            Permissions.VentasCrear, Permissions.VentasVer, Permissions.VentasAnular, Permissions.VentasDescuento,
            Permissions.DevolucionesCrear, Permissions.DevolucionesAprobar, Permissions.ReportesVer, Permissions.PagosResolver,
        ]),
        (SystemRoles.Cashier, "Cajero",
        [
            Permissions.ProductosVer, Permissions.VentasCrear, Permissions.VentasVer,
            Permissions.TurnosAbrir, Permissions.TurnosCerrar, Permissions.CajaMovimientos,
        ]),
    ];
}

public sealed class IdentityProvisioning : IIdentityProvisioning
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _clock;

    public IdentityProvisioning(IAppDbContext db, IPasswordHasher hasher, TimeProvider clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<ProvisionedOwner> ProvisionCompanyAsync(Guid companyId, string ownerFullName, string ownerEmail, CancellationToken ct)
    {
        var permissionIds = await _db.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        if (permissionIds.Count == 0)
            throw new InvalidOperationException("No hay permisos en la base: aplica las migraciones antes de crear empresas.");

        Role? ownerRole = null;
        foreach (var (code, name, permissions) in DefaultRoles.All)
        {
            var role = Role.Create(companyId, code, name, isSystem: true, permissions.Select(p => permissionIds[p]));
            _db.Roles.Add(role);
            if (code == SystemRoles.Owner) ownerRole = role;
        }

        var email = User.NormalizeEmail(ownerEmail)!;
        string? temporaryPassword = null;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            temporaryPassword = SecretTokens.NewTemporaryPassword();
            user = User.Create(ownerFullName, email, _hasher.Hash(temporaryPassword));
            _db.Users.Add(user);
        }

        var membership = Membership.Create(companyId, user.Id, pinHash: null);
        _db.Memberships.Add(membership);
        _db.RoleAssignments.Add(RoleAssignment.Create(companyId, membership.Id, ownerRole!.Id, branchId: null,
            _clock.GetUtcNow().UtcDateTime, validTo: null));

        return new ProvisionedOwner(user.Id, email, temporaryPassword);
    }
}
