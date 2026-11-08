# Diseño de software: POS para panaderías (v1 ventas y caja)

Oct 24, 2026 · @Kennys Alfaro

## Alcance y principios de diseño

La v1 modela solo venta y caja; cada decisión deja un punto de extensión para inventario, facturación electrónica y pagos con Bold sin reescribir lo anterior.

### Dentro de la v1

- Organización, sucursales, cajas y dispositivos
- Usuarios, roles y permisos
- Catálogo de productos, impuestos y listas de precio
- Terceros (cliente opcional, con "consumidor final" por defecto)
- Turnos y movimientos de caja
- Ventas, líneas de venta y pagos
- Auditoría y bandeja de eventos

### Reservado para después (sin tablas en la v1)

- Inventario, recetas y producción
- Compras y proveedores
- Fiado, devoluciones y pedidos por encargo
- Facturación electrónica DIAN
- Cobro integrado con Bold
- Contabilidad auxiliar

### Principios

1. Lo transaccional no se edita: una venta se anula, no se corrige en sitio.
2. Lo maestro (productos, precios, usuarios) se edita y conserva su historial de cambios.
3. Cada módulo es dueño de sus tablas; los demás lo referencian por identificador, nunca por reglas internas.
4. Los catálogos viven en tablas, no en enumeraciones fijas; los estados cerrados usan CHECK.
5. Las transacciones guardan una copia del precio, el impuesto y el nombre vigentes al vender.
6. Todo hecho relevante se escribe en una bandeja de eventos dentro de la misma transacción.
7. El diseño asume caja sin internet: identificadores generados en el dispositivo y operaciones repetibles sin duplicar.

## Convenciones de modelado y auditoría

Todas las tablas comparten nombres, claves y un bloque de auditoría heredado de una clase abstracta, de modo que el esquema se lee y se extiende igual en cualquier módulo.

### Convenciones

| Tema | Regla |
| --- | --- |
| Nombres | `snake_case`, singular, sin tildes ni ñ; clave foránea = `<tabla>_id`; fechas con sufijo `_en` |
| Clave primaria | `id` uuid (versión 7) generado por quien crea el registro; también en tablas puente, con UNIQUE sobre la pareja |
| Clave de negocio | `codigo` único dentro de la organización |
| Dinero y cantidades | Dinero numeric(18,2) en la moneda de la organización; tarifas numeric(7,4); cantidades numeric(18,4) |
| Fechas | timestamptz en UTC; la zona horaria se aplica al mostrar |
| Multi-organización | `organizacion_id` en cada tabla de negocio; es la única desnormalización deliberada, para aislar y filtrar filas |
| Catálogos globales | `permiso`, `tipo_documento_identidad` y `unidad_medida` no llevan `organizacion_id` |
| Estados | Texto con CHECK; cambiar la lista exige una migración controlada |
| Borrado | Nunca físico: `eliminado_en` en datos maestros; en transacciones solo estados de anulación |
| Numeración | Consecutivo por caja, asignado en el dispositivo, único por prefijo |
| Notación de columnas | `?` = admite nulo; PK = clave primaria; FK→tabla = clave foránea; UQ = único |

### Auditoría en cada tabla (clase abstracta EntidadAuditable)

Cada tabla del esquema hereda estas siete columnas; en las tablas se indica con EA.

| Columna | Tipo | Uso |
| --- | --- | --- |
| `creado_en` | timestamptz | Momento de creación en el servidor |
| `creado_por` | uuid FK→usuario | Quién creó el registro (nulo solo en datos semilla; usuario de sistema en procesos automáticos) |
| `modificado_en` | timestamptz? | Última modificación |
| `modificado_por` | uuid FK→usuario? | Quién modificó |
| `version_fila` | integer | Sube en cada cambio; permite concurrencia optimista |
| `eliminado_en` | timestamptz? | Borrado lógico en datos maestros |
| `eliminado_por` | uuid FK→usuario? | Quién eliminó |

Además, `registro_auditoria` guarda el antes y el después de cada cambio importante: quién, cuándo, desde qué dispositivo y bajo qué correlación.

### Relaciones principales (UML)

| Relación | Tipo UML | Cardinalidad |
| --- | --- | --- |
| `organizacion` — `sucursal` | Composición | 1 a 1..\* |
| `sucursal` — `caja` | Composición | 1 a 1..\* |
| `turno_caja` — `venta` | Agregación | 1 a 0..\* |
| `turno_caja` — `movimiento_caja` | Composición | 1 a 0..\* |
| `venta` — `venta_linea` | Composición (ciclo de vida compartido) | 1 a 1..\* |
| `venta` — `pago` | Composición | 1 a 1..\* |
| `tercero` — `venta` | Asociación (cliente) | 1 a 0..\* |
| `producto` — `precio` | Asociación con vigencia | 1 a 0..\* |
| `categoria_producto` — `categoria_producto` | Asociación reflexiva (jerarquía) | 0..1 a 0..\* |
| `rol` — `permiso` | Muchos a muchos vía `rol_permiso` | *..* |
| `usuario` — `rol` | Muchos a muchos vía `asignacion_usuario`, acotado por sucursal | *..* |
| `EntidadAuditable` — todas las tablas | Generalización (clase abstracta) | Herencia de columnas |

## Esquema de tablas (español)

Son 28 tablas en ocho grupos. Cada fila lista las columnas propias; todas heredan además el bloque de auditoría (EA). Las claves foráneas llevan siempre índice.

### Organización y dispositivos

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `organizacion` | id uuid PK; codigo varchar(20); razon\_social varchar(200); nit varchar(20)?; moneda char(3) por defecto 'COP'; zona\_horaria varchar(40); configuracion jsonb?; activo boolean | UQ(codigo) | EA |
| `sucursal` | id uuid PK; organizacion\_id FK→organizacion; codigo varchar(20); nombre varchar(120); direccion varchar(200)?; ciudad varchar(80)?; zona\_horaria varchar(40)?; lista\_precio\_id FK→lista\_precio?; activo boolean | UQ(organizacion\_id, codigo) | EA |
| `caja` | id uuid PK; organizacion\_id FK; sucursal\_id FK→sucursal; codigo varchar(20); nombre varchar(80); activo boolean | UQ(sucursal\_id, codigo) | EA |
| `dispositivo` | id uuid PK; organizacion\_id FK; caja\_id FK→caja?; nombre varchar(80); identificador\_externo varchar(100); ultima\_sincronizacion\_en timestamptz?; activo boolean | UQ(organizacion\_id, identificador\_externo) | EA |

### Seguridad

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `usuario` | id uuid PK; organizacion\_id FK; nombre\_completo varchar(150); correo varchar(150)?; identidad\_externa varchar(100)?; pin\_hash varchar(255)?; activo boolean | UQ(organizacion\_id, correo) | EA |
| `rol` | id uuid PK; organizacion\_id FK; codigo varchar(30); nombre varchar(80); descripcion varchar(200)?; es\_sistema boolean | UQ(organizacion\_id, codigo) | EA |
| `permiso` (global) | id uuid PK; codigo varchar(60); descripcion varchar(200) | UQ(codigo); ejemplo de código: `venta.anular` | EA |
| `rol_permiso` | id uuid PK; rol\_id FK→rol; permiso\_id FK→permiso | UQ(rol\_id, permiso\_id) | EA |
| `asignacion_usuario` | id uuid PK; organizacion\_id FK; usuario\_id FK→usuario; rol\_id FK→rol; sucursal\_id FK→sucursal? (nulo = todas); vigente\_desde date; vigente\_hasta date? | UQ(usuario\_id, rol\_id, sucursal\_id, vigente\_desde); CHECK(vigente\_hasta ≥ vigente\_desde) | EA |

### Terceros

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `tipo_documento_identidad` (global) | id uuid PK; codigo varchar(10); nombre varchar(80); activo boolean | UQ(codigo) | EA |
| `tercero` | id uuid PK; organizacion\_id FK; tipo\_persona varchar(10); tipo\_documento\_identidad\_id FK?; numero\_documento varchar(30)?; digito\_verificacion char(1)?; nombre varchar(200); correo varchar(150)?; telefono varchar(30)?; direccion varchar(200)?; es\_consumidor\_final boolean; activo boolean | CHECK(tipo\_persona IN ('natural','juridica')); UQ(organizacion\_id, tipo\_documento\_identidad\_id, numero\_documento); UQ parcial: un solo consumidor final por organización | EA |

### Catálogo y precios

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `unidad_medida` (global) | id uuid PK; codigo varchar(10); nombre varchar(50); tipo varchar(10) | UQ(codigo); CHECK(tipo IN ('conteo','masa','volumen')) | EA |
| `impuesto` | id uuid PK; organizacion\_id FK; codigo varchar(20); nombre varchar(80); tipo varchar(20); tarifa numeric(7,4); activo boolean | UQ(organizacion\_id, codigo); CHECK(tipo IN ('iva','impoconsumo','otro')); CHECK(tarifa ≥ 0) | EA |
| `categoria_producto` | id uuid PK; organizacion\_id FK; categoria\_padre\_id FK→categoria\_producto?; codigo varchar(20); nombre varchar(80); orden integer; activo boolean | UQ(organizacion\_id, codigo) | EA |
| `producto` | id uuid PK; organizacion\_id FK; codigo varchar(30); nombre varchar(150); descripcion varchar(300)?; categoria\_producto\_id FK; unidad\_medida\_id FK; impuesto\_id FK; tipo varchar(20); se\_vende\_por\_peso boolean; atributos jsonb?; activo boolean | UQ(organizacion\_id, codigo); CHECK(tipo IN ('simple')) | EA |
| `producto_codigo_barras` | id uuid PK; organizacion\_id FK; producto\_id FK→producto; codigo\_barras varchar(50) | UQ(organizacion\_id, codigo\_barras) | EA |
| `lista_precio` | id uuid PK; organizacion\_id FK; codigo varchar(20); nombre varchar(80); moneda char(3); precios\_incluyen\_impuesto boolean; activo boolean | UQ(organizacion\_id, codigo) | EA |
| `precio` | id uuid PK; organizacion\_id FK; lista\_precio\_id FK; producto\_id FK; valor numeric(18,2); vigente\_desde timestamptz; vigente\_hasta timestamptz? | CHECK(valor ≥ 0); CHECK(vigente\_hasta > vigente\_desde); sin solapamiento por (lista\_precio\_id, producto\_id) | EA |

### Parametrización operativa

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `medio_pago` | id uuid PK; organizacion\_id FK; codigo varchar(20); nombre varchar(80); tipo varchar(20); requiere\_referencia boolean; mueve\_efectivo\_caja boolean; proveedor\_externo varchar(30)?; activo boolean | UQ(organizacion\_id, codigo); CHECK(tipo IN ('efectivo','tarjeta','transferencia','billetera','otro')) | EA |
| `motivo_operacion` | id uuid PK; organizacion\_id FK; aplica\_a varchar(20); codigo varchar(20); nombre varchar(80); activo boolean | UQ(organizacion\_id, aplica\_a, codigo); CHECK(aplica\_a IN ('anulacion','descuento','ingreso\_caja','retiro\_caja')) | EA |
| `secuencia_documental` | id uuid PK; organizacion\_id FK; caja\_id FK→caja; tipo\_documento varchar(20); prefijo varchar(10); ultimo\_numero bigint | UQ(caja\_id, tipo\_documento, prefijo); CHECK(tipo\_documento IN ('venta')) | EA |

### Caja

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `turno_caja` | id uuid PK; organizacion\_id FK; caja\_id FK→caja; dispositivo\_id FK→dispositivo?; usuario\_apertura\_id FK→usuario; usuario\_cierre\_id FK→usuario?; estado varchar(10); abierto\_en timestamptz; cerrado\_en timestamptz?; efectivo\_inicial numeric(18,2); efectivo\_esperado numeric(18,2)?; efectivo\_contado numeric(18,2)?; diferencia numeric(18,2)?; observacion varchar(300)? | CHECK(estado IN ('abierto','cerrado')); UQ parcial: un turno abierto por caja | EA |
| `movimiento_caja` | id uuid PK; organizacion\_id FK; turno\_caja\_id FK→turno\_caja; tipo\_movimiento varchar(10); monto numeric(18,2); motivo\_operacion\_id FK; descripcion varchar(200)?; autorizado\_por\_usuario\_id FK→usuario?; ocurrido\_en timestamptz | CHECK(tipo\_movimiento IN ('ingreso','retiro')); CHECK(monto > 0) | EA |

### Ventas

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `venta` | id uuid PK (generado en el dispositivo); organizacion\_id FK; turno\_caja\_id FK; dispositivo\_id FK?; usuario\_id FK→usuario; tercero\_id FK→tercero; prefijo varchar(10); consecutivo bigint; estado varchar(12); fecha\_venta timestamptz; creada\_en\_dispositivo timestamptz; subtotal numeric(18,2); valor\_descuento numeric(18,2); valor\_impuesto numeric(18,2); redondeo numeric(18,2); total numeric(18,2); anulada\_en timestamptz?; anulada\_por\_usuario\_id FK→usuario?; motivo\_anulacion\_id FK→motivo\_operacion?; observacion varchar(300)? | UQ(organizacion\_id, prefijo, consecutivo); CHECK(estado IN ('completada','anulada')); CHECK(total ≥ 0); si está anulada, `anulada_en` y `motivo_anulacion_id` son obligatorios | EA |
| `venta_linea` | id uuid PK; organizacion\_id FK; venta\_id FK→venta; numero\_linea integer; producto\_id FK→producto; codigo\_producto varchar(30); descripcion varchar(150); cantidad numeric(18,4); unidad\_medida\_id FK; precio\_unitario numeric(18,2); precio\_incluye\_impuesto boolean; valor\_descuento numeric(18,2); motivo\_descuento\_id FK→motivo\_operacion?; impuesto\_id FK→impuesto; tarifa\_impuesto numeric(7,4); base\_gravable numeric(18,2); valor\_impuesto numeric(18,2); total\_linea numeric(18,2); nota varchar(200)? | UQ(venta\_id, numero\_linea); CHECK(cantidad > 0); código, descripción, tarifa y precio son copias del momento de la venta | EA |
| `pago` | id uuid PK; organizacion\_id FK; venta\_id FK→venta; medio\_pago\_id FK→medio\_pago; monto numeric(18,2); monto\_recibido numeric(18,2)?; valor\_cambio numeric(18,2); referencia varchar(100)?; referencia\_externa varchar(100)?; estado varchar(10); pagado\_en timestamptz | CHECK(monto > 0); CHECK(estado IN ('aprobado','anulado')); la suma de pagos aprobados es igual al total de la venta al completarla | EA |

### Transversal

| Tabla | Columnas | Claves y reglas | Aud. |
| --- | --- | --- | --- |
| `registro_auditoria` | id uuid PK; organizacion\_id FK?; entidad varchar(60); entidad\_id uuid; accion varchar(20); usuario\_id FK→usuario?; dispositivo\_id FK→dispositivo?; ocurrido\_en timestamptz; valores\_anteriores jsonb?; valores\_nuevos jsonb?; id\_correlacion uuid?; direccion\_ip varchar(45)? | Solo inserciones; sin FK sobre `entidad_id` (referencia polimórfica); CHECK(accion IN ('creacion','modificacion','eliminacion','anulacion','acceso')); índice (organizacion\_id, entidad, entidad\_id, ocurrido\_en) | EA |
| `bandeja_evento` | id uuid PK; organizacion\_id FK; tipo\_evento varchar(80); tipo\_agregado varchar(60); agregado\_id uuid; version\_esquema integer; carga jsonb; estado varchar(10); intentos integer; publicado\_en timestamptz?; ultimo\_error varchar(500)?; ocurrido\_en timestamptz | CHECK(estado IN ('pendiente','publicado','fallido')); índice (estado, ocurrido\_en); se escribe en la misma transacción que el cambio que la origina | EA |

## Table schema (English)

The same 28 tables with English names; the last column gives the Spanish equivalent. Every table inherits the abstract class `AuditableEntity` (AE): created\_at, created\_by, updated\_at, updated\_by, row\_version, deleted\_at, deleted\_by. The user table is named `app_user` because `user` is reserved in SQL.

### Organization and devices

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `organization` | id uuid PK; code varchar(20); legal\_name varchar(200); tax\_id varchar(20)?; currency char(3) default 'COP'; time\_zone varchar(40); settings jsonb?; is\_active boolean | UQ(code) | AE | `organizacion` |
| `branch` | id uuid PK; organization\_id FK→organization; code varchar(20); name varchar(120); address varchar(200)?; city varchar(80)?; time\_zone varchar(40)?; price\_list\_id FK→price\_list?; is\_active boolean | UQ(organization\_id, code) | AE | `sucursal` |
| `cash_register` | id uuid PK; organization\_id FK; branch\_id FK→branch; code varchar(20); name varchar(80); is\_active boolean | UQ(branch\_id, code) | AE | `caja` |
| `device` | id uuid PK; organization\_id FK; cash\_register\_id FK→cash\_register?; name varchar(80); external\_id varchar(100); last\_sync\_at timestamptz?; is\_active boolean | UQ(organization\_id, external\_id) | AE | `dispositivo` |

### Security

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `app_user` | id uuid PK; organization\_id FK; full\_name varchar(150); email varchar(150)?; external\_identity varchar(100)?; pin\_hash varchar(255)?; is\_active boolean | UQ(organization\_id, email) | AE | `usuario` |
| `role` | id uuid PK; organization\_id FK; code varchar(30); name varchar(80); description varchar(200)?; is\_system boolean | UQ(organization\_id, code) | AE | `rol` |
| `permission` (global) | id uuid PK; code varchar(60); description varchar(200) | UQ(code); example code: `sale.void` | AE | `permiso` |
| `role_permission` | id uuid PK; role\_id FK→role; permission\_id FK→permission | UQ(role\_id, permission\_id) | AE | `rol_permiso` |
| `user_assignment` | id uuid PK; organization\_id FK; user\_id FK→app\_user; role\_id FK→role; branch\_id FK→branch? (null = all); valid\_from date; valid\_to date? | UQ(user\_id, role\_id, branch\_id, valid\_from); CHECK(valid\_to ≥ valid\_from) | AE | `asignacion_usuario` |

### Parties

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `identity_document_type` (global) | id uuid PK; code varchar(10); name varchar(80); is\_active boolean | UQ(code) | AE | `tipo_documento_identidad` |
| `party` | id uuid PK; organization\_id FK; party\_type varchar(10); identity\_document\_type\_id FK?; document\_number varchar(30)?; check\_digit char(1)?; name varchar(200); email varchar(150)?; phone varchar(30)?; address varchar(200)?; is\_default\_consumer boolean; is\_active boolean | CHECK(party\_type IN ('person','company')); UQ(organization\_id, identity\_document\_type\_id, document\_number); partial UQ: one default consumer per organization | AE | `tercero` |

### Catalog and pricing

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `unit_of_measure` (global) | id uuid PK; code varchar(10); name varchar(50); kind varchar(10) | UQ(code); CHECK(kind IN ('count','mass','volume')) | AE | `unidad_medida` |
| `tax` | id uuid PK; organization\_id FK; code varchar(20); name varchar(80); kind varchar(20); rate numeric(7,4); is\_active boolean | UQ(organization\_id, code); CHECK(kind IN ('vat','consumption\_tax','other')); CHECK(rate ≥ 0) | AE | `impuesto` |
| `product_category` | id uuid PK; organization\_id FK; parent\_category\_id FK→product\_category?; code varchar(20); name varchar(80); sort\_order integer; is\_active boolean | UQ(organization\_id, code) | AE | `categoria_producto` |
| `product` | id uuid PK; organization\_id FK; code varchar(30); name varchar(150); description varchar(300)?; product\_category\_id FK; unit\_of\_measure\_id FK; tax\_id FK; product\_type varchar(20); is\_sold\_by\_weight boolean; attributes jsonb?; is\_active boolean | UQ(organization\_id, code); CHECK(product\_type IN ('simple')) | AE | `producto` |
| `product_barcode` | id uuid PK; organization\_id FK; product\_id FK→product; barcode varchar(50) | UQ(organization\_id, barcode) | AE | `producto_codigo_barras` |
| `price_list` | id uuid PK; organization\_id FK; code varchar(20); name varchar(80); currency char(3); prices\_include\_tax boolean; is\_active boolean | UQ(organization\_id, code) | AE | `lista_precio` |
| `price` | id uuid PK; organization\_id FK; price\_list\_id FK; product\_id FK; amount numeric(18,2); valid\_from timestamptz; valid\_to timestamptz? | CHECK(amount ≥ 0); CHECK(valid\_to > valid\_from); no overlap per (price\_list\_id, product\_id) | AE | `precio` |

### Operational settings

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `payment_method` | id uuid PK; organization\_id FK; code varchar(20); name varchar(80); kind varchar(20); requires\_reference boolean; moves\_cash\_drawer boolean; external\_provider varchar(30)?; is\_active boolean | UQ(organization\_id, code); CHECK(kind IN ('cash','card','transfer','wallet','other')) | AE | `medio_pago` |
| `operation_reason` | id uuid PK; organization\_id FK; applies\_to varchar(20); code varchar(20); name varchar(80); is\_active boolean | UQ(organization\_id, applies\_to, code); CHECK(applies\_to IN ('void','discount','cash\_in','cash\_out')) | AE | `motivo_operacion` |
| `document_sequence` | id uuid PK; organization\_id FK; cash\_register\_id FK→cash\_register; document\_type varchar(20); prefix varchar(10); last\_number bigint | UQ(cash\_register\_id, document\_type, prefix); CHECK(document\_type IN ('sale')) | AE | `secuencia_documental` |

### Cash handling

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `cash_shift` | id uuid PK; organization\_id FK; cash\_register\_id FK→cash\_register; device\_id FK→device?; opened\_by\_user\_id FK→app\_user; closed\_by\_user\_id FK→app\_user?; status varchar(10); opened\_at timestamptz; closed\_at timestamptz?; opening\_cash numeric(18,2); expected\_cash numeric(18,2)?; counted\_cash numeric(18,2)?; difference numeric(18,2)?; notes varchar(300)? | CHECK(status IN ('open','closed')); partial UQ: one open shift per cash register | AE | `turno_caja` |
| `cash_movement` | id uuid PK; organization\_id FK; cash\_shift\_id FK→cash\_shift; movement\_type varchar(10); amount numeric(18,2); operation\_reason\_id FK; description varchar(200)?; authorized\_by\_user\_id FK→app\_user?; occurred\_at timestamptz | CHECK(movement\_type IN ('cash\_in','cash\_out')); CHECK(amount > 0) | AE | `movimiento_caja` |

### Sales

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `sale` | id uuid PK (generated on the device); organization\_id FK; cash\_shift\_id FK; device\_id FK?; user\_id FK→app\_user; party\_id FK→party; prefix varchar(10); sequence\_number bigint; status varchar(12); sold\_at timestamptz; created\_on\_device\_at timestamptz; subtotal numeric(18,2); discount\_amount numeric(18,2); tax\_amount numeric(18,2); rounding\_amount numeric(18,2); total numeric(18,2); voided\_at timestamptz?; voided\_by\_user\_id FK→app\_user?; void\_reason\_id FK→operation\_reason?; notes varchar(300)? | UQ(organization\_id, prefix, sequence\_number); CHECK(status IN ('completed','voided')); CHECK(total ≥ 0); a voided sale requires `voided_at` and `void_reason_id` | AE | `venta` |
| `sale_line` | id uuid PK; organization\_id FK; sale\_id FK→sale; line\_number integer; product\_id FK→product; product\_code varchar(30); description varchar(150); quantity numeric(18,4); unit\_of\_measure\_id FK; unit\_price numeric(18,2); price\_includes\_tax boolean; discount\_amount numeric(18,2); discount\_reason\_id FK→operation\_reason?; tax\_id FK→tax; tax\_rate numeric(7,4); taxable\_base numeric(18,2); tax\_amount numeric(18,2); line\_total numeric(18,2); note varchar(200)? | UQ(sale\_id, line\_number); CHECK(quantity > 0); code, description, rate and price are copies taken at the time of sale | AE | `venta_linea` |
| `payment` | id uuid PK; organization\_id FK; sale\_id FK→sale; payment\_method\_id FK→payment\_method; amount numeric(18,2); amount\_tendered numeric(18,2)?; change\_amount numeric(18,2); reference varchar(100)?; external\_reference varchar(100)?; status varchar(10); paid\_at timestamptz | CHECK(amount > 0); CHECK(status IN ('approved','voided')); approved payments sum to the sale total when the sale completes | AE | `pago` |

### Cross-cutting

| Table | Columns | Keys and rules | Aud. | Spanish name |
| --- | --- | --- | --- | --- |
| `audit_log` | id uuid PK; organization\_id FK?; entity\_name varchar(60); entity\_id uuid; action varchar(20); user\_id FK→app\_user?; device\_id FK→device?; occurred\_at timestamptz; old\_values jsonb?; new\_values jsonb?; correlation\_id uuid?; ip\_address varchar(45)? | Insert-only; no FK on `entity_id` (polymorphic reference); CHECK(action IN ('create','update','delete','void','access')); index (organization\_id, entity\_name, entity\_id, occurred\_at) | AE | `registro_auditoria` |
| `outbox_event` | id uuid PK; organization\_id FK; event\_type varchar(80); aggregate\_type varchar(60); aggregate\_id uuid; schema\_version integer; payload jsonb; status varchar(10); attempts integer; published\_at timestamptz?; last\_error varchar(500)?; occurred\_at timestamptz | CHECK(status IN ('pending','published','failed')); index (status, occurred\_at); written in the same transaction as the change that causes it | AE | `bandeja_evento` |

## Arquitectura sugerida

La v1 vive en un monolito modular; inventario, facturación y pagos con Bold se conectan después por eventos, sin tocar el módulo de ventas.

&#91;embedded content: arquitectura lógica · monolito modular con eventos\]

Los clientes hablan solo con la puerta de entrada. Los módulos comparten una base de datos y escriben cada hecho relevante en la bandeja de eventos; de allí el publicador alimenta las integraciones sin frenar la caja. Los nombres son lógicos: la tecnología de cada caja se elige después.

### Cómo crece sin rediseñar

1. Inventario escucha el evento de venta completada y descuenta insumos según receta.
2. Facturación escucha el mismo evento y lo envía al proveedor DIAN.
3. Bold se suma como medio de pago externo: guarda su referencia en `pago.referencia_externa` y se concilia con sus notificaciones.

## Decisiones abiertas y siguientes pasos

Antes de elegir tecnología conviene cerrar cinco decisiones de diseño; la columna de la derecha es una recomendación para discutir, no una decisión tomada.

| Decisión | Opciones | Recomendación |
| --- | --- | --- |
| Aislamiento entre organizaciones | Base compartida con `organizacion_id` o una base por organización | Base compartida con reglas de acceso por fila; pasar a base por organización solo si un cliente lo exige |
| Cliente en la venta | Venta sin tercero (nulo) o tercero predefinido "consumidor final" | Tercero predefinido: evita nulos y deja lista la facturación futura |
| Numeración sin internet | Consecutivo central o consecutivo por caja asignado en el dispositivo | Por caja con prefijo, para no bloquear la venta; confirmar con tu contador qué numeración exigen los documentos fiscales |
| Precios por sucursal | Precio dentro del producto o listas de precio con vigencia | Listas de precio: permiten promociones y cambios sin perder el historial |
| Retención de auditoría | Indefinida o por plazo | Particionar `registro_auditoria` por mes y definir el plazo con tu contador |

### Siguientes pasos de diseño (sin elegir tecnología)

1. Casos de uso de la v1 y reglas de negocio de cada uno
2. Diagrama de secuencia de una venta sin internet y su sincronización
3. Contratos de la API: recursos, errores y versionado
4. Estrategia de pruebas y de migraciones del esquema
5. Solo entonces, tecnología: lenguaje, base de datos y alojamiento

## Paso 1 · Casos de uso y reglas de negocio

La v1 se resume en 11 casos de uso; las reglas de cada uno son lo que las pruebas del paso 4 deben comprobar.

| Caso de uso | Actor | Flujo principal | Reglas de negocio |
| --- | --- | --- | --- |
| CU-01 Iniciar sesión con PIN | Cajero, dueño | Elige su usuario, digita el PIN y entra a su sucursal | El PIN se guarda como hash; los intentos fallidos bloquean al usuario (límite configurable); solo entra a sucursales donde tiene asignación vigente |
| CU-02 Abrir turno | Cajero | Elige la caja, declara el efectivo inicial y abre el turno | Un solo turno abierto por caja; efectivo inicial ≥ 0; la caja debe estar activa |
| CU-03 Registrar venta | Cajero | Agrega productos por atajo o código de barras, ajusta cantidades y aplica descuentos | Requiere turno abierto; cliente por defecto = consumidor final; precio vigente de la lista de la sucursal; cantidad > 0, con decimales solo en productos por peso; descuento exige motivo y permiso; la línea guarda copia de precio, impuesto y nombre |
| CU-04 Cobrar | Cajero | Elige uno o varios medios de pago y confirma | La suma de pagos aprobados es igual al total; el efectivo admite monto recibido mayor y calcula el cambio; los demás medios no admiten exceso; la referencia es obligatoria si el medio la exige |
| CU-05 Anular venta | Dueño, o cajero con permiso `venta.anular` | Abre la venta, indica el motivo y confirma | Exige permiso y motivo; la venta queda anulada y sus pagos también, sin borrarse; una venta de un turno ya cerrado solo la anula el dueño y su efecto en caja se registra en el turno abierto (a confirmar con tu contador) |
| CU-06 Ingreso o retiro de efectivo | Cajero | Registra el movimiento con monto y motivo | Monto > 0; el retiro exige motivo y autorización del dueño; no puede superar el efectivo esperado del turno |
| CU-07 Cerrar turno | Cajero | Cuenta el efectivo, el sistema calcula y muestra la diferencia, y cierra | Efectivo esperado = inicial + ventas en efectivo + ingresos − retiros; diferencia = contado − esperado; un turno cerrado no admite ventas; se puede cerrar sin internet y los totales se recalculan al sincronizar |
| CU-08 Consultar reportes | Dueño | Elige rango de fechas, sucursal y vista | Solo lectura; muestra consolidado y por sucursal; las ventas anuladas se presentan aparte del total vendido |
| CU-09 Gestionar catálogo y precios | Dueño | Crea o edita productos, categorías, impuestos y listas de precio | Un cambio de precio abre una nueva vigencia, no sobrescribe; un producto con ventas se desactiva, no se elimina; el código es único por organización |
| CU-10 Gestionar sucursales, cajas y usuarios | Dueño | Crea sucursales, cajas, usuarios y asigna roles por sucursal | Códigos únicos; se desactiva en vez de borrar; todo cambio queda en el registro de auditoría |
| CU-11 Sincronizar ventas pendientes | Dispositivo (automático) | Envía en orden cronológico lo guardado sin conexión | Cada venta viaja con su identificador, por lo que reenviarla no la duplica; el servidor puede rechazarla con un motivo que el dueño ve en el backoffice |

## Paso 2 · Venta sin internet y sincronización

El cajero nunca espera al servidor: la venta se confirma en el dispositivo y viaja después, y el servidor reconoce los reenvíos por el identificador de la venta.

&#91;embedded content: secuencia · venta sin internet y sincronización\]

Al volver la conexión, el POS envía el lote en orden. El servidor guarda en una sola transacción la venta, su registro de auditoría y el evento de la bandeja; si el envío se repite porque se cortó la conexión, reconoce el identificador y responde "duplicada" sin guardar de nuevo. El resultado de cada venta (aceptada, duplicada o rechazada) vuelve al POS, que solo entonces la marca como enviada.

### Qué garantiza este flujo

- La venta se cobra y se imprime sin esperar al servidor.
- Reenviar es seguro, porque el identificador lo genera el POS y no el servidor.
- Las ventas rechazadas se conservan en el dispositivo y el dueño las ve con su motivo.
- Si el POS falla antes de marcar una venta como enviada, la reenvía y el servidor responde "duplicada".

## Paso 3 · Contratos de la API

La API se diseña alrededor de recursos y de una regla central: toda operación que el dispositivo pueda repetir debe ser idempotente. Los contratos son independientes de la tecnología.

### Convenciones

| Tema | Regla |
| --- | --- |
| Estilo | Recursos con JSON sobre HTTPS, ruta base `/api/v1` |
| Organización | Se toma del token de acceso; nunca viaja como parámetro libre |
| Identificadores | uuid generado por el cliente en ventas, turnos y movimientos |
| Dinero | Cadena decimal (`"12500.00"`) para evitar errores de redondeo |
| Fechas | ISO 8601 en UTC; la hora del dispositivo viaja aparte en `creada_en_dispositivo` |
| Idempotencia | Mismo `id` y mismo contenido devuelve el resultado ya guardado; mismo `id` con contenido distinto devuelve conflicto |
| Concurrencia | Datos maestros se editan con `version_fila` (ETag e If-Match); si cambió, responde conflicto |
| Paginación | Por cursor, con límite máximo por página |
| Versionado | En la ruta; agregar campos no cambia la versión, quitarlos o cambiar su sentido sí |

### Operaciones de la v1

| Operación | Caso de uso | Permiso | Notas |
| --- | --- | --- | --- |
| `POST /auth/pin` | CU-01 | Dispositivo registrado | Devuelve token de corta duración |
| `POST /turnos` | CU-02 | `turno.abrir` | El `id` lo genera el cliente |
| `POST /turnos/{id}/cierre` | CU-07 | `turno.cerrar` | Recalcula esperado y diferencia en el servidor |
| `POST /turnos/{id}/movimientos` | CU-06 | `caja.movimiento` | Retiro exige autorización |
| `PUT /ventas/{id}` | CU-03, CU-04 | `venta.crear` | Venta completa con líneas y pagos; idempotente |
| `POST /ventas/{id}/anulacion` | CU-05 | `venta.anular` | Exige motivo |
| `POST /sincronizacion` | CU-11 | `venta.crear` | Lote de operaciones; responde el resultado de cada una |
| `GET /sincronizacion/catalogo?desde={cursor}` | CU-03 sin internet | Dispositivo registrado | Solo lo que cambió desde el cursor |
| `GET /ventas`, `GET /reportes/ventas-diarias` | CU-08 | `reporte.ver` | Filtros por fecha, sucursal y cajero |
| `/productos`, `/categorias`, `/impuestos`, `/listas-precio`, `/precios` | CU-09 | `catalogo.gestionar` | Crear, consultar, editar y desactivar |
| `/sucursales`, `/cajas`, `/usuarios`, `/asignaciones` | CU-10 | `organizacion.gestionar` | Crear, consultar, editar y desactivar |

En `POST /sincronizacion`, cada elemento del lote termina como aceptado, duplicado (ya existía, sin cambios) o rechazado con un código; el dispositivo conserva las rechazadas y las muestra al dueño.

### Errores

Todos los errores devuelven un cuerpo con `codigo` estable, `mensaje` legible y `detalles` por campo cuando aplique.

| HTTP | Código | Cuándo |
| --- | --- | --- |
| 401 | `NO_AUTENTICADO` | Token ausente o vencido |
| 403 | `SIN_PERMISO` | El rol no tiene el permiso requerido |
| 404 | `NO_ENCONTRADO` | El recurso no existe en la organización |
| 409 | `CONFLICTO_VERSION` | El dato maestro cambió desde que se leyó |
| 409 | `VENTA_DUPLICADA_DISTINTA` | Mismo `id` de venta con contenido diferente |
| 409 | `TURNO_YA_ABIERTO` | La caja ya tiene un turno abierto |
| 409 | `TURNO_CERRADO` | Se intenta vender o mover efectivo en un turno cerrado |
| 422 | `VALIDACION` | Campos inválidos o reglas de negocio de formato |
| 422 | `PAGOS_NO_CUBREN_TOTAL` | La suma de pagos aprobados no iguala el total |
| 429 | `LIMITE_EXCEDIDO` | Demasiadas solicitudes del mismo dispositivo |

## Paso 4 · Estrategia de pruebas y de migraciones

Cada regla del paso 1 debe quedar cubierta por una prueba automática, y cada cambio del esquema debe poder aplicarse sin detener las cajas ni perder una venta.

### Pruebas por nivel

| Nivel | Qué verifica | Ejemplos |
| --- | --- | --- |
| Unidad (dominio) | Reglas puras, sin base de datos | Totales, impuestos incluidos y no incluidos, redondeo, cambio, efectivo esperado del turno |
| Integración con base de datos | Restricciones, índices y transacciones reales | Un solo turno abierto por caja, consecutivo único, venta y bandeja de eventos guardadas en la misma transacción, columnas de auditoría llenas |
| Contrato de la API | Que el contrato del paso 3 no se rompa | Formato de errores, idempotencia de `PUT /ventas/{id}`, versionado |
| Extremo a extremo | Flujos completos desde la pantalla | Abrir turno, vender, cobrar, anular y cerrar; venta sin internet y su sincronización |
| Aislamiento | Que una organización jamás vea datos de otra | Consultas con tokens de dos organizaciones sobre los mismos identificadores |
| Carga y resistencia | Comportamiento bajo presión y fallas | Lote grande de sincronización, corte de conexión a mitad del envío |

### Escenarios críticos sin internet

1. **Reintento:** el mismo envío llega dos veces y la venta no se duplica.
2. **Corte a mitad del lote:** lo ya aceptado no se reenvía y lo pendiente continúa en orden.
3. **Reloj del dispositivo desfasado:** el servidor conserva su propia hora de recepción y la del dispositivo por separado.
4. **Catálogo desactualizado:** el servidor acepta el precio con que se vendió, porque la línea guarda su copia, y marca la venta si difiere de la lista vigente.
5. **Turno cerrado en el dispositivo y en el servidor:** los totales se recalculan al sincronizar y la diferencia queda visible.
6. **Dispositivo con versión antigua:** el servidor acepta el contrato anterior durante un plazo o pide actualizar antes de vender.

### Migraciones del esquema

| Regla | Detalle |
| --- | --- |
| Versionadas y solo hacia adelante | Cada cambio es un script numerado en el repositorio, aplicado por el proceso de despliegue; una migración aplicada nunca se edita |
| Expandir y contraer | Para cambios sin parada: agregar columna opcional, desplegar código que escriba en ambas, rellenar datos, volver obligatoria y solo después retirar la antigua, en otra versión |
| Sin tocar historia | Las migraciones no reescriben ventas, pagos ni auditoría; si es imprescindible, queda registrada en `registro_auditoria` |
| Ensayo previo | Cada migración se prueba contra una copia de datos representativos y anonimizados, con respaldo verificado antes de producción |
| Datos semilla | Catálogos globales (`permiso`, `unidad_medida`, `tipo_documento_identidad`) se cargan por migración, no a mano |
| Compatibilidad con dispositivos | Un cambio de esquema nunca debe obligar a actualizar de inmediato una caja que pasó días sin conexión |

### Definición de terminado de la v1

- Las reglas de los 11 casos de uso tienen pruebas automáticas que pasan.
- Los seis escenarios sin internet están automatizados.
- Hay una migración inicial y una de prueba aplicadas desde cero en un entorno limpio.
- Una semana de uso real en tus panaderías sin diferencias de caja sin explicar.

## Paso 5 · Tecnología

Con un solo desarrollador la regla es elegir lo que ya dominas y lo más sencillo que cumpla los requisitos; tres pruebas de concepto deben confirmarlo antes de comprometerse. Las recomendaciones son punto de partida para discutir, no una decisión.

### Criterios, en orden de peso

1. Lo dominas hoy: cada tecnología nueva es tiempo de aprendizaje y de errores.
2. Soporta caja sin internet y operaciones repetibles.
3. Costo de operar al inicio, con pocos clientes.
4. Madurez de herramientas, documentación y comunidad.
5. Facilidad de encontrar ayuda o contratar después.

### Opciones por capa

| Capa | Recomendación inicial | Alternativas | Por qué | Cuándo reconsiderar |
| --- | --- | --- | --- | --- |
| Backend | C# con .NET en un monolito modular | Node.js con TypeScript | Tipado fuerte para reglas de dinero, buenas herramientas de migraciones y pruebas, y es tu stack | Si el POS y el backend compartieran mucha lógica en el navegador |
| Base de datos | PostgreSQL | SQL Server; MongoDB | El modelo es relacional y usa restricciones que PostgreSQL cubre bien: únicos parciales, no solapamiento de precios, JSON y seguridad por fila. MongoDB no encaja con un modelo transaccional con tantas restricciones | Si el hospedaje o las licencias empujan hacia SQL Server |
| POS en caja | App web instalable (PWA) con almacenamiento local en el navegador | Aplicación de escritorio; aplicación móvil nativa | Es la única opción web, económica, instalable en tablet o PC y que permite trabajar sin internet | Si la impresión o el cajón monedero no funcionan bien desde el navegador (ver prueba 1) |
| Interfaz web | TypeScript con un framework de componentes para POS y backoffice | Blazor | Ecosistema más amplio para trabajo sin conexión y para contratar; Blazor permitiría un solo lenguaje a costa de menos madurez sin internet | Si prefieres un solo lenguaje y la prueba 2 sale bien con Blazor |
| Alojamiento | Servicios administrados de un proveedor de nube: aplicación, base de datos con respaldos y almacenamiento | Servidor virtual propio | Respaldos, actualizaciones y alta disponibilidad no dependen de ti; evitar orquestadores complejos al inicio | Si el costo mensual supera lo que dejan los primeros clientes; revisa precios actuales de cada proveedor |
| Eventos | Bandeja de eventos en la base y un proceso en segundo plano dentro de la misma aplicación | Cola de mensajes administrada | Cero infraestructura adicional hasta que haya integraciones externas reales | Al conectar Bold, DIAN o contabilidad con volumen |
| Identidad | PIN con hash y token propios | Proveedor de identidad externo | La v1 solo necesita PIN y roles; un proveedor externo añade costo y complejidad | Al ofrecer acceso por correo o inicio con terceros |

### Tres pruebas de concepto antes de decidir

1. **Impresión y cajón monedero desde el navegador**, con la impresora y la tablet que usarías en tus panaderías. Es el mayor riesgo de la opción web.
2. **Venta sin internet y sincronización con idempotencia**, con corte de red a mitad del envío, en el dispositivo más modesto que vayas a soportar.
3. **Pantalla de venta con un catálogo realista** (cientos de productos) midiendo fluidez en ese mismo dispositivo.

Si la prueba 1 falla, la alternativa es una aplicación de escritorio o móvil para el POS, con el mismo backend y los mismos contratos.

### Cómo apoyarte en Claude sin perder control

- Usa este documento como fuente: tablas, contratos y reglas son las especificaciones que se entregan a Claude.
- Pide pruebas antes que código: escribe primero las pruebas del paso 4 y luego la implementación que las haga pasar.
- Revisa tú todo lo que toca dinero, permisos, auditoría y migraciones; ahí un error silencioso sale caro.
- Registra cada decisión de tecnología en un registro breve (contexto, decisión, alternativas descartadas, fecha) para no repetir la discusión.
