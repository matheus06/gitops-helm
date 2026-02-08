import http from 'k6/http';
import { check, sleep } from 'k6';

// Smoke test - quick validation that services are responding
export const options = {
  vus: 1,              // 1 virtual user
  duration: '10s',     // Run for 10 seconds
  thresholds: {
    http_req_failed: ['rate<0.01'],  // Less than 1% failures
    http_req_duration: ['p(95)<1000'], // 95% under 1s
  },
};

const PRODUCT_URL = __ENV.PRODUCT_URL || 'http://product.local';
const ORDER_URL = __ENV.ORDER_URL || 'http://order.local';

export default function () {
  // Product service health check
  const productHealth = http.get(`${PRODUCT_URL}/health`);
  check(productHealth, {
    'product service healthy': (r) => r.status === 200,
  });

  // Order service health check
  const orderHealth = http.get(`${ORDER_URL}/health`);
  check(orderHealth, {
    'order service healthy': (r) => r.status === 200,
  });

  // Basic API calls
  const products = http.get(`${PRODUCT_URL}/api/products`);
  check(products, {
    'get products returns 200': (r) => r.status === 200,
  });

  const orders = http.get(`${ORDER_URL}/api/orders`);
  check(orders, {
    'get orders returns 200': (r) => r.status === 200,
  });

  sleep(1);
}
