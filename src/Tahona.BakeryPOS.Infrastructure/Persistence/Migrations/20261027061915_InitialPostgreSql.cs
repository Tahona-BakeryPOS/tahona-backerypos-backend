using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "aud");

            migrationBuilder.EnsureSchema(
                name: "org");

            migrationBuilder.EnsureSchema(
                name: "cash");

            migrationBuilder.EnsureSchema(
                name: "sec");

            migrationBuilder.EnsureSchema(
                name: "int");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "aud",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    old_values = table.Column<string>(type: "text", nullable: true),
                    new_values = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nit_number = table.Column<string>(type: "character varying(15)", unicode: false, maxLength: 15, nullable: false),
                    nit_check_digit = table.Column<int>(type: "integer", nullable: false),
                    tax_regime = table.Column<string>(type: "character varying(5)", unicode: false, maxLength: 5, nullable: false),
                    tax_responsibilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city_code = table.Column<string>(type: "character(5)", unicode: false, fixedLength: true, maxLength: 5, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "character(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
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
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.CheckConstraint("ck_companies_status", "status IN ('active','suspended')");
                    table.CheckConstraint("ck_companies_tax_regime", "tax_regime IN ('48','49')");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_requests",
                schema: "int",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_requests", x => new { x.company_id, x.key, x.endpoint });
                    table.CheckConstraint("ck_idempotency_requests_status", "status IN ('in_progress','completed')");
                });

            migrationBuilder.CreateTable(
                name: "inbound_events",
                schema: "int",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", unicode: false, maxLength: 40, nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_events", x => x.id);
                    table.CheckConstraint("ck_inbound_events_status", "status IN ('received','processed','ignored','failed')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "int",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_status", "status IN ('pending','processing','done','failed')");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", unicode: false, maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    is_platform_admin = table.Column<bool>(type: "boolean", nullable: false),
                    failed_logins = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city_code = table.Column<string>(type: "character(5)", unicode: false, fixedLength: true, maxLength: 5, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
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
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_branches_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", unicode: false, maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.UniqueConstraint("ak_roles_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_roles_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    failed_pin_attempts = table.Column<int>(type: "integer", nullable: false),
                    pin_locked_until = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.UniqueConstraint("ak_memberships_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_memberships_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sec",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sec",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_registers",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
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
                    table.PrimaryKey("pk_cash_registers", x => x.id);
                    table.UniqueConstraint("ak_cash_registers_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "fk_cash_registers_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "sec",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "sec",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "sec",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_assignments",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_role_assignments", x => x.id);
                    table.CheckConstraint("ck_role_assignments_validity", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "fk_role_assignments_branches_company_id_branch_id",
                        columns: x => new { x.company_id, x.branch_id },
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_assignments_memberships_company_id_membership_id",
                        columns: x => new { x.company_id, x.membership_id },
                        principalSchema: "sec",
                        principalTable: "memberships",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_assignments_roles_company_id_role_id",
                        columns: x => new { x.company_id, x.role_id },
                        principalSchema: "sec",
                        principalTable: "roles",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_pairing_codes",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_device_pairing_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_pairing_codes_cash_registers_company_id_cash_registe~",
                        columns: x => new { x.company_id, x.cash_register_id },
                        principalSchema: "cash",
                        principalTable: "cash_registers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_register_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
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
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_cash_registers_company_id_cash_register_id",
                        columns: x => new { x.company_id, x.cash_register_id },
                        principalSchema: "cash",
                        principalTable: "cash_registers",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "sec",
                table: "permissions",
                columns: new[] { "id", "code", "module", "name" },
                values: new object[,]
                {
                    { new Guid("1e6f3531-9d64-2cfe-0f2b-ed14d377ed70"), "catalogo.gestionar", "Catálogo", "Gestionar catálogo y precios base" },
                    { new Guid("2bb160a0-d0a0-2eec-39cf-6b408a1aff5a"), "plataforma.admin", "Plataforma", "Administrar la plataforma" },
                    { new Guid("3483b5dc-bc0f-a87e-f929-493b021e4e10"), "turnos.cerrar", "Caja", "Cerrar caja" },
                    { new Guid("429d64b9-f543-a8c6-900e-0e91b30bca07"), "devoluciones.crear", "Ventas", "Registrar devoluciones" },
                    { new Guid("5699e562-ded3-b8cc-3968-059b4222cfea"), "precios.sucursal", "Catálogo", "Gestionar precios y disponibilidad de la sucursal" },
                    { new Guid("60ea4f3b-f97c-f5f0-0d6d-eb2083cd69da"), "caja.retiro", "Caja", "Registrar retiros de efectivo" },
                    { new Guid("6714a5b1-96a9-4ae9-eb63-efe1ed34ca58"), "ventas.anular", "Ventas", "Anular ventas" },
                    { new Guid("903ed309-cabe-415a-29a7-4759a3fcb73f"), "turnos.abrir", "Caja", "Abrir caja" },
                    { new Guid("9a7e57d1-568c-f695-5843-3598c2142362"), "caja.movimientos", "Caja", "Registrar ingresos de efectivo" },
                    { new Guid("9ec411e3-597d-6442-4fb2-9098a7085b25"), "inventario.ver", "Inventario", "Ver existencias y movimientos" },
                    { new Guid("9f72f78d-7e54-e76f-0bb5-bcfff6e9be69"), "reportes.ver", "Empresa", "Ver reportes" },
                    { new Guid("a7e80275-4017-22a0-3665-451912f1b9de"), "inventario.ajustar", "Inventario", "Registrar ajustes de inventario" },
                    { new Guid("aed95fd7-1a78-8759-e076-d0d54c8b28d7"), "productos.ver", "Catálogo", "Ver productos" },
                    { new Guid("b3c5bb24-3997-8941-88a6-a3b1e7c5f94e"), "fiscal.gestionar", "Facturación", "Gestionar facturación electrónica" },
                    { new Guid("c2dbe7b9-20f0-01dd-480e-76d55e2c191d"), "organizacion.gestionar", "Empresa", "Gestionar empresa, sucursales, cajas y usuarios" },
                    { new Guid("c9b108e8-18cf-8664-4584-976993ae9060"), "ventas.crear", "Ventas", "Registrar ventas" },
                    { new Guid("f966b923-ecea-4a53-4620-391fa2861f7f"), "devoluciones.aprobar", "Ventas", "Aprobar devoluciones y reembolsos" },
                    { new Guid("fd1b97ed-fbb7-0108-2ab7-247eba64afbb"), "ventas.ver", "Ventas", "Ver ventas" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id_entity_entity_id_occurred_at",
                schema: "aud",
                table: "audit_logs",
                columns: new[] { "company_id", "entity", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id_occurred_at",
                schema: "aud",
                table: "audit_logs",
                columns: new[] { "company_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_company_id_code",
                schema: "org",
                table: "branches",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_registers_branch_id_code",
                schema: "cash",
                table: "cash_registers",
                columns: new[] { "branch_id", "code" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_registers_company_id_branch_id",
                schema: "cash",
                table: "cash_registers",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_companies_nit_number",
                schema: "org",
                table: "companies",
                column: "nit_number",
                unique: true,
                filter: "\"deleted_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_codes_code_hash",
                schema: "sec",
                table: "device_pairing_codes",
                column: "code_hash",
                unique: true,
                filter: "\"consumed_at\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_codes_company_id_cash_register_id",
                schema: "sec",
                table: "device_pairing_codes",
                columns: new[] { "company_id", "cash_register_id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_company_id_cash_register_id",
                schema: "sec",
                table: "devices",
                columns: new[] { "company_id", "cash_register_id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_token_hash",
                schema: "sec",
                table: "devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_events_provider_external_event_id",
                schema: "int",
                table: "inbound_events",
                columns: new[] { "provider", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_company_id_user_id",
                schema: "sec",
                table: "memberships",
                columns: new[] { "company_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                schema: "sec",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                schema: "int",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                schema: "sec",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "sec",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_revoked_at",
                schema: "sec",
                table: "refresh_tokens",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_company_id_branch_id",
                schema: "sec",
                table: "role_assignments",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_company_id_membership_id",
                schema: "sec",
                table: "role_assignments",
                columns: new[] { "company_id", "membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_company_id_role_id",
                schema: "sec",
                table: "role_assignments",
                columns: new[] { "company_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_membership_id",
                schema: "sec",
                table: "role_assignments",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                schema: "sec",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_company_id_code",
                schema: "sec",
                table: "roles",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "sec",
                table: "users",
                column: "email",
                unique: true,
                filter: "\"email\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "aud");

            migrationBuilder.DropTable(
                name: "device_pairing_codes",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "idempotency_requests",
                schema: "int");

            migrationBuilder.DropTable(
                name: "inbound_events",
                schema: "int");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "int");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "role_assignments",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "cash_registers",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "org");

            migrationBuilder.DropTable(
                name: "users",
                schema: "sec");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "org");
        }
    }
}
