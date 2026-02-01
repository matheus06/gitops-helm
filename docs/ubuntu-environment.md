# Ubuntu Environment Setup

This guide explains how to deploy and access the microservices on a local Ubuntu server using MicroK8s.

## Prerequisites

- Ubuntu Server (20.04 or later)
- SSH access to the server
- At least 4GB RAM and 20GB disk space

## Installation

### 1. Install MicroK8s

```bash
# SSH into your Ubuntu server
ssh user@your-ubuntu-server

# Clone the repository
git clone https://github.com/matheus06/gitops-helm.git
cd gitops-helm

# Make scripts executable
chmod +x infra/scripts/local-ubuntu/*.sh

# Install MicroK8s
cd ~/gitops-helm/infra/scripts/local-ubuntu
sudo ./setup-microk8s-ubuntu.sh
```

Log out and log back in for group permissions to take effect.

### 2. Install ArgoCD

```bash
sudo ./install-argocd-microk8s.sh
```

### 3. Deploy the Ubuntu Environment

```bash
sudo ./setup-argocd-apps-ubuntu.sh
```

## Accessing the Services

### Ingress Setup

The Ubuntu environment uses **Ingress** to route traffic to services using hostnames instead of random ports.

| Service | Hostname | URL |
|---------|----------|-----|
| Product Service | `product.local` | `http://product.local/api/products` |
| Order Service | `order.local` | `http://order.local/api/orders` |

### Configure Hosts File

Add your Ubuntu server's IP to your hosts file:

**Windows** (`C:\Windows\System32\drivers\etc\hosts`):
```
ubuntu-machine-ip  product.local order.local
```
### Test the APIs

```bash
# From any machine with hosts file configured
curl http://product.local/api/products
curl http://order.local/api/orders
```

### Verify Ingress is Working

```bash
# Check ingress resources
microk8s kubectl get ingress -n microservices-ubuntu

# Check ingress controller pods
microk8s kubectl get pods -n ingress
```

## Accessing ArgoCD UI

Expose ArgoCD via NodePort:

```bash
# Patch ArgoCD to use NodePort
microk8s kubectl patch svc argocd-server -n argocd -p '{"spec": {"type": "NodePort"}}'

# Get the assigned port
microk8s kubectl get svc argocd-server -n argocd
```

Access at: `https://<SERVER_IP>:<NODEPORT>`

### Get ArgoCD Admin Password

```bash
microk8s kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d
```

- **Username:** admin
- **Password:** (output from command above)

## Accessing Grafana UI

Expose Grafana via NodePort:

```bash
# Patch Grafana to use NodePort
microk8s kubectl patch svc kube-prom-stack-grafana -n observability -p '{"spec": {"type": "NodePort"}}'

# Get the assigned port
microk8s kubectl get svc kube-prom-stack-grafana -n observability

# Get credentials
microk8s kubectl get secret -n observability kube-prom-stack-grafana -o jsonpath="{.data.admin-user}" | base64 -d
echo ""
microk8s kubectl get secret -n observability kube-prom-stack-grafana -o jsonpath="{.data.admin-password}" | base64 -d
echo ""

```

Access at: `https://<SERVER_IP>:<NODEPORT>`

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                      Ubuntu Server                           │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │                     MicroK8s                         │    │
│  │                                                      │    │
│  │  ┌─────────────────────────────────────────────────┐ │    │
│  │  │            Ingress Controller (nginx)           │ │    │
│  │  │                    :80                          │ │    │
│  │  └──────────────┬─────────────────┬────────────────┘ │    │
│  │                 │                 │                  │    │
│  │    product.local│     order.local │                  │    │
│  │                 ▼                 ▼                  │    │
│  │  ┌──────────────────┐  ┌──────────────────┐          │    │
│  │  │  product-service │  │   order-service  │          │    │
│  │  │   (ClusterIP)    │  │   (ClusterIP)    │          │    │
│  │  └──────────────────┘  └──────────────────┘          │    │
│  │                                                      │    │
│  │  Namespace: microservices-ubuntu                     │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
└───────────────────────────┬──────────────────────────────────┘
                            │ :80
                            ▼
                 ┌─────────────────────┐
                 │   Your Network/LAN  │
                 │  (via hosts file)   │
                 └─────────────────────┘
```

## Differences from Dev/Prod Environments

| Aspect | Dev/Prod (AKS) | Ubuntu (MicroK8s) |
|--------|----------------|-------------------|
| Cluster | Azure AKS | Local MicroK8s |
| Service Type | ClusterIP | ClusterIP |
| External Access | Port-forward | Ingress |
| Ingress Class | nginx | nginx |
| Resources | Higher limits | Lower limits (200m CPU, 128Mi RAM) |
| Use Case | Cloud deployment | Local development/testing |

## ArgoCD Project Configuration

The ArgoCD project must whitelist Kubernetes resource types it can manage:

```yaml
# argocd/projects/microservices-project.yaml
namespaceResourceWhitelist:
  - group: ''                    # Core: Pod, Service, ConfigMap, Secret
    kind: '*'
  - group: 'apps'                # Deployment, StatefulSet, ReplicaSet
    kind: '*'
  - group: 'autoscaling'         # HorizontalPodAutoscaler
    kind: '*'
  - group: 'networking.k8s.io'   # Ingress, NetworkPolicy
    kind: '*'
  - group: 'argoproj.io'         # ArgoCD Application
    kind: 'Application'
```

If ArgoCD fails to sync with "resource not allowed" errors, check this whitelist.

## Useful Commands

### Check Pod Status

```bash
microk8s kubectl get pods -n microservices-ubuntu
```

### View Pod Logs

```bash
microk8s kubectl logs -n microservices-ubuntu deploy/product-service-ubuntu
microk8s kubectl logs -n microservices-ubuntu deploy/order-service-ubuntu
```

### Check Ingress Status

```bash
microk8s kubectl get ingress -n microservices-ubuntu
microk8s kubectl describe ingress -n microservices-ubuntu
```

### Check ArgoCD Sync Status

```bash
microk8s kubectl get applications -n argocd
```

### Force ArgoCD Sync

```bash
microk8s kubectl -n argocd patch app order-service-ubuntu --type merge -p '{"operation": {"initiatedBy": {"username": "admin"}, "sync": {"prune": true}}}'
```

### Restart a Deployment

```bash
microk8s kubectl rollout restart deploy/product-service-ubuntu -n microservices-ubuntu
```

## Troubleshooting

### Ingress not routing traffic

```bash
# Check ingress controller is running
microk8s kubectl get pods -n ingress

# Check ingress resource has an address
microk8s kubectl get ingress -n microservices-ubuntu

# Check ingress controller logs
microk8s kubectl logs -n ingress -l name=nginx-ingress-microk8s
```

### Pods not starting

```bash
# Check pod events
microk8s kubectl describe pod -n microservices-ubuntu <pod-name>

# Check if images can be pulled
microk8s kubectl get events -n microservices-ubuntu
```

### ArgoCD sync fails with "resource not allowed"

The ArgoCD project needs to whitelist the resource type. Update `argocd/projects/microservices-project.yaml` and add the missing group to `namespaceResourceWhitelist`.

### ArgoCD not syncing

```bash
# Check application status
microk8s kubectl get applications -n argocd

# Check app details
microk8s kubectl describe application <app-name> -n argocd
```

### MicroK8s not running

```bash
# Check status
microk8s status

# Start if stopped
microk8s start

# Check logs
sudo journalctl -u snap.microk8s.daemon-kubelite
```
