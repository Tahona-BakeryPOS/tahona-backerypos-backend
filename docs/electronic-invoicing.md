# Integración con facturación electrónica

Estado: **D-04 = Siigo, D-17 = facturación electrónica opcional por empresa (sustituye a D-05), D-15 = 5 años**. Lo verificado
de la API de Siigo y sus consecuencias están en la sección 9. Pendiente: prueba con un documento aceptado en
ambiente de habilitación (criterio de aceptación de la fase 7) y confirmación del contador (D-05, D-15).

## 1. Marco normativo (resumen, a confirmar con el contador)

| Tema | Lo encontrado | Fuente | Confianza |
|---|---|---|---|
| Documento equivalente electrónico POS | El "tiquete de máquina registradora con sistema P.O.S." pasa a ser **electrónico**; el obligado lo sustituye por el documento equivalente electrónico o por factura electrónica de venta | [DIAN – Documento equivalente electrónico](https://micrositios.dian.gov.co/sistema-de-facturacion-electronica/documento-equivalente-electronico) | Alta |
| Base legal | Resolución DIAN 000165 de 2023 (arts. 15, 19, 20); calendario modificado por Resolución 000008 de 2024 | DIAN; [Actualícese](https://actualicese.com/dian-modifica-el-calendario-de-implementacion-de-documentos-equivalentes-electronicos/) | Alta / media |
| Compilación vigente | Resolución Única DIAN 000227 de 2025 | [Siempre al Día](https://siemprealdia.co/colombia/calendario-del-sistema-de-facturacion-electronica/) | **Media — verificar texto oficial** |
| Fechas de obligatoriedad | Escalonadas en 2024 por tipo de contribuyente; nuevos obligados desde su habilitación | Fuentes secundarias | Media |
| Tope de 5 UVT | Fuentes contradictorias: el texto original lo menciona; Sovos y el concepto DIAN 1563 de 2024 indican que el POS electrónico se expide sin importar el valor | [Sovos](https://sovos.com/co/iva/documento-equivalente-electronico-pos/), [Accounter](https://accounter.co/normatividad/pos-electronico-soporte-de-costos-y-deducciones-en-renta-concepto-1563-dian-de-2024) | **Baja — decisión del contador** |
| Identificación del adquirente | El documento puede identificar al comprador (nombre, NIT); con ello da derecho a IVA descontable y deducción | DIAN | Alta |
| Transmisión, validación previa, CUDE, anexo técnico | No especificado en la página consultada | — | **Por verificar** |

**Implicación (si la empresa está obligada a facturar):** la panadería debe emitir, por cada venta, un documento equivalente electrónico POS o
una factura electrónica, a través de un proveedor tecnológico o del servicio de la DIAN. Qué
documento corresponde en cada caso (consumidor final vs. cliente con NIT, monto) lo define D-05 con el
contador.

> **Opcional por empresa (D-17).** La facturación se enciende o apaga en Configuración → Facturación electrónica. Encendida,
> cada venta genera su factura (§4). Apagada, la venta queda con `fiscal_status = not_required` y no se crea ningún
> documento; lo demás (cobros con Bold, inventario, caja, devoluciones) no cambia. Al apagarla, los documentos que ya
> existen (facturas en trámite y notas crédito de ventas ya facturadas) se siguen enviando con la configuración guardada;
> una venta no facturada no genera nota crédito. Si una empresa está obligada por la DIAN, mantener la facturación apagada
> es responsabilidad de la empresa; el sistema no lo valida.

## 2. Selección del proveedor tecnológico

No se encontró documentación oficial de API pública verificable en esta revisión para los
candidatos. Criterios para decidir:

| Criterio | Peso | Cómo se verifica |
|---|---|---|
| Soporta **documento equivalente POS electrónico** y factura electrónica, más notas | Obligatorio | Documentación + demo |
| API REST documentada con ambiente de pruebas (habilitación DIAN) | Obligatorio | Cuenta sandbox |
| Consulta de estado por id propio o consecutivo (para resultados desconocidos) | Obligatorio | Documentación |
| Idempotencia o rechazo de duplicados por prefijo+número | Obligatorio | Prueba en sandbox |
| Devuelve CUFE/CUDE, XML firmado, PDF/representación gráfica, QR | Obligatorio | Documentación |
| Webhooks de cambio de estado (deseable) | Deseable | Documentación |
| Precio por documento y escalabilidad SaaS (multi-NIT bajo una cuenta) | Alto | Cotización |
| Soporte y SLA | Medio | Contrato |

Candidatos a contactar (sin orden de preferencia): Factus (Halltec), Alegra, Siigo, Dataico, The
Factory HKA, Carvajal, Sovos, y la opción gratuita de la DIAN (no apta para integración vía API).
La recomendación se presentará tras validar al menos dos en sandbox.

## 3. Diseño independiente del proveedor

Puerto `IFiscalDocumentProvider`:
```
SubmitAsync(FiscalDocumentPayload)      → Accepted | Rejected(reasons) | Pending(providerId) | Unknown
GetStatusAsync(providerId | prefix+number) → mismo resultado
GetArtifactsAsync(providerId)            → XML, PDF, QR, CUFE/CUDE
SubmitNoteAsync(...)                     → nota crédito / de ajuste
```
El dominio construye el documento (emisor, adquirente, líneas, impuestos, totales, numeración) y el
adaptador lo traduce al formato del proveedor.

## 4. Ciclo de vida del documento

```
venta confirmada → fiscal_document 'pending' (número asignado del rango vigente, en la misma TX)
worker → 'submitting' → SubmitAsync
   ├─ Accepted  → 'accepted' + CUFE/CUDE + archivos guardados
   ├─ Rejected  → 'rejected' + motivos (no se reintenta solo)
   ├─ Pending   → seguimiento con GetStatusAsync
   ├─ error transitorio (5xx, timeout antes de enviar) → 'temporary_error', reintento con backoff
   └─ Unknown (timeout tras enviar) → 'unknown' → GetStatusAsync obligatorio antes de reenviar
```
Reglas:
- **Nunca** se marca `accepted` sin respuesta verificable del proveedor (CUFE/CUDE y estado DIAN).
- **Nunca** se reenvía un documento `unknown` sin una consulta de estado registrada en
  `fiscal_submission_attempts`. El reenvío usa el **mismo prefijo y número**.
- Los números del rango se asignan con bloqueo de fila y no se reutilizan; un documento rechazado
  conserva su número y la corrección sigue el procedimiento del proveedor (*por verificar*).
- Rango por vencer o agotado → alerta al propietario con anticipación (umbral configurable).

## 5. Configuración fiscal

Por empresa (y sucursal cuando la numeración es por punto de venta): NIT y DV, régimen y
responsabilidades, actividad económica, dirección y municipio (DANE), resoluciones y rangos de
numeración, ambiente (pruebas/producción), credenciales del proveedor cifradas. Pruebas y producción
en registros separados; cambiar a producción requiere confirmación explícita y queda auditado.

## 6. Conservación

XML firmado, PDF/representación gráfica, CUFE/CUDE, respuesta del proveedor y de la DIAN se guardan
en almacenamiento de objetos (rutas en `xml_storage_key`, `pdf_storage_key`) y se conservan según
el plazo legal que confirme el contador (D-15).

## 7. Notas y correcciones

- Devolución o anulación de una venta con documento aceptado → nota crédito / nota de ajuste al
  documento equivalente, según el tipo emitido.
- Venta anulada antes de emitir el documento → el documento no se envía (queda `cancelled`) solo si
  la normativa lo permite (D-11).

## 8. Por verificar

1. Texto oficial vigente (Resolución Única 000227 de 2025) y calendario aplicable a los clientes objetivo.
2. Tope de UVT para el POS electrónico y cuándo se exige factura electrónica.
3. Anexo técnico vigente del documento equivalente POS y si requiere validación previa.
4. Proceso de habilitación y set de pruebas por cada NIT cliente (impacto en el onboarding SaaS).
5. API, sandbox, consulta de estado e idempotencia del proveedor elegido.

## 9. Siigo (verificado el 28/10/2026)

Fuentes oficiales: [Introducción](https://developers.siigo.com/docs/siigoapi/), [Crear factura](https://developers.siigo.com/docs/siigoapi/invoice/1-create-invoice),
[Tipos de comprobante](https://developers.siigo.com/docs/siigoapi/catalog/6-ge-types-of-receipt/), [Idempotencia](https://developers.siigo.com/docs/siigoapi/idempotencia/),
[Partner Id](https://developers.siigo.com/docs/siigoapi/partner-id/), [Bloqueo de usuarios](https://developers.siigo.com/docs/siigoapi/bloqueo-de-usuarios/),
[Facturación electrónica](https://developers.siigo.com/docs/siigoapi/facturacion-electronica/), API Blueprint oficial
([siigoapi.docs.apiary.io](https://siigoapi.docs.apiary.io/)) y el SDK oficial [SiigoSAS/siigo_sdk_javascript](https://github.com/SiigoSAS/siigo_sdk_javascript).

**Por qué cambió D-05 (antes de D-17):** Siigo dice en su página del sistema POS que decidió **no desarrollar el documento
equivalente electrónico POS** y enfocarse en la factura electrónica; ningún otro proveedor revisado (Factus, Alegra,
Dataico, The Factory HKA) documenta públicamente una API para el POS electrónico. La DIAN permite sustituir el
documento equivalente por factura electrónica, así que cada venta se factura electrónicamente: con los datos del
cliente si los da y, si no, como consumidor final. **Confirmar con el contador.**

| Tema | Verificado |
|---|---|
| URL base | `https://api.siigo.com` (recursos en `/v1/...`) |
| Token | `POST /auth` con `username` y `access_key` → `access_token` (Bearer) válido 24 h. Credenciales por empresa, generadas en Siigo Nube |
| Encabezados | `Authorization: Bearer <token>` y `Partner-Id` (nombre de la aplicación, 3–100 alfanuméricos, el mismo para todas las empresas) |
| Crear factura | `POST /v1/invoices`: `document.id` (tipo FV electrónico), `date` (no anterior a hoy), `customer.identification` (debe existir en Siigo), `seller` (usuario de Siigo), `items[]` (`code` de un producto que exista en Siigo, `quantity` ≤ 2 decimales, `price` o `taxed_price` con IVA incluido, `taxes[].id`), `payments[]` (`id` de `/payment-types`, `value`; la suma debe igualar el total), `stamp.send = true` envía a la DIAN |
| Respuesta | `id` (GUID), `prefix`, `number`, `name`, `total`, `stamp.status` (`Draft`, `Accepted`, `Rejected`), `stamp.cufe`, `stamp.errors` |
| Errores DIAN | `GET /v1/invoices/{id}/stamp/errors`; reenviar: `POST /v1/invoices/{id}/stamp`; una rechazada se corrige y reenvía |
| Idempotencia | `Idempotency-Key` en POST de facturas y notas crédito (alfanumérico, máx. 30 según la página; el Blueprint dice 32): repetir la clave devuelve la factura ya creada |
| Notas crédito | `POST /v1/credit-notes` con `invoice` (id de la factura), `reason` (motivo DIAN), `items`, `payments`, `stamp` |
| Representación | `GET /v1/invoices/{id}/pdf`. **No hay endpoint de XML** en la referencia: el XML queda en Siigo Nube |
| Catálogos | `/v1/document-types?type=FV`, `/v1/users`, `/v1/taxes`, `/v1/payment-types`, `/v1/customers`, `/v1/products` |
| Bloqueo | Usuario API bloqueado si en 7 días más del 80 % de las peticiones son errores |
| Pruebas | No hay ambiente de pruebas separado documentado; el ambiente (habilitación o producción) es el de la empresa en Siigo Nube. El Blueprint usa una cuenta de ejemplo `sandbox@siigoapi.com` |

**Consecuencias en el diseño (I-24):**
1. La numeración y las resoluciones las lleva Siigo (tipo de comprobante FV electrónico); la v2 guarda el prefijo y
   número que devuelve Siigo. "Numeraciones" muestra los tipos de comprobante de Siigo, no rangos propios.
2. Cada empresa necesita su cuenta Siigo Nube con API; sus llaves se guardan cifradas como las de Bold.
3. Los productos deben existir en Siigo con el mismo código (SKU): se crean en Siigo la primera vez que se facturan
   (`POST /v1/products`, con el grupo de inventario configurado). Los impuestos y medios de pago se emparejan con los de
   Siigo en la configuración. El consumidor final debe existir en Siigo (identificación configurable, por defecto
   `222222222222`).
4. El envío lo hace el outbox (fuera de la transacción) con `Idempotency-Key` = id del documento en base 36; si el
   resultado es desconocido, repetir el POST con la misma clave funciona como consulta de estado (no hay otra por
   referencia propia).
5. Sin XML por API: se guarda el PDF bajo demanda y el CUFE; el XML se consulta en Siigo Nube. Conservación 5 años (D-15).

**Por verificar con una cuenta Siigo en habilitación:** formato exacto del cuerpo de producto y cliente, valores de
`stamp.status` además de `Accepted`, si `Draft` aparece cuando la DIAN tarda, y el límite real de `Idempotency-Key` (30 o 32).

## 10. Notas crédito con Siigo (fase 9, verificado en developers.siigo.com y el SDK oficial)

| Tema | Detalle |
|---|---|
| Crear | `POST /v1/credit-notes`: `document.id` (tipo de comprobante NC), `date` (no anterior a hoy), `invoice` (GUID de la factura en Siigo), `reason` (motivo DIAN: 1 devolución parcial, 2 anulación de la factura, 3 descuento, 4 ajuste de precio…), `items[]` (`code`, `quantity` ≤ 2 decimales, `price`), `payments[]` (`id`, `value`), `stamp`, `mail` |
| Consultar / PDF | `GET /v1/credit-notes/{id}` y `GET /v1/credit-notes/{id}/pdf` (SDK oficial `SiigoSAS/siigo_sdk_javascript`) |
| Respuesta | `id`, `number`, `name`, `total`, `stamp` (`status`, `cufe`, `cude`, `errors`) |
| Por verificar | La guía usa `price` (sin IVA) y solo el ejemplo del spec muestra `taxed_price`: se envía `taxed_price` como en la factura. No hay endpoint documentado de errores DIAN ni de reenvío para notas: se consulta con `GET`. La documentación no es consistente en el rango de `reason` |

Reglas (D-11, I-26): anulación → nota con motivo 2 por toda la factura; devolución → motivo 1 por lo devuelto, con los
medios reembolsados. La nota espera en el outbox hasta que la factura sea aceptada; si la factura fue rechazada, la nota
queda sin efecto (`cancelled`) y, en una anulación, la factura también.
