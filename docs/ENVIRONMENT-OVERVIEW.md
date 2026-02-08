# Environment Overview - Ubuntu/MicroK8s

This document provides an overview of the microservices environment running on the Ubuntu MicroK8s cluster.

## Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Ubuntu Server (MicroK8s)                            │
│                         Namespace: microservices-ubuntu                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Ingress Controller                            │   │
│  │                    (nginx - port 80/443)                             │   │
│  └───────────┬─────────────────┬─────────────────┬─────────────────────┘   │
│              │                 │                 │                         │
│    frontend.local      product.local      order.local                      │
│              │                 │                 │                         │
│              ▼                 ▼                 ▼                         │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐                   │
│  │ Frontend App  │  │Product Service│  │ Order Service │                   │
│  │ (Blazor WASM) │  │   (.NET 9)    │  │   (.NET 9)    │                   │
│  │    1 pod      │  │  1 pod (2/2)  │  │  1 pod (2/2)  │                   │
│  └───────────────┘  └───────┬───────┘  └───────┬───────┘                   │
│                             │                  │                           │
│                             │   ┌──────────────┘                           │
│                             │   │                                          │
│                             ▼   ▼                                          │
│                      ┌─────────────────┐      ┌─────────────────┐          │
│                      │    MongoDB      │      │     Vault       │          │
│                      │   (8.0)         │      │  (Dev Mode)     │          │
│                      │    1 pod        │      │  1 pod + injector│         │
│                      └─────────────────┘      └─────────────────┘          │
│                                                       │                    │
│                                                       │ Dynamic            │
│                                                       │ Credentials        │
│                                                       ▼                    │
│                                            ┌─────────────────┐             │
│                                            │  Vault Agent    │             │
│                                            │  (sidecars in   │             │
│                                            │   services)     │             │
│                                            └─────────────────┘             │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    Observability Stack                               │   │
│  │  ┌─────────────┐                                                     │   │
│  │  │    OTel     │ ──► Prometheus (metrics)                            │   │
│  │  │  Collector  │ ──► Tempo (traces)                                  │   │
│  │  │   1 pod     │ ──► Loki (logs)                                     │   │
│  │  └─────────────┘                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Components

### Application Services

| Service | Description | Pods | Containers | Port |
|---------|-------------|------|------------|------|
| **frontend-app-ubuntu** | Blazor WebAssembly dashboard UI | 1 | 1 | 80 |
| **product-service-ubuntu** | .NET 9 REST API for product catalog | 1 | 2 (app + vault-agent) | 80 |
| **order-service-ubuntu** | .NET 9 REST API for order management | 1 | 2 (app + vault-agent) | 80 |

### Infrastructure Services

| Service | Description | Pods | Port |
|---------|-------------|------|------|
| **mongodb-ubuntu** | MongoDB 8.0 database | 1 | 27017 |
| **vault-ubuntu** | HashiCorp Vault (dev mode) for secrets management | 1 | 8200 |
| **vault-ubuntu-agent-injector** | Mutating webhook that injects Vault sidecars | 1 | 443 |
| **otel-collector** | OpenTelemetry Collector for telemetry | 1 | 4317, 4318, 8889 |

## Why 2/2 Containers in Services?

The product and order services show `2/2` containers because they include:

1. **Main application container** - The .NET microservice
2. **Vault Agent sidecar** - Fetches dynamic MongoDB credentials from Vault

```
┌─────────────────────────────────────────┐
│           product-service pod           │
├─────────────────────┬───────────────────┤
│  product-service    │   vault-agent     │
│  (main app)         │   (sidecar)       │
│                     │                   │
│  - Runs .NET API    │  - Authenticates  │
│  - Reads creds from │    to Vault       │
│    /vault/secrets/  │  - Writes creds   │
│                     │    to shared vol  │
│                     │  - Auto-renews    │
└─────────────────────┴───────────────────┘
```

## GitOps with ArgoCD

All applications are managed by **ArgoCD** using the **App-of-Apps** pattern:

```
┌─────────────────────────────────────────────────────────────────┐
│                    ArgoCD (argocd namespace)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  microservices-ubuntu (parent app)                              │
│         │                                                       │
│         ├── frontend-app-ubuntu ──► charts/frontend-app/        │
│         ├── product-service-ubuntu ──► charts/product-service/  │
│         ├── order-service-ubuntu ──► charts/order-service/      │
│         ├── mongodb-ubuntu ──► charts/mongodb/                  │
│         ├── vault-ubuntu ──► charts/vault/                      │
│         └── otel-collector-ubuntu ──► charts/otel-collector/    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### ArgoCD Applications Status

| Application | Sync Status | Health Status | Description |
|-------------|-------------|---------------|-------------|
| microservices-ubuntu | Synced | Healthy | Parent app (App-of-Apps) |
| frontend-app-ubuntu | Synced | Healthy | Blazor WASM frontend |
| product-service-ubuntu | Synced | Healthy | Product catalog API |
| order-service-ubuntu | Synced | Healthy | Order management API |
| mongodb-ubuntu | Synced | Healthy | Database |
| vault-ubuntu | Synced | Healthy | Secrets management |
| otel-collector-ubuntu | Synced | Healthy | Telemetry collector |

## Secrets Management with Vault

Vault provides **dynamic database credentials** with automatic rotation:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Secrets Flow                                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Pod starts                                                  │
│         │                                                       │
│         ▼                                                       │
│  2. Vault Agent authenticates via Kubernetes ServiceAccount     │
│         │                                                       │
│         ▼                                                       │
│  3. Vault generates unique MongoDB credentials (1h TTL)         │
│         │                                                       │
│         ▼                                                       │
│  4. Credentials written to /vault/secrets/mongodb               │
│         │                                                       │
│         ▼                                                       │
│  5. .NET app reads connection string and connects to MongoDB    │
│         │                                                       │
│         ▼                                                       │
│  6. Vault Agent auto-renews before TTL expires                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Benefits:**
- No hardcoded credentials in application code
- Each pod gets unique credentials
- Automatic credential rotation (1h default TTL)
- Credentials revoked when pod terminates

## Observability Stack

All services export telemetry via **OpenTelemetry** to the OTel Collector:

| Signal | Backend | Purpose |
|--------|---------|---------|
| **Metrics** | Prometheus | CPU, memory, request counts, latencies |
| **Traces** | Tempo | Distributed tracing across services |
| **Logs** | Loki | Centralized logging with trace correlation |

**Visualization:** Grafana (in `observability` namespace)

## Network Access

### Internal (ClusterIP)

All services use `ClusterIP` - only accessible within the cluster.

| Service | Cluster IP | Port |
|---------|------------|------|
| frontend-app-ubuntu | 10.152.183.100 | 80 |
| product-service-ubuntu | 10.152.183.24 | 80 |
| order-service-ubuntu | 10.152.183.179 | 80 |
| mongodb-ubuntu | 10.152.183.115 | 27017 |
| vault-ubuntu | 10.152.183.243 | 8200 |

### External (Ingress)

External access via nginx Ingress Controller:

| Hostname | Service | URL |
|----------|---------|-----|
| `frontend.local` | frontend-app-ubuntu | http://frontend.local |
| `product.local` | product-service-ubuntu | http://product.local/api/products |
| `order.local` | order-service-ubuntu | http://order.local/api/orders |

**Note:** Add entries to `/etc/hosts` (Linux/Mac) or `C:\Windows\System32\drivers\etc\hosts` (Windows):
```
<VM-IP>  frontend.local product.local order.local
```

## Kubernetes Concepts Explained

### Core Resources

#### Pod
The smallest deployable unit in Kubernetes. A pod contains one or more containers that share:
- Network (same IP address)
- Storage (shared volumes)
- Lifecycle (created/destroyed together)

```
┌────────────────────────────────────┐
│              Pod                   │
│  ┌───────────┐    ┌───────────┐    │
│  │ Container │    │ Container │    │
│  │   (app)   │    │ (sidecar) │    │
│  └───────────┘    └───────────┘    │
│         │              │           │
│         └──────┬───────┘           │
│                │                   │
│         Shared Volume              │
│         Shared Network (localhost) │
└────────────────────────────────────┘
```

**In our environment:** Product and Order services run as pods with 2 containers each (app + vault-agent sidecar).

---

#### ReplicaSet
Ensures a specified number of pod replicas are running at all times. If a pod crashes, the ReplicaSet creates a new one.

```
┌────────────────────────────────────────────┐
│            ReplicaSet (replicas: 3)        │
│                                            │
│    ┌─────┐      ┌─────┐      ┌─────┐       │
│    │ Pod │      │ Pod │      │ Pod │       │
│    └─────┘      └─────┘      └─────┘       │
│                                            │
│    If one dies, ReplicaSet creates another │
└────────────────────────────────────────────┘
```

**In our environment:** Each service has a ReplicaSet managing 1 replica (dev environment). The multiple ReplicaSets you see (with 0 replicas) are old versions kept for rollback history.

---

#### Deployment
A higher-level resource that manages ReplicaSets and enables:
- Rolling updates (zero-downtime deployments)
- Rollbacks to previous versions
- Scaling (increase/decrease replicas)

```
┌────────────────────────────────────────────────────┐
│                   Deployment                       │
│                                                    │
│   ┌────────────────────────────────────────┐       │
│   │         ReplicaSet (current)           │       │
│   │    ┌─────┐  ┌─────┐  ┌─────┐           │       │
│   │    │ Pod │  │ Pod │  │ Pod │           │       │
│   │    └─────┘  └─────┘  └─────┘           │       │
│   └────────────────────────────────────────┘       │
│                                                    │
│   ┌─────────────────────────────────────────┐      │
│   │     ReplicaSet (previous - 0 replicas)  │      │
│   │         (kept for rollback)             │      │
│   └─────────────────────────────────────────┘      │
└────────────────────────────────────────────────────┘
```

**In our environment:** All services use Deployments. ArgoCD updates the Deployment, which creates new ReplicaSets for rolling updates.

---

#### StatefulSet
Like Deployment, but for stateful applications that need:
- Stable network identity (predictable pod names)
- Persistent storage that follows the pod
- Ordered startup/shutdown

```
┌─────────────────────────────────────────────┐
│              StatefulSet                    │
│                                             │
│    vault-ubuntu-0  (always this name)       │
│         │                                   │
│         ▼                                   │
│    ┌─────────┐                              │
│    │   PVC   │  (persistent storage)        │
│    └─────────┘                              │
└─────────────────────────────────────────────┘
```

**In our environment:** Vault uses a StatefulSet (`vault-ubuntu-0`) because it needs stable identity and storage.

---

### Networking

#### Service
An abstraction that provides a stable endpoint (IP/DNS) to access pods. Pods are ephemeral (IPs change), but Services provide a consistent address.

```
┌─────────────────────────────────────────────────────┐
│                                                     │
│   Client ──► Service ──► Pod (10.1.62.45)           │
│              (stable IP)     │                      │
│                              ├──► Pod (10.1.62.46)  │
│                              │                      │
│                              └──► Pod (10.1.62.47)  │
│                                                     │
│   Service load-balances across healthy pods         │
└─────────────────────────────────────────────────────┘
```

#### Service Types

| Type | Description | Use Case |
|------|-------------|----------|
| **ClusterIP** | Internal IP only, accessible within cluster | Default, service-to-service communication |
| **NodePort** | Exposes on each node's IP at a static port (30000-32767) | Direct access without Ingress |
| **LoadBalancer** | Provisions cloud load balancer (AWS ELB, Azure LB) | Production cloud environments |

```
┌─────────────────────────────────────────────────────────────────┐
│                      Service Types                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ClusterIP (Internal Only)                                      │
│  ┌─────────────┐                                                │
│  │ 10.96.0.100 │ ◄── Only pods inside cluster can reach         │
│  └─────────────┘                                                │
│                                                                 │
│  NodePort (Node IP + Port)                                      │
│  ┌─────────────┐                                                │
│  │ <NodeIP>    │                                                │
│  │   :31234    │ ◄── External access via any node's IP          │
│  └─────────────┘                                                │
│                                                                 │
│  LoadBalancer (Cloud)                                           │
│  ┌─────────────┐                                                │
│  │ External IP │ ◄── Cloud provider provisions public IP        │
│  │ 52.x.x.x    │                                                │
│  └─────────────┘                                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**In our environment:** All services use `ClusterIP` (internal only). External access is via Ingress.

---

#### Ingress
HTTP/HTTPS routing rules that expose services externally based on hostnames or paths. Requires an Ingress Controller (nginx, traefik, etc.).

```
┌────────────────────────────────────────────────────────────────┐
│                         Internet                               │
│                            │                                   │
│                            ▼                                   │
│              ┌─────────────────────────┐                       │
│              │   Ingress Controller    │                       │
│              │        (nginx)          │                       │
│              │        :80/:443         │                       │
│              └────────────┬────────────┘                       │
│                           │                                    │
│         ┌─────────────────┼─────────────────┐                  │
│         │                 │                 │                  │
│         ▼                 ▼                 ▼                  │
│   frontend.local    product.local    order.local               │
│         │                 │                 │                  │
│         ▼                 ▼                 ▼                  │
│   ┌──────────┐      ┌──────────┐      ┌──────────┐             │
│   │ frontend │      │ product  │      │  order   │             │
│   │ service  │      │ service  │      │ service  │             │
│   └──────────┘      └──────────┘      └──────────┘             │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**In our environment:** nginx Ingress routes:
- `frontend.local` → frontend-app-ubuntu
- `product.local` → product-service-ubuntu
- `order.local` → order-service-ubuntu

---

### Storage

#### PersistentVolume (PV)
A piece of storage provisioned by an admin or dynamically by a StorageClass. Think of it as a "disk" available in the cluster.

#### PersistentVolumeClaim (PVC)
A request for storage by a pod. The PVC binds to a PV and mounts it into the pod.

```
┌─────────────────────────────────────────────────────────────────┐
│                      Storage Flow                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Pod                                                           │
│    │                                                            │
│    │ "I need 1Gi of storage"                                    │
│    ▼                                                            │
│   PVC (PersistentVolumeClaim)                                   │
│    │                                                            │
│    │ Claims available storage                                   │
│    ▼                                                            │
│   PV (PersistentVolume)                                         │
│    │                                                            │
│    │ Backed by actual storage                                   │
│    ▼                                                            │
│   ┌─────────────────────────────────────────┐                   │
│   │   Actual Storage                        │                   │
│   │   - Local disk (microk8s-hostpath)      │                   │
│   │   - Cloud disk (Azure Managed Disk)     │                   │
│   │   - Network storage (NFS, Ceph)         │                   │
│   └─────────────────────────────────────────┘                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**In our environment:** MongoDB uses a PVC with `microk8s-hostpath` StorageClass to persist data across pod restarts.

---

### Configuration

#### ConfigMap
Stores non-sensitive configuration data (key-value pairs, config files). Mounted as files or environment variables in pods.

```yaml
# Example: ConfigMap for app settings
apiVersion: v1
kind: ConfigMap
metadata:
  name: app-config
data:
  API_URL: "http://product-service:80"
  LOG_LEVEL: "info"
```

#### Secret
Like ConfigMap, but for sensitive data (passwords, tokens, certificates). Values are base64-encoded (not encrypted by default).

```yaml
# Example: Secret for database credentials
apiVersion: v1
kind: Secret
metadata:
  name: db-credentials
type: Opaque
data:
  username: cm9vdA==        # base64 of "root"
  password: cGFzc3dvcmQ=    # base64 of "password"
```

**In our environment:**
- MongoDB credentials stored in Secrets
- Vault provides dynamic secrets injected into pods

---

#### Namespace
Virtual clusters within a physical cluster. Used to isolate resources and apply policies.

```
┌────────────────────────────────────────────────────────────────┐
│                    Kubernetes Cluster                          │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌──────────────────┐  ┌──────────────────┐                    │
│  │ microservices-   │  │     argocd       │                    │
│  │     ubuntu       │  │   (namespace)    │                    │
│  │   (namespace)    │  │                  │                    │
│  │                  │  │  ArgoCD pods     │                    │
│  │  App pods        │  │                  │                    │
│  │  MongoDB         │  └──────────────────┘                    │
│  │  Vault           │                                          │
│  └──────────────────┘  ┌──────────────────┐                    │
│                        │  observability   │                    │
│                        │   (namespace)    │                    │
│                        │                  │                    │
│                        │  Prometheus      │                    │
│                        │  Grafana         │                    │
│                        │  Tempo, Loki     │                    │
│                        └──────────────────┘                    │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**In our environment:**
- `microservices-ubuntu` - Application workloads
- `argocd` - GitOps tooling
- `observability` - Monitoring stack

---

### Other Resources

#### Job
Runs a task to completion (one-time or batch processing). Pod terminates after task completes.

**In our environment:** `vault-ubuntu-init` Job configures Vault after startup.

#### ServiceAccount
Identity for pods to authenticate with the Kubernetes API or external services (like Vault).

**In our environment:** Product and Order services use ServiceAccounts to authenticate with Vault.

---

## Resource Hierarchy Summary

```
Cluster
 └── Namespace
      ├── Deployment ──► ReplicaSet ──► Pod(s) ──► Container(s)
      ├── StatefulSet ──► Pod(s) ──► Container(s)
      ├── Service (ClusterIP/NodePort/LoadBalancer)
      ├── Ingress
      ├── ConfigMap
      ├── Secret
      ├── PersistentVolumeClaim ──► PersistentVolume
      ├── ServiceAccount
      └── Job
```

## Technology Stack

| Layer | Technology |
|-------|------------|
| **Platform** | MicroK8s on Ubuntu |
| **GitOps** | ArgoCD |
| **Package Management** | Helm Charts |
| **Backend Services** | .NET 9 (C#) |
| **Frontend** | Blazor WebAssembly |
| **Database** | MongoDB 8.0 |
| **Secrets** | HashiCorp Vault |
| **Observability** | OpenTelemetry, Prometheus, Tempo, Loki, Grafana |
| **Ingress** | nginx Ingress Controller |

## Cluster Restart Procedure

Since Vault runs in **dev mode** (in-memory storage), configuration is lost on restart. Use the startup script:

```bash
# Instead of 'microk8s start', use:
./infra/scripts/local-ubuntu/cluster-start.sh
```

The script:
1. Starts MicroK8s
2. Waits for cluster readiness
3. Triggers ArgoCD sync to reconfigure Vault
4. Restarts services to pick up new credentials
5. Starts MongoDB port-forward for external access

## Quick Commands

```bash
# Check all pods
kubectl get pods -n microservices-ubuntu

# Check ArgoCD apps
kubectl get applications -n argocd

# View logs
kubectl logs -n microservices-ubuntu deploy/product-service-ubuntu -c product-service

# Access APIs
curl http://product.local/api/products
curl http://order.local/api/orders

# MongoDB connection (after port-forward)
mongodb://root:local-dev-password@<VM-IP>:27017/?authSource=admin
```
