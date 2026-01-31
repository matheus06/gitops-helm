# GitOps Helm Lab

A lab environment demonstrating GitOps with ArgoCD, Helm, and .NET microservices on Azure Kubernetes Service (AKS).

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         GitHub Repository                           │
├─────────────────────────────────────────────────────────────────────┤
│  src/                    │  charts/              │  argocd/         │
│  ├── ProductService/     │  ├── product-service/ │  ├── apps/       │
│  └── OrderService/       │  └── order-service/   │  │   ├── dev/    │
│                          │                       │  │   └── prod/   │
│  .github/workflows/      │  infra/terraform/     │  └── projects/   │
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

### 1. Fork and Clone

1. Fork this repository to your GitHub account
2. Clone it locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/gitops-helm.git
   cd gitops-helm
   ```

### 2. Update GitHub Username

Replace `GITHUB_USERNAME` with your actual GitHub username in all files:

```bash
# Linux/Mac
find . -type f \( -name "*.yaml" -o -name "*.yml" \) -exec sed -i 's/GITHUB_USERNAME/your-username/g' {} \;

# Windows PowerShell
Get-ChildItem -Recurse -Include *.yaml,*.yml | ForEach-Object {
    (Get-Content $_.FullName) -replace 'GITHUB_USERNAME', 'your-username' | Set-Content $_.FullName
}
```

### 3. Create AKS Cluster

```bash
cd infra/terraform

# Copy and customize variables
cp terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars with your values

# Initialize and apply
terraform init
terraform plan
terraform apply
```

### 4. Configure kubectl

```bash
az aks get-credentials --resource-group gitops-helm-rg --name gitops-helm-aks
```

### 5. Install ArgoCD

```bash
cd infra/scripts
chmod +x install-argocd.sh
./install-argocd.sh
```

### 6. Access ArgoCD UI

```bash
kubectl port-forward svc/argocd-server -n argocd 8080:443
```

Open https://localhost:8080 and login with:
- Username: `admin`
- Password: (output from install script)

### 7. Deploy Applications

```bash
chmod +x setup-argocd-apps.sh
./setup-argocd-apps.sh your-github-username
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
│   └── order-service/           # Helm chart for OrderService
├── argocd/
│   ├── apps/
│   │   ├── dev/                 # Dev environment apps
│   │   ├── prod/                # Prod environment apps
│   │   ├── app-of-apps-dev.yaml
│   │   └── app-of-apps-prod.yaml
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

### Run services locally

```bash
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

## License

MIT
