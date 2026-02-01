#!/bin/bash
set -e

echo "Setting up ArgoCD applications for Ubuntu environment..."

# Apply the microservices project
echo "Creating ArgoCD project..."
microk8s kubectl apply -f ../../argocd/projects/microservices-project.yaml

# Apply namespaces
echo "Creating namespaces..."
microk8s kubectl apply -f ../../argocd/base/namespace.yaml

# Apply the app-of-apps for ubuntu environment
echo "Deploying ubuntu environment apps..."
microk8s kubectl apply -f ../../argocd/apps/app-of-apps-ubuntu.yaml

echo ""
echo "ArgoCD applications configured!"
echo ""
echo "Check the status with:"
echo "  microk8s kubectl get applications -n argocd"
echo ""
echo "To see the deployed services:"
echo "  microk8s kubectl get pods -n microservices-ubuntu"
echo ""
