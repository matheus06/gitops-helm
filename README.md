# GitOps Helm Lab

A lab environment demonstrating GitOps with ArgoCD, Helm, and .NET microservices on Azure Kubernetes Service (AKS).

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         GitHub Repository                           │
├─────────────────────────────────────────────────────────────────────┤
│  src/                    │  charts/              │  argocd/         │
│  ├── ProductService/     │  ├── product-service/ │  ├── apps/       │
│  └── OrderService/       │  ├── order-service/   │  │   ├── dev/    │
│                          │  ├── mongodb/         │  │   ├── prod/   │
│  .github/workflows/      │  └── vault/           │  │   └── ubuntu/ │
│                          │                       │  └── projects/   │
│  infra/terraform/        │                       │                  │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                              AKS Cluster                             │
├──────────────────────────────┬──────────────────────────────────────┤
│       microservices-dev      │         microservices-prod           │
│  ┌─────────────────────────┐ │  ┌─────────────────────────────────┐ │
│  │ product-service (1 pod) │ │  │ product-service (3+ pods, HPA)  │ │
│  │ order-service (1 pod)   │ │  │ order-service (3+ pods, HPA)    │ │
│  │ mongodb (1 pod + PVC)   │ │  │ mongodb (1 pod + PVC)           │ │
│  │ vault (server+injector) │ │  │ vault (server+injector)         │ │
│  └─────────────────────────┘ │  └─────────────────────────────────┘ │
└──────────────────────────────┴──────────────────────────────────────┘
```

## Prerequisites

- Azure CLI installed and logged in
- Terraform >= 1.0
- kubectl
- Helm 3
- .NET 9 SDK (for local development)
- Docker (for local testing)

## Quick Start

### 1. Create AKS Cluster

```bash
cd infra/terraform
terraform apply
```

### 2. Configure kubectl

```bash
az aks get-credentials --resource-group gitops-helm-rg --name gitops-helm-aks
```

### 3. Install ArgoCD

```bash
cd infra/scripts
chmod +x install-argocd.sh
./install-argocd.sh
```

### 4. Access ArgoCD UI

```bash
kubectl port-forward svc/argocd-server -n argocd 8080:443
```

Open https://localhost:8080 and login with:
- Username: `admin`
- Password: (output from install script)

### 5. Deploy Applications

```bash
chmod +x setup-argocd-apps.sh
./setup-argocd-apps.sh
```

## Project Structure

```
gitops-helm/
├── src/
│   ├── ProductService/          # Product catalog microservice
│   │   ├── Program.cs
│   │   ├── ProductService.csproj
│   │   ├── Dockerfile
│   │   └── appsettings.json
│   └── OrderService/            # Order management microservice
│       ├── Program.cs
│       ├── OrderService.csproj
│       ├── Dockerfile
│       └── appsettings.json
├── charts/
│   ├── product-service/         # Helm chart for ProductService
│   │   ├── Chart.yaml
│   │   ├── values.yaml          # Default values
│   │   ├── values-dev.yaml      # Dev environment overrides
│   │   ├── values-prod.yaml     # Prod environment overrides
│   │   └── templates/
│   ├── order-service/           # Helm chart for OrderService
│   ├── mongodb/                 # Helm chart for MongoDB
│   │   ├── Chart.yaml
│   │   ├── values.yaml
│   │   ├── values-{dev,prod,ubuntu}.yaml
│   │   └── templates/           # deployment, service, secret, pvc
│   └── vault/                   # Helm chart for HashiCorp Vault
│       ├── Chart.yaml           # Depends on official Vault chart
│       ├── values.yaml
│       ├── values-{dev,prod,ubuntu}.yaml
│       └── templates/           # init job for DB secrets engine
├── argocd/
│   ├── apps/
│   │   ├── dev/                 # Dev environment apps
│   │   │   ├── product-service.yaml
│   │   │   ├── order-service.yaml
│   │   │   ├── mongodb.yaml
│   │   │   └── vault.yaml
│   │   ├── prod/                # Prod environment apps
│   │   ├── ubuntu/              # Ubuntu/MicroK8s environment apps
│   │   ├── app-of-apps-dev.yaml
│   │   ├── app-of-apps-prod.yaml
│   │   └── app-of-apps-ubuntu.yaml
│   ├── projects/
│   │   └── microservices-project.yaml
│   └── base/
│       └── namespace.yaml
├── infra/
│   ├── terraform/               # AKS infrastructure
│   └── scripts/                 # Setup scripts
└── .github/
    └── workflows/
        ├── ci.yaml              # Build and test
        ├── cd-dev.yaml          # Deploy to dev
        └── cd-prod.yaml         # Deploy to prod
```

## GitOps Workflow

### Development Flow
1. Push code to `main` or `develop` branch
2. GitHub Actions builds and pushes Docker images with `dev-<sha>` tag
3. Workflow updates `values-dev.yaml` with new image tag
4. ArgoCD detects changes and syncs to `microservices-dev` namespace

### Production Flow
1. Create a GitHub Release (e.g., `v1.0.0`)
2. GitHub Actions builds and pushes Docker images with version tag
3. Workflow updates `values-prod.yaml` with version tag
4. ArgoCD syncs to `microservices-prod` namespace

## API Endpoints

### ProductService (port 80)
- `GET /health` - Health check
- `GET /api/products` - List all products
- `GET /api/products/{id}` - Get product by ID
- `POST /api/products` - Create product
- `PUT /api/products/{id}` - Update product
- `DELETE /api/products/{id}` - Delete product

### OrderService (port 80)
- `GET /health` - Health check
- `GET /api/orders` - List all orders
- `GET /api/orders/{id}` - Get order by ID
- `GET /api/orders/customer/{customerId}` - Get orders by customer
- `POST /api/orders` - Create order
- `PUT /api/orders/{id}/status` - Update order status
- `DELETE /api/orders/{id}` - Delete order

## Local Development

### Run MongoDB locally

```bash
# Start MongoDB container
docker run -d --name mongodb -p 27017:27017 \
  -e MONGO_INITDB_ROOT_USERNAME=root \
  -e MONGO_INITDB_ROOT_PASSWORD=localdev \
  mongo:7.0

# Connection string for local dev
# mongodb://root:localdev@localhost:27017/microservices?authSource=admin
```

### Run services locally

```bash
# Set MongoDB connection (PowerShell)
$env:MONGODB_CONNECTION_STRING="mongodb://root:localdev@localhost:27017/microservices?authSource=admin"

# Set MongoDB connection (Bash)
export MONGODB_CONNECTION_STRING="mongodb://root:localdev@localhost:27017/microservices?authSource=admin"

# ProductService
cd src/ProductService
dotnet run

# OrderService (in another terminal)
cd src/OrderService
dotnet run
```

### Build Docker images locally

```bash
# ProductService
docker build -t product-service:local -f src/ProductService/Dockerfile src/ProductService

# OrderService
docker build -t order-service:local -f src/OrderService/Dockerfile src/OrderService
```

## Useful Commands

```bash
# View ArgoCD applications
kubectl get applications -n argocd

# View pods in dev namespace
kubectl get pods -n microservices-dev

# View pods in prod namespace
kubectl get pods -n microservices-prod

# Port forward to access ProductService
kubectl port-forward svc/product-service -n microservices-dev 8081:80

# View ArgoCD application sync status
argocd app list

# Manually sync an application
argocd app sync product-service-dev

# MongoDB commands
kubectl exec -it mongodb-0 -n microservices-dev -- mongosh -u root -p
# > use microservices
# > db.products.find()
# > db.orders.find()

# Connect to MongoDB from host using MongoDB Compass
# Option 1: Port forward (recommended)
kubectl port-forward svc/mongodb-ubuntu -n microservices-ubuntu 27017:27017 --address 0.0.0.0
# Then connect with: mongodb://root:local-dev-password@localhost:27017/?authSource=admin

# Option 2: NodePort (for VM IP access)
kubectl patch svc mongodb-ubuntu -n microservices-ubuntu --type='merge' -p '{"spec":{"type":"NodePort"}}'
kubectl get svc mongodb-ubuntu -n microservices-ubuntu  # Note the NodePort (e.g., 31234)
# Then connect with: mongodb://root:local-dev-password@<VM-IP>:<NodePort>/?authSource=admin

# Vault commands
kubectl port-forward svc/vault 8200:8200 -n microservices-dev
# Access UI at http://localhost:8200 (token: root in dev mode)

# Check Vault dynamic credentials
kubectl exec -it vault-0 -n microservices-dev -- vault read database/creds/microservices-role

# View Vault Agent logs in a service pod
kubectl logs <pod-name> -c vault-agent -n microservices-dev
```

## Cleanup

```bash
# Delete ArgoCD applications
kubectl delete applications --all -n argocd

# Delete namespaces
kubectl delete namespace microservices-dev microservices-prod

# Destroy AKS cluster
cd infra/terraform
terraform destroy
```

## Combined Benefits of Helm and ArgoCD

```
  ┌─────────────────────────────────────────────────────────────┐
  │                     Developer pushes code                   │
  └─────────────────────────────┬───────────────────────────────┘
                                ▼
  ┌─────────────────────────────────────────────────────────────┐
  │  HELM: Generates K8s manifests from templates + values      │
  │  - Same template → different configs per environment        │
  │  - Easy to update image tags, replicas, resources           │
  └─────────────────────────────┬───────────────────────────────┘
                                ▼
  ┌─────────────────────────────────────────────────────────────┐
  │  ARGOCD: Syncs Git state to Kubernetes                      │
  │  - Automatic deployments                                    │
  │  - Self-healing                                             │
  │  - Rollback capability                                      │
  │  - Audit trail                                              │
  └─────────────────────────────────────────────────────────────┘
  ```

## Understanding Helm Structure

Helm charts are templates that generate Kubernetes manifests. Each service has its own chart:

```
charts/
├── product-service/
│   ├── Chart.yaml              # Chart metadata (name, version)
│   ├── values.yaml             # Default values (base config)
│   ├── values-dev.yaml         # Dev overrides (1 replica, less resources)
│   ├── values-prod.yaml        # Prod overrides (3 replicas, HPA enabled)
│   └── templates/
│       ├── _helpers.tpl        # Reusable template functions
│       ├── deployment.yaml     # Pod deployment template
│       ├── service.yaml        # ClusterIP service template
│       ├── serviceaccount.yaml # Service account template
│       └── hpa.yaml            # Horizontal Pod Autoscaler template
```

### How Values Cascade

```
values.yaml (base)          values-dev.yaml (override)     Final for Dev
─────────────────────────   ─────────────────────────      ─────────────────
replicaCount: 1             (not set)                   →  replicaCount: 1
image.tag: "latest"         image.tag: "dev"            →  image.tag: "dev"
resources.limits.cpu: 500m  resources.limits.cpu: 250m  →  resources.limits.cpu: 250m
autoscaling.enabled: false  (not set)                   →  autoscaling.enabled: false
```

### Dev vs Prod Differences

| Setting | Dev | Prod |
|---------|-----|------|
| Replicas | 1 | 3 |
| CPU limit | 250m | 500m |
| Memory limit | 128Mi | 256Mi |
| Autoscaling | disabled | enabled (3-10 pods) |
| Image tag | `dev-<sha>` | `v1.0.0` |

---

## Understanding ArgoCD Structure

ArgoCD watches Git and syncs to Kubernetes. It uses the "App of Apps" pattern:

```
argocd/
├── base/
│   └── namespace.yaml              # Creates microservices-dev & microservices-prod namespaces
├── projects/
│   └── microservices-project.yaml  # Defines which repos/namespaces are allowed
└── apps/
    ├── app-of-apps-dev.yaml        # Parent app that deploys all dev apps
    ├── app-of-apps-prod.yaml       # Parent app that deploys all prod apps
    ├── dev/
    │   ├── product-service.yaml    # ArgoCD Application for product-service in dev
    │   └── order-service.yaml      # ArgoCD Application for order-service in dev
    └── prod/
        ├── product-service.yaml    # ArgoCD Application for product-service in prod
        └── order-service.yaml      # ArgoCD Application for order-service in prod
```

### How ArgoCD Applications Work

Each Application manifest tells ArgoCD:
- **Where to get the chart** (Git repo + path)
- **Which values files to use** (base + environment-specific)
- **Where to deploy** (namespace)

```yaml
# argocd/apps/dev/product-service.yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: product-service-dev
  namespace: argocd
spec:
  project: microservices

  source:
    repoURL: https://github.com/USER/gitops-helm.git
    targetRevision: main
    path: charts/product-service       # Helm chart location
    helm:
      valueFiles:
        - values.yaml                  # Base values
        - values-dev.yaml              # Dev overrides (applied second)

  destination:
    server: https://kubernetes.default.svc
    namespace: microservices-dev       # Target namespace

  syncPolicy:
    automated:
      prune: true      # Delete resources removed from Git
      selfHeal: true   # Revert manual changes in cluster
```

### App of Apps Pattern

Instead of applying each app manually, one "parent" app deploys all others:

```
┌─────────────────────────────────────┐
│   app-of-apps-dev                   │  ← You apply this ONE app
│   (watches argocd/apps/dev/)        │
└─────────────────┬───────────────────┘
                  │ creates
        ┌─────────┴─────────┐
        ▼                   ▼
┌───────────────┐   ┌───────────────┐
│ product-      │   │ order-        │
│ service-dev   │   │ service-dev   │
└───────┬───────┘   └───────┬───────┘
        │ deploys           │ deploys
        ▼                   ▼
┌───────────────┐   ┌───────────────┐
│ Deployment    │   │ Deployment    │
│ Service       │   │ Service       │
│ ServiceAcc    │   │ ServiceAcc    │
└───────────────┘   └───────────────┘
    (microservices-dev namespace)
```

### GitOps Flow Diagram

```
Developer pushes code
        │
        ▼
┌─────────────────────┐
│   GitHub Actions    │
│   - Build image     │
│   - Push to GHCR    │
│   - Update values   │◄──── Updates values-dev.yaml with new tag
└─────────┬───────────┘
          │ commits change
          ▼
┌─────────────────────┐
│   Git Repository    │
│   values-dev.yaml   │
│   tag: "dev-abc123" │
└─────────┬───────────┘
          │ ArgoCD polls (every 3 min)
          ▼
┌─────────────────────┐
│      ArgoCD         │
│  Detects drift      │
│  Syncs to cluster   │
└─────────┬───────────┘
          │ applies
          ▼
┌─────────────────────┐
│   AKS Cluster       │
│   New pods with     │
│   updated image     │
└─────────────────────┘
```

### Key Concepts

| Concept | Purpose |
|---------|---------|
| **Helm Chart** | Template engine that generates K8s YAML from values |
| **Values files** | Configuration per environment (dev/prod) |
| **ArgoCD Application** | Tells ArgoCD what to deploy and where |
| **ArgoCD Project** | Security boundary (allowed repos, namespaces) |
| **App of Apps** | Single entry point to manage multiple apps |
| **Self-heal** | ArgoCD reverts manual cluster changes |
| **Prune** | ArgoCD deletes resources removed from Git |

This structure keeps your environments consistent and auditable - every change goes through Git.

---

## HPA - Horizontal Pod Autoscaler

HPA automatically scales the number of pods based on resource usage (CPU/memory).

### How It Works

```
                    ┌─────────────────────────────┐
                    │      HPA Controller         │
                    │  Monitors: CPU usage        │
                    │  Target: 70%                │
                    └──────────────┬──────────────┘
                                   │
            ┌──────────────────────┼──────────────────────┐
            │                      │                      │
            ▼                      ▼                      ▼
    CPU: 80% (high)         CPU: 75% (high)         CPU: 20% (low)
    ┌─────────┐             ┌─────────┐             ┌─────────┐
    │  Pod 1  │             │  Pod 2  │             │  Pod 3  │
    └─────────┘             └─────────┘             └─────────┘
            │                                              │
            ▼                                              ▼
    HPA adds more pods                           HPA removes pods
    (scale up)                                   (scale down)
```

### Environment Configuration

| Environment | Replicas | HPA | Min/Max Pods |
|-------------|----------|-----|--------------|
| Dev | 1 (fixed) | Off | - |
| Prod | 3+ | On | 3-10 pods |

### Config in values-prod.yaml

```yaml
autoscaling:
  enabled: true
  minReplicas: 3
  maxReplicas: 10
  targetCPUUtilizationPercentage: 70  # Scale up when CPU > 70%
```

### Check HPA Status

```bash
# View HPA
kubectl get hpa -n microservices-prod

# Detailed view
kubectl describe hpa product-service -n microservices-prod
```

### Example Output

```
NAME              REFERENCE                    TARGETS   MINPODS   MAXPODS   REPLICAS
product-service   Deployment/product-service   25%/70%   3         10        3
```

This means: CPU is at 25%, target is 70%, so no scaling needed. Currently running 3 pods.

---

## Connecting to MongoDB with Compass

To connect to MongoDB running in Kubernetes using MongoDB Compass or other GUI tools:

### Option 1: Port Forward (Recommended)

```bash
# Forward MongoDB port to your local machine
kubectl port-forward svc/mongodb-ubuntu -n microservices-ubuntu 27017:27017

# If connecting from another machine, bind to all interfaces
kubectl port-forward svc/mongodb-ubuntu -n microservices-ubuntu 27017:27017 --address 0.0.0.0
```

**Connection string for Compass:**
```
mongodb://root:local-dev-password@localhost:27017/?authSource=admin
```

### Option 2: NodePort (Direct VM Access)

If you need to connect using the VM's IP address:

```bash
# Change service type to NodePort
kubectl patch svc mongodb-ubuntu -n microservices-ubuntu --type='merge' -p '{"spec":{"type":"NodePort"}}'

# Get the assigned NodePort
kubectl get svc mongodb-ubuntu -n microservices-ubuntu
# Output: mongodb-ubuntu   NodePort   10.x.x.x   <none>   27017:31234/TCP
```

**Connection string for Compass:**
```
mongodb://root:local-dev-password@<VM-IP>:31234/?authSource=admin
```

Replace `<VM-IP>` with your VM's IP address and `31234` with the actual NodePort shown.

### Compass Connection Settings

| Field | Value |
|-------|-------|
| Host | `localhost` (port-forward) or `<VM-IP>` (NodePort) |
| Port | `27017` (port-forward) or NodePort |
| Authentication | Username/Password |
| Username | `root` |
| Password | `local-dev-password` |
| Auth Database | `admin` |

**Note:** Exposing MongoDB via NodePort is not recommended for production. Use port-forward for development.

---

## MongoDB Persistence

Both services use MongoDB for data persistence instead of in-memory storage.

### Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ ProductService  │───> │    MongoDB      │<────│  OrderService   │
│ (products coll) │     │  microservices  │     │ (orders coll)   │
└─────────────────┘     │    database     │     └─────────────────┘
                        └────────┬────────┘
                                 │
                        ┌────────▼────────┐
                        │ PersistentVolume│
                        │   (1Gi - 10Gi)  │
                        └─────────────────┘
```

### Collections

| Collection | Service | Purpose |
|------------|---------|---------|
| `products` | ProductService | Product catalog (id, name, price, stock) |
| `orders` | OrderService | Order records (id, customerId, items, status) |
| `counters` | Both | Auto-increment ID sequences |

### Data Seeding

On startup, services check if data exists and seed initial records if empty (idempotent):
- ProductService: 4 sample products (Laptop, Mouse, Keyboard, Monitor)
- OrderService: 3 sample orders

Control seeding via environment variable:
```yaml
env:
  - name: SEED_DATA
    value: "true"  # or "false" to disable
```

### Storage Configuration

| Environment | Storage Class | Size |
|-------------|---------------|------|
| dev | default | 1Gi |
| prod | managed-premium | 1Gi |
| ubuntu | microk8s-hostpath | 1Gi |

---

## HashiCorp Vault - Secrets Management

Vault provides dynamic MongoDB credentials with automatic rotation.

### Architecture

```
┌─────────────────┐     1. Request creds       ┌─────────────────┐
│  Vault Agent    │ ─────────────────────────> │  Vault Server   │
│  (sidecar)      │ <───────────────────────── │                 │
└────────┬────────┘     2. Dynamic user/pass   └────────┬────────┘
         │                   (1h lease)                 │
         │ 3. Write to                                  │
         │    /vault/secrets/mongodb                    ▼
         ▼                                     ┌─────────────────┐
┌─────────────────┐                            │     MongoDB     │
│    .NET App     │ ─────────────────────────> │   (validates    │
│  (reads file)   │     4. Connect with        │    creds)       │
└─────────────────┘        dynamic creds       └─────────────────┘
```

### Dynamic Credentials

Instead of static passwords, Vault generates unique credentials per pod:

| Property | Value |
|----------|-------|
| Secret Engine | Database (MongoDB plugin) |
| Default TTL | **1 hour** |
| Max TTL | 24 hours |
| Role | `readWrite` on microservices DB |
| Rotation | Automatic before expiry |

### How It Works

1. **Pod starts** → Vault Agent sidecar authenticates via Kubernetes ServiceAccount
2. **Agent requests credentials** → Vault creates temporary MongoDB user
3. **Credentials written to file** → `/vault/secrets/mongodb` contains connection string
4. **.NET app reads file** → Connects to MongoDB with dynamic credentials
5. **Before TTL expires** → Agent automatically renews credentials
6. **Pod terminates** → Vault revokes the credentials

### Vault Components

| Component | Purpose |
|-----------|---------|
| Vault Server | Stores secrets, manages leases |
| Vault Agent Injector | Mutating webhook that adds sidecar to pods |
| Vault Agent (sidecar) | Authenticates and fetches secrets for the pod |

### Pod Annotations

Services use these annotations to enable Vault injection:

```yaml
annotations:
  vault.hashicorp.com/agent-inject: "true"
  vault.hashicorp.com/role: "product-service"
  vault.hashicorp.com/agent-inject-secret-mongodb: "database/creds/microservices-role"
  vault.hashicorp.com/agent-inject-template-mongodb: |
    {{- with secret "database/creds/microservices-role" -}}
    mongodb://{{ .Data.username }}:{{ .Data.password }}@mongodb:27017/microservices?authSource=admin
    {{- end -}}
```

### Verify Vault Status

```bash
# Check Vault pods
kubectl get pods -n microservices-dev -l app.kubernetes.io/name=vault

# Check injector logs
kubectl logs -n microservices-dev -l app.kubernetes.io/name=vault-agent-injector

# Access Vault UI (dev mode)
kubectl port-forward svc/vault 8200:8200 -n microservices-dev
# Open http://localhost:8200 (token: root)

# Check dynamic credentials
kubectl exec -it vault-0 -n microservices-dev -- vault read database/creds/microservices-role
```

### Auto-Unseal with Azure Key Vault (Production)

In production, Vault uses Azure Key Vault for automatic unsealing instead of manual keys.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Vault Auto-Unseal Flow                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────┐    Workload Identity    ┌──────────────────────┐       │
│  │ Vault Pod   │ ──────────────────────> │   Azure Key Vault    │       │
│  │ (AKS)       │                         │   (unseal key)       │       │
│  └──────┬──────┘ <─────────────────────  └──────────────────────┘       │
│         │           Decrypt master key                                  │
│         ▼                                                               │
│  ┌─────────────┐                                                        │
│  │ Vault       │  Auto-unsealed and ready to serve                      │
│  │ Unsealed    │                                                        │
│  └─────────────┘                                                        │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Environment Configuration

| Environment | Unseal Method | Security Level |
|-------------|---------------|----------------|
| dev | Dev mode (auto) | Low (dev only) |
| ubuntu | Dev mode (auto) | Low (local dev) |
| **prod** | **Azure Key Vault** | **Production-ready** |

#### Setup Steps for Production

1. **Deploy infrastructure with Terraform:**
```bash
cd infra/terraform
terraform apply
```

2. **Get the required values:**
```bash
# Get Terraform outputs
terraform output vault_keyvault_name
terraform output vault_identity_client_id

# Get Azure tenant ID
az account show --query tenantId -o tsv
```

3. **Update `charts/vault/values-prod.yaml` with actual values:**
```yaml
vault:
  server:
    extraEnvironmentVars:
      AZURE_TENANT_ID: "<your-tenant-id>"
      AZURE_KEYVAULT_NAME: "<your-keyvault-name>"
    serviceAccount:
      annotations:
        azure.workload.identity/client-id: "<your-client-id>"
```

4. **Initialize Vault (first time only):**
```bash
kubectl exec -it vault-0 -n microservices-prod -- vault operator init
# Save the recovery keys and root token securely!
```

#### How It Works

1. **Terraform creates:** Azure Key Vault + RSA key + Managed Identity
2. **Workload Identity:** Links Kubernetes ServiceAccount to Azure Identity
3. **On Vault startup:** Vault uses Azure Key Vault to decrypt its master key
4. **No manual intervention:** Vault auto-unseals on every restart

#### Terraform Resources Created

| Resource | Purpose |
|----------|---------|
| `azurerm_key_vault` | Stores the unseal key |
| `azurerm_key_vault_key` | RSA key for encrypting Vault's master key |
| `azurerm_user_assigned_identity` | Managed identity for Vault |
| `azurerm_federated_identity_credential` | Links K8s SA to Azure identity |
| `azurerm_role_assignment` | Grants "Key Vault Crypto User" role |

---

## Future Improvements

### Service Communication
- [ ] Add inter-service calls (e.g., OrderService validates products before creating orders)
- [ ] Consider API Gateway or service mesh (Istio/Linkerd)

### Security
- [ ] Add Kubernetes NetworkPolicies to restrict pod-to-pod traffic
- [ ] Add authentication/authorization (JWT, OAuth2)
- [ ] Enable container image scanning in CI pipeline
- [ ] Configure Vault HA mode for production

### Production Readiness
- [ ] Add meaningful readiness/liveness probes (beyond HTTP 200)
- [ ] Implement circuit breakers and retry policies (Polly for .NET)
- [ ] Add resource quotas and limit ranges per namespace

### Observability Enhancements
- [ ] Create Grafana dashboards for custom metrics
- [ ] Add alerting rules (PrometheusRule CRDs)
- [ ] Implement SLOs/SLIs tracking

### CI/CD
- [ ] Add Helm chart linting (`helm lint`) in CI
- [ ] Add Kubernetes manifest validation (kubeconform, kube-score)
- [ ] Implement blue/green or canary deployments via Argo Rollouts

### Testing
- [ ] Add integration tests against deployed services
- [ ] Add load testing (k6, Locust) in the pipeline

## License

MIT
