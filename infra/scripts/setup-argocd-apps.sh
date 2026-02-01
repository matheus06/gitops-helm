#!/bin/bash
set -e

# Create namespaces
kubectl apply -f ../../argocd/base/namespace.yaml

# Apply ArgoCD project
kubectl apply -f ../../argocd/projects/microservices-project.yaml

# Apply App of Apps for dev environment
kubectl apply -f ../../argocd/apps/app-of-apps-dev.yaml

# Apply App of Apps for prod environment
kubectl apply -f ../../argocd/apps/app-of-apps-prod.yaml

echo ""
echo "ArgoCD applications configured!"
echo ""
echo "View applications in ArgoCD UI or run:"
echo "  kubectl get applications -n argocd"
