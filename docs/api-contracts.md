# Contratos API — Tahona.BakeryPOS MVP v1.1

Estado: **propuesta para revisión**. Se publicará como OpenAPI 3.1 generado desde el código
(`/openapi/v1.json`) una vez aprobado.

## Convenciones

| Tema | Regla |
|---|---|
| Base | `/api/v1`, JSON, HTTPS obligatorio |
| Empresa | Sale del token; nunca viaja como parámetro libre. Un id de otra empresa responde 404 |
| Idempotencia | Header `Idempotency-Key` (UUID) obligatorio en POST que crean ventas, pagos, devoluciones, ajustes y movimientos de caja. Mismo key + mismo cuerpo → misma respuesta; mismo key + otro cuerpo → 409 `IDEMPOTENCIA_CONFLICTO` |
| Concurrencia | `ETag`/`If-Match` en PUT de datos maestros → 412 si cambió, 428 si falta *(I-13)* |
| Dinero | Número decimal con 2 decimales |
| Fechas | ISO 8601 UTC |
| Paginación | `?page=&pageSize=` (máx. 200) → `{ items, page, pageSize, totalCount }` |
| Errores | `{ codigo, mensaje, detalles?: { campo: [mensajes] }, correlationId }` |

## Errores estándar

| HTTP | Código | Cuándo |
|---|---|---|
| 400 | `VALIDACION` | Datos inválidos |
| 401 | `NO_AUTENTICADO` | Token ausente o vencido |
| 403 | `SIN_PERMISO`, `DISPOSITIVO_NO_VINCULADO` | Permiso o alcance insuficiente |
| 404 | `NO_ENCONTRADO` | No existe en la empresa del token |
| 409 | `IDEMPOTENCIA_CONFLICTO`, `CAJA_YA_ABIERTA`, `CAJA_CERRADA`, `ESTADO_INVALIDO` | Conflictos de estado |
| 412 | `CONFLICTO_VERSION` | `If-Match` no coincide |
| 422 | `EXISTENCIAS_INSUFICIENTES` (producto con política `block` sin autorizador), `PAGOS_NO_CUBREN_TOTAL`, `DEVOLUCION_EXCEDE_VENTA`, `PAGO_PENDIENTE` | Reglas de negocio |
| 429 | `LIMITE_EXCEDIDO` | Rate limit (login, vinculación, webhooks) |
| 502/503 | `PROVEEDOR_NO_DISPONIBLE` | Pasarela o proveedor fiscal caído (la operación queda pendiente, no fallida) |

## Endpoints

### Autenticación y plataforma
| Método y ruta | Permiso | Descripción |
|---|---|---|
| `POST /auth/login` | anónimo | Correo + contraseña → lista de empresas o token si hay una |
| `POST /auth/select-company` | sesión | Emite token para una empresa |
| `POST /auth/refresh` | refresh token | Rota el refresh token |
| `POST /auth/login-pin` | token de dispositivo (`X-Device-Token`) | `{ userId, pin }` → token de cajero; 5 fallos → 423 por 5 min |
| `POST /auth/cambiar-contrasena` | sesión | Obligatorio tras la contraseña de un solo uso; cierra las demás sesiones *(I-15)* |
| `POST /auth/logout` | sesión | Revoca el refresh token enviado *(I-15)* |
| `POST /dispositivo/vincular` | anónimo + código | `{ code, deviceName }` → token de dispositivo (se entrega una vez) |
| `GET /dispositivo/usuarios` | token de dispositivo | Usuarios con PIN y asignación vigente en la sucursal de la caja *(I-15)* |
| `POST /plataforma/empresas` | `plataforma.admin` | Alta de empresa + propietario |
| `GET /plataforma/empresas` | `plataforma.admin` | Lista con estado (sin datos de ventas) |

### Empresas, sucursales, usuarios
| Método y ruta | Permiso |
|---|---|
| `GET/PUT /empresa` | `organizacion.gestionar` |
| `GET/POST /sucursales`, `PUT /sucursales/{id}`, `POST /sucursales/{id}/desactivar` | `organizacion.gestionar` |
| `GET/POST /cajas`, `POST /cajas/{id}/codigos-vinculacion`, `GET /dispositivos`, `POST /dispositivos/{id}/desactivar` | `organizacion.gestionar` |
| `GET/POST /usuarios`, `GET/PUT /usuarios/{id}`, `POST /usuarios/{id}/asignaciones`, `DELETE /usuarios/{id}/asignaciones/{asignacionId}` | `organizacion.gestionar` |
| `GET /roles` | `organizacion.gestionar` |

### Catálogo y precios
| Método y ruta | Permiso |
|---|---|
| `GET /productos?sucursalId=&buscar=&categoriaId=&incluirInactivos=&page=&pageSize=` | `productos.ver` — paginado; precio vigente (`price`: el de la sucursal o, si no hay, el base), `basePrice`, `branchPrice`, `priceIncludesTax` y disponibilidad (`isAvailable`) de la sucursal |
| `GET /productos/{id}?sucursalId=` | `productos.ver` — con `ETag` |
| `POST /productos` `{ sku, name, categoryId, unitId, taxId, barcode?, trackStock, basePrice? }` | `catalogo.gestionar` + Idempotency-Key; `basePrice` abre el primer precio base vigente desde ya *(I-20)* |
| `PUT /productos/{id}` (sin SKU), `POST /productos/{id}/desactivar`, `POST /productos/{id}/activar` | `catalogo.gestionar` (PUT con If-Match) |
| `POST /productos/{id}/precios` `{ branchId?, amount, includesTax?=true, validFrom? }` | `catalogo.gestionar` (sucursal: también `precios.sucursal` + alcance) + Idempotency-Key. Sin `validFrom` = desde ahora; no se aceptan fechas pasadas ni anteriores a la última vigencia |
| `GET /productos/{id}/precios` | `productos.ver` — historial (base y por sucursal) |
| `PUT /sucursales/{id}/productos/{productoId}/disponibilidad` `{ isAvailable }` | `precios.sucursal` + alcance |
| `GET/POST /categorias`, `PUT /categorias/{id}` (nombre, orden, estado) | `catalogo.gestionar` (POST con Idempotency-Key, PUT con If-Match) |
| `GET/POST /impuestos`, `PUT /impuestos/{id}` (nombre y estado; la tarifa no cambia) | `catalogo.gestionar` |
| `GET /unidades` | `catalogo.gestionar` — catálogo global |


### Inventario
| Método y ruta | Permiso |
|---|---|
| `GET /existencias?sucursalId=&ubicacionId=&buscar=&page=&pageSize=` | `inventario.ver` — cada producto con `trackStock` por ubicación activa (0 y `hasMovements=false` si no tiene movimientos); paginado por producto |
| `GET /movimientos-inventario?sucursalId=&ubicacionId=&productoId=&tipo=&desde=&hasta=&page=&pageSize=` | `inventario.ver` — `tipo`: initial, sale, return, void_reversal, adjustment_in, adjustment_out |
| `GET /ubicaciones?sucursalId=`, `POST /ubicaciones` `{ branchId, code, name }` | `inventario.ver` / `inventario.ajustar` + Idempotency-Key |
| `GET /inventario/motivos` | `inventario.ver` |
| `POST /inventario/inicial` `{ locationId, lines: [{ productId, quantity }], note? }` | `inventario.ajustar` + Idempotency-Key; una vez por producto y ubicación (409 `INVENTARIO_INICIAL_REGISTRADO`) |
| `POST /inventario/ajustes` `{ locationId, productId, quantity(±), reasonId, note? }` | `inventario.ajustar` + Idempotency-Key; quien registra queda como autorizador; el motivo limita el signo |
| `POST /inventario/conteos` `{ locationId, lines: [{ productId, counted }], note? }` | `inventario.ajustar` + Idempotency-Key — ajusta a lo contado con el motivo "Conteo físico" y devuelve las diferencias |
| `GET /inventario/reconciliacion?sucursalId=` | `inventario.ver` — saldos que no cuadran con Σ movimientos (debe salir vacío) |
| Productos: `negativeStockPolicy` (`allow`/`block`) en `POST/PUT /productos` y en sus respuestas | D-06 |

### Caja
| Método y ruta | Permiso |
|---|---|
| `POST /cajas/{id}/sesiones` `{ openingAmount }` | `turnos.abrir` + Idempotency-Key — 409 `CAJA_YA_ABIERTA` |
| `GET /cajas/{id}/sesion-abierta` | `turnos.abrir` — 404 si no hay (relevo de cajero) |
| `GET /motivos-caja` | `caja.movimientos` |
| `POST /sesiones-caja/{id}/movimientos` `{ kind: income\|expense, amount, reasonId, note? }` | `caja.movimientos` (+ `caja.retiro` para retiros: el superior envía su token) + Idempotency-Key — 422 `RETIRO_EXCEDE_SALDO` |
| `GET /sesiones-caja/{id}/movimientos` | `caja.movimientos` |
| `GET /sesiones-caja/{id}/resumen` | `turnos.cerrar` — esperado desglosado y `pendingPayments` |
| `POST /sesiones-caja/{id}/cierre` `{ countedAmount, notes? }` | `turnos.cerrar` + Idempotency-Key — 422 `PAGO_PENDIENTE` (D-12), 409 `CAJA_CERRADA` |
| `GET /sesiones-caja?desde=&hasta=&sucursalId=&estado=open\|closed` | `reportes.ver` — por fecha de apertura |

### Ventas
| Método y ruta | Permiso | Respuesta |
|---|---|---|
| `POST /ventas` | `ventas.crear` + Idempotency-Key | `{ id, estado:'draft', lineas, totales }` — precios e impuestos los calcula el servidor |
| `PUT /ventas/{id}/cliente` | `ventas.crear` | — |
| `POST /ventas/{id}/pagos` `{ medioPagoId, monto, montoRecibido?, telefono?, referencia? }` | `ventas.crear` + Idempotency-Key | `{ pagoId, intentoId, estado, accion?: { tipo:'qr'|'push'|'redirect', datos } }` |
| `GET /ventas/{id}` | `ventas.ver` | Venta con estados de pago, inventario y fiscal |
| `POST /ventas/{id}/autorizar-existencias` | `inventario.ajustar` (token de quien autoriza) | D-06; solo antes de confirmar |
| `POST /ventas/{id}/cancelar` | `ventas.crear` | Solo `draft`/`pending_payment`; cancela intentos pendientes |
| `POST /ventas/{id}/anulacion` `{ reason }` | `ventas.anular` (token de quien autoriza) | D-11: solo con el turno de la venta abierto (si no, 422 `ANULACION_NO_PERMITIDA`) |
| `GET /ventas?desde=&hasta=&sucursalId=&sesionCajaId=&estado=&page=&pageSize=` | `ventas.ver` | Paginado |

Implementado en las fases 8 (I-25) y 9 (I-26). El POS consulta `GET /ventas/{id}` mientras un pago con pasarela está pendiente; no hay endpoint `/estado` aparte. Errores propios: `EXISTENCIAS_INSUFICIENTES` (422, con los productos faltantes), `PAGO_EN_CURSO` (409), `ESTADO_INVALIDO` (409).

### Devoluciones
| Método y ruta | Permiso |
|---|---|
| `POST /ventas/{id}/devoluciones` `{ cashRegisterId, reason, lines:[{saleLineId, quantity, restock}] }` | `devoluciones.aprobar` (token de quien autoriza) + Idempotency-Key |
| `GET /devoluciones?desde=&hasta=&sucursalId=&page=&pageSize=` | `reportes.ver` |
| `GET /informes/ventas?desde=yyyy-MM-dd&hasta=yyyy-MM-dd&sucursalId=` | `reportes.ver` |

Fase 9 (D-11, I-26): la devolución se autoriza y completa en el momento (no hay `/aprobar`); el reembolso es siempre en
efectivo de la caja indicada. `GET /ventas?numero=` busca una venta por su consecutivo.

### Pagos
| Método y ruta | Permiso |
|---|---|
| `GET /medios-pago` | sesión — los usa el POS para cobrar |
| `POST /medios-pago` `{ code, name, kind, gatewayMethod?, requiresReference, displayOrder }`, `PUT /medios-pago/{id}` | `organizacion.gestionar` (POST con Idempotency-Key, PUT con If-Match); un medio de Bold solo se activa con la integración activa |
| `GET /pagos/{id}` | sesión — el POS lo consulta mientras espera el datáfono |
| `GET /pagos?desde=&hasta=&estado=&sesionCajaId=` | `reportes.ver` |
| `POST /pagos/{id}/resolver` `{ resolution: approved\|declined\|cancelled, approvalNumber?, note }` | `pagos.resolver` — resolución manual auditada cuando el webhook no llegó *(I-23)* |
| `POST /webhooks/pagos/bold` | anónimo; firma `x-bold-signature` obligatoria (401 si no es válida); responde 200 de inmediato |
| `GET/PUT /integraciones/pasarela` `{ environment, identityKey?, secretKey?, sellerEmail, activate }` | `organizacion.gestionar` — nunca devuelve las llaves, solo si están configuradas |
| `GET /integraciones/pasarela/datafonos`, `GET /integraciones/pasarela/metodos` | `organizacion.gestionar` — consulta en vivo a Bold (503 `PROVEEDOR_NO_DISPONIBLE` si no responde) |
| `GET /integraciones/pasarela/cajas`, `PUT /cajas/{id}/datafono` `{ terminalModel, terminalSerial }` | `organizacion.gestionar` — datáfono Bold de cada caja |
| `POST /integraciones/pasarela/prueba` `{ cashRegisterId, paymentMethodId, amount }` | `organizacion.gestionar` + Idempotency-Key — cobro de prueba sin venta en el datáfono de una caja abierta |
| `POST /pagos/intentos/{id}/consultar`, `GET /pagos/conciliacion` | *No aplican con Bold:* su API de datáfono no tiene consulta de estado (payment-integration.md §11) |

### Facturación electrónica
| Método y ruta | Permiso |
|---|---|
| `GET/PUT /facturacion/configuracion` `{ username?, accessKey?, invoiceDocumentTypeId, creditNoteDocumentTypeId, sellerId, productAccountGroupId, consumerFinalIdentification, retentionYears, activate }` | `fiscal.gestionar` — nunca devuelve las credenciales de Siigo *(I-24)* |
| `GET /facturacion/siigo/catalogos` | `fiscal.gestionar` — comprobantes FV/NC, vendedores, impuestos y medios de pago de Siigo (503 si no responde) |
| `GET/PUT /facturacion/mapeos` `{ mappings: [{ kind: tax\|payment_method, localId, externalId }] }` | `fiscal.gestionar` |
| `POST /facturacion/prueba` `{ productId, quantity, paymentMethodId, confirmTestEnvironment: true }` | `fiscal.gestionar` + Idempotency-Key — factura de prueba a consumidor final (va a la DIAN en el ambiente de la empresa en Siigo) |
| `GET /documentos-fiscales?estado=&desde=&hasta=` | `ventas.ver` |
| `GET /documentos-fiscales/{id}/intentos` | `fiscal.gestionar` — cada creación, consulta o envío a la DIAN |
| `POST /documentos-fiscales/{id}/reintentar` | `fiscal.gestionar` — rechazados, con error o sin confirmar; el envío repite la misma Idempotency-Key |
| `GET /documentos-fiscales/{id}/pdf` | `ventas.ver` — de Siigo. **Sin `/xml`**: la API de Siigo no lo expone (queda en Siigo Nube) |
| Numeraciones | No hay endpoints propios: las lleva Siigo; la pantalla muestra sus tipos de comprobante |

### Auditoría
| `GET /auditoria?entidad=&entidadId=&desde=&hasta=&page=&pageSize=` | `organizacion.gestionar` — `entidad` es la tabla con esquema (`cat.products`); del más reciente al más antiguo |
|---|---|
| `GET /auditoria/entidades` | `organizacion.gestionar` — tablas con registros, para filtrar |

## Permisos nuevos respecto a la v1

`plataforma.admin`, `precios.sucursal`, `inventario.ver`, `inventario.ajustar`, `devoluciones.crear`,
`devoluciones.aprobar`, `fiscal.gestionar`, `pagos.resolver` (fase 6), `ventas.descuento` (fase 8). Los de la v1 se conservan.
