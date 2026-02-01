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

# Checkout the ubuntu environment branch
git checkout feature/ubuntu-environment

# Make scripts executable
chmod +x infra/scripts/*.sh

# Install MicroK8s
sudo ./infra/scripts/setup-microk8s-ubuntu.sh
```

Log out and log back in for group permissions to take effect.

### 2. Install ArgoCD

```bash
cd ~/gitops-helm/infra/scripts
./install-argocd-microk8s.sh
```

### 3. Deploy the Ubuntu Environment

```bash
./setup-argocd-apps-ubuntu.sh
```

## Accessing the Services

### Service Type: NodePort

The Ubuntu environment uses **NodePort** services, which expose the APIs directly on the server's IP address without requiring port-forward commands.

### Get the Service Ports

```bash
microk8s kubectl get svc -n microservices-ubuntu
```

Example output:
```
NAME                     TYPE       CLUSTER-IP       PORT(S)        AGE
product-service-ubuntu   NodePort   10.152.183.100   80:31234/TCP   10m
order-service-ubuntu     NodePort   10.152.183.101   80:31567/TCP   10m
```

The NodePort is the second port number (e.g., `31234`, `31567`).

### Access the APIs

Replace `<SERVER_IP>` with your Ubuntu server's IP address and `<NODEPORT>` with the port from the command above:

| Service | URL |
|---------|-----|
| Product Service | `http://<SERVER_IP>:<NODEPORT>/api/products` |
| Order Service | `http://<SERVER_IP>:<NODEPORT>/api/orders` |

Example:
```bash
# From any machine on the network
curl http://192.168.129.147:31234/api/products
curl http://192.168.129.147:31567/api/orders
```

## Accessing ArgoCD UI

ArgoCD is also exposed via NodePort:

```bash
# Get ArgoCD server port
microk8s kubectl get svc argocd-server -n argocd
```

Access at: `https://<SERVER_IP>:<NODEPORT>`

### Get ArgoCD Admin Password

```bash
microk8s kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d
```

- **Username:** admin
- **Password:** (output from command above)

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

### Check ArgoCD Sync Status

```bash
microk8s kubectl get applications -n argocd
```

### Restart a Deployment

```bash
microk8s kubectl rollout restart deploy/product-service-ubuntu -n microservices-ubuntu
```

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Ubuntu Server                        │
│                                                         │
│   ┌─────────────────────────────────────────────────┐   │
│   │                  MicroK8s                       │   │
│   │                                                 │   │
│   │   Namespace: microservices-ubuntu               │   │
│   │   ┌─────────────────┐  ┌─────────────────┐      │   │
│   │   │ product-service │  │  order-service  │      │   │
│   │   │   (NodePort)    │  │   (NodePort)    │      │   │
│   │   └────────┬────────┘  └────────┬────────┘      │   │
│   │            │                    │               │   │
│   └────────────┼────────────────────┼───────────────┘   │
│                │                    │                   │
└────────────────┼────────────────────┼───────────────────┘
                 │                    │
         :31234 (example)     :31567 (example)
                 │                    │
                 ▼                    ▼
         ┌─────────────────────────────────┐
         │        Your Network/LAN         │
         │   (Access from any device)      │
         └─────────────────────────────────┘
```

## Differences from Dev/Prod Environments

| Aspect | Dev/Prod (AKS) | Ubuntu (MicroK8s) |
|--------|----------------|-------------------|
| Cluster | Azure AKS | Local MicroK8s |
| Service Type | ClusterIP | NodePort |
| Access | Port-forward or Ingress | Direct via NodePort |
| Resources | Higher limits | Lower limits (200m CPU, 128Mi RAM) |
| Use Case | Cloud deployment | Local development/testing |

## Troubleshooting

### Pods not starting

```bash
# Check pod events
microk8s kubectl describe pod -n microservices-ubuntu <pod-name>

# Check if images can be pulled
microk8s kubectl get events -n microservices-ubuntu
```

### ArgoCD not syncing

```bash
# Check application status
microk8s kubectl get applications -n argocd

# Force sync
microk8s kubectl -n argocd patch app microservices-ubuntu --type merge -p '{"operation": {"initiatedBy": {"username": "admin"}, "sync": {}}}'
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
