using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fis");

            migrationBuilder.CreateTable(
                name: "fiscal_documents",
                schema: "fis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(30)", unicode: false, maxLength: 30, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    customer_identification = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    prefix = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: true),
                    number = table.Column<long>(type: "bigint", nullable: true),
                    cufe = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    errors = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    retain_until = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_fiscal_documents", x => x.id);
                    table.UniqueConstraint("ak_fiscal_documents_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_fiscal_documents_kind", "kind IN ('invoice','credit_note')");
                    table.CheckConstraint("ck_fiscal_documents_status", "status IN ('pending','submitting','accepted','rejected','temporary_error','unknown','cancelled')");
                    table.CheckConstraint("ck_fiscal_documents_total", "total > 0");
                });

            migrationBuilder.CreateTable(
                name: "fiscal_mappings",
                schema: "fis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    local_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
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
                    table.PrimaryKey("pk_fiscal_mappings", x => x.id);
                    table.CheckConstraint("ck_fiscal_mappings_kind", "kind IN ('tax','payment_method','product')");
                });

            migrationBuilder.CreateTable(
                name: "fiscal_settings",
                schema: "fis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    encrypted_username = table.Column<string>(type: "text", nullable: false),
                    encrypted_access_key = table.Column<string>(type: "text", nullable: false),
                    invoice_document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    credit_note_document_type_id = table.Column<long>(type: "bigint", nullable: true),
                    seller_id = table.Column<long>(type: "bigint", nullable: true),
                    product_account_group_id = table.Column<long>(type: "bigint", nullable: true),
                    consumer_final_identification = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    retention_years = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_fiscal_settings", x => x.id);
                    table.CheckConstraint("ck_fiscal_settings_retention", "retention_years >= 1");
                    table.ForeignKey(
                        name: "fk_fiscal_settings_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_submission_attempts",
                schema: "fis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_fiscal_submission_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_submission_attempts_fiscal_documents_company_id_fisc~",
                        columns: x => new { x.company_id, x.fiscal_document_id },
                        principalSchema: "fis",
                        principalTable: "fiscal_documents",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_company_id_status",
                schema: "fis",
                table: "fiscal_documents",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_provider_external_id",
                schema: "fis",
                table: "fiscal_documents",
                columns: new[] { "provider", "external_id" },
                unique: true,
                filter: "\"external_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_provider_idempotency_key",
                schema: "fis",
                table: "fiscal_documents",
                columns: new[] { "provider", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_documents_invoice_per_sale",
                schema: "fis",
                table: "fiscal_documents",
                column: "sale_id",
                unique: true,
                filter: "\"kind\" = 'invoice'");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_mappings_company_id_kind_local_id",
                schema: "fis",
                table: "fiscal_mappings",
                columns: new[] { "company_id", "kind", "local_id" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_settings_company_id",
                schema: "fis",
                table: "fiscal_settings",
                column: "company_id",
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_submission_attempts_company_id_fiscal_document_id",
                schema: "fis",
                table: "fiscal_submission_attempts",
                columns: new[] { "company_id", "fiscal_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_submission_attempts_fiscal_document_id_number",
                schema: "fis",
                table: "fiscal_submission_attempts",
                columns: new[] { "fiscal_document_id", "number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_mappings",
                schema: "fis");

            migrationBuilder.DropTable(
                name: "fiscal_settings",
                schema: "fis");

            migrationBuilder.DropTable(
                name: "fiscal_submission_attempts",
                schema: "fis");

            migrationBuilder.DropTable(
                name: "fiscal_documents",
                schema: "fis");
        }
    }
}
