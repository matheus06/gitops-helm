# Load Testing Guide

This guide explains how to implement load testing with **k6**.

## Environments

| Environment | How to Run |
|-------------|------------|
| **Local (MicroK8s)** | Run k6 manually from your machine |
| **Cloud (AKS)** | GitHub Actions workflow |

---

## Quick Start

### Local (MicroK8s)

```bash
# Install k6
brew install k6      # macOS
sudo apt install k6  # Ubuntu
choco install k6     # Windows

# Run smoke test (requires /etc/hosts: <VM-IP> product.local order.local)
k6 run tests/load/smoke-test.js \
  --env PRODUCT_URL=http://product.local \
  --env ORDER_URL=http://order.local

# Run full load test
k6 run tests/load/product-service.js --env BASE_URL=http://product.local
k6 run tests/load/order-service.js --env BASE_URL=http://order.local
```

### Cloud (AKS)

Use the GitHub Actions workflow:
1. Go to **Actions** → **Load Tests**
2. Click **Run workflow**
3. Enter your AKS service URLs (e.g., `https://product.example.com`)
4. Select test type and run

---

## Overview

| Tool | Language | Best For | GitHub Actions Support |
|------|----------|----------|------------------------|
| **k6** | JavaScript | Developer-friendly, CI/CD integration | Excellent (lightweight) |
| **Locust** | Python | Complex scenarios, distributed testing | Good |

**Recommendation:** k6 is simpler to integrate in CI/CD pipelines and has lower resource requirements.

---

## Option 1: k6 (Recommended)

### 1.1 Create Load Test Scripts

Create a `tests/load/` directory for load test scripts:

```
tests/
└── load/
    ├── product-service.js
    ├── order-service.js
    └── smoke-test.js
```

**`tests/load/product-service.js`:**
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

// Test configuration
export const options = {
  // Smoke test (quick validation)
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

export default function () {
  // GET all products
  const productsRes = http.get(`${BASE_URL}/api/products`);
  check(productsRes, {
    'products status is 200': (r) => r.status === 200,
    'products response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);

  // GET single product
  const productRes = http.get(`${BASE_URL}/api/products/1`);
  check(productRes, {
    'product status is 200 or 404': (r) => r.status === 200 || r.status === 404,
  });

  sleep(1);
}
```

**`tests/load/order-service.js`:**
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 10 },
    { duration: '1m', target: 10 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://order.local';

export default function () {
  // GET all orders
  const ordersRes = http.get(`${BASE_URL}/api/orders`);
  check(ordersRes, {
    'orders status is 200': (r) => r.status === 200,
    'orders response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);

  // POST new order (write test)
  const payload = JSON.stringify({
    customerId: `customer-${Math.random().toString(36).substr(2, 9)}`,
    items: [
      { productId: 1, quantity: 2 },
      { productId: 2, quantity: 1 },
    ],
  });

  const createRes = http.post(`${BASE_URL}/api/orders`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(createRes, {
    'create order status is 201': (r) => r.status === 201,
  });

  sleep(1);
}
```

**`tests/load/smoke-test.js`** (Quick health check):
```javascript
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 1,              // 1 virtual user
  duration: '10s',     // Run for 10 seconds
  thresholds: {
    http_req_failed: ['rate<0.01'],
  },
};

const PRODUCT_URL = __ENV.PRODUCT_URL || 'http://product.local';
const ORDER_URL = __ENV.ORDER_URL || 'http://order.local';

export default function () {
  // Health checks
  const productHealth = http.get(`${PRODUCT_URL}/health`);
  check(productHealth, {
    'product service healthy': (r) => r.status === 200,
  });

  const orderHealth = http.get(`${ORDER_URL}/health`);
  check(orderHealth, {
    'order service healthy': (r) => r.status === 200,
  });
}
```

### 1.2 Add GitHub Actions Workflow

**`.github/workflows/load-test.yaml`:**
```yaml
name: Load Tests

on:
  # Run after deployment to dev
  workflow_run:
    workflows: ["CD - Dev"]
    types:
      - completed

  # Manual trigger
  workflow_dispatch:
    inputs:
      target_env:
        description: 'Target environment URL base'
        required: true
        default: 'http://product.local'
      test_type:
        description: 'Test type (smoke, load, stress)'
        required: true
        default: 'smoke'
        type: choice
        options:
          - smoke
          - load
          - stress

jobs:
  load-test:
    runs-on: ubuntu-latest
    if: ${{ github.event.workflow_run.conclusion == 'success' || github.event_name == 'workflow_dispatch' }}

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Install k6
        run: |
          sudo gpg -k
          sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
          echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
          sudo apt-get update
          sudo apt-get install k6

      - name: Run smoke test
        if: ${{ github.event.inputs.test_type == 'smoke' || github.event_name == 'workflow_run' }}
        run: |
          k6 run tests/load/smoke-test.js \
            --env PRODUCT_URL=${{ github.event.inputs.target_env || 'http://product.local' }} \
            --env ORDER_URL=${{ github.event.inputs.target_env || 'http://order.local' }}

      - name: Run product service load test
        if: ${{ github.event.inputs.test_type == 'load' }}
        run: |
          k6 run tests/load/product-service.js \
            --env BASE_URL=${{ github.event.inputs.target_env }}

      - name: Run order service load test
        if: ${{ github.event.inputs.test_type == 'load' }}
        run: |
          k6 run tests/load/order-service.js \
            --env BASE_URL=${{ github.event.inputs.target_env }}

      - name: Upload results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: k6-results
          path: |
            *.json
            *.html
```

### 1.3 Run Locally

```bash
# Install k6
# macOS
brew install k6

# Ubuntu/Debian
sudo apt-get install k6

# Windows
choco install k6

# Run smoke test
k6 run tests/load/smoke-test.js --env PRODUCT_URL=http://product.local --env ORDER_URL=http://order.local

# Run with more users (stress test)
k6 run tests/load/product-service.js --vus 50 --duration 5m --env BASE_URL=http://product.local
```

---

## Option 2: Locust

### 2.1 Create Locust Test Files

**`tests/load/locustfile.py`:**
```python
from locust import HttpUser, task, between
import random
import string

class ProductServiceUser(HttpUser):
    wait_time = between(1, 3)

    @task(3)
    def get_products(self):
        self.client.get("/api/products")

    @task(1)
    def get_single_product(self):
        product_id = random.randint(1, 10)
        self.client.get(f"/api/products/{product_id}")

    @task(1)
    def create_product(self):
        self.client.post("/api/products", json={
            "name": f"Test Product {''.join(random.choices(string.ascii_letters, k=5))}",
            "price": round(random.uniform(10, 100), 2),
            "stock": random.randint(1, 100)
        })


class OrderServiceUser(HttpUser):
    wait_time = between(1, 3)

    @task(3)
    def get_orders(self):
        self.client.get("/api/orders")

    @task(1)
    def create_order(self):
        self.client.post("/api/orders", json={
            "customerId": f"customer-{''.join(random.choices(string.ascii_letters, k=8))}",
            "items": [
                {"productId": random.randint(1, 5), "quantity": random.randint(1, 3)},
            ]
        })
```

**`tests/load/requirements.txt`:**
```
locust>=2.15.0
```

### 2.2 GitHub Actions with Locust

**`.github/workflows/load-test-locust.yaml`:**
```yaml
name: Load Tests (Locust)

on:
  workflow_dispatch:
    inputs:
      target_url:
        description: 'Target URL'
        required: true
        default: 'http://product.local'
      users:
        description: 'Number of users'
        required: true
        default: '10'
      duration:
        description: 'Test duration (e.g., 1m, 5m)'
        required: true
        default: '1m'

jobs:
  locust-test:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.11'

      - name: Install dependencies
        run: |
          pip install -r tests/load/requirements.txt

      - name: Run Locust
        run: |
          locust -f tests/load/locustfile.py \
            --headless \
            --host=${{ github.event.inputs.target_url }} \
            --users=${{ github.event.inputs.users }} \
            --spawn-rate=5 \
            --run-time=${{ github.event.inputs.duration }} \
            --html=locust-report.html \
            --csv=locust-results

      - name: Upload results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: locust-results
          path: |
            locust-report.html
            locust-results*.csv
```

### 2.3 Run Locust Locally

```bash
# Install
pip install locust

# Run with web UI
locust -f tests/load/locustfile.py --host=http://product.local
# Open http://localhost:8089

# Run headless
locust -f tests/load/locustfile.py \
  --headless \
  --host=http://product.local \
  --users=10 \
  --spawn-rate=2 \
  --run-time=1m
```

---

## Integration with CD Pipeline

### Add Load Test Stage to CD

Modify `.github/workflows/cd-dev.yaml` to include load testing after deployment:

```yaml
jobs:
  # ... existing deploy job ...

  load-test:
    needs: deploy
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Install k6
        run: |
          sudo apt-get update
          sudo apt-get install -y k6 || {
            wget https://github.com/grafana/k6/releases/download/v0.47.0/k6-v0.47.0-linux-amd64.tar.gz
            tar xzf k6-v0.47.0-linux-amd64.tar.gz
            sudo mv k6-v0.47.0-linux-amd64/k6 /usr/local/bin/
          }

      - name: Wait for deployment to stabilize
        run: sleep 60

      - name: Run smoke tests
        run: |
          k6 run tests/load/smoke-test.js \
            --env PRODUCT_URL=https://product-dev.yourdomain.com \
            --env ORDER_URL=https://order-dev.yourdomain.com
        continue-on-error: true

      - name: Upload results
        uses: actions/upload-artifact@v4
        with:
          name: load-test-results
          path: "*.json"
```

---

## Test Types Explained

| Type | Purpose | Users | Duration | When to Use |
|------|---------|-------|----------|-------------|
| **Smoke** | Verify service is working | 1-5 | 10-30s | After every deployment |
| **Load** | Normal expected traffic | 10-100 | 5-15min | Before release |
| **Stress** | Find breaking point | 100-1000+ | 15-30min | Capacity planning |
| **Soak** | Find memory leaks | 50-100 | 1-4 hours | Periodically |

---

## Thresholds and SLOs

Define performance requirements in your tests:

```javascript
export const options = {
  thresholds: {
    // Response time
    http_req_duration: [
      'p(50)<200',   // 50% of requests under 200ms
      'p(90)<400',   // 90% under 400ms
      'p(95)<500',   // 95% under 500ms (SLO)
      'p(99)<1000',  // 99% under 1s
    ],

    // Error rate
    http_req_failed: ['rate<0.01'],  // Less than 1% errors

    // Throughput
    http_reqs: ['rate>100'],  // At least 100 req/s
  },
};
```

---

## Viewing Results

### k6 Cloud (Optional)

For dashboards and historical data:

```bash
# Sign up at https://app.k6.io
k6 login cloud
k6 run --out cloud tests/load/product-service.js
```

### Grafana + InfluxDB (Self-hosted)

```bash
# Run k6 with InfluxDB output
k6 run --out influxdb=http://localhost:8086/k6 tests/load/product-service.js
```

---

## Directory Structure

```
tests/
└── load/
    ├── product-service.js    # Product service load test
    ├── order-service.js      # Order service load test
    ├── smoke-test.js         # Quick smoke test
    ├── stress-test.js        # High load stress test
    ├── locustfile.py         # Locust alternative
    └── requirements.txt      # Python dependencies for Locust
```

---

## Quick Start

```bash
# 1. Create test directory
mkdir -p tests/load

# 2. Create smoke test (copy from above)

# 3. Install k6
brew install k6  # or apt-get install k6

# 4. Run locally
k6 run tests/load/smoke-test.js --env PRODUCT_URL=http://product.local

# 5. Add to pipeline (copy workflow from above)
```
