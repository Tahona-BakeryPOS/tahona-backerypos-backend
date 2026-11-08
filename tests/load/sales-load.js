// Prueba de carga básica con k6 (fase 10): varias cajas venden en efectivo al mismo tiempo.
// Mide la latencia de crear venta + pagar y que no haya errores. Solo contra staging o local, nunca producción.
//
// Prepara antes (runbook "Prueba de carga"): una caja con sesión abierta por cada VU, o una sola caja compartida, y un
// token de cajero con ventas.crear. Corre:
//   k6 run -e API_URL=https://staging.example.com/api/v1 -e TOKEN=... -e CASH_REGISTER_ID=... \
//          -e PRODUCT_ID=... -e CASH_METHOD_ID=... -e PRICE=2500 tests/load/sales-load.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const API = __ENV.API_URL;
if (!API || !(/localhost|127\.0\.0\.1|staging/.test(API) || __ENV.ALLOW_ANY_HOST === '1'))
  throw new Error('API_URL debe ser local o de staging.');

export const options = {
  scenarios: {
    // Hora pico de una panadería con varias sedes: hasta 20 cajas cobrando sin pausa durante 3 minutos.
    rush: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '30s', target: 10 },
        { duration: '2m', target: 20 },
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{step:create}': ['p(95)<800'],
    'http_req_duration{step:pay}': ['p(95)<800'],
    checks: ['rate>0.99'],
  },
};

const headers = (key) => ({
  'Content-Type': 'application/json',
  Authorization: `Bearer ${__ENV.TOKEN}`,
  ...(key ? { 'Idempotency-Key': key } : {}),
});

export default function () {
  const price = Number(__ENV.PRICE ?? 2500);
  const quantity = 1 + Math.floor(Math.random() * 3);

  const created = http.post(`${API}/ventas`, JSON.stringify({
    cashRegisterId: __ENV.CASH_REGISTER_ID,
    customerIdentification: null,
    lines: [{ productId: __ENV.PRODUCT_ID, quantity, discountAmount: 0, discountReason: null }],
  }), { headers: headers(uuidv4()), tags: { step: 'create' } });
  if (!check(created, { 'venta creada': (r) => r.status === 201 })) return;
  const sale = created.json();

  const paid = http.post(`${API}/ventas/${sale.id}/pagos`, JSON.stringify({
    paymentMethodId: __ENV.CASH_METHOD_ID, amount: sale.total, amountTendered: sale.total, reference: null,
  }), { headers: headers(uuidv4()), tags: { step: 'pay' } });
  check(paid, {
    'pago registrado': (r) => r.status === 201,
    'venta confirmada': (r) => r.status === 201 && r.json('status') === 'confirmed',
    'total correcto': () => Math.abs(sale.total - price * quantity) < 1,
  });

  sleep(1 + Math.random() * 2);
}
