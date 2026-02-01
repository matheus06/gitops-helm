# Observability Guide

This guide explains the observability stack used in this project, covering metrics, traces, and logs.

## Overview

Observability is built on three pillars:

| Pillar | What it answers | Tool |
|--------|-----------------|------|
| **Metrics** | How is the system performing? | Prometheus + Grafana |
| **Traces** | What path did a request take? | Tempo |
| **Logs** | What happened at a specific moment? | Loki |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      Microservices                              │
│  ┌─────────────────┐              ┌─────────────────┐           │
│  │ product-service │              │  order-service  │           │
│  │                 │              │                 │           │
│  │ OpenTelemetry   │              │ OpenTelemetry   │           │
│  │ SDK (.NET)      │              │ SDK (.NET)      │           │
│  └────────┬────────┘              └────────┬────────┘           │
│           │         OTLP (gRPC)            │                    │
│           └───────────────┬────────────────┘                    │
│                           ▼                                     │
│              ┌─────────────────────────┐                        │
│              │    OTel Collector       │                        │
│              │                         │                        │
│              │  Receivers:  OTLP       │                        │
│              │  Processors: Batch      │                        │
│              │  Exporters:  Prometheus │                        │
│              │              Tempo      │                        │
│              └─────────────┬───────────┘                        │
│                            │                                    │
└────────────────────────────┼────────────────────────────────────┘
                             │
           ┌─────────────────┼─────────────────┐
           ▼                 ▼                 ▼
    ┌────────────┐    ┌────────────┐    ┌────────────┐
    │ Prometheus │    │   Tempo    │    │    Loki    │
    │  (metrics) │    │  (traces)  │    │   (logs)   │
    └──────┬─────┘    └──────┬─────┘    └──────┬─────┘
           │                 │                 │
           └─────────────────┼─────────────────┘
                             ▼
                      ┌────────────┐
                      │  Grafana   │
                      │ (visualize)│
                      └────────────┘
```

## The Three Pillars of Observability

### 1. Metrics

Metrics are numerical measurements collected over time. They answer questions like:
- How many requests per second?
- What's the average response time?
- How much CPU/memory is being used?

**Types of metrics:**

| Type | Description | Example |
|------|-------------|---------|
| Counter | Only increases | `http_requests_total` |
| Gauge | Can go up or down | `http_requests_in_progress` |
| Histogram | Distribution of values | `http_request_duration_seconds` |

**Key metrics from our services:**

```promql
# Request rate (requests per second)
rate(http_server_request_duration_seconds_count[5m])

# Average response time
rate(http_server_request_duration_seconds_sum[5m]) / rate(http_server_request_duration_seconds_count[5m])

# 95th percentile latency
histogram_quantile(0.95, rate(http_server_request_duration_seconds_bucket[5m]))

# Error rate
sum(rate(http_server_request_duration_seconds_count{http_status_code=~"5.."}[5m])) / sum(rate(http_server_request_duration_seconds_count[5m]))
```

### 2. Traces

Traces track a request as it flows through multiple services. They answer:
- Which services did the request touch?
- Where did the latency come from?
- Where did the error occur?

**Trace structure:**

```
Trace (unique trace_id)
│
├── Span: product-service GET /api/products (120ms)
│   ├── Span: database query (45ms)
│   └── Span: cache lookup (5ms)
│
└── Span: order-service GET /api/orders (80ms)
    └── Span: HTTP call to product-service (50ms)
```

**Key concepts:**

| Term | Description |
|------|-------------|
| Trace | End-to-end journey of a request |
| Span | Single operation within a trace |
| Trace ID | Unique identifier linking all spans |
| Parent Span | The span that initiated the current span |

### 3. Logs

Logs are timestamped text records of events. They answer:
- What exactly happened?
- What were the input parameters?
- What was the error message?

**Log levels:**

| Level | When to use |
|-------|-------------|
| DEBUG | Detailed diagnostic information |
| INFO | General operational events |
| WARN | Something unexpected but not critical |
| ERROR | Something failed |
| FATAL | Application cannot continue |

## OpenTelemetry (OTel)

OpenTelemetry is a vendor-neutral standard for collecting telemetry data.

### Why OpenTelemetry?

| Aspect | Without OTel | With OTel |
|--------|--------------|-----------|
| Vendor lock-in | Tied to specific tools | Vendor-agnostic |
| Instrumentation | Different library per signal | Unified SDK |
| Data format | Proprietary formats | Standard OTLP |
| Flexibility | Hard to switch backends | Easy to change exporters |

### OTel Components

```
┌─────────────────────────────────────────────────────────────┐
│                    Application                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              OpenTelemetry SDK                      │    │
│  │                                                     │    │
│  │  ┌──────────────┐  ┌──────────────┐  ┌───────────┐  │    │
│  │  │ Trace API    │  │ Metrics API  │  │ Logs API  │  │    │
│  │  └──────────────┘  └──────────────┘  └───────────┘  │    │
│  │                                                     │    │
│  │  ┌──────────────────────────────────────────────┐   │    │
│  │  │         Auto-Instrumentation                 │   │    │
│  │  │  (ASP.NET Core, HttpClient, etc.)            │   │    │
│  │  └──────────────────────────────────────────────┘   │    │
│  │                                                     │    │
│  │  ┌──────────────────────────────────────────────┐   │    │
│  │  │              OTLP Exporter                   │   │    │
│  │  │      (sends data to OTel Collector)          │   │    │
│  │  └──────────────────────────────────────────────┘   │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### OTel Collector

The collector is a proxy that receives, processes, and exports telemetry data.

**Pipeline structure:**

```yaml
receivers:    # How data comes in
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

processors:   # How data is transformed
  batch:
    timeout: 10s

exporters:    # Where data goes out
  prometheus:
    endpoint: "0.0.0.0:8889"
  otlp/tempo:
    endpoint: "tempo:4317"

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/tempo]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [prometheus]
```

**Benefits of using a collector:**

1. **Decoupling** - Applications don't need to know about backends
2. **Processing** - Filter, transform, sample data centrally
3. **Reliability** - Buffer data if backend is temporarily unavailable
4. **Multi-backend** - Send same data to multiple destinations

## Stack Components

### Prometheus

Time-series database for metrics.

**Access:** Grafana → Explore → Prometheus data source

**Useful queries:**

```promql
# All metrics from our services
{job=~".*product.*|.*order.*"}

# HTTP request rate by service
sum by (service_name) (rate(http_server_request_duration_seconds_count[5m]))

# Memory usage
process_runtime_dotnet_gc_heap_size_bytes
```

### Tempo

Distributed tracing backend.

**Access:** Grafana → Explore → Tempo data source

**Search by:**
- Service name: `product-service`, `order-service`
- Trace ID: If you have a specific trace ID
- Duration: Find slow requests

### Loki

Log aggregation system.

**Access:** Grafana → Explore → Loki data source

**Query examples:**

```logql
# All logs from product-service
{app="product-service-ubuntu"}

# Error logs only
{namespace="microservices-ubuntu"} |= "error"

# Logs with specific text
{app="order-service-ubuntu"} |= "OrderId"
```

### Grafana

Visualization and dashboarding.

**Pre-configured data sources:**
- Prometheus (metrics)
- Tempo (traces)
- Loki (logs)

## .NET Integration

Our services use the OpenTelemetry .NET SDK:

```csharp
// Program.cs
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("product-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()  // Auto-instrument HTTP requests
        .AddHttpClientInstrumentation()   // Auto-instrument outgoing HTTP
        .AddOtlpExporter())               // Send to collector
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
```

**Environment variable:**
```yaml
env:
  - name: OTEL_EXPORTER_OTLP_ENDPOINT
    value: "http://otel-collector:4317"
```

## Accessing the Tools

### Grafana

```bash
# Get NodePort
microk8s kubectl get svc kube-prom-stack-grafana -n observability

# Get credentials
microk8s kubectl get secret -n observability kube-prom-stack-grafana \
  -o jsonpath="{.data.admin-user}" | base64 -d
microk8s kubectl get secret -n observability kube-prom-stack-grafana \
  -o jsonpath="{.data.admin-password}" | base64 -d
```

Access: `http://<SERVER_IP>:<NODEPORT>`

### Prometheus UI

```bash
microk8s kubectl port-forward -n observability svc/kube-prom-stack-kube-prome-prometheus 9090:9090 --address 0.0.0.0
```

Access: `http://<SERVER_IP>:9090`

## Recommended Dashboards

Import these dashboards in Grafana (Dashboards → Import):

| ID | Name | Purpose |
|----|------|---------|
| 3119 | Kubernetes Cluster Monitoring | Cluster overview |
| 6417 | Kubernetes Pods | Pod-level metrics |
| 1860 | Node Exporter Full | Server metrics |
| 15760 | Kubernetes Views / Pods | Detailed pod view |

## Troubleshooting

### No metrics appearing

```bash
# Check OTel Collector is running
microk8s kubectl get pods -n microservices-ubuntu | grep otel

# Check collector logs
microk8s kubectl logs -n microservices-ubuntu deploy/otel-collector

# Verify services are sending data
microk8s kubectl logs -n microservices-ubuntu deploy/product-service-ubuntu | grep -i otel
```

### No traces in Tempo

```bash
# Check Tempo is running
microk8s kubectl get pods -n observability | grep tempo

# Verify collector exports to Tempo
microk8s kubectl logs -n microservices-ubuntu deploy/otel-collector | grep -i tempo
```

### Collector not starting

```bash
# Check collector config
microk8s kubectl get configmap -n microservices-ubuntu otel-collector-config -o yaml

# Describe pod for errors
microk8s kubectl describe pod -n microservices-ubuntu -l app=otel-collector
```

## Best Practices

### Metrics

1. **Use standard names** - Follow OpenTelemetry semantic conventions
2. **Add labels wisely** - High cardinality labels increase storage
3. **Set appropriate intervals** - 15-30s for most use cases

### Traces

1. **Sample in production** - Don't trace 100% of requests
2. **Add context** - Include relevant attributes (user ID, order ID)
3. **Propagate context** - Ensure trace context flows between services

### Logs

1. **Structured logging** - Use JSON format
2. **Include trace ID** - Correlate logs with traces
3. **Log levels** - Use appropriate levels, not everything is ERROR

## Correlation: Connecting the Pillars

The power of observability comes from correlating signals:

```
1. Alert fires: High error rate (METRICS)
        ↓
2. Find failing endpoint in metrics
        ↓
3. Search traces for that endpoint (TRACES)
        ↓
4. Find slow/failing span
        ↓
5. Get trace ID, search logs (LOGS)
        ↓
6. Find root cause in log message
```

In Grafana, you can click from metrics → traces → logs using the data source correlation features.
