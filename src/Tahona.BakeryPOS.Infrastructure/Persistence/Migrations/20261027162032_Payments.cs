using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pay");

            migrationBuilder.CreateTable(
                name: "gateway_accounts",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    environment = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    encrypted_identity_key = table.Column<string>(type: "text", nullable: false),
                    encrypted_secret_key = table.Column<string>(type: "text", nullable: true),
                    seller_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_accounts", x => x.id);
                    table.CheckConstraint("ck_gateway_accounts_environment", "environment IN ('sandbox','production')");
                    table.ForeignKey(
                        name: "fk_gateway_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    gateway_method = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: true),
                    requires_reference = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                    table.UniqueConstraint("ak_payment_methods_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_payment_methods_kind", "kind IN ('cash','gateway','external_card','transfer_manual')");
                    table.ForeignKey(
                        name: "fk_payment_methods_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "register_terminals",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    terminal_model = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    terminal_serial = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_register_terminals", x => x.id);
                    table.ForeignKey(
                        name: "fk_register_terminals_cash_registers_company_id_cash_register_~",
                        columns: x => new { x.company_id, x.cash_register_id },
                        principalSchema: "cash",
                        principalTable: "cash_registers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_manually = table.Column<bool>(type: "boolean", nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_purpose", "purpose IN ('sale','test')");
                    table.CheckConstraint("ck_payments_status", "status IN ('pending','approved','declined','cancelled','refunded','partially_refunded')");
                    table.ForeignKey(
                        name: "fk_payments_cash_sessions_company_id_cash_session_id",
                        columns: x => new { x.company_id, x.cash_session_id },
                        principalSchema: "cash",
                        principalTable: "cash_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_payment_methods_company_id_payment_method_id",
                        columns: x => new { x.company_id, x.payment_method_id },
                        principalSchema: "pay",
                        principalTable: "payment_methods",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    environment = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    integration_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    raw_status = table.Column<string>(type: "character varying(40)", unicode: false, maxLength: 40, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gateway_method = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    terminal_serial = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    error_code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    error_message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_event_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempts_status", "status IN ('created','pending','approved','declined','voided','error','unknown','expired')");
                    table.ForeignKey(
                        name: "fk_payment_attempts_payments_company_id_payment_id",
                        columns: x => new { x.company_id, x.payment_id },
                        principalSchema: "pay",
                        principalTable: "payments",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "sec",
                table: "permissions",
                columns: new[] { "id", "code", "module", "name" },
                values: new object[] { new Guid("85e6eb3e-d26f-959f-2afe-49aef7c38286"), "pagos.resolver", "Pagos", "Resolver a mano pagos sin respuesta de la pasarela" });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_accounts_company_id_provider_environment",
                schema: "pay",
                table: "gateway_accounts",
                columns: new[] { "company_id", "provider", "environment" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_gateway_accounts_one_active",
                schema: "pay",
                table: "gateway_accounts",
                columns: new[] { "company_id", "provider" },
                unique: true,
                filter: "\"is_active\" AND \"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_company_id_payment_id",
                schema: "pay",
                table: "payment_attempts",
                columns: new[] { "company_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_provider_external_id",
                schema: "pay",
                table: "payment_attempts",
                columns: new[] { "provider", "external_id" },
                unique: true,
                filter: "\"external_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_provider_reference",
                schema: "pay",
                table: "payment_attempts",
                columns: new[] { "provider", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_open_per_payment",
                schema: "pay",
                table: "payment_attempts",
                column: "payment_id",
                unique: true,
                filter: "\"status\" IN ('created','pending','unknown')");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_company_id_code",
                schema: "pay",
                table: "payment_methods",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_cash_session_id_status",
                schema: "pay",
                table: "payments",
                columns: new[] { "cash_session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_company_id_cash_session_id",
                schema: "pay",
                table: "payments",
                columns: new[] { "company_id", "cash_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_company_id_payment_method_id",
                schema: "pay",
                table: "payments",
                columns: new[] { "company_id", "payment_method_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_company_id_sale_id",
                schema: "pay",
                table: "payments",
                columns: new[] { "company_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_register_terminals_cash_register_id_provider",
                schema: "pay",
                table: "register_terminals",
                columns: new[] { "cash_register_id", "provider" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_register_terminals_company_id_cash_register_id",
                schema: "pay",
                table: "register_terminals",
                columns: new[] { "company_id", "cash_register_id" });

            // Empresas creadas antes de la fase 6: medios de pago por defecto (los de Bold inactivos hasta
            // configurar la integración) y el permiso pagos.resolver para Propietario y Administrador de sucursal.
            migrationBuilder.Sql("""
                INSERT INTO pay.payment_methods (id, company_id, code, name, kind, gateway_method, requires_reference, display_order, is_active, created_at, created_by, row_version)
                SELECT gen_random_uuid(), c.id, m.code, m.name, m.kind, m.gateway_method, m.requires_reference, m.display_order, m.is_active, now(), 'migration', decode(md5(random()::text), 'hex')
                FROM org.companies c
                CROSS JOIN (VALUES
                    ('EFECTIVO', 'Efectivo', 'cash', NULL, FALSE, 0, TRUE),
                    ('BOLD_TARJETA', 'Tarjeta (datáfono)', 'gateway', 'POS', FALSE, 1, FALSE),
                    ('BOLD_NEQUI', 'Nequi', 'gateway', 'NEQUI', FALSE, 2, FALSE),
                    ('BOLD_QR', 'QR', 'gateway', 'PAY_BY_QR_BOLD', FALSE, 3, FALSE),
                    ('TRANSFERENCIA', 'Transferencia', 'transfer_manual', NULL, TRUE, 4, TRUE)
                ) AS m(code, name, kind, gateway_method, requires_reference, display_order, is_active)
                WHERE c.deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM pay.payment_methods x WHERE x.company_id = c.id);

                INSERT INTO sec.role_permissions (role_id, permission_id)
                SELECT r.id, p.id
                FROM sec.roles r
                JOIN sec.permissions p ON p.code = 'pagos.resolver'
                WHERE r.code IN ('PROPIETARIO', 'ADMIN_SUCURSAL')
                  AND NOT EXISTS (SELECT 1 FROM sec.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM sec.role_permissions WHERE permission_id IN (SELECT id FROM sec.permissions WHERE code = 'pagos.resolver');");

            migrationBuilder.DropTable(
                name: "gateway_accounts",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "register_terminals",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "pay");

            migrationBuilder.DeleteData(
                schema: "sec",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("85e6eb3e-d26f-959f-2afe-49aef7c38286"));
        }
    }
}
