using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_reasons",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
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
                    table.PrimaryKey("pk_cash_reasons", x => x.id);
                    table.CheckConstraint("ck_cash_reasons_kind", "kind IN ('income','expense')");
                    table.ForeignKey(
                        name: "fk_cash_reasons_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    opening_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    expected_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    counted_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_cash_sessions", x => x.id);
                    table.UniqueConstraint("ak_cash_sessions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_cash_sessions_opening", "opening_amount >= 0");
                    table.CheckConstraint("ck_cash_sessions_status", "status IN ('open','closing','closed')");
                    table.ForeignKey(
                        name: "fk_cash_sessions_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_sessions_cash_registers_company_id_cash_register_id",
                        columns: x => new { x.company_id, x.cash_register_id },
                        principalSchema: "cash",
                        principalTable: "cash_registers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorized_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
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
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                    table.CheckConstraint("ck_cash_movements_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_movements_kind", "kind IN ('opening','sale_cash','change_given','refund_cash','income','expense')");
                    table.ForeignKey(
                        name: "fk_cash_movements_cash_reasons_reason_id",
                        column: x => x.reason_id,
                        principalSchema: "cash",
                        principalTable: "cash_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_movements_cash_sessions_company_id_cash_session_id",
                        columns: x => new { x.company_id, x.cash_session_id },
                        principalSchema: "cash",
                        principalTable: "cash_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_cash_session_id_occurred_at",
                schema: "cash",
                table: "cash_movements",
                columns: new[] { "cash_session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_company_id_cash_session_id",
                schema: "cash",
                table: "cash_movements",
                columns: new[] { "company_id", "cash_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_reason_id",
                schema: "cash",
                table: "cash_movements",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "ux_cash_movements_source",
                schema: "cash",
                table: "cash_movements",
                columns: new[] { "company_id", "kind", "source_type", "source_id" },
                unique: true,
                filter: "\"source_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_reasons_company_id_kind_name",
                schema: "cash",
                table: "cash_reasons",
                columns: new[] { "company_id", "kind", "name" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_company_id_branch_id_opened_at",
                schema: "cash",
                table: "cash_sessions",
                columns: new[] { "company_id", "branch_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_company_id_cash_register_id",
                schema: "cash",
                table: "cash_sessions",
                columns: new[] { "company_id", "cash_register_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_open_per_register",
                schema: "cash",
                table: "cash_sessions",
                column: "cash_register_id",
                unique: true,
                filter: "\"status\" <> 'closed'");

            // Empresas creadas antes de la fase 5: motivos de caja por defecto (las nuevas los reciben al
            // crearse, ver CashProvisioning).
            migrationBuilder.Sql("""
                INSERT INTO cash.cash_reasons (id, company_id, kind, name, is_active, created_at, created_by, row_version)
                SELECT gen_random_uuid(), c.id, r.kind, r.name, TRUE, now(), 'migration', decode(md5(random()::text), 'hex')
                FROM org.companies c
                CROSS JOIN (VALUES
                    ('income', 'Cambio adicional'),
                    ('income', 'Aporte del dueño'),
                    ('expense', 'Pago a proveedor'),
                    ('expense', 'Retiro de seguridad'),
                    ('expense', 'Gastos menores')
                ) AS r(kind, name)
                WHERE c.deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM cash.cash_reasons x WHERE x.company_id = c.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "cash_reasons",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "cash_sessions",
                schema: "cash");
        }
    }
}
