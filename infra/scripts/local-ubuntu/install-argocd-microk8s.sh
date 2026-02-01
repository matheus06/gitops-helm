#!/bin/bash
set -e

echo "Installing ArgoCD on MicroK8s..."

# Create namespace
microk8s kubectl create namespace argocd --dry-run=client -o yaml | microk8s kubectl apply -f -

# Install ArgoCD
microk8s kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml

# Wait for ArgoCD to be ready
echo "Waiting for ArgoCD to be ready..."
microk8s kubectl wait --for=condition=available --timeout=300s deployment/argocd-server -n argocd

# Get initial admin password
echo ""
echo "ArgoCD installed successfully!"
echo ""
echo "Initial admin password:"
microk8s kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d
echo ""
echo ""
echo "To access ArgoCD UI, run:"
echo "  microk8s kubectl port-forward svc/argocd-server -n argocd 8080:443"
echo ""
echo "Then open: https://localhost:8080"
echo "Username: admin"
echo ""
