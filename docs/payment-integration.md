# Integración con pasarela de pagos

Estado: **D-03 aprobada: Bold, con su API de datáfono ("API Integrations")** (27/10/2026). Lo verificado
en la documentación oficial está en la sección 11; lo que sigue *por verificar* se confirma en el sandbox
del comercio antes de producción. Nada se implementa sin estar en la documentación oficial.

## 1. Contexto: pago presencial en una panadería

El cobro es presencial. En Colombia los medios relevantes son efectivo, tarjeta en datáfono,
Nequi/Daviplata, QR bancario (Bre-B/Bancolombia) y transferencias. Las pasarelas con API pública
están orientadas a comercio electrónico (el cliente paga desde su celular o con un link/QR); la
integración de **datáfonos físicos** por API no tiene documentación pública verificada.

## 2. Opciones evaluadas

| Criterio | Wompi (Bancolombia) | Bold | Otras (PayU, Mercado Pago, ePayco) |
|---|---|---|---|
| Documentación pública de API | **Sí** (docs.wompi.co) | Parcial: existe portal de desarrolladores; detalles vistos solo en fuentes de terceros | Sí (no evaluadas a fondo) |
| Sandbox | **Sí**, con URL de eventos separada para sandbox y producción | *Por verificar* | — |
| Métodos útiles en mostrador | `NEQUI` (push al celular), `BANCOLOMBIA_QR` (escanear en la app; solo personas naturales), `CARD` (sin presencia de tarjeta), `DAVIPLATA` (OTP) | Link de pago / botón; datáfono propio | — |
| Consulta de estado | **Sí**: `GET /v1/transactions/{id}` con llave privada | *Por verificar* | — |
| Estados | **`PENDING`, `APPROVED`, `DECLINED`, `VOIDED` (solo tarjeta), `ERROR`** | *Por verificar* | — |
| Webhooks firmados | **Sí**: evento `transaction.updated`; checksum SHA256 de los campos de `signature.properties` + `timestamp` + secreto de eventos; header `X-Event-Checksum`; 3 reintentos en 24 h | Webhook mencionado por terceros | — |
| Referencia única | **Sí**: `reference` debe ser única | *Por verificar* | — |
| Idempotencia explícita en la creación | No documentada en lo revisado | *Por verificar* | — |
| Datáfono integrado por API | No | Posible, sin documentación pública | — |

Fuentes revisadas (27/10/2026): [Eventos Wompi](https://docs.wompi.co/en/docs/colombia/eventos/),
[Seguimiento de transacciones Wompi](https://docs.wompi.co/en/docs/colombia/seguimiento-de-transacciones/),
[Métodos de pago Wompi](https://docs.wompi.co/en/docs/colombia/metodos-de-pago/). Sobre Bold no se
encontró documentación oficial accesible; lo visto (endpoint de links de pago, URL base) proviene de
plugins de terceros y **no se usará** como base.

## 3. Recomendación (para aprobar)

1. **Wompi** como pasarela del MVP para pagos electrónicos iniciados desde el POS: `NEQUI` (push) y
   `BANCOLOMBIA_QR` (QR en pantalla), y opcionalmente link de pago con tarjeta.
2. **Tarjeta en datáfono externo** (Bold, Redeban, etc.) como medio **no integrado**: el cajero cobra
   en el datáfono y registra el número de aprobación. Se marca como `external_card` y entra en la
   conciliación manual. Es explícito que **no** es una confirmación de la pasarela.
3. Antes de implementar: crear cuenta sandbox, confirmar con Wompi los puntos *por verificar* de la
   sección 7 y validar comisiones/tiempos de abono con el dueño.

## 4. Flujo de pago con pasarela

```
POS                 API (Payments)                       Wompi
 │ POST /ventas/{id}/pagos ─► TX: payment + attempt 'created'
 │                          (reference = attempt.id)
 │                          fuera de TX: POST transacción ──────────►
 │                          ◄── id externo, PENDING (o timeout)
 │                          TX: attempt 'pending' (+ external_id) | 'unknown' si timeout
 │ ◄── { estado, accion: QR / push }
 │ GET /ventas/{id}/estado (polling 2–3 s)
 │                                       ◄── webhook transaction.updated (firmado)
 │                          TX: inbound_event (dedupe) → attempt 'approved' → confirmar venta
 │ ◄── confirmada
```
- **Nunca** se aprueba un pago por una respuesta del frontend o por el retorno de una redirección:
  solo por webhook válido o consulta servidor-a-servidor con llave privada.
- El monto se envía en centavos y se compara contra el del intento al recibir el evento; si no
  coincide, el intento pasa a `error` y se alerta.

## 5. Idempotencia y duplicados

| Riesgo | Control |
|---|---|
| El POS reintenta "cobrar" | `Idempotency-Key` en `POST /ventas/{id}/pagos` → mismo intento |
| Dos intentos activos para la misma venta | Un solo intento `pending/unknown` por pago; uno nuevo solo si el anterior es final no aprobado |
| Webhook repetido | `inbound_events` UQ(provider, external_event_id) o (provider, transaction id, status) — *clave exacta por verificar* |
| Webhook falsificado | Checksum validado con el secreto de eventos; evento inválido → 401 y auditoría |
| Timeout al crear | Estado `unknown`; consulta por id externo o por referencia antes de crear otro intento |

## 6. Recuperación de resultados desconocidos

Worker cada 15 s para intentos `pending`/`unknown`:
1. Si hay `external_id` → `GET /v1/transactions/{id}`.
2. Si no hay `external_id` (timeout al crear) → buscar por referencia (*endpoint por verificar*).
   Si no existe en la pasarela tras N minutos → `expired`, se permite un nuevo intento.
3. Pasado el tiempo máximo del método (p. ej. Nequi) sin estado final → alerta al cajero y al dueño;
   la venta queda `pending_payment` y bloquea el cierre de caja hasta resolverse (D-12).

## 7. Por verificar con el proveedor antes de implementar

1. Endpoint y payload de creación de transacciones para `NEQUI` y `BANCOLOMBIA_QR`; vigencia del QR.
2. Consulta de transacción por `reference`.
3. Clave única de evento para deduplicar webhooks.
4. Reembolsos/anulaciones: `VOIDED` aplica solo a tarjeta según la documentación; cómo se reembolsa
   un pago Nequi o QR (¿API o proceso manual?).
5. Firma de integridad requerida al crear transacciones (si aplica) y formato de llaves por ambiente.
6. Tiempos de abono, comisiones y certificaciones exigidas al comercio.

## 8. Reembolsos y devoluciones

- Devolución de una venta pagada en efectivo → reembolso en efectivo desde la caja abierta
  (`cash_movements.refund_cash`).
- Pagada por pasarela → reembolso por API si el proveedor lo soporta para ese método; si no, reembolso
  **manual** registrado con soporte y aprobado por el propietario. El estado del reembolso es propio
  (`refunds.status`) y no se mezcla con el de la devolución.

## 9. Conciliación

Diaria por empresa: pagos aprobados en la pasarela (consulta/reportes del proveedor, *por verificar*)
vs. intentos aprobados vs. ventas confirmadas. Diferencias → informe con acciones (reembolsar,
confirmar venta, investigar).

## 10. Credenciales

Llave pública, llave privada y secreto de eventos **por empresa y ambiente** en
`pay.gateway_accounts`, cifradas con Data Protection; nunca devueltas por la API ni escritas en logs.
Sandbox y producción en registros separados; el arranque en producción rechaza llaves de sandbox.
No se guardan datos de tarjeta (si se usa `CARD`, la tokenización ocurre en el widget/checkout del
proveedor).

## 11. Bold — API de datáfono (verificado en developers.bold.co, 27/10/2026)

Fuentes: [API Integrations](https://developers.bold.co/api-integrations/integration),
[Sandbox](https://developers.bold.co/api-integrations/integration-sandbox), [Webhook](https://developers.bold.co/webhook),
[Llaves](https://developers.bold.co/pagos-en-linea/llaves-de-integracion),
[Consulta de transacciones](https://developers.bold.co/pagos-en-linea/consulta-de-transacciones).
La búsqueda previa (sección 2) no encontró la documentación; sí existe y es la base de esta integración.

| Tema | Verificado |
|---|---|
| URL base | `https://integrations.api.bold.co` (la misma para pruebas y producción; el ambiente lo define la llave) |
| Autenticación | `Authorization: x-api-key <llave_de_identidad>`; llaves de pruebas y de producción en panel.bold.co → Integraciones → API datáfono |
| Métodos habilitados | `GET /payments/payment-methods` → `POS` (tarjeta en el datáfono), `NEQUI`, `PAY_BY_QR_BOLD`, `PAY_BY_LINK` |
| Datáfonos | `GET /payments/binded-terminals` → `terminal_model`, `terminal_serial`, `status`, `name` |
| Crear cobro | `POST /payments/app-checkout` con `amount` (`currency`, total, `taxes`, propina), `user_email` (vendedor), `payment_method`, `terminal_model`, `terminal_serial`, `reference`; 201 → `payload.integration_id` (solo para soporte). Errores AP001–AP006 |
| Resultado | **Solo por webhook**: `SALE_APPROVED`, `SALE_REJECTED`, `VOID_APPROVED`, `VOID_REJECTED`; `data.metadata.reference` trae nuestra referencia y `data.payment_id` el id de Bold |
| Firma del webhook | Encabezado `x-bold-signature` = HMAC-SHA256 en hexadecimal del **cuerpo en Base64**, con la llave secreta; en pruebas la llave es vacía |
| Reintentos del webhook | Responder 200 en ≤ 2 s; hasta 5 reintentos (15 min, 1 h, 4 h, 8 h, 24 h). Bold puede notificar varias veces la misma transacción: se deduplica por `id` de la notificación y se valida por `payment_id` |
| Sandbox | Resultado por monto: 1.000–2.000.000 aprobado; 111.111, 222.222, 333.333, 444.444 y 999.999 rechazos simulados. Los datáfonos de pruebas son los mismos del comercio |
| Consulta de estado | **No existe para app-checkout** (la de `payment-voucher` es solo del botón de pagos) |
| Cancelación remota | No existe: el cobro no se puede cancelar desde el sistema |

**Por verificar en el sandbox del comercio:**
1. Nombres de los campos del monto: la tabla de la documentación dice `amount.total` y `amount.tip`, el
   ejemplo usa `total_amount` y `tip_amount`. Se implementó el del ejemplo (`BoldClient`); si el sandbox
   responde AP005/AP006 se cambia en un solo lugar.
2. Si los webhooks de transacciones de prueba de API Integrations llegan al webhook de pruebas (la página
   tiene dos frases que se contradicen).
3. Unicidad de `reference` (no está documentada; se envía el id del intento, que es único).
4. Reembolso o anulación de pagos Nequi/QR por API (no documentado): reembolso manual (sección 8).

**Consecuencias en el diseño (I-23):** sin consulta de estado no hay worker de recuperación que pregunte
a Bold; un intento sin respuesta queda `pending`/`unknown` hasta que llegue el webhook. Si no llega, un
superior con `pagos.resolver` lo resuelve a mano: **aprobado** registrando el número de aprobación del
recibo del datáfono (queda marcado "aprobado manualmente", auditado y señalado en la conciliación),
**rechazado** o **cancelado**. Como no hay cancelación remota, antes de cancelar hay que verificar en el
datáfono que el cobro no se hizo.
