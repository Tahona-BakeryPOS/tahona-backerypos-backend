using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inv");

            migrationBuilder.AddColumn<string>(
                name: "negative_stock_policy",
                schema: "cat",
                table: "products",
                type: "character varying(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "allow");

            migrationBuilder.CreateTable(
                name: "adjustment_reasons",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
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
                    table.PrimaryKey("pk_adjustment_reasons", x => x.id);
                    table.UniqueConstraint("ak_adjustment_reasons_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_adjustment_reasons_direction", "direction IN ('in','out','both')");
                    table.ForeignKey(
                        name: "fk_adjustment_reasons_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_locations",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_stock_locations", x => x.id);
                    table.UniqueConstraint("ak_stock_locations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_stock_locations_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_hand = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    last_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_stock_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_balances_products_company_id_product_id",
                        columns: x => new { x.company_id, x.product_id },
                        principalSchema: "cat",
                        principalTable: "products",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_stock_locations_company_id_location_id",
                        columns: x => new { x.company_id, x.location_id },
                        principalSchema: "inv",
                        principalTable: "stock_locations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_kind", "kind IN ('initial','sale','return','void_reversal','adjustment_in','adjustment_out')");
                    table.CheckConstraint("ck_stock_movements_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_adjustment_reasons_reason_id",
                        column: x => x.reason_id,
                        principalSchema: "inv",
                        principalTable: "adjustment_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_company_id_product_id",
                        columns: x => new { x.company_id, x.product_id },
                        principalSchema: "cat",
                        principalTable: "products",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_stock_locations_company_id_location_id",
                        columns: x => new { x.company_id, x.location_id },
                        principalSchema: "inv",
                        principalTable: "stock_locations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_negative_stock_policy",
                schema: "cat",
                table: "products",
                sql: "negative_stock_policy IN ('allow','block')");

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_reasons_company_id_code",
                schema: "inv",
                table: "adjustment_reasons",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_company_id_location_id",
                schema: "inv",
                table: "stock_balances",
                columns: new[] { "company_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_company_id_product_id",
                schema: "inv",
                table: "stock_balances",
                columns: new[] { "company_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_location_id_product_id",
                schema: "inv",
                table: "stock_balances",
                columns: new[] { "location_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_branch_id_code",
                schema: "inv",
                table: "stock_locations",
                columns: new[] { "branch_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_company_id_branch_id",
                schema: "inv",
                table: "stock_locations",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_locations_default_per_branch",
                schema: "inv",
                table: "stock_locations",
                column: "branch_id",
                unique: true,
                filter: "\"is_default\" AND \"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_location_id_product_id_occurred_~",
                schema: "inv",
                table: "stock_movements",
                columns: new[] { "company_id", "location_id", "product_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_product_id",
                schema: "inv",
                table: "stock_movements",
                columns: new[] { "company_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reason_id",
                schema: "inv",
                table: "stock_movements",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_source",
                schema: "inv",
                table: "stock_movements",
                columns: new[] { "company_id", "kind", "source_type", "source_id", "source_line_id" },
                unique: true);

            // Empresas y sucursales creadas antes de la fase 4: motivos de ajuste y ubicación "Mostrador"
            // (las nuevas los reciben al crearse, ver InventoryProvisioning).
            migrationBuilder.Sql("""
                INSERT INTO inv.stock_locations (id, company_id, branch_id, code, name, is_default, is_active, created_at, created_by, row_version)
                SELECT gen_random_uuid(), b.company_id, b.id, 'MOSTRADOR', 'Mostrador', TRUE, TRUE, now(), 'migration', decode(md5(random()::text), 'hex')
                FROM org.branches b
                WHERE b.deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM inv.stock_locations l WHERE l.branch_id = b.id);

                INSERT INTO inv.adjustment_reasons (id, company_id, code, name, direction, is_active, created_at, created_by, row_version)
                SELECT gen_random_uuid(), c.id, r.code, r.name, r.direction, TRUE, now(), 'migration', decode(md5(random()::text), 'hex')
                FROM org.companies c
                CROSS JOIN (VALUES
                    ('PRODUCCION', 'Producción del día', 'in'),
                    ('COMPRA', 'Compra o recepción', 'in'),
                    ('MERMA', 'Merma (no vendido)', 'out'),
                    ('DANO', 'Daño o vencimiento', 'out'),
                    ('CONSUMO', 'Consumo interno', 'out'),
                    ('CONTEO', 'Conteo físico', 'both'),
                    ('CORRECCION', 'Corrección', 'both')
                ) AS r(code, name, direction)
                WHERE c.deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM inv.adjustment_reasons x WHERE x.company_id = c.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "adjustment_reasons",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "stock_locations",
                schema: "inv");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_negative_stock_policy",
                schema: "cat",
                table: "products");

            migrationBuilder.DropColumn(
                name: "negative_stock_policy",
                schema: "cat",
                table: "products");
        }
    }
}
