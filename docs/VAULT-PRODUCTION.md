# Vault Production Setup Guide

This guide explains how to set up HashiCorp Vault in a production AKS environment with proper security practices.

## Table of Contents

1. [Dev Mode vs Production Mode](#dev-mode-vs-production-mode)
2. [Understanding Vault Initialization](#understanding-vault-initialization)
3. [Prerequisites](#prerequisites)
4. [Step 1: Deploy Infrastructure](#step-1-deploy-infrastructure)
5. [Step 2: Deploy Vault via ArgoCD](#step-2-deploy-vault-via-argocd)
6. [Step 3: Initialize Vault](#step-3-initialize-vault)
7. [Step 4: Configure Secrets Engines](#step-4-configure-secrets-engines)
8. [Step 5: Configure Kubernetes Authentication](#step-5-configure-kubernetes-authentication)
9. [Step 6: Create Policies and Roles](#step-6-create-policies-and-roles)
10. [Step 7: Verify Configuration](#step-7-verify-configuration)
11. [Day 2 Operations](#day-2-operations)

---

## Dev Mode vs Production Mode

| Aspect | Dev Mode | Production Mode |
|--------|----------|-----------------|
| Storage | In-memory (lost on restart) | Persistent (file/consul/raft) |
| Unsealing | Auto-unsealed | Requires manual unseal or auto-unseal |
| Root Token | Preset (`root`) | Generated during init |
| TLS | Disabled | Should be enabled |
| Init Job | Automatic (ArgoCD hook) | Manual (this guide) |
| Use Case | Local dev, testing | Production workloads |

**Why no automatic init in production?**
- Credentials shouldn't be stored in Git/Helm values
- Root token and unseal keys must be securely managed
- Production requires audit trail and manual verification
- Security compliance requirements

---

## Understanding Vault Initialization

### What is Vault Initialization?

When Vault starts for the first time with persistent storage, it's in a **sealed** state. Initialization is a one-time process that:

1. **Generates the master key** - Used to encrypt all Vault data
2. **Splits the master key** - Using Shamir's Secret Sharing (e.g., 5 shares, 3 required to unseal)
3. **Creates the root token** - Initial superuser access token
4. **Encrypts the storage backend** - All data at rest is encrypted

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Vault Initialization                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  First Start                    Initialization                          │
│  ┌─────────────┐               ┌─────────────────────────────────┐     │
│  │   Vault     │  vault init   │  Generate Master Key             │     │
│  │  (Sealed)   │ ────────────> │  Split into N shares (5)         │     │
│  │             │               │  Threshold to unseal: M (3)      │     │
│  └─────────────┘               │  Generate Root Token             │     │
│                                └─────────────────────────────────┘     │
│                                              │                          │
│                                              ▼                          │
│                                ┌─────────────────────────────────┐     │
│                                │  Output:                         │     │
│                                │  - 5 Unseal Keys (distribute!)   │     │
│                                │  - 1 Root Token (secure it!)     │     │
│                                └─────────────────────────────────┘     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### What is Unsealing?

After initialization (and after every restart), Vault is **sealed**. Unsealing reconstructs the master key:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          Vault Unsealing                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐               │
│  │  Unseal     │     │  Unseal     │     │  Unseal     │               │
│  │  Key 1      │     │  Key 2      │     │  Key 3      │               │
│  └──────┬──────┘     └──────┬──────┘     └──────┬──────┘               │
│         │                   │                   │                       │
│         └───────────────────┼───────────────────┘                       │
│                             │                                           │
│                             ▼                                           │
│                   ┌─────────────────┐                                   │
│                   │  Reconstruct    │                                   │
│                   │  Master Key     │                                   │
│                   └────────┬────────┘                                   │
│                            │                                            │
│                            ▼                                            │
│                   ┌─────────────────┐                                   │
│                   │     Vault       │                                   │
│                   │   (Unsealed)    │                                   │
│                   │    Ready!       │                                   │
│                   └─────────────────┘                                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Auto-Unseal with Azure Key Vault

Instead of manually providing unseal keys, Vault can use Azure Key Vault to automatically unseal:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      Auto-Unseal with Azure Key Vault                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────┐                    ┌──────────────────────┐       │
│  │   Vault Pod     │   Workload         │   Azure Key Vault    │       │
│  │   (AKS)         │   Identity         │                      │       │
│  │                 │ ─────────────────> │   ┌──────────────┐   │       │
│  │                 │                    │   │  RSA Key     │   │       │
│  │                 │ <───────────────── │   │  (unseal)    │   │       │
│  └─────────────────┘   Decrypt master   │   └──────────────┘   │       │
│          │             key              └──────────────────────┘       │
│          │                                                              │
│          ▼                                                              │
│  ┌─────────────────┐                                                   │
│  │     Vault       │  No manual intervention needed!                   │
│  │   (Unsealed)    │  Survives pod restarts automatically              │
│  └─────────────────┘                                                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

This project uses Azure Key Vault auto-unseal for production (configured in `values-prod.yaml`).

---

## Prerequisites

Before starting, ensure you have:

1. **Azure CLI** logged in with appropriate permissions
2. **kubectl** configured for your AKS cluster
3. **Terraform** applied (creates Azure Key Vault and managed identity)
4. **ArgoCD** installed and accessible

```bash
# Verify Azure login
az account show

# Verify AKS access
kubectl get nodes

# Verify ArgoCD
kubectl get pods -n argocd
```

---

## Step 1: Deploy Infrastructure

The Terraform configuration creates the Azure resources needed for Vault auto-unseal:

```bash
cd infra/terraform
terraform init
terraform apply
```

This creates:
- Azure Key Vault with RSA key for auto-unseal
- Managed Identity for Vault
- Federated Identity Credential (links K8s ServiceAccount to Azure Identity)

**Get the output values:**

```bash
terraform output vault_keyvault_name
terraform output vault_identity_client_id
az account show --query tenantId -o tsv
```

---

## Step 2: Deploy Vault via ArgoCD

### 2.1 Update Production Values

Edit `charts/vault/values-prod.yaml` with the Terraform outputs:

```yaml
vault:
  server:
    extraEnvironmentVars:
      AZURE_TENANT_ID: "<your-tenant-id>"           # From: az account show
      AZURE_KEYVAULT_NAME: "<your-keyvault-name>"   # From: terraform output

    serviceAccount:
      annotations:
        azure.workload.identity/client-id: "<your-client-id>"  # From: terraform output
```

### 2.2 Deploy via ArgoCD

```bash
# Apply the app-of-apps for prod (if not already)
kubectl apply -f argocd/apps/app-of-apps-prod.yaml

# Or sync just Vault
kubectl -n argocd patch application vault-prod --type merge -p '{"operation":{"initiatedBy":{"username":"admin"},"sync":{}}}'
```

### 2.3 Wait for Vault Pod

```bash
# Watch until vault-prod-0 is Running (but NOT Ready - it's sealed)
kubectl get pods -n microservices-prod -w
```

The pod will be `Running` but `0/1 Ready` because Vault is sealed.

---

## Step 3: Initialize Vault

This is a **one-time operation** that generates the root token and recovery keys.

### 3.1 Port Forward to Vault

```bash
kubectl port-forward svc/vault-prod -n microservices-prod 8200:8200
```

### 3.2 Initialize Vault

In another terminal:

```bash
# Set Vault address
export VAULT_ADDR="http://localhost:8200"

# Initialize Vault (with Azure auto-unseal, we get recovery keys instead of unseal keys)
vault operator init -recovery-shares=5 -recovery-threshold=3
```

**CRITICAL: Save the output securely!**

```
Recovery Key 1: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Recovery Key 2: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Recovery Key 3: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Recovery Key 4: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Recovery Key 5: xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

Initial Root Token: hvs.xxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

**Store these securely:**
- Use Azure Key Vault, AWS Secrets Manager, or similar
- Distribute recovery keys to different administrators
- Never store in Git, plain text files, or chat logs

### 3.3 Verify Vault is Unsealed

```bash
vault status
```

Expected output:
```
Key                      Value
---                      -----
Recovery Seal Type       azurekeyvault
Initialized              true
Sealed                   false        # <-- Should be false
Total Recovery Shares    5
Threshold                3
Version                  1.15.x
Storage Type             file
HA Enabled               false
```

---

## Step 4: Configure Secrets Engines

### 4.1 Login with Root Token

```bash
export VAULT_ADDR="http://localhost:8200"
vault login <your-root-token>
```

### 4.2 Enable Database Secrets Engine

```bash
vault secrets enable -path=database database
```

### 4.3 Configure MongoDB Connection

**First, get your MongoDB credentials securely** (don't use values from Git):

```bash
# Example: Get MongoDB password from Azure Key Vault
MONGO_PASSWORD=$(az keyvault secret show --vault-name <keyvault> --name mongodb-password --query value -o tsv)

# Or from Kubernetes secret
MONGO_PASSWORD=$(kubectl get secret mongodb-prod-credentials -n microservices-prod -o jsonpath='{.data.password}' | base64 -d)
```

**Configure the connection:**

```bash
vault write database/config/mongodb \
  plugin_name=mongodb-database-plugin \
  allowed_roles="microservices-role" \
  connection_url="mongodb://{{username}}:{{password}}@mongodb-prod:27017/admin?authSource=admin" \
  username="root" \
  password="${MONGO_PASSWORD}"
```

### 4.4 Create Database Role

```bash
vault write database/roles/microservices-role \
  db_name=mongodb \
  creation_statements='{"db": "admin", "roles": [{"role": "readWrite", "db": "microservices"}]}' \
  default_ttl="1h" \
  max_ttl="24h"
```

### 4.5 Test Dynamic Credentials

```bash
vault read database/creds/microservices-role
```

You should see generated username/password.

---

## Step 5: Configure Kubernetes Authentication

### 5.1 Enable Kubernetes Auth

```bash
vault auth enable kubernetes
```

### 5.2 Configure Kubernetes Auth

```bash
# Get the Kubernetes host from inside the cluster
vault write auth/kubernetes/config \
  kubernetes_host="https://kubernetes.default.svc:443"
```

---

## Step 6: Create Policies and Roles

### 6.1 Create Microservices Policy

```bash
vault policy write microservices - <<EOF
path "database/creds/microservices-role" {
  capabilities = ["read"]
}
EOF
```

### 6.2 Create Service Roles

```bash
# Product Service
vault write auth/kubernetes/role/product-service \
  bound_service_account_names=product-service,product-service-prod,default \
  bound_service_account_namespaces=microservices-prod \
  policies=microservices \
  ttl=1h

# Order Service
vault write auth/kubernetes/role/order-service \
  bound_service_account_names=order-service,order-service-prod,default \
  bound_service_account_namespaces=microservices-prod \
  policies=microservices \
  ttl=1h
```

---

## Step 7: Verify Configuration

### 7.1 Check Auth Methods

```bash
vault auth list
```

Expected:
```
Path           Type          Description
----           ----          -----------
kubernetes/    kubernetes    n/a
token/         token         token based credentials
```

### 7.2 Check Secrets Engines

```bash
vault secrets list
```

Expected:
```
Path          Type         Description
----          ----         -----------
database/     database     n/a
...
```

### 7.3 Check Roles

```bash
vault read auth/kubernetes/role/product-service
vault read auth/kubernetes/role/order-service
```

### 7.4 Restart Services

Now that Vault is configured, restart the services so they can authenticate:

```bash
kubectl rollout restart deployment product-service-prod -n microservices-prod
kubectl rollout restart deployment order-service-prod -n microservices-prod
```

### 7.5 Check Service Logs

```bash
kubectl logs -n microservices-prod -l app.kubernetes.io/name=product-service -c vault-agent-init
```

Should show successful authentication.

---

## Day 2 Operations

### Rotating Root Token

After initial setup, revoke the root token and use policies:

```bash
# Create admin policy first
vault policy write admin - <<EOF
path "*" {
  capabilities = ["create", "read", "update", "delete", "list", "sudo"]
}
EOF

# Create admin token
vault token create -policy=admin -ttl=8h

# Revoke root token
vault token revoke <root-token>
```

### Rotating MongoDB Root Password

If you rotate the MongoDB root password:

```bash
vault write database/config/mongodb \
  plugin_name=mongodb-database-plugin \
  allowed_roles="microservices-role" \
  connection_url="mongodb://{{username}}:{{password}}@mongodb-prod:27017/admin?authSource=admin" \
  username="root" \
  password="${NEW_MONGO_PASSWORD}"
```

### Backup and Recovery

With Azure Key Vault auto-unseal:
- Vault data is in persistent storage (`/vault/data`)
- Auto-unseal key is in Azure Key Vault
- Recovery keys are for emergency recovery only

**Backup strategy:**
1. Regular backups of PVC data
2. Secure storage of recovery keys
3. Azure Key Vault has its own backup/recovery

### Monitoring

```bash
# Check Vault status
kubectl exec -n microservices-prod vault-prod-0 -- vault status

# Check lease count
kubectl exec -n microservices-prod vault-prod-0 -- vault read sys/metrics

# View audit logs (if enabled)
kubectl exec -n microservices-prod vault-prod-0 -- vault audit list
```

---

## Quick Reference Commands

```bash
# Port forward to Vault
kubectl port-forward svc/vault-prod -n microservices-prod 8200:8200

# Check status
vault status

# Login
vault login <token>

# List auth methods
vault auth list

# List secrets engines
vault secrets list

# Generate database credentials
vault read database/creds/microservices-role

# Check Kubernetes roles
vault list auth/kubernetes/role
vault read auth/kubernetes/role/product-service

# View policies
vault policy list
vault policy read microservices
```

---

## Troubleshooting

### Vault Pod Not Ready

Check if Vault is sealed:
```bash
kubectl exec -n microservices-prod vault-prod-0 -- vault status
```

If sealed and auto-unseal isn't working, check:
1. Azure Key Vault permissions
2. Workload Identity configuration
3. Vault logs: `kubectl logs -n microservices-prod vault-prod-0`

### Services Can't Authenticate

1. Check service account name matches the role
2. Check namespace in role configuration
3. Check Vault agent logs in the service pod

### Database Credentials Not Working

1. Verify MongoDB connection in Vault
2. Check credentials are being generated: `vault read database/creds/microservices-role`
3. Verify the user appears in MongoDB
