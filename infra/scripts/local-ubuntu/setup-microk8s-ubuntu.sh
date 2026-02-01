#!/bin/bash
set -e

echo "============================================"
echo "MicroK8s Setup for Ubuntu Server"
echo "============================================"
echo ""

# Check if running as root or with sudo
if [ "$EUID" -ne 0 ]; then
    echo "Please run with sudo: sudo ./setup-microk8s-ubuntu.sh"
    exit 1
fi

# Install microk8s
echo "Installing MicroK8s..."
snap install microk8s --classic

# Add current user to microk8s group
CURRENT_USER=${SUDO_USER:-$USER}
usermod -a -G microk8s $CURRENT_USER
chown -f -R $CURRENT_USER ~/.kube || true

echo "Waiting for MicroK8s to be ready..."
microk8s status --wait-ready

# Enable required addons
echo "Enabling required addons..."
microk8s enable dns
microk8s enable storage
microk8s enable helm3
microk8s enable obeservability
microk8s enable ingress

echo ""
echo "============================================"
echo "MicroK8s installed successfully!"
echo "============================================"
echo ""
echo "IMPORTANT: Log out and log back in for group changes to take effect."
echo "Or run: newgrp microk8s"
echo ""
echo "To use kubectl, you can either:"
echo "  1. Use microk8s kubectl (e.g., microk8s kubectl get pods)"
echo "  2. Create an alias: alias kubectl='microk8s kubectl'"
echo "  3. Export kubeconfig: microk8s config > ~/.kube/config"
echo ""
echo "Next steps:"
echo "  1. Run: ./install-argocd-microk8s.sh"
echo "  2. Apply the ubuntu environment: ./setup-argocd-apps-ubuntu.sh"
echo ""
