using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogAndPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cat");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
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
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.UniqueConstraint("ak_categories_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_categories_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxes",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_taxes", x => x.id);
                    table.UniqueConstraint("ak_taxes_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("ck_taxes_kind", "kind IN ('iva','inc','none')");
                    table.CheckConstraint("ck_taxes_rate", "rate >= 0 AND rate <= 100");
                    table.ForeignKey(
                        name: "fk_taxes_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    dian_code = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.CheckConstraint("ck_units_kind", "kind IN ('count','mass','volume')");
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    track_stock = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_products", x => x.id);
                    table.UniqueConstraint("ak_products_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_products_categories_company_id_category_id",
                        columns: x => new { x.company_id, x.category_id },
                        principalSchema: "cat",
                        principalTable: "categories",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_taxes_company_id_tax_id",
                        columns: x => new { x.company_id, x.tax_id },
                        principalSchema: "cat",
                        principalTable: "taxes",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "cat",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_products",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_branch_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_branch_products_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branch_products_products_company_id_product_id",
                        columns: x => new { x.company_id, x.product_id },
                        principalSchema: "cat",
                        principalTable: "products",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_prices",
                schema: "cat",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    includes_tax = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_product_prices", x => x.id);
                    table.CheckConstraint("ck_product_prices_amount", "amount >= 0");
                    table.CheckConstraint("ck_product_prices_validity", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "fk_product_prices_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_prices_products_company_id_product_id",
                        columns: x => new { x.company_id, x.product_id },
                        principalSchema: "cat",
                        principalTable: "products",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "cat",
                table: "units",
                columns: new[] { "id", "code", "dian_code", "kind", "name" },
                values: new object[,]
                {
                    { new Guid("4ea097d5-64f3-6b88-df2c-de9f32609cac"), "ML", "MLT", "volume", "Mililitro" },
                    { new Guid("7af4081e-3bab-d28f-1051-6a20d05d40ca"), "KG", "KGM", "mass", "Kilogramo" },
                    { new Guid("99aef15e-0fc1-bd38-b4b8-37923b530b86"), "L", "LTR", "volume", "Litro" },
                    { new Guid("b81c33ff-20f7-5fcd-ab70-4c3c53dc9272"), "UND", "94", "count", "Unidad" },
                    { new Guid("ba1d969a-74e0-bc66-0a6b-969dff36f16a"), "G", "GRM", "mass", "Gramo" },
                    { new Guid("fbb0b969-00d5-1dbf-999e-871ed004b6a8"), "LB", "LBR", "mass", "Libra" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_branch_products_branch_id_product_id",
                schema: "cat",
                table: "branch_products",
                columns: new[] { "branch_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_products_company_id_branch_id",
                schema: "cat",
                table: "branch_products",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_products_company_id_product_id",
                schema: "cat",
                table: "branch_products",
                columns: new[] { "company_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_company_id_name",
                schema: "cat",
                table: "categories",
                columns: new[] { "company_id", "name" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_prices_branch_id",
                schema: "cat",
                table: "product_prices",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_prices_company_id_product_id_branch_id_valid_from",
                schema: "cat",
                table: "product_prices",
                columns: new[] { "company_id", "product_id", "branch_id", "valid_from" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_barcode",
                schema: "cat",
                table: "products",
                columns: new[] { "company_id", "barcode" },
                unique: true,
                filter: "\"barcode\" IS NOT NULL AND \"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_category_id",
                schema: "cat",
                table: "products",
                columns: new[] { "company_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_sku",
                schema: "cat",
                table: "products",
                columns: new[] { "company_id", "sku" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_tax_id",
                schema: "cat",
                table: "products",
                columns: new[] { "company_id", "tax_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_unit_id",
                schema: "cat",
                table: "products",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_taxes_company_id_code",
                schema: "cat",
                table: "taxes",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_units_code",
                schema: "cat",
                table: "units",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_products",
                schema: "cat");

            migrationBuilder.DropTable(
                name: "product_prices",
                schema: "cat");

            migrationBuilder.DropTable(
                name: "products",
                schema: "cat");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "cat");

            migrationBuilder.DropTable(
                name: "taxes",
                schema: "cat");

            migrationBuilder.DropTable(
                name: "units",
                schema: "cat");
        }
    }
}
