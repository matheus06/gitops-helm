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

const BASE_URL = __ENV.BASE_URL || 'http://product.local';

export function setup() {
  console.log(`Testing Product Service at: ${BASE_URL}`);
}

export default function () {
  // GET all products (most common operation)
  const productsRes = http.get(`${BASE_URL}/api/products`);
  check(productsRes, {
    'GET /products status is 200': (r) => r.status === 200,
    'GET /products response time < 500ms': (r) => r.timings.duration < 500,
    'GET /products returns array': (r) => {
      try {
        const body = JSON.parse(r.body);
        return Array.isArray(body);
      } catch {
        return false;
      }
    },
  });

  sleep(1);

  // GET single product
  const productId = Math.floor(Math.random() * 5) + 1;
  const productRes = http.get(`${BASE_URL}/api/products/${productId}`);
  check(productRes, {
    'GET /products/:id status is 200 or 404': (r) => r.status === 200 || r.status === 404,
  });

  sleep(1);

  // POST new product (less frequent)
  if (Math.random() < 0.1) { // 10% of iterations
    const payload = JSON.stringify({
      name: `Load Test Product ${Date.now()}`,
      price: Math.round(Math.random() * 100 * 100) / 100,
      stock: Math.floor(Math.random() * 100),
    });

    const createRes = http.post(`${BASE_URL}/api/products`, payload, {
      headers: { 'Content-Type': 'application/json' },
    });

    check(createRes, {
      'POST /products status is 201': (r) => r.status === 201,
    });
  }

  sleep(1);
}

export function teardown(data) {
  console.log('Load test completed');
}
