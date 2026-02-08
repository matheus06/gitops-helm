#!/bin/bash
# MicroK8s Cluster Start Script
# This script starts the cluster and reconfigures Vault (dev mode loses config on restart)

set -e

# Set up kubectl alias if not already configured
if ! command -v kubectl &> /dev/null; then
    echo "Setting up kubectl alias..."
    sudo snap alias microk8s.kubectl kubectl 2>/dev/null || true
fi

echo "=========================================="
echo "Starting MicroK8s Cluster"
echo "=========================================="

echo "[1/5] Starting MicroK8s..."
microk8s start

echo "[2/5] Waiting for cluster to be ready..."
microk8s status --wait-ready

echo "[3/5] Waiting for pods to stabilize..."
sleep 30

echo "[4/5] Triggering Vault sync (reconfigures Vault after restart)..."
microk8s kubectl -n argocd patch application vault-ubuntu --type merge -p '{"operation":{"initiatedBy":{"username":"admin"},"sync":{}}}' || {
    echo "Warning: Could not trigger Vault sync. ArgoCD may not be ready yet."
    echo "Try running manually: microk8s kubectl -n argocd patch application vault-ubuntu --type merge -p '{\"operation\":{\"initiatedBy\":{\"username\":\"admin\"},\"sync\":{}}}'"
}

echo "[5/5] Waiting for Vault init job to complete..."
sleep 20

echo "Restarting services to pick up new Vault credentials..."
microk8s kubectl rollout restart deployment product-service-ubuntu order-service-ubuntu -n microservices-ubuntu || {
    echo "Warning: Could not restart services. They may not be deployed yet."
}

echo ""
echo "=========================================="
echo "Cluster startup complete!"
echo "=========================================="
echo ""
echo "Checking pod status..."
microk8s kubectl get pods -n microservices-ubuntu

echo ""
echo "Starting MongoDB port-forward in background (port 27017)..."
nohup microk8s kubectl port-forward svc/mongodb-ubuntu -n microservices-ubuntu 27017:27017 --address 0.0.0.0 > /tmp/mongodb-portforward.log 2>&1 &
echo "MongoDB port-forward PID: $!"
echo "Connect with: mongodb://root:local-dev-password@<VM-IP>:27017/?authSource=admin"

echo ""
echo "If services are still in Init state, wait a moment and check again:"
echo "  microk8s kubectl get pods -n microservices-ubuntu"
echo ""
echo "To stop MongoDB port-forward later:"
echo "  pkill -f 'port-forward svc/mongodb-ubuntu'"
