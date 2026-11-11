using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Stored codes move from Spanish to English: the 20 permission codes (sec.permissions), the audit actions
    /// (aud.audit_logs.action) and the codes seeded for every company (roles, adjustment reasons, payment methods...). A permission id is derived from its code, so the code cannot just be updated in place
    /// without leaving the seed out of sync with the model. The migration therefore inserts the new rows, re-points
    /// every role assignment (sec.role_permissions) to them and deletes the old rows. Nothing is lost and it is reversible.
    /// The display name and module of each permission (Spanish, shown to users) are copied unchanged.
    /// </summary>
    public partial class EnglishCodes : Migration
    {
        private static readonly (string Old, string New)[] PermissionCodes =
        [
            ("plataforma.admin", "platform.admin"),
            ("organizacion.gestionar", "organization.manage"),
            ("reportes.ver", "reports.view"),
            ("productos.ver", "products.view"),
            ("catalogo.gestionar", "catalog.manage"),
            ("precios.sucursal", "prices.branch"),
            ("inventario.ver", "inventory.view"),
            ("inventario.ajustar", "inventory.adjust"),
            ("turnos.abrir", "shifts.open"),
            ("turnos.cerrar", "shifts.close"),
            ("caja.movimientos", "cash.movements"),
            ("caja.retiro", "cash.withdrawal"),
            ("ventas.crear", "sales.create"),
            ("ventas.ver", "sales.view"),
            ("ventas.anular", "sales.void"),
            ("ventas.descuento", "sales.discount"),
            ("devoluciones.crear", "returns.create"),
            ("devoluciones.aprobar", "returns.approve"),
            ("pagos.resolver", "payments.resolve"),
            ("fiscal.gestionar", "fiscal.manage"),
        ];

        /// <summary>
        /// Codes seeded per company (not derived into ids, so a plain UPDATE is enough): system roles, default adjustment
        /// reasons, default payment methods, the default stock location of each branch and one default tax.
        /// </summary>
        private static readonly (string Table, string Old, string New)[] CompanyCodes =
        [
            ("sec.roles", "PROPIETARIO", "OWNER"),
            ("sec.roles", "ADMIN_SUCURSAL", "BRANCH_MANAGER"),
            ("sec.roles", "CAJERO", "CASHIER"),
            ("inv.adjustment_reasons", "PRODUCCION", "PRODUCTION"),
            ("inv.adjustment_reasons", "COMPRA", "PURCHASE"),
            ("inv.adjustment_reasons", "MERMA", "WASTE"),
            ("inv.adjustment_reasons", "DANO", "DAMAGE"),
            ("inv.adjustment_reasons", "CONSUMO", "INTERNAL_USE"),
            ("inv.adjustment_reasons", "CONTEO", "PHYSICAL_COUNT"),
            ("inv.adjustment_reasons", "CORRECCION", "CORRECTION"),
            ("pay.payment_methods", "EFECTIVO", "CASH"),
            ("pay.payment_methods", "BOLD_TARJETA", "BOLD_CARD"),
            ("pay.payment_methods", "TRANSFERENCIA", "TRANSFER"),
            ("inv.stock_locations", "MOSTRADOR", "COUNTER"),
            ("cat.taxes", "EXCLUIDO", "EXCLUDED"),
        ];

        /// <summary>Same derivation the seed uses (PermissionConfiguration.StableId), repeated here so the migration stays self-contained.</summary>
        private static Guid IdOf(string code) => new(SHA256.HashData(Encoding.UTF8.GetBytes("permission:" + code)).AsSpan(0, 16));

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Move(migrationBuilder, toEnglish: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Move(migrationBuilder, toEnglish: false);

        private static void Move(MigrationBuilder migrationBuilder, bool toEnglish)
        {
            foreach (var (spanish, english) in PermissionCodes)
            {
                var from = toEnglish ? spanish : english;
                var to = toEnglish ? english : spanish;
                // 1) new row with the same name and module
                migrationBuilder.Sql(
                    $"INSERT INTO sec.permissions (id, code, name, module) SELECT '{IdOf(to)}', '{to}', name, module FROM sec.permissions WHERE code = '{from}';");
                // 2) every role that had the old permission now has the new one
                migrationBuilder.Sql(
                    $"UPDATE sec.role_permissions SET permission_id = '{IdOf(to)}' WHERE permission_id = '{IdOf(from)}';");
                // 3) the old row is no longer referenced
                migrationBuilder.Sql($"DELETE FROM sec.permissions WHERE id = '{IdOf(from)}';");
            }

            foreach (var (table, spanish, english) in CompanyCodes)
                migrationBuilder.Sql(toEnglish
                    ? $"UPDATE {table} SET code = '{english}' WHERE code = '{spanish}';"
                    : $"UPDATE {table} SET code = '{spanish}' WHERE code = '{english}';");

            if (toEnglish)
                migrationBuilder.Sql(
                    "UPDATE aud.audit_logs SET action = CASE action WHEN 'creacion' THEN 'create' WHEN 'modificacion' THEN 'update' WHEN 'eliminacion' THEN 'delete' END " +
                    "WHERE action IN ('creacion', 'modificacion', 'eliminacion');");
            else
                migrationBuilder.Sql(
                    "UPDATE aud.audit_logs SET action = CASE action WHEN 'create' THEN 'creacion' WHEN 'update' THEN 'modificacion' WHEN 'delete' THEN 'eliminacion' END " +
                    "WHERE action IN ('create', 'update', 'delete');");
        }
    }
}
