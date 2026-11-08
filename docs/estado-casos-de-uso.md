# Estado de los casos de uso — Tahona.BakeryPOS v2

Punto de partida para cualquier sesión nueva: qué está hecho, qué falta verificar y qué sigue. Actualizado al
28/10/2026, al cerrar la fase 10. Detalle de cada decisión en `decisions.md`; contrato en `api-contracts.md`;
operación en `runbooks.md`.

**Leyenda.** ✅ implementado (backend + pantalla, compila) · 🔎 implementado, falta verificación real ·
⏳ pendiente · ❌ fuera del MVP por ahora.

> Nada se ha probado de punta a punta en el navegador ni contra los sandbox. Las pruebas unitarias, de integración y
> E2E están escritas pero **no se han ejecutado** (instrucción del dueño). Nada tiene commit todavía.

## 1. Casos de uso abordados (fases 1 a 10)

### M1. Empresas y sucursales — fase 2
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-1.1 | Alta de empresa con su propietario (administrador de plataforma) | ✅ | Plataforma → Empresas; `POST /plataforma/empresas` |
| HU-1.2 | Editar datos de la empresa | ✅ (logo pendiente: falta almacenamiento de archivos, I-17) | Mi negocio → Empresa |
| HU-1.3 | Crear, editar y desactivar sucursales y cajas | ✅ | Mi negocio → Sucursales y cajas |

### M2. Seguridad — fase 2
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-2.1 | Usuarios con rol por sucursal | ✅ | Configuración → Usuarios, Roles |
| HU-2.2 | Un usuario en varias empresas (D-08) | ✅ | Login backoffice → elegir empresa |
| HU-2.3 | Aislamiento entre empresas | ✅ (pruebas escritas, sin ejecutar) | Filtro global por empresa |
| HU-2.4 | Entrar a la caja con nombre + PIN | ✅ | POS → `/login` |
| — | Primer administrador de plataforma en un ambiente nuevo | ✅ | `bootstrap-admin` (fase 10) |

### M3. Catálogo y precios — fase 3
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-3.1 | Productos con categoría, unidad, impuesto y código de barras | ✅ | Productos → Catálogo y precios, Categorías, Unidades |
| HU-3.2 | Precio base y por sucursal con vigencia (D-07: IVA incluido) | ✅ | Catálogo → Precio |
| HU-3.3 | Producto no disponible en una sucursal | ✅ | Catálogo (filtro por sucursal) |
| — | Impuestos con código DIAN | ✅ (tarifas a confirmar con el contador, I-20) | Configuración → Impuestos |

### M4. Inventario — fase 4
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-4.1 | Existencia inicial | ✅ | Inventario → Ajustes |
| HU-4.2 | Existencias por sucursal y ubicación | ✅ | Inventario → Existencias |
| HU-4.3 | Descontar al confirmar la venta, una sola vez | ✅ | `SaleConfirmation` |
| HU-4.4 | Ajustes autorizados con motivo | ✅ | Inventario → Ajustes |
| HU-4.5 | Existencias insuficientes por producto + PIN (D-06) | ✅ | Venta → autorización del supervisor |
| HU-4.6 | Reconciliación saldo = Σ movimientos | ✅ (bajo demanda; sin job nocturno) | Inventario → Movimientos |

### M5. Cajas y turnos — fase 5
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-5.1 | Vincular dispositivos a cajas con código de un solo uso | ✅ | Mi negocio → Puntos de venta; POS → `/vincular` |
| HU-5.2 | Abrir caja con efectivo inicial | ✅ | POS → Abrir caja |
| HU-5.3 | Ingresos y retiros (retiro autorizado) | ✅ | POS → Caja |
| HU-5.4 | Cerrar caja contando; bloqueo con pagos pendientes (D-12) | ✅ | POS → Cerrar caja; backoffice → Turnos, Cierres |

### M6. POS y ventas — fase 8
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-6.1 | Agregar por botón, búsqueda o código de barras | ✅ | POS → Venta |
| HU-6.2 | Descuento con motivo (`ventas.descuento`) | ✅ (autorización por otro usuario pendiente, I-25) | Ticket → % |
| HU-6.3 | Identificar al cliente (NIT/cédula o consumidor final) | ✅ | Ticket |
| HU-6.4 | Cobrar en efectivo con cambio, pasarela o pago mixto | ✅ efectivo/manual · 🔎 Bold | Cobro |
| HU-6.5 | Ver el estado del pago electrónico en vivo | 🔎 (sin sandbox de Bold) | Cobro |
| HU-6.6 | Comprobante con estado de la factura | ✅ | Comprobante |
| — | Venta como proceso persistente con 4 estados (I-25) | ✅ | `POST /ventas` … |

### M7. Pagos — fase 6 (D-03: Bold)
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-7.1 | Cobro en la pasarela con referencia idempotente | 🔎 | `GatewayPaymentService` |
| HU-7.2 | Webhook firmado, sin duplicados | 🔎 | `POST /webhooks/pagos/bold` |
| HU-7.3 | Resolver pagos desconocidos | ✅ manual auditado (Bold no tiene consulta, I-23) | Ventas y caja → Pagos |
| HU-7.4 | Conciliar pagos contra ventas y cajas | ✅ consulta · ⏳ conciliación con extracto | Pagos, Informe de ventas |
| — | Medios de pago y credenciales cifradas | ✅ | Métodos de pago, Integraciones |

### M8. Facturación electrónica — fases 7 y 9 (D-04 Siigo, D-05 factura en cada venta)
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-8.1 | Configuración fiscal, numeración y credenciales | ✅ | Configuración → Facturación electrónica, Numeraciones |
| HU-8.2 | Emitir la factura de cada venta y seguir su estado | 🔎 (sin documento aceptado en habilitación) | Outbox → Siigo |
| HU-8.3 | Consultar antes de reintentar | ✅ | Misma Idempotency-Key |
| HU-8.4 | Ver rechazados y reintentar | ✅ | Ventas y caja → Facturas electrónicas |
| HU-8.5 | Nota crédito por anulación (motivo 2) o devolución (motivo 1) | 🔎 (`taxed_price` por verificar) | Fase 9 |

### M9. Anulaciones y devoluciones — fase 9 (D-11)
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-9.1 | Anular una venta (solo con su turno abierto, PIN de supervisor) | ✅ | POS → Ventas del turno → Anular |
| HU-9.2 | Devolución total o parcial, reembolso en efectivo, reingreso por línea | ✅ | POS → Ventas del turno → Devolver; backoffice → Devoluciones |

### M10. Consultas y auditoría — fases 3 a 9
| HU | Caso de uso | Estado | Dónde |
|---|---|---|---|
| HU-10.1 | Consultar ventas, pagos, documentos, caja e inventario | ✅ | Ventas y caja; Inventario |
| HU-10.2 | Historial de cambios | ✅ | Informes → Auditoría; Configuración → Historial |
| — | Informe de ventas (día, sucursal, medio de pago, top productos) | ✅ (error de consulta corregido el 28/10) | Informes → Ventas |
| — | Panel de control del día | ✅ | Panel de control |

### Fase 10 — Despliegue y pruebas (D-10: VPS + Docker Compose)
| Caso de uso | Estado | Dónde |
|---|---|---|
| Desplegar / actualizar / volver atrás | 🔎 (imágenes construyen; nada desplegado) | `backend/deploy/`, `runbooks.md` §2–3 |
| Backups diarios (base + llaves) y restauración | 🔎 | `backup.sh`, `restore.sh`, `runbooks.md` §4–5 |
| E2E del flujo de caja | 🔎 (escrito, sin ejecutar) | `tahona-backerypos-frontend/e2e` |
| Prueba de carga | 🔎 (escrito, sin ejecutar) | `backend/tests/load/sales-load.js` |

## 2. Verificaciones pendientes antes de producción

Lo implementado que todavía no cuenta como terminado (criterios de salida de `testing-strategy.md`):

1. **Probar el flujo en el navegador** con la API local (demo): vincular → PIN → abrir caja → vender → anular → devolver → cerrar → informe.
2. **Ejecutar las pruebas** unitarias, de integración y de arquitectura (`dotnet test`) y las del frontend (`ng test`).
3. **Bold en sandbox**: cobro de prueba, venta con datáfono, webhook firmado real (fase 6).
4. **Siigo en habilitación**: factura, nota crédito por anulación y por devolución aceptadas con CUFE/CUDE (fases 7 y 9).
5. **Staging**: primer despliegue, E2E (`npm run e2e`), carga (k6) y una restauración de backup (fase 10).
6. **Contador**: D-05 (factura en cada venta), D-11 (anulación/devolución), D-15 (5 años), tarifas de impuestos (I-20), permisos del Cajero.
7. **Commits**: nada tiene commit; revisar y commitear por fase en `backend` y `frontend`.

## 3. Próximos casos de uso

### Prioridad alta (para operar)
| Caso de uso | Por qué | Dependencias |
|---|---|---|
| Datos de demostración (empresa, sucursal, caja, productos, cajero y supervisor con PIN) | Hacer la demo sin alta manual | Solo desarrollo |
| Versionar `docs/` (D-13) | Hoy `SoftwerePOS-v2/docs` no está en ningún repositorio | Decidir: dentro del repo backend o repo propio |
| Autorización de descuentos por un supervisor | Hoy solo quien tiene `ventas.descuento` puede dar descuento | I-25 |
| Arqueos (conteo parcial de caja sin cerrar) | Menú "Próximamente" en Ventas y caja | Fase 5 |
| Conciliación de pagos de Bold contra el extracto | HU-7.4 completo | Bold en sandbox |
| Job nocturno de reconciliación de inventario | Hoy solo bajo demanda (I-21) | — |

### Menús de la v1 que siguen como "Próximamente" (sin backend)
| Menú | Casos de uso |
|---|---|
| Mi negocio | Bodegas (varias ubicaciones por sucursal) |
| Productos e inventario | Transferencias entre sucursales/ubicaciones |
| Producción | Recetas, Órdenes de producción, Consumo de ingredientes, Lotes, Mermas |
| Abastecimiento | Proveedores, Compras, Recepciones |
| Informes | Costos, Márgenes, Inventario, Mermas |

Orden sugerido: Bodegas → Transferencias → Proveedores/Compras/Recepciones → Recetas/Producción/Consumo → Lotes/Mermas →
informes de Costos/Márgenes. Cada uno necesita decisiones de negocio (D-xx) antes de implementar.

### Fuera del MVP por ahora
❌ Venta sin conexión (se retiró en la fase 8) · ❌ impresión física del comprobante · ❌ logo de empresa ·
❌ registro de contenedores y CI de despliegue automático.

## 4. Cómo trabajar en las próximas sesiones

1. **Una sesión por tarea acotada** (una fase, un menú o una verificación), no una conversación larga para todo.
   Al empezar: "Lee `CLAUDE.md` y `docs/estado-casos-de-uso.md` y sigue con <tarea>".
2. **La fuente de verdad son los archivos, no la conversación:** `CLAUDE.md` (raíz, backend y frontend),
   `docs/decisions.md`, `docs/implementation-plan.md` y este archivo. Al cerrar cada tarea se actualizan.
3. **Commit al terminar cada tarea** (después de tu revisión): deja un punto de retorno y permite revisar por partes.
4. **Decisiones de negocio siempre por escrito** en `decisions.md` antes de implementar (las D-xx que bloquean).
5. **Verificar antes de avanzar:** cada fase nueva arranca después de probar la anterior en el navegador o sandbox.
