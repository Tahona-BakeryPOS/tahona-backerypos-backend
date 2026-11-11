# Database diagram

Relational model of **PostgreSQL** generated from the backend's EF Core model (`AppDbContext`) on 2026-11-11: **44 tables** in **10 schemas** and **52 foreign keys**. The diagrams render directly on GitHub.

> If the model changes (a new migration), this file must be regenerated. The detail of each table, its integrity rules and its indexes is in [`data-model.md`](data-model.md); the implemented model is the source of truth if anything differs.

## How to read the diagrams

| Symbol | Meaning |
|---|---|
| `PK` / `FK` / `UK` | Primary key, foreign key and unique column |
| `"null"` | The column allows null values |
| Solid line `──` | A real foreign key in the database |
| Dotted line `┄┄` | A reference between modules **without** a foreign key: integrity is validated by the application (modules are not linked by navigation) |
| `‖──o{` | One to many: a parent row has zero or many child rows |
| `\|o──o{` | Same, but the reference is optional (the column allows null) |
| `‖──o\|` | One to one |

Tables of other schemas that appear in each diagram are shown only with their `id`: they are the junction with that module. To avoid cluttering the drawings, **the `company_id` lines towards `org.companies` are not drawn** (every business table has it; see the next section): the column is still marked `FK`.

## Company isolation

Every business table carries `company_id` and the backend applies a global filter per company. **38 of 44 tables** have it. The ones that do not are: the company itself (`org.companies`), the global catalogs (`cat.units`, `sec.permissions`), users (`sec.users`, linked to each company through `sec.memberships`), `sec.role_permissions` (it depends on its role) and `int.inbound_events` (raw events from external providers).

## Overview by module

Each arrow means the source module references tables of the target module (with or without a foreign key). Arrows towards `org` are not drawn: every business table carries `company_id` towards `org.companies`.

```mermaid
flowchart LR
    org["org · Companies<br/>2 tables"]
    sec["sec · Security<br/>9 tables"]
    cat["cat · Catalog<br/>6 tables"]
    inv["inv · Inventory<br/>4 tables"]
    cash["cash · Cash<br/>4 tables"]
    sales["sales · Sales<br/>5 tables"]
    pay["pay · Payments<br/>6 tables"]
    fis["fis · Invoicing<br/>4 tables"]
    int["int · Integration<br/>3 tables"]
    aud["aud · Audit<br/>1 table"]
    inv --> cat
    pay --> cash
    sales --> cash
    sec --> cash
    aud --> sec
    cash --> sec
    fis --> sales
    inv --> sec
    pay --> sales
    sales --> cat
    sales --> fis
    sales --> sec
```

## `org` — Companies and branches

2 tables: `branches`, `companies`.

```mermaid
erDiagram
    org_branches {
        uuid id PK
        uuid company_id FK
        varchar(200) address "null"
        char(5) city_code "null"
        varchar(20) code
        bool is_active
        varchar(120) name
        varchar(30) phone "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    org_companies {
        uuid id PK
        varchar(200) address "null"
        char(5) city_code "null"
        char(3) currency
        varchar(200) email
        varchar(200) legal_name
        int nit_check_digit
        varchar(15) nit_number
        varchar(30) phone "null"
        varchar(20) status
        varchar(5) tax_regime
        text[] tax_responsibilities
        varchar(64) time_zone
        varchar(200) trade_name "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_registers {
        uuid id PK
    }
    cash_cash_sessions {
        uuid id PK
    }
    cat_branch_products {
        uuid id PK
    }
    cat_product_prices {
        uuid id PK
    }
    inv_stock_locations {
        uuid id PK
    }
    sales_returns {
        uuid id PK
    }
    sales_sales {
        uuid id PK
    }
    sec_role_assignments {
        uuid id PK
    }
    org_companies ||--o{ org_branches : "company_id"
    org_branches |o..o{ sales_returns : "branch_id (sin FK)"
    org_branches ||--o{ cash_cash_registers : "company_id, branch_id"
    org_branches ||--o{ cash_cash_sessions : "company_id, branch_id"
    org_branches ||--o{ cat_branch_products : "company_id, branch_id"
    org_branches |o--o{ cat_product_prices : "branch_id"
    org_branches ||--o{ inv_stock_locations : "company_id, branch_id"
    org_branches ||--o{ sales_sales : "company_id, branch_id"
    org_branches |o--o{ sec_role_assignments : "company_id, branch_id"
```

## `sec` — Security: users, roles, devices and sessions

9 tables: `device_pairing_codes`, `devices`, `memberships`, `permissions`, `refresh_tokens`, `role_assignments`, `role_permissions`, `roles`, `users`.

```mermaid
erDiagram
    sec_device_pairing_codes {
        uuid id PK
        uuid cash_register_id FK
        uuid company_id FK
        varchar(64) code_hash
        timestamptz consumed_at "null"
        uuid device_id "null"
        timestamptz expires_at
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sec_devices {
        uuid id PK
        uuid cash_register_id FK
        uuid company_id FK
        bool is_active
        timestamptz last_seen_at "null"
        varchar(80) name
        varchar(64) token_hash
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sec_memberships {
        uuid id PK
        uuid company_id FK
        uuid user_id FK
        int failed_pin_attempts
        bool is_active
        varchar(100) pin_hash "null"
        timestamptz pin_locked_until "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sec_permissions {
        uuid id PK
        varchar(60) code
        varchar(40) module
        varchar(120) name
    }
    sec_refresh_tokens {
        uuid id PK
        uuid user_id FK
        uuid company_id "null"
        timestamptz expires_at
        uuid replaced_by_id "null"
        timestamptz revoked_at "null"
        varchar(64) token_hash
        timestamptz created_at
    }
    sec_role_assignments {
        uuid id PK
        uuid branch_id FK
        uuid company_id FK
        uuid membership_id FK
        uuid role_id FK
        timestamptz valid_from
        timestamptz valid_to "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sec_role_permissions {
        uuid permission_id PK, FK
        uuid role_id PK, FK
    }
    sec_roles {
        uuid id PK
        uuid company_id FK
        varchar(40) code
        bool is_system
        varchar(80) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sec_users {
        uuid id PK
        varchar(200) email "null"
        int failed_logins
        varchar(120) full_name
        bool is_active
        bool is_platform_admin
        timestamptz locked_until "null"
        bool must_change_password
        varchar(100) password_hash "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    aud_audit_logs {
        uuid id PK
    }
    cash_cash_movements {
        uuid id PK
    }
    cash_cash_registers {
        uuid id PK
    }
    inv_stock_movements {
        uuid id PK
    }
    org_branches {
        uuid id PK
    }
    sales_sales {
        uuid id PK
    }
    cash_cash_registers ||--o{ sec_device_pairing_codes : "company_id, cash_register_id"
    cash_cash_registers ||--o{ sec_devices : "company_id, cash_register_id"
    sec_users ||--o{ sec_memberships : "user_id"
    sec_users ||--o{ sec_refresh_tokens : "user_id"
    org_branches |o--o{ sec_role_assignments : "company_id, branch_id"
    sec_memberships ||--o{ sec_role_assignments : "company_id, membership_id"
    sec_roles ||--o{ sec_role_assignments : "company_id, role_id"
    sec_permissions ||--o{ sec_role_permissions : "permission_id"
    sec_roles ||--o{ sec_role_permissions : "role_id"
    sec_users |o..o{ aud_audit_logs : "user_id (sin FK)"
    sec_users |o..o{ cash_cash_movements : "user_id (sin FK)"
    sec_users |o..o{ inv_stock_movements : "user_id (sin FK)"
    sec_users |o..o{ sales_sales : "cashier_id (sin FK)"
    sec_devices |o..o{ sec_device_pairing_codes : "device_id (sin FK)"
    sec_refresh_tokens |o..o{ sec_refresh_tokens : "replaced_by_id (sin FK)"
```

## `cat` — Catalog and prices

6 tables: `branch_products`, `categories`, `product_prices`, `products`, `taxes`, `units`.

```mermaid
erDiagram
    cat_branch_products {
        uuid id PK
        uuid branch_id FK
        uuid company_id FK
        uuid product_id FK
        bool is_available
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_categories {
        uuid id PK
        uuid company_id FK
        int display_order
        bool is_active
        varchar(80) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_product_prices {
        uuid id PK
        uuid branch_id FK
        uuid company_id FK
        uuid product_id FK
        numeric amount
        bool includes_tax
        timestamptz valid_from
        timestamptz valid_to "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_products {
        uuid id PK
        uuid category_id FK
        uuid company_id FK
        uuid tax_id FK
        uuid unit_id FK
        varchar(50) barcode "null"
        bool is_active
        varchar(200) name
        varchar(10) negative_stock_policy
        varchar(50) sku
        bool track_stock
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_taxes {
        uuid id PK
        uuid company_id FK
        varchar(20) code
        bool is_active
        varchar(10) kind
        varchar(80) name
        numeric rate
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_units {
        uuid id PK
        varchar(10) code
        varchar(10) dian_code
        varchar(10) kind
        varchar(60) name
    }
    inv_stock_balances {
        uuid id PK
    }
    inv_stock_movements {
        uuid id PK
    }
    org_branches {
        uuid id PK
    }
    sales_return_lines {
        uuid id PK
    }
    sales_sale_lines {
        uuid id PK
    }
    org_branches ||--o{ cat_branch_products : "company_id, branch_id"
    cat_products ||--o{ cat_branch_products : "company_id, product_id"
    org_branches |o--o{ cat_product_prices : "branch_id"
    cat_products ||--o{ cat_product_prices : "company_id, product_id"
    cat_categories ||--o{ cat_products : "company_id, category_id"
    cat_taxes ||--o{ cat_products : "company_id, tax_id"
    cat_units ||--o{ cat_products : "unit_id"
    cat_products |o..o{ sales_return_lines : "product_id (sin FK)"
    cat_products |o..o{ sales_sale_lines : "product_id (sin FK)"
    cat_taxes |o..o{ sales_sale_lines : "tax_id (sin FK)"
    cat_products ||--o{ inv_stock_balances : "company_id, product_id"
    cat_products ||--o{ inv_stock_movements : "company_id, product_id"
```

## `inv` — Inventory

4 tables: `adjustment_reasons`, `stock_balances`, `stock_locations`, `stock_movements`.

```mermaid
erDiagram
    inv_adjustment_reasons {
        uuid id PK
        uuid company_id FK
        varchar(20) code
        varchar(10) direction
        bool is_active
        varchar(80) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    inv_stock_balances {
        uuid id PK
        uuid company_id FK
        uuid location_id FK
        uuid product_id FK
        uuid last_movement_id "null"
        numeric on_hand
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    inv_stock_locations {
        uuid id PK
        uuid branch_id FK
        uuid company_id FK
        varchar(20) code
        bool is_active
        bool is_default
        varchar(80) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    inv_stock_movements {
        uuid id PK
        uuid company_id FK
        uuid location_id FK
        uuid product_id FK
        uuid reason_id FK
        uuid authorized_by "null"
        varchar(20) kind
        varchar(200) note "null"
        timestamptz occurred_at
        numeric quantity
        uuid source_id
        uuid source_line_id
        varchar(20) source_type
        uuid user_id "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cat_products {
        uuid id PK
    }
    org_branches {
        uuid id PK
    }
    sec_users {
        uuid id PK
    }
    cat_products ||--o{ inv_stock_balances : "company_id, product_id"
    inv_stock_locations ||--o{ inv_stock_balances : "company_id, location_id"
    org_branches ||--o{ inv_stock_locations : "company_id, branch_id"
    inv_adjustment_reasons |o--o{ inv_stock_movements : "reason_id"
    cat_products ||--o{ inv_stock_movements : "company_id, product_id"
    inv_stock_locations ||--o{ inv_stock_movements : "company_id, location_id"
    inv_stock_movements |o..o{ inv_stock_balances : "last_movement_id (sin FK)"
    sec_users |o..o{ inv_stock_movements : "user_id (sin FK)"
```

## `cash` — Cash

4 tables: `cash_movements`, `cash_reasons`, `cash_registers`, `cash_sessions`.

```mermaid
erDiagram
    cash_cash_movements {
        uuid id PK
        uuid cash_session_id FK
        uuid company_id FK
        uuid reason_id FK
        numeric amount
        uuid authorized_by "null"
        varchar(20) kind
        varchar(200) note "null"
        timestamptz occurred_at
        uuid source_id "null"
        varchar(20) source_type "null"
        uuid user_id "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_reasons {
        uuid id PK
        uuid company_id FK
        bool is_active
        varchar(10) kind
        varchar(80) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_registers {
        uuid id PK
        uuid branch_id FK
        uuid company_id FK
        varchar(20) code
        bool is_active
        varchar(120) name
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_sessions {
        uuid id PK
        uuid branch_id FK
        uuid cash_register_id FK
        uuid company_id FK
        timestamptz closed_at "null"
        uuid closed_by "null"
        numeric counted_amount "null"
        numeric difference "null"
        numeric expected_amount "null"
        varchar(300) notes "null"
        timestamptz opened_at
        uuid opened_by
        numeric opening_amount
        varchar(10) status
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    org_branches {
        uuid id PK
    }
    pay_payments {
        uuid id PK
    }
    pay_register_terminals {
        uuid id PK
    }
    sales_returns {
        uuid id PK
    }
    sales_sales {
        uuid id PK
    }
    sec_device_pairing_codes {
        uuid id PK
    }
    sec_devices {
        uuid id PK
    }
    sec_users {
        uuid id PK
    }
    cash_cash_reasons |o--o{ cash_cash_movements : "reason_id"
    cash_cash_sessions ||--o{ cash_cash_movements : "company_id, cash_session_id"
    org_branches ||--o{ cash_cash_registers : "company_id, branch_id"
    org_branches ||--o{ cash_cash_sessions : "company_id, branch_id"
    cash_cash_registers ||--o{ cash_cash_sessions : "company_id, cash_register_id"
    sec_users |o..o{ cash_cash_movements : "user_id (sin FK)"
    cash_cash_registers |o..o{ sales_sales : "cash_register_id (sin FK)"
    cash_cash_sessions ||--o{ pay_payments : "company_id, cash_session_id"
    cash_cash_registers ||--o{ pay_register_terminals : "company_id, cash_register_id"
    cash_cash_sessions ||--o{ sales_returns : "company_id, cash_session_id"
    cash_cash_sessions ||--o{ sales_sales : "company_id, cash_session_id"
    cash_cash_registers ||--o{ sec_device_pairing_codes : "company_id, cash_register_id"
    cash_cash_registers ||--o{ sec_devices : "company_id, cash_register_id"
```

## `sales` — Sales and returns

5 tables: `return_lines`, `returns`, `sale_counters`, `sale_lines`, `sales`.

```mermaid
erDiagram
    sales_return_lines {
        uuid id PK
        uuid company_id FK
        uuid sale_line_id FK
        uuid sale_return_id FK
        numeric amount
        uuid product_id
        numeric quantity
        bool restock
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sales_returns {
        uuid id PK
        uuid cash_session_id FK
        uuid company_id FK
        uuid sale_id FK
        uuid approved_by
        uuid branch_id
        uuid fiscal_document_id "null"
        timestamptz occurred_at
        varchar(300) reason
        numeric total
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sales_sale_counters {
        uuid id PK
        uuid company_id FK
        bigint last_number
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sales_sale_lines {
        uuid id PK
        uuid company_id FK
        uuid sale_id FK
        varchar(200) description
        numeric discount_amount
        varchar(200) discount_reason "null"
        int line_no
        numeric line_total
        bool price_includes_tax
        uuid product_id
        numeric quantity
        varchar(50) sku
        numeric tax_amount
        uuid tax_id
        numeric tax_rate
        numeric taxable_base
        varchar(10) unit_code
        numeric unit_price
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sales_sales {
        uuid id PK
        uuid branch_id FK
        uuid cash_session_id FK
        uuid company_id FK
        timestamptz cancelled_at "null"
        uuid cash_register_id
        uuid cashier_id
        timestamptz confirmed_at "null"
        varchar(20) customer_identification "null"
        numeric discount_total
        uuid fiscal_document_id "null"
        varchar(20) fiscal_status
        varchar(20) inventory_status
        bigint number "null"
        varchar(20) payment_status
        numeric refunded_total
        varchar(20) status
        uuid stock_authorized_by "null"
        numeric subtotal
        numeric tax_total
        numeric total
        varchar(300) void_reason "null"
        timestamptz voided_at "null"
        uuid voided_by "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_registers {
        uuid id PK
    }
    cash_cash_sessions {
        uuid id PK
    }
    cat_products {
        uuid id PK
    }
    cat_taxes {
        uuid id PK
    }
    fis_fiscal_documents {
        uuid id PK
    }
    org_branches {
        uuid id PK
    }
    pay_payments {
        uuid id PK
    }
    pay_refunds {
        uuid id PK
    }
    sec_users {
        uuid id PK
    }
    sales_returns ||--o{ sales_return_lines : "company_id, sale_return_id"
    sales_sale_lines ||--o{ sales_return_lines : "sale_line_id"
    cash_cash_sessions ||--o{ sales_returns : "company_id, cash_session_id"
    sales_sales ||--o{ sales_returns : "company_id, sale_id"
    sales_sales ||--o{ sales_sale_lines : "company_id, sale_id"
    org_branches ||--o{ sales_sales : "company_id, branch_id"
    cash_cash_sessions ||--o{ sales_sales : "company_id, cash_session_id"
    sales_sales |o..o{ fis_fiscal_documents : "sale_id (sin FK)"
    sales_sales |o..o{ pay_payments : "sale_id (sin FK)"
    sales_sales |o..o{ pay_refunds : "sale_id (sin FK)"
    cat_products |o..o{ sales_return_lines : "product_id (sin FK)"
    org_branches |o..o{ sales_returns : "branch_id (sin FK)"
    fis_fiscal_documents |o..o{ sales_returns : "fiscal_document_id (sin FK)"
    cat_products |o..o{ sales_sale_lines : "product_id (sin FK)"
    cat_taxes |o..o{ sales_sale_lines : "tax_id (sin FK)"
    cash_cash_registers |o..o{ sales_sales : "cash_register_id (sin FK)"
    sec_users |o..o{ sales_sales : "cashier_id (sin FK)"
    fis_fiscal_documents |o..o{ sales_sales : "fiscal_document_id (sin FK)"
```

## `pay` — Payments

6 tables: `gateway_accounts`, `payment_attempts`, `payment_methods`, `payments`, `refunds`, `register_terminals`.

```mermaid
erDiagram
    pay_gateway_accounts {
        uuid id PK
        uuid company_id FK
        text encrypted_identity_key
        text encrypted_secret_key "null"
        varchar(10) environment
        bool is_active
        varchar(20) provider
        varchar(200) seller_email
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    pay_payment_attempts {
        uuid id PK
        uuid company_id FK
        uuid payment_id FK
        numeric amount
        varchar(10) environment
        varchar(20) error_code "null"
        varchar(300) error_message "null"
        varchar(100) external_id "null"
        varchar(30) gateway_method
        varchar(100) integration_id "null"
        timestamptz last_event_at "null"
        varchar(20) provider
        varchar(40) raw_status "null"
        varchar(64) reference
        varchar(10) status
        varchar(50) terminal_serial "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    pay_payment_methods {
        uuid id PK
        uuid company_id FK
        varchar(30) code
        int display_order
        varchar(30) gateway_method "null"
        bool is_active
        varchar(20) kind
        varchar(80) name
        bool requires_reference
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    pay_payments {
        uuid id PK
        uuid cash_session_id FK
        uuid company_id FK
        uuid payment_method_id FK
        numeric amount
        numeric amount_tendered "null"
        numeric change_amount "null"
        varchar(20) method_kind
        varchar(10) purpose
        varchar(100) reference "null"
        numeric refunded_amount
        varchar(300) resolution_note "null"
        timestamptz resolved_at "null"
        uuid resolved_by "null"
        bool resolved_manually
        uuid sale_id "null"
        varchar(20) status
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    pay_refunds {
        uuid id PK
        uuid company_id FK
        uuid payment_id FK
        numeric amount
        varchar(10) method
        timestamptz occurred_at
        uuid sale_id
        uuid source_id
        varchar(10) source_type
        varchar(10) status
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    pay_register_terminals {
        uuid id PK
        uuid cash_register_id FK
        uuid company_id FK
        varchar(20) provider
        varchar(30) terminal_model
        varchar(50) terminal_serial
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    cash_cash_registers {
        uuid id PK
    }
    cash_cash_sessions {
        uuid id PK
    }
    sales_sales {
        uuid id PK
    }
    pay_payments ||--o{ pay_payment_attempts : "company_id, payment_id"
    cash_cash_sessions ||--o{ pay_payments : "company_id, cash_session_id"
    pay_payment_methods ||--o{ pay_payments : "company_id, payment_method_id"
    pay_payments ||--o{ pay_refunds : "company_id, payment_id"
    cash_cash_registers ||--o{ pay_register_terminals : "company_id, cash_register_id"
    sales_sales |o..o{ pay_payments : "sale_id (sin FK)"
    sales_sales |o..o{ pay_refunds : "sale_id (sin FK)"
```

## `fis` — Electronic invoicing

4 tables: `fiscal_documents`, `fiscal_mappings`, `fiscal_settings`, `fiscal_submission_attempts`.

```mermaid
erDiagram
    fis_fiscal_documents {
        uuid id PK
        uuid invoice_document_id FK
        timestamptz accepted_at "null"
        int attempts
        uuid company_id
        int credit_reason "null"
        varchar(200) cufe "null"
        varchar(20) customer_identification
        varchar(2000) errors "null"
        varchar(100) external_id "null"
        varchar(30) idempotency_key
        varchar(20) kind
        bigint number "null"
        jsonb payload
        varchar(10) prefix "null"
        varchar(20) provider
        timestamptz retain_until "null"
        uuid sale_id
        uuid source_id "null"
        varchar(20) status
        numeric total
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    fis_fiscal_mappings {
        uuid id PK
        uuid company_id
        varchar(100) external_id
        varchar(20) kind
        uuid local_id
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    fis_fiscal_settings {
        uuid id PK
        uuid company_id FK
        varchar(20) consumer_final_identification
        bigint credit_note_document_type_id "null"
        text encrypted_access_key
        text encrypted_username
        bigint invoice_document_type_id "null"
        bool is_active
        bigint product_account_group_id "null"
        varchar(20) provider
        int retention_years
        bigint seller_id "null"
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    fis_fiscal_submission_attempts {
        uuid id PK
        uuid company_id FK
        uuid fiscal_document_id FK
        varchar(1000) detail "null"
        int http_status "null"
        int number
        timestamptz occurred_at
        varchar(20) operation
        varchar(20) outcome
        timestamptz created_at
        varchar(64) created_by
        timestamptz modified_at "null"
        varchar(64) modified_by "null"
        timestamptz deleted_at "null"
        varchar(64) deleted_by "null"
        bytea row_version
    }
    sales_returns {
        uuid id PK
    }
    sales_sales {
        uuid id PK
    }
    fis_fiscal_documents |o--o{ fis_fiscal_documents : "invoice_document_id"
    fis_fiscal_documents ||--o{ fis_fiscal_submission_attempts : "company_id, fiscal_document_id"
    sales_sales |o..o{ fis_fiscal_documents : "sale_id (sin FK)"
    fis_fiscal_documents |o..o{ sales_returns : "fiscal_document_id (sin FK)"
    fis_fiscal_documents |o..o{ sales_sales : "fiscal_document_id (sin FK)"
```

## `int` — Integration: outbox, inbound events and idempotency

3 tables: `idempotency_requests`, `inbound_events`, `outbox_messages`.

```mermaid
erDiagram
    int_idempotency_requests {
        uuid company_id PK
        varchar(200) endpoint PK
        varchar(64) key PK
        timestamptz completed_at "null"
        varchar(64) request_hash
        text response_body "null"
        int response_status "null"
        varchar(20) status
        timestamptz created_at
    }
    int_inbound_events {
        uuid id PK
        varchar(200) external_event_id
        varchar(2000) last_error "null"
        text payload
        timestamptz processed_at "null"
        varchar(40) provider
        timestamptz received_at
        bool signature_valid
        varchar(20) status
    }
    int_outbox_messages {
        uuid id PK
        int attempts
        uuid company_id "null"
        varchar(2000) last_error "null"
        timestamptz locked_until "null"
        timestamptz next_attempt_at
        text payload
        timestamptz processed_at "null"
        varchar(20) status
        varchar(100) type
        timestamptz created_at
    }
```

## `aud` — Audit

1 table: `audit_logs`.

```mermaid
erDiagram
    aud_audit_logs {
        uuid id PK
        varchar(20) action
        uuid company_id "null"
        varchar(64) correlation_id "null"
        varchar(100) entity
        uuid entity_id
        text new_values "null"
        timestamptz occurred_at
        text old_values "null"
        uuid user_id "null"
    }
    sec_users {
        uuid id PK
    }
    sec_users |o..o{ aud_audit_logs : "user_id (sin FK)"
```
