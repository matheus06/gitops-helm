import http from 'k6/http';
import { check, sleep } from 'k6';

// Load test configuration
export const options = {
  stages: [
    { duration: '30s', target: 10 },   // Ramp up to 10 users
    { duration: '1m', target: 10 },    // Stay at 10 users
    { duration: '30s', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],  // 95% of requests under 500ms
    http_req_failed: ['rate<0.01'],    // Less than 1% failures
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://order.local';

export function setup() {
  console.log(`Testing Order Service at: ${BASE_URL}`);
}

export default function () {
  // GET all orders
  const ordersRes = http.get(`${BASE_URL}/api/orders`);
  check(ordersRes, {
    'GET /orders status is 200': (r) => r.status === 200,
    'GET /orders response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);

  // GET orders by customer
  const customerId = `customer-${Math.floor(Math.random() * 100)}`;
  const customerOrdersRes = http.get(`${BASE_URL}/api/orders/customer/${customerId}`);
  check(customerOrdersRes, {
    'GET /orders/customer/:id status is 200': (r) => r.status === 200,
  });

  sleep(1);

  // POST new order (simulating real user behavior)
  if (Math.random() < 0.2) { // 20% of iterations create orders
    const payload = JSON.stringify({
      customerId: `customer-${Math.floor(Math.random() * 1000)}`,
      items: [
        { productId: Math.floor(Math.random() * 5) + 1, quantity: Math.floor(Math.random() * 3) + 1 },
        { productId: Math.floor(Math.random() * 5) + 1, quantity: Math.floor(Math.random() * 2) + 1 },
      ],
    });

    const createRes = http.post(`${BASE_URL}/api/orders`, payload, {
      headers: { 'Content-Type': 'application/json' },
    });

    check(createRes, {
      'POST /orders status is 201': (r) => r.status === 201,
      'POST /orders response time < 1000ms': (r) => r.timings.duration < 1000,
    });
  }

  sleep(1);
}

export function teardown(data) {
  console.log('Load test completed');
}
