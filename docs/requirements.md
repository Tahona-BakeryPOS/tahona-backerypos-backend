# Requisitos — Tahona.BakeryPOS MVP v1.1

Estado: **borrador para revisión**. No hay código funcional nuevo hasta aprobar este documento y las
decisiones bloqueantes de [`decisions.md`](decisions.md).

## 1. Punto de partida: el sistema anterior

Este backend parte de un sistema anterior (una API .NET con PostgreSQL y un POS offline-first). Lo reutilizable se conserva; lo demás cambia como indica la tabla.

| Área | Sistema anterior | Qué cambia en el MVP |
|---|---|---|
| Backend | .NET 9, Clean Architecture + CQRS (MediatR), EF Core, **PostgreSQL**, JWT, Result pattern | .NET 10 LTS, **PostgreSQL** (I-18), monolito modular con límites explícitos |
| Empresas | Una organización demo por seed; sin alta de empresas | Alta y configuración de empresas (onboarding) |
| Seguridad | Roles Propietario/Cajero, permisos como claims, PIN por dispositivo vinculado, login backoffice por correo | Se conserva; se añade pertenencia usuario–empresa y pruebas de aislamiento |
| Catálogo | Productos, categorías, impuestos, lista de precios con vigencia | Precios **por sucursal** |
| Inventario | No existe | Ubicaciones, movimientos auditables, saldos, ajustes |
| Caja | Turnos, ingresos/retiros, cierre con diferencia | Se conserva el modelo; sesión de caja por cajero |
| Ventas | Venta completa en un paso (`POST /sales`), anulación | Venta como **proceso persistente** con estados separados |
| Pagos | Registro manual (efectivo, tarjeta con referencia) | Pasarela real con intentos, webhooks y conciliación |
| Facturación | No existe | Documento equivalente POS / factura electrónica vía proveedor tecnológico |
| Devoluciones | No existen | Devoluciones autorizadas con reembolso y reingreso a inventario |
| Auditoría | Automática en `SaveChangesAsync` (antes/después) | Se conserva; se amplía a eventos de integración |

Lo reutilizable sin cambios de fondo: estructura de capas, Result/errores, permisos por claims,
auditoría automática, vinculación de dispositivos, reglas de dinero (`sale-math`), backoffice Angular.
Lo que se retira: cola offline, sincronización (`/sincronizacion`), consecutivo local de ticket.

## 2. Alcance

### Dentro del MVP
1. Alta y configuración de empresas.
2. Sucursales.
3. Usuarios, roles y permisos.
4. Catálogo básico de productos vendibles.
5. Precios por sucursal.
6. Control básico de existencias con movimientos auditables.
7. Configuración de cajas y cajeros.
8. Apertura, movimientos y cierre de caja.
9. POS con carrito y cálculo de totales.
10. Pagos en efectivo y medios electrónicos admitidos.
11. Integración con una pasarela de pagos real.
12. Integración con un proveedor tecnológico de facturación electrónica.
13. Consulta de ventas, pagos, documentos y movimientos.
14. Anulaciones y devoluciones autorizadas.
15. Auditoría y aislamiento estricto entre empresas.

### Fuera del MVP
Recetas y producción · compras y proveedores · costeo · lotes y vencimientos · transferencias entre
bodegas · modo offline · contabilidad formal · microservicios/Kubernetes.

## 3. Actores

| Actor | Descripción |
|---|---|
| Administrador de plataforma | Opera el SaaS: da de alta empresas, ve su estado. No ve datos de ventas de las empresas. |
| Propietario | Dueño de la empresa: configura sucursales, catálogo, precios, impuestos, fiscal, pagos y usuarios. Autoriza anulaciones, devoluciones y ajustes. |
| Administrador de sucursal | Gestiona su sucursal: precios locales, existencias, cajas, consultas. |
| Cajero | Abre y cierra caja, vende, cobra, registra ingresos/retiros. |
| Pasarela de pagos | Sistema externo: confirma pagos y envía eventos. |
| Proveedor tecnológico | Sistema externo: valida y transmite documentos fiscales a la DIAN. |

## 4. Historias de usuario y criterios de aceptación

Formato: *Como … quiero … para …* · **CA** = criterios de aceptación verificables.

### M1. Empresas y sucursales
- **HU-1.1** Como administrador de plataforma quiero dar de alta una empresa con su propietario para que pueda empezar a operar.
  **CA:** razón social, NIT con dígito de verificación válido, régimen y responsabilidades fiscales obligatorios; NIT único en la plataforma; se crean roles, medios de pago e impuestos por defecto; el propietario recibe acceso con contraseña de un solo uso que debe cambiar.
- **HU-1.2** Como propietario quiero editar los datos de mi empresa (contacto, logo, zona horaria).
  **CA:** cambios auditados con antes/después; la zona horaria define el "día" de reportes y cierres.
- **HU-1.3** Como propietario quiero crear, editar y desactivar sucursales.
  **CA:** código único por empresa; una sucursal con ventas no se borra, se desactiva; al crearla se crea su ubicación de inventario por defecto.

### M2. Seguridad
- **HU-2.1** Como propietario quiero crear usuarios y asignarles un rol por sucursal.
  **CA:** el usuario solo opera en sucursales con asignación vigente; el PIN de caja se guarda como hash; correo único entre usuarios con acceso al backoffice.
- **HU-2.2** Como usuario quiero pertenecer a más de una empresa con el mismo correo (contador, asesor).
  **CA:** al iniciar sesión elige la empresa; el token lleva una sola empresa; cambiar de empresa emite un token nuevo. *(Decisión D-08.)*
- **HU-2.3** Como sistema quiero rechazar cualquier acceso a recursos de otra empresa aunque el identificador sea válido.
  **CA:** toda consulta por id devuelve 404 si el recurso es de otra empresa; prueba automática por cada endpoint con id.
- **HU-2.4** Como cajero quiero entrar en una caja vinculada tocando mi nombre y digitando mi PIN.
  **CA:** 5 intentos fallidos bloquean al usuario 5 minutos; el bloqueo queda auditado.

### M3. Catálogo y precios
- **HU-3.1** Como propietario quiero crear productos vendibles con código, nombre, categoría, unidad, impuesto y código de barras.
  **CA:** código único por empresa; producto con ventas solo se desactiva; unidades de masa admiten cantidades decimales.
- **HU-3.2** Como propietario quiero definir el precio base de un producto y precios distintos por sucursal.
  **CA:** el precio vigente en una sucursal es el de la sucursal si existe, si no el base; un cambio abre una nueva vigencia sin borrar la anterior; no hay solapamiento de vigencias.
- **HU-3.3** Como administrador de sucursal quiero marcar productos como no disponibles en mi sucursal.
  **CA:** un producto no disponible no aparece en el POS de esa sucursal.

### M4. Inventario
- **HU-4.1** Como administrador de sucursal quiero registrar la existencia inicial de cada producto.
  **CA:** se registra como movimiento `inicial`; el saldo no se edita directamente.
- **HU-4.2** Como administrador quiero consultar existencias por sucursal y ubicación.
- **HU-4.3** Como sistema quiero descontar existencias cuando una venta queda confirmada (pago aprobado), una sola vez.
  **CA:** reintentar la confirmación no duplica el movimiento (clave idempotente).
- **HU-4.4** Como propietario quiero autorizar ajustes de inventario con motivo.
  **CA:** ajuste con motivo obligatorio y usuario autorizador; auditado.
- **HU-4.5** Como sistema quiero aplicar la política de existencias insuficientes definida en D-06.
- **HU-4.6** Como propietario quiero ver si los saldos cuadran con la suma de movimientos (reconciliación).

### M5. Cajas y turnos
- **HU-5.1** Como propietario quiero crear cajas por sucursal y vincular dispositivos a cada caja (código de un solo uso).
- **HU-5.2** Como cajero quiero abrir mi sesión de caja declarando el efectivo inicial.
  **CA:** una sesión abierta por caja; efectivo inicial ≥ 0.
- **HU-5.3** Como cajero quiero registrar ingresos y retiros de efectivo con motivo; el retiro requiere autorización.
- **HU-5.4** Como cajero quiero cerrar caja contando el efectivo.
  **CA:** esperado = inicial + efectivo neto de ventas (recibido − cambio) − efectivo devuelto + ingresos − retiros; diferencia visible; no se cierra con ventas en estado de pago pendiente sin resolver (ver D-12).

### M6. POS y ventas
- **HU-6.1** Como cajero quiero agregar productos por botón, búsqueda o código de barras y ver subtotal, impuestos y total.
- **HU-6.2** Como cajero quiero aplicar descuentos con motivo (requiere permiso `ventas.descuento` o autorización).
- **HU-6.3** Como cajero quiero identificar al cliente (consumidor final por defecto, o NIT/cédula si pide factura).
- **HU-6.4** Como cajero quiero cobrar en efectivo (con cambio), con medios electrónicos de la pasarela, o con pago mixto.
  **CA:** la venta solo se confirma cuando todos sus pagos están aprobados por el backend; ningún cliente marca un pago como aprobado.
- **HU-6.5** Como cajero quiero ver en pantalla el estado de un pago electrónico (esperando, aprobado, rechazado) y reintentar con otro medio si falla.
- **HU-6.6** Como cajero quiero imprimir/mostrar el comprobante con el estado del documento fiscal.

### M7. Pagos
- **HU-7.1** Como sistema quiero crear el pago en la pasarela con una referencia única e idempotente.
- **HU-7.2** Como sistema quiero recibir y validar eventos firmados de la pasarela, ignorando duplicados.
- **HU-7.3** Como sistema quiero resolver pagos con resultado desconocido consultando a la pasarela antes de reintentar.
- **HU-7.4** Como propietario quiero conciliar pagos electrónicos aprobados contra ventas y cajas.

### M8. Facturación electrónica
- **HU-8.1** Como propietario quiero configurar los datos fiscales de la empresa, la numeración autorizada y las credenciales del proveedor (pruebas y producción separadas).
- **HU-8.2** Como sistema quiero emitir el documento fiscal de cada venta confirmada **cuando la empresa tiene la facturación encendida** y seguir su estado hasta tener respuesta verificable; con la facturación apagada la venta queda "sin factura" (D-17).
- **HU-8.6** Como propietario quiero encender o apagar la facturación electrónica según mi necesidad, sin afectar los cobros ni lo ya facturado.
- **HU-8.3** Como sistema quiero consultar el estado antes de reintentar un envío con resultado desconocido.
- **HU-8.4** Como propietario quiero ver documentos rechazados, su motivo y corregir/reemitir según el procedimiento definido.
- **HU-8.5** Como sistema quiero emitir la nota (crédito/ajuste) correspondiente a una devolución o anulación de venta ya documentada.

### M9. Anulaciones y devoluciones
- **HU-9.1** Como propietario quiero anular una venta del día antes de que el documento fiscal sea aceptado *(regla exacta en D-11)*.
- **HU-9.2** Como cajero autorizado quiero registrar una devolución total o parcial de una venta.
  **CA:** requiere autorización y motivo; genera reembolso (efectivo o por pasarela), movimiento de inventario compensatorio y nota fiscal; ninguna devolución supera lo vendido.

### M10. Consultas y auditoría
- **HU-10.1** Como propietario quiero consultar ventas, pagos, documentos fiscales, movimientos de caja e inventario con filtros.
- **HU-10.2** Como propietario quiero ver el historial de cambios y de eventos de integración.

## 5. Pantallas y flujos

### 5.1 POS (caja)
| Pantalla | Propósito | Notas |
|---|---|---|
| Vincular dispositivo | Asociar la tablet/PC a una caja con un código | Reutiliza v1 |
| Login de cajero | Elegir usuario y digitar PIN | Reutiliza v1; añade bloqueo por intentos |
| Abrir caja | Efectivo inicial; retomar sesión abierta | Reutiliza v1 |
| Venta | Catálogo por categoría, búsqueda/escáner, ticket, cliente | Precios de la sucursal; muestra existencias bajas |
| Cobro | Efectivo, pasarela (Nequi/QR/link), mixto | **Nuevo**: estado del pago en vivo, cancelar/reintentar |
| Comprobante | Ticket + estado fiscal (pendiente/aceptado/rechazado) | **Nuevo** estado fiscal |
| Ventas del turno | Lista, detalle, solicitar anulación/devolución | Devolución **nueva** |
| Caja | Ingresos/retiros, esperado | Reutiliza v1 |
| Cerrar caja | Conteo, diferencia, pendientes por resolver | Bloqueo por pagos pendientes |

**Flujo principal de venta:** Venta → (cliente) → Cobrar → elegir medio → [efectivo: confirmar] /
[electrónico: esperar confirmación del backend] → venta confirmada → inventario descontado →
documento fiscal en curso → comprobante.

### 5.2 Backoffice
Reutiliza la navegación de 8 módulos de la v1. Pantallas nuevas o ampliadas:

| Módulo | Pantalla | Estado |
|---|---|---|
| Plataforma | Empresas (alta, estado) — solo administrador de plataforma | Nueva |
| Mi negocio | Empresa (datos fiscales), Sucursales, Puntos de venta | Ampliada |
| Productos e inventario | Catálogo, Categorías, Unidades, **Precios por sucursal**, **Existencias**, **Movimientos**, **Ajustes** | Nuevas: precios, existencias, movimientos, ajustes |
| Ventas y caja | Ventas, Turnos, Cierres, **Pagos**, **Devoluciones**, Métodos de pago | Nuevas: pagos, devoluciones |
| Facturación | **Configuración fiscal**, **Numeraciones**, **Documentos** (estado, reintento, descargas) | Nuevo módulo |
| Integraciones | **Pasarela de pagos** (credenciales, webhooks), **Proveedor fiscal** | Nuevo |
| Informes | Ventas, **Conciliación de pagos**, **Reconciliación de inventario**, Auditoría | Nuevas: conciliaciones |
| Configuración | Usuarios, Roles, Impuestos, Historial | Reutiliza |

## 6. Requisitos no funcionales

| Tema | Requisito |
|---|---|
| Aislamiento | Ningún dato de una empresa es visible o modificable desde otra; prueba automática obligatoria por endpoint. |
| Consistencia | Venta, pagos, inventario y documento fiscal con estados separados y transiciones explícitas; ninguna operación externa dentro de una transacción SQL. |
| Idempotencia | Toda operación que pueda reintentarse (crear venta, pagar, descontar inventario, emitir documento, procesar webhook) es idempotente. |
| Auditoría | Ventas, pagos, devoluciones, inventario y documentos fiscales auditados con usuario, fecha y antes/después. |
| Seguridad | Credenciales de terceros cifradas en reposo; secretos fuera del repositorio; HTTPS obligatorio; sin datos de tarjeta en nuestra base. |
| Disponibilidad | El POS requiere conexión (sin modo offline). Si la pasarela o el proveedor fiscal fallan, la venta en efectivo continúa y el documento fiscal queda pendiente de envío. |
| Rendimiento | Pantalla de venta con 500 productos usable en una tablet de gama media; confirmación de venta en efectivo < 1 s en servidor. |
| Moneda y tiempo | COP con 2 decimales; fechas en UTC; día operativo según la zona horaria de la empresa. |
