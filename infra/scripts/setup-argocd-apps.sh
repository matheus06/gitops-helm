#!/bin/bash
set -e

GITHUB_USERNAME=${1:-"GITHUB_USERNAME"}
REPO_URL="https://github.com/${GITHUB_USERNAME}/gitops-helm.git"

echo "Setting up ArgoCD applications..."
echo "GitHub Username: ${GITHUB_USERNAME}"
echo "Repository URL: ${REPO_URL}"

# Create namespaces
kubectl apply -f ../../argocd/base/namespace.yaml

# Update ArgoCD manifests with correct GitHub username
find ../../argocd -name "*.yaml" -type f -exec sed -i "s|GITHUB_USERNAME|${GITHUB_USERNAME}|g" {} \;

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
