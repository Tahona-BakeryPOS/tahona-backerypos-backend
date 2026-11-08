using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.AddColumn<decimal>(
                name: "amount_tendered",
                schema: "pay",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "change_amount",
                schema: "pay",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sale_counters",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_sale_counters", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_counters_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_identification = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    number = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    inventory_status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    fiscal_status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    stock_authorized_by = table.Column<Guid>(type: "uuid", nullable: true),
                    fiscal_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.UniqueConstraint("ak_sales_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_sales_fiscal_status", "fiscal_status IN ('not_required','pending','accepted','rejected','in_review')");
                    table.CheckConstraint("ck_sales_inventory_status", "inventory_status IN ('not_applied','applied','reversed')");
                    table.CheckConstraint("ck_sales_payment_status", "payment_status IN ('unpaid','partially_paid','paid','refunded','partially_refunded')");
                    table.CheckConstraint("ck_sales_status", "status IN ('draft','pending_payment','confirmed','cancelled','voided')");
                    table.ForeignKey(
                        name: "fk_sales_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_cash_sessions_company_id_cash_session_id",
                        columns: x => new { x.company_id, x.cash_session_id },
                        principalSchema: "cash",
                        principalTable: "cash_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_code = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_includes_tax = table.Column<bool>(type: "boolean", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    taxable_base = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("pk_sale_lines", x => x.id);
                    table.CheckConstraint("ck_sale_lines_discount", "discount_amount >= 0");
                    table.CheckConstraint("ck_sale_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sale_lines_sales_company_id_sale_id",
                        columns: x => new { x.company_id, x.sale_id },
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "sec",
                table: "permissions",
                columns: new[] { "id", "code", "module", "name" },
                values: new object[] { new Guid("176dd187-91e2-0438-76c7-329019e22430"), "ventas.descuento", "Ventas", "Aplicar descuentos en la venta" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_change",
                schema: "pay",
                table: "payments",
                sql: "change_amount IS NULL OR change_amount >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_sale_counters_company_id",
                schema: "sales",
                table: "sale_counters",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_company_id_sale_id",
                schema: "sales",
                table: "sale_lines",
                columns: new[] { "company_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_sale_id_line_no",
                schema: "sales",
                table: "sale_lines",
                columns: new[] { "sale_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_cash_session_id_status",
                schema: "sales",
                table: "sales",
                columns: new[] { "cash_session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_company_id_branch_id_created_at",
                schema: "sales",
                table: "sales",
                columns: new[] { "company_id", "branch_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_company_id_cash_session_id",
                schema: "sales",
                table: "sales",
                columns: new[] { "company_id", "cash_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_company_id_number",
                schema: "sales",
                table: "sales",
                columns: new[] { "company_id", "number" },
                unique: true,
                filter: "\"number\" IS NOT NULL");

            // El permiso nuevo ventas.descuento para los roles Propietario y Administrador de sucursal existentes.
            migrationBuilder.Sql("""
                INSERT INTO sec.role_permissions (role_id, permission_id)
                SELECT r.id, p.id
                FROM sec.roles r
                JOIN sec.permissions p ON p.code = 'ventas.descuento'
                WHERE r.code IN ('PROPIETARIO', 'ADMIN_SUCURSAL')
                  AND NOT EXISTS (SELECT 1 FROM sec.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM sec.role_permissions WHERE permission_id IN (SELECT id FROM sec.permissions WHERE code = 'ventas.descuento');");

            migrationBuilder.DropTable(
                name: "sale_counters",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sale_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_change",
                schema: "pay",
                table: "payments");

            migrationBuilder.DeleteData(
                schema: "sec",
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("176dd187-91e2-0438-76c7-329019e22430"));

            migrationBuilder.DropColumn(
                name: "amount_tendered",
                schema: "pay",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "change_amount",
                schema: "pay",
                table: "payments");
        }
    }
}
