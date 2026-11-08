# Inventario básico

Estado: **implementado en la fase 4** (D-06 = opción C; detalles en I-21). Controla existencias de productos vendibles; sin recetas,
producción, lotes ni transferencias.

## 1. Modelo

- **Ubicación** (`inv.stock_locations`): cada sucursal tiene una ubicación por defecto ("Mostrador").
  Se pueden crear más (p. ej. "Vitrina", "Bodega") pero no hay transferencias en el MVP.
- **Movimiento** (`inv.stock_movements`): libro mayor de solo inserción. Cantidad con signo.
- **Saldo** (`inv.stock_balances`): proyección `on_hand` por (ubicación, producto), actualizada en la
  **misma transacción** que el movimiento, con concurrencia optimista (`row_version`).
- Solo los productos con `track_stock = true` generan movimientos.

## 2. Tipos de movimiento

| Tipo | Signo | Origen | Autorización |
|---|---|---|---|
| `initial` | + | Registro inicial por sucursal | `inventario.ajustar` |
| `sale` | − | Línea de venta confirmada | Automático |
| `return` | + | Línea de devolución aprobada (si el producto vuelve a la venta) | `devoluciones.aprobar` |
| `void_reversal` | + | Anulación de venta confirmada | `ventas.anular` |
| `adjustment_in` / `adjustment_out` | ± | Conteo, merma, daño, consumo interno | `inventario.ajustar` + motivo |

Las mermas de panadería (producto del día no vendido) se registran como `adjustment_out` con motivo
"Merma" en el MVP; su costeo queda fuera.

## 3. Reglas

1. **No existe edición directa del saldo.** Cualquier cambio es un movimiento.
2. **Idempotencia**: índice único `(company_id, kind, source_type, source_id, source_line_id)`.
   Confirmar dos veces la misma venta no descuenta dos veces.
3. **Descuento por venta confirmada**: ocurre en la transacción de confirmación (paso 4 del flujo),
   nunca al agregar al carrito ni al crear la venta en borrador.
4. **Devolución**: movimiento compensatorio; la devolución puede indicar "no reingresa" (producto
   dañado), y entonces no genera movimiento `return` sino solo el registro de la devolución.
5. **Anulación**: `void_reversal` por cada línea, enlazado a la venta.
6. **Auditoría**: cada movimiento registra usuario, dispositivo, motivo y autorizador.

## 4. Política de existencias insuficientes — D-06 aprobada: opción C

| Opción | Comportamiento | Ventajas | Riesgos |
|---|---|---|---|
| A. Bloquear siempre | La venta no se confirma si `on_hand < cantidad` | Saldos siempre ≥ 0 | En panadería el pan se produce y vende sin registrar entradas a tiempo; frena la caja |
| B. Permitir negativo con aviso | Se vende y el saldo queda negativo; alerta al administrador | Nunca frena la venta | Saldos poco confiables si nadie corrige |
| **C. Configurable por producto + autorización (recomendada)** | Cada producto define `negative_stock_policy` = `block` o `allow`. En `block`, el cajero puede pedir autorización del propietario (PIN) y queda auditado | Pan del día en `allow`, productos empacados en `block` | Algo más de configuración |

Aprobada la C (27/10/2026): `products.negative_stock_policy` (`allow` por defecto). La aplica `InventoryLedger`: con
`block`, un movimiento que dejaría el saldo negativo exige `authorized_by`.

## 5. Reconciliación

- Consulta `GET /inventario/reconciliacion`: compara `stock_balances.on_hand` con `Σ stock_movements`
  por (ubicación, producto) y lista diferencias (deberían ser cero).
- Job nocturno por empresa: ejecuta la reconciliación y registra el resultado; una diferencia genera
  alerta (indica un error de software, no de operación).
- Conteo físico: el administrador registra el conteo y el sistema propone `adjustment_in/out` por la
  diferencia, que se confirman con motivo "Conteo físico".

## 6. Concurrencia

Dos cajas venden el mismo producto a la vez: la actualización del saldo usa `row_version`; ante
conflicto se reintenta la transacción de confirmación (hasta 3 veces). Con la política `block`, la
verificación de existencias se hace dentro de esa transacción con bloqueo de fila
(`UPDLOCK, ROWLOCK`) para no vender dos veces la última unidad.
