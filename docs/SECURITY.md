# Security Architecture

This document describes the security architecture, secrets management, and authentication flows used in this GitOps Helm project.

## Table of Contents

1. [Overview](#overview)
2. [HashiCorp Vault Integration](#hashicorp-vault-integration)
3. [MongoDB Authentication](#mongodb-authentication)
4. [Kubernetes Authentication](#kubernetes-authentication)
5. [Secrets Flow Diagram](#secrets-flow-diagram)
6. [Security Considerations](#security-considerations)
7. [Production Recommendations](#production-recommendations)

---

## Overview

This project uses **HashiCorp Vault** for dynamic secrets management, providing:

- **Dynamic database credentials** - Short-lived MongoDB credentials (1-hour TTL)
- **Automatic rotation** - Credentials are automatically renewed before expiration
- **Kubernetes-native authentication** - Services authenticate using ServiceAccount tokens
- **Zero hardcoded secrets** - No database passwords in application code or config

### Components

| Component | Purpose |
|-----------|---------|
| Vault Server | Central secrets management |
| Vault Agent Injector | Mutating webhook that injects sidecar containers |
| Vault Agent (sidecar) | Authenticates and renders secrets to files |
| MongoDB | Database with dynamic user management |

---

## HashiCorp Vault Integration

### Vault Server Configuration

Vault runs in **dev mode** for this lab environment:

```yaml
vault:
  server:
    dev:
      enabled: true
      devRootToken: "root"  # Only for dev - use proper unseal in production
```

**Warning:** Dev mode is NOT secure for production. See [Production Recommendations](#production-recommendations).

### Secrets Engines Enabled

#### 1. Database Secrets Engine

Manages dynamic MongoDB credentials:

```bash
# Enable database secrets engine
vault secrets enable -path=database database

# Configure MongoDB connection
vault write database/config/mongodb \
  plugin_name=mongodb-database-plugin \
  allowed_roles="microservices-role" \
  connection_url="mongodb://{{username}}:{{password}}@mongodb-ubuntu:27017/admin?authSource=admin" \
  username="root" \
  password="<root-password>"

# Create role for dynamic credentials
vault write database/roles/microservices-role \
  db_name=mongodb \
  creation_statements='{"db": "admin", "roles": [{"role": "readWrite", "db": "microservices"}]}' \
  default_ttl="1h" \
  max_ttl="24h"
```

#### 2. Kubernetes Auth Method

Allows Kubernetes ServiceAccounts to authenticate:

```bash
# Enable Kubernetes auth
vault auth enable kubernetes

# Configure Kubernetes auth
vault write auth/kubernetes/config \
  kubernetes_host="https://${KUBERNETES_PORT_443_TCP_ADDR}:443"

# Create roles for services
vault write auth/kubernetes/role/product-service \
  bound_service_account_names=product-service,product-service-ubuntu,default \
  bound_service_account_namespaces=microservices-ubuntu \
  policies=microservices \
  ttl=1h

vault write auth/kubernetes/role/order-service \
  bound_service_account_names=order-service,order-service-ubuntu,default \
  bound_service_account_namespaces=microservices-ubuntu \
  policies=microservices \
  ttl=1h
```

### Vault Policies

The `microservices` policy grants read access to database credentials:

```hcl
path "database/creds/microservices-role" {
  capabilities = ["read"]
}
```

---

## MongoDB Authentication

### Root User

MongoDB is deployed with a root user for Vault to manage dynamic credentials:

```yaml
# charts/mongodb/values-ubuntu.yaml
auth:
  rootPassword: local-dev-password  # Change in production!
```

### Dynamic Users

Vault creates short-lived users with this format:
- **Username:** `v-kubernetes-micr-microservices-r-XXXX-TIMESTAMP`
- **Database:** `admin`
- **Roles:** `readWrite` on `microservices` database
- **TTL:** 1 hour (automatically renewed)

### User Lifecycle

1. Service pod starts
2. Vault agent authenticates using Kubernetes ServiceAccount
3. Vault generates new MongoDB credentials
4. Vault creates user in MongoDB `admin` database
5. Credentials written to `/vault/secrets/mongodb`
6. Application reads connection string from file
7. Vault agent renews credentials before expiration
8. On pod termination, Vault revokes credentials

---

## Kubernetes Authentication

### ServiceAccount Token Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Kubernetes     │     │   Vault Agent   │     │  Vault Server   │
│  ServiceAccount │────>│   (sidecar)     │────>│                 │
│  Token (JWT)    │     │                 │     │  Validates JWT  │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

1. Pod has a ServiceAccount with a projected token
2. Vault Agent reads the token from `/var/run/secrets/kubernetes.io/serviceaccount/token`
3. Vault Agent sends token to Vault's Kubernetes auth endpoint
4. Vault validates token against Kubernetes API
5. If valid, Vault issues a Vault token with attached policies
6. Vault Agent uses this token to read secrets

### Pod Annotations

Services must have these annotations for Vault injection:

```yaml
annotations:
  vault.hashicorp.com/agent-inject: "true"
  vault.hashicorp.com/role: "order-service"
  vault.hashicorp.com/agent-inject-secret-mongodb: "database/creds/microservices-role"
  vault.hashicorp.com/agent-inject-template-mongodb: |
    {{- with secret "database/creds/microservices-role" -}}
    mongodb://{{ .Data.username }}:{{ .Data.password }}@mongodb-ubuntu:27017/microservices?authSource=admin
    {{- end -}}
```

---

## Secrets Flow Diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           KUBERNETES CLUSTER                             │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                         SERVICE POD                                │  │
│  │                                                                    │  │
│  │  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────┐  │  │
│  │  │  vault-agent-init│    │   vault-agent    │    │   app        │  │  │
│  │  │  (init container)│    │   (sidecar)      │    │  container   │  │  │
│  │  │                  │    │                  │    │              │  │  │
│  │  │ 1. Auth to Vault │    │ 4. Renew creds   │    │ 3. Read file │  │  │
│  │  │ 2. Render secret │    │    every 20min   │    │    & connect │  │  │
│  │  └────────┬─────────┘    └────────┬─────────┘    └──────┬───────┘  │  │
│  │           │                       │                     │          │  │
│  │           └───────────────────────┴─────────────────────┘          │  │
│  │                                   │                                │  │
│  │                    /vault/secrets/mongodb                          │  │
│  │                    (shared volume)                                 │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                      │                                   │
│                                      │ Kubernetes Auth                   │
│                                      ▼                                   │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                         VAULT SERVER                               │  │
│  │                                                                    │  │
│  │  ┌─────────────────┐    ┌─────────────────┐    ┌────────────────┐  │  │
│  │  │  Kubernetes     │    │   Database      │    │   Policies     │  │  │
│  │  │  Auth Method    │    │   Secrets       │    │                │  │  │
│  │  │                 │    │   Engine        │    │ microservices  │  │  │
│  │  │ Validates JWT   │    │ Creates/Revokes │    │ policy         │  │  │
│  │  │ Issues tokens   │    │ MongoDB users   │    │                │  │  │
│  │  └─────────────────┘    └────────┬────────┘    └────────────────┘  │  │
│  └──────────────────────────────────┼─────────────────────────────────┘  │
│                                     │                                    │
│                                     │ Create/Delete Users                │
│                                     ▼                                    │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │                         MONGODB                                    │  │
│  │                                                                    │  │
│  │  ┌─────────────────┐    ┌─────────────────────────────────────┐    │  │
│  │  │  Root User      │    │  Dynamic Users (v-kubernetes-...)   │    │  │
│  │  │  (Vault admin)  │    │  TTL: 1 hour, auto-revoked          │    │  │
│  │  └─────────────────┘    └─────────────────────────────────────┘    │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Security Considerations

### Current Security Status (Lab Environment)

| Aspect | Status | Notes |
|--------|--------|-------|
| Dynamic credentials | Enabled | 1-hour TTL with auto-renewal |
| Secrets in Git | No | Connection strings injected at runtime |
| Secrets in env vars | Fallback only | Primary source is Vault |
| TLS for Vault | No | Dev mode uses HTTP |
| TLS for MongoDB | No | Internal cluster traffic |
| Vault unsealing | Auto (dev mode) | Not secure for production |
| Root token | Hardcoded "root" | Not secure for production |

### What IS Secure

1. **No hardcoded database passwords in code** - Application reads from `/vault/secrets/mongodb`
2. **Short-lived credentials** - 1-hour TTL limits exposure window
3. **Automatic credential rotation** - Vault agent renews before expiration
4. **Principle of least privilege** - Services only get `readWrite` on their database
5. **Kubernetes RBAC** - ServiceAccounts bound to specific namespaces
6. **Automatic revocation** - Credentials revoked when pods terminate

### What is NOT Secure (Lab Only)

1. **Dev mode Vault** - Data stored in memory, auto-unsealed
2. **Root token "root"** - Trivially guessable
3. **No TLS** - Traffic is unencrypted within cluster
4. **Passwords in values files** - MongoDB root password in Git
5. **No network policies** - All pods can reach all services

---

## Production Recommendations

### 1. Vault Server

```yaml
vault:
  server:
    dev:
      enabled: false  # Disable dev mode
    ha:
      enabled: true   # High availability with Raft
      replicas: 3

    # Use auto-unseal with cloud KMS
    seal:
      type: azurekeyvault  # or awskms, gcpkms
      config:
        tenant_id: "<azure-tenant-id>"
        vault_name: "<keyvault-name>"
        key_name: "vault-unseal-key"
```

### 2. TLS Configuration

```yaml
vault:
  server:
    extraEnvironmentVars:
      VAULT_CACERT: /vault/userconfig/vault-tls/ca.crt
    volumes:
      - name: vault-tls
        secret:
          secretName: vault-tls
    volumeMounts:
      - name: vault-tls
        mountPath: /vault/userconfig/vault-tls
```

### 3. MongoDB TLS

```yaml
mongodb:
  tls:
    enabled: true
    certificatesSecret: mongodb-tls
```

### 4. Network Policies

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: mongodb-access
spec:
  podSelector:
    matchLabels:
      app: mongodb
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app.kubernetes.io/name: vault
        - podSelector:
            matchLabels:
              app.kubernetes.io/name: order-service
        - podSelector:
            matchLabels:
              app.kubernetes.io/name: product-service
      ports:
        - port: 27017
```

### 5. Remove Secrets from Git

Use external secrets management:

```yaml
# External Secrets Operator
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
metadata:
  name: mongodb-root
spec:
  secretStoreRef:
    name: azure-keyvault
    kind: ClusterSecretStore
  target:
    name: mongodb-root-secret
  data:
    - secretKey: password
      remoteRef:
        key: mongodb-root-password
```

### 6. Audit Logging

Enable Vault audit logging:

```bash
vault audit enable file file_path=/vault/audit/audit.log
```

---

## Verification Commands

### Verify Vault Agent Injection

```bash
# Check pod has vault-agent containers
kubectl get pod <pod-name> -n microservices-ubuntu -o jsonpath='{.spec.containers[*].name}'
# Should show: order-service vault-agent

# Check init container completed
kubectl get pod <pod-name> -n microservices-ubuntu -o jsonpath='{.status.initContainerStatuses[*].name}'
# Should show: vault-agent-init
```

### Verify Secrets Mounted

```bash
# Check secret file exists
kubectl exec <pod-name> -n microservices-ubuntu -c vault-agent -- cat /vault/secrets/mongodb
```

### Verify Dynamic Users in MongoDB

```bash
kubectl exec -n microservices-ubuntu deploy/mongodb-ubuntu -- \
  mongo -u root -p <password> --authenticationDatabase admin \
  --eval "db.getSiblingDB('admin').getUsers()"
```

### Verify Vault Auth Working

```bash
# Check Vault logs for successful authentications
kubectl logs vault-ubuntu-0 -n microservices-ubuntu | grep "authentication successful"
```

---

## Incident Response

### Credential Leak

If credentials are leaked:

1. **Rotate Vault root credentials:**
   ```bash
   kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- \
     vault write -force database/rotate-root/mongodb
   ```

2. **Revoke all leases:**
   ```bash
   kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- \
     vault lease revoke -prefix database/creds/microservices-role
   ```

3. **Restart all services** to get new credentials:
   ```bash
   kubectl rollout restart deployment -n microservices-ubuntu
   ```

### MongoDB Root Password Compromised

1. Change password in MongoDB directly
2. Update Vault database config with new password
3. Rotate root credentials in Vault
4. Update values file and redeploy

---

## References

- [Vault Database Secrets Engine](https://developer.hashicorp.com/vault/docs/secrets/databases)
- [Vault Kubernetes Auth](https://developer.hashicorp.com/vault/docs/auth/kubernetes)
- [Vault Agent Sidecar Injector](https://developer.hashicorp.com/vault/docs/platform/k8s/injector)
- [MongoDB Vault Plugin](https://developer.hashicorp.com/vault/docs/secrets/databases/mongodb)
