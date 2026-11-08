using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Returns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "refunded_total",
                schema: "sales",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "sales",
                table: "sales",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "voided_at",
                schema: "sales",
                table: "sales",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by",
                schema: "sales",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                schema: "pay",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "credit_reason",
                schema: "fis",
                table: "fiscal_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_document_id",
                schema: "fis",
                table: "fiscal_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                schema: "fis",
                table: "fiscal_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
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
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount", "amount > 0");
                    table.CheckConstraint("ck_refunds_method", "method IN ('cash','gateway','manual')");
                    table.CheckConstraint("ck_refunds_source", "source_type IN ('void','return')");
                    table.CheckConstraint("ck_refunds_status", "status IN ('pending','completed','failed')");
                    table.ForeignKey(
                        name: "fk_refunds_payments_company_id_payment_id",
                        columns: x => new { x.company_id, x.payment_id },
                        principalSchema: "pay",
                        principalTable: "payments",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "returns",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fiscal_document_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_returns", x => x.id);
                    table.UniqueConstraint("ak_returns_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_returns_total", "total > 0");
                    table.ForeignKey(
                        name: "fk_returns_cash_sessions_company_id_cash_session_id",
                        columns: x => new { x.company_id, x.cash_session_id },
                        principalSchema: "cash",
                        principalTable: "cash_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_returns_sales_company_id_sale_id",
                        columns: x => new { x.company_id, x.sale_id },
                        principalSchema: "sales",
                        principalTable: "sales",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    restock = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_return_lines", x => x.id);
                    table.CheckConstraint("ck_return_lines_amount", "amount >= 0");
                    table.CheckConstraint("ck_return_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_return_lines_returns_company_id_sale_return_id",
                        columns: x => new { x.company_id, x.sale_return_id },
                        principalSchema: "sales",
                        principalTable: "returns",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_lines_sale_lines_sale_line_id",
                        column: x => x.sale_line_id,
                        principalSchema: "sales",
                        principalTable: "sale_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refunded",
                schema: "pay",
                table: "payments",
                sql: "refunded_amount >= 0 AND refunded_amount <= amount");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_invoice_document_id",
                schema: "fis",
                table: "fiscal_documents",
                column: "invoice_document_id");

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_credit_note_per_source",
                schema: "fis",
                table: "fiscal_documents",
                column: "source_id",
                unique: true,
                filter: "\"kind\" = 'credit_note'");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_company_id_payment_id",
                schema: "pay",
                table: "refunds",
                columns: new[] { "company_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_company_id_sale_id",
                schema: "pay",
                table: "refunds",
                columns: new[] { "company_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id_source_type_source_id",
                schema: "pay",
                table: "refunds",
                columns: new[] { "payment_id", "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_company_id_sale_return_id",
                schema: "sales",
                table: "return_lines",
                columns: new[] { "company_id", "sale_return_id" });

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_sale_line_id",
                schema: "sales",
                table: "return_lines",
                column: "sale_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_sale_return_id_sale_line_id",
                schema: "sales",
                table: "return_lines",
                columns: new[] { "sale_return_id", "sale_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_returns_company_id_branch_id_occurred_at",
                schema: "sales",
                table: "returns",
                columns: new[] { "company_id", "branch_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_company_id_cash_session_id",
                schema: "sales",
                table: "returns",
                columns: new[] { "company_id", "cash_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_company_id_sale_id",
                schema: "sales",
                table: "returns",
                columns: new[] { "company_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_returns_sale_id",
                schema: "sales",
                table: "returns",
                column: "sale_id");

            migrationBuilder.AddForeignKey(
                name: "fk_fiscal_documents_fiscal_documents_invoice_document_id",
                schema: "fis",
                table: "fiscal_documents",
                column: "invoice_document_id",
                principalSchema: "fis",
                principalTable: "fiscal_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_fiscal_documents_fiscal_documents_invoice_document_id",
                schema: "fis",
                table: "fiscal_documents");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "return_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "returns",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refunded",
                schema: "pay",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_fiscal_documents_invoice_document_id",
                schema: "fis",
                table: "fiscal_documents");

            migrationBuilder.DropIndex(
                name: "ux_fiscal_documents_credit_note_per_source",
                schema: "fis",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "refunded_total",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "voided_at",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "voided_by",
                schema: "sales",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                schema: "pay",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "credit_reason",
                schema: "fis",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "invoice_document_id",
                schema: "fis",
                table: "fiscal_documents");

            migrationBuilder.DropColumn(
                name: "source_id",
                schema: "fis",
                table: "fiscal_documents");
        }
    }
}
