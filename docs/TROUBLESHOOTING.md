# Troubleshooting Guide

This document captures issues encountered during deployment and their solutions.

## Table of Contents

1. [MongoDB CrashLoopBackOff - AVX CPU Support](#1-mongodb-crashloopbackoff---avx-cpu-support)
2. [MongoDB Liveness Probe - mongosh Not Found](#2-mongodb-liveness-probe---mongosh-not-found)
3. [Vault Sync Failed - Cluster Resources Not Permitted](#3-vault-sync-failed---cluster-resources-not-permitted)
4. [Vault Sync Failed - batch:Job Not Permitted](#4-vault-sync-failed---batchjob-not-permitted)
5. [Vault Init Job - ServiceAccount Not Found](#5-vault-init-job---serviceaccount-not-found)
6. [Vault Init Job - Cannot Connect to MongoDB](#6-vault-init-job---cannot-connect-to-mongodb)
7. [Ingress Sync Failed - networking.k8s.io Not Permitted](#7-ingress-sync-failed---networkingk8sio-not-permitted)
8. [CD Workflow Not Updating Ubuntu Values](#8-cd-workflow-not-updating-ubuntu-values)
9. [OpenTelemetry Loki Exporter Configuration](#9-opentelemetry-loki-exporter-configuration)
10. [Service Access on MicroK8s - NodePort vs Ingress](#10-service-access-on-microk8s---nodeport-vs-ingress)
11. [Vault Agent - Service Account Not Authorized](#11-vault-agent---service-account-not-authorized)
12. [Services Cannot Connect to MongoDB - Wrong Hostname](#12-services-cannot-connect-to-mongodb---wrong-hostname)
13. [Vault Database Plugin Not Creating Users](#13-vault-database-plugin-not-creating-users)
14. [Vault Configuration Lost After Cluster Restart](#14-vault-configuration-lost-after-cluster-restart)
15. [MongoDB Upgrade - featureCompatibilityVersion Error](#15-mongodb-upgrade---featurecompatibilityversion-error)

---

## 1. MongoDB CrashLoopBackOff - AVX CPU Support

### Symptom

MongoDB pod stuck in `CrashLoopBackOff` status.

```bash
$ kubectl get pods -n microservices-ubuntu
NAME                               READY   STATUS             RESTARTS   AGE
mongodb-ubuntu-6f9c58df6f-grrsl    0/1     CrashLoopBackOff   21         89m
```

### Error Message

```
WARNING: MongoDB 5.0+ requires a CPU with AVX support, and your current system does not appear to have that!
/usr/local/bin/docker-entrypoint.sh: line 416: Illegal instruction (core dumped)
```

### Cause

MongoDB 5.0+ requires CPUs with AVX (Advanced Vector Extensions) instruction support. Older CPUs or some virtualized environments don't have AVX.

### Solution

Downgrade to MongoDB 4.4 (last version without AVX requirement).

**File:** `charts/mongodb/values-ubuntu.yaml`

```yaml
image:
  tag: "4.4"
```

**Commit:** `cb36f3a` - change mongo to v4

---

## 2. MongoDB Liveness Probe - mongosh Not Found

### Symptom

MongoDB pod fails liveness/readiness probes after downgrading to 4.4.

### Error Message

```
Liveness probe errored: exec: "mongosh": executable file not found in $PATH
```

### Cause

MongoDB 4.4 uses the `mongo` shell. The `mongosh` shell was introduced in MongoDB 5.0+.

### Solution

Make the shell command configurable in the deployment template.

**File:** `charts/mongodb/templates/deployment.yaml`

```yaml
livenessProbe:
  exec:
    command:
      - {{ .Values.shell | default "mongosh" }}
      - --eval
      - "db.adminCommand('ping')"
```

**File:** `charts/mongodb/values-ubuntu.yaml`

```yaml
image:
  tag: "4.4"

# MongoDB 4.4 uses 'mongo' shell, not 'mongosh' (5.0+)
shell: mongo
```

**Commit:** `cff0233` - use mongo shell for MongoDB 4.4 compatibility on Ubuntu

---

## 3. Vault Sync Failed - Cluster Resources Not Permitted

### Symptom

Vault ArgoCD application fails to sync with resources not found.

### Error Message

```
Resource not found in cluster: rbac.authorization.k8s.io/v1/ClusterRole:vault-ubuntu-agent-injector-clusterrole
Resource not found in cluster: admissionregistration.k8s.io/v1/MutatingWebhookConfiguration:vault-ubuntu-agent-injector-cfg
Resource not found in cluster: rbac.authorization.k8s.io/v1/ClusterRoleBinding:vault-ubuntu-agent-injector-binding
```

### Cause

The ArgoCD project `clusterResourceWhitelist` only allowed `Namespace` resources. Vault requires cluster-scoped resources like ClusterRole, ClusterRoleBinding, and MutatingWebhookConfiguration.

### Solution

Add the required cluster resources to the ArgoCD project whitelist.

**File:** `argocd/projects/microservices-project.yaml`

```yaml
clusterResourceWhitelist:
  - group: ''
    kind: Namespace
  - group: 'rbac.authorization.k8s.io'
    kind: ClusterRole
  - group: 'rbac.authorization.k8s.io'
    kind: ClusterRoleBinding
  - group: 'admissionregistration.k8s.io'
    kind: MutatingWebhookConfiguration
```

**Commit:** `c8107fa` - allow Vault cluster-scoped resources in ArgoCD project

---

## 4. Vault Sync Failed - batch:Job Not Permitted

### Symptom

Vault sync fails on the init job.

### Error Message

```
message: resource batch:Job is not permitted in project microservices
```

### Cause

The Vault chart includes an init job (`batch/v1 Job`) that wasn't whitelisted in the ArgoCD project.

### Solution

Add the `batch` API group to the namespace resource whitelist.

**File:** `argocd/projects/microservices-project.yaml`

```yaml
namespaceResourceWhitelist:
  - group: ''
    kind: '*'
  - group: 'apps'
    kind: '*'
  - group: 'batch'
    kind: '*'
  # ... other groups
```

**Commit:** `a3f6f94` - allow batch:Job resources in ArgoCD project for Vault init

---

## 5. Vault Init Job - ServiceAccount Not Found

### Symptom

Vault init job pod fails to create.

### Error Message

```
Error creating: pods "vault-ubuntu-init-" is forbidden: error looking up service account
microservices-ubuntu/vault-ubuntu-vault: serviceaccount "vault-ubuntu-vault" not found
```

### Cause

The init job template used `{{ .Release.Name }}-vault` for the service account name, resulting in `vault-ubuntu-vault`. However, the HashiCorp Vault subchart creates service accounts named just `{{ .Release.Name }}` (i.e., `vault-ubuntu`).

### Solution

Fix the service account and service name references in the init job template.

**File:** `charts/vault/templates/init-job.yaml`

```yaml
# Before
serviceAccountName: {{ .Release.Name }}-vault
env:
  - name: VAULT_ADDR
    value: "http://{{ .Release.Name }}-vault:8200"

# After
serviceAccountName: {{ .Release.Name }}
env:
  - name: VAULT_ADDR
    value: "http://{{ .Release.Name }}:8200"
```

**Commit:** `ff9c5db` - fix: correct service account and service name in Vault init job

---

## 6. Vault Init Job - Cannot Connect to MongoDB

### Symptom

Vault init job runs but gets stuck waiting for MongoDB.

### Error Message (from job logs)

```
Waiting for MongoDB to be ready...
MongoDB is not ready yet, waiting...
MongoDB is not ready yet, waiting...
```

### Cause

The Vault init script uses `{{ .Values.mongodb.host }}` to connect to MongoDB. The values file had `host: mongodb` but the actual MongoDB service name is `mongodb-ubuntu` (based on the Helm release name).

### Solution

Update the MongoDB hostname in the Vault values file to match the actual service name.

**File:** `charts/vault/values-ubuntu.yaml`

```yaml
# Before
mongodb:
  host: mongodb
  port: 27017

# After
mongodb:
  host: mongodb-ubuntu
  port: 27017
```

To find the correct service name:
```bash
kubectl get svc -n microservices-ubuntu | grep mongo
```

**Note:** After fixing, you may need to delete and recreate the init job:
```bash
kubectl delete job vault-ubuntu-init -n microservices-ubuntu --force --grace-period=0
```

---

## 7. Ingress Sync Failed - networking.k8s.io Not Permitted

### Symptom

Ingress resources fail to sync in ArgoCD.

### Error Message

```
resource networking.k8s.io/Ingress is not permitted in project microservices
```

### Cause

The ArgoCD project didn't include `networking.k8s.io` in the namespace resource whitelist. When adding Ingress support for the Ubuntu environment, this API group was missing.

### Solution

Add the `networking.k8s.io` API group to the namespace resource whitelist.

**File:** `argocd/projects/microservices-project.yaml`

```yaml
namespaceResourceWhitelist:
  # ... other groups
  - group: 'networking.k8s.io'
    kind: '*'
```

**Commit:** `fb629f0` - Allow networking.k8s.io resources in ArgoCD project

---

## 8. CD Workflow Not Updating Ubuntu Values

### Symptom

After pushing code changes, the Ubuntu environment doesn't get the new image tags. Only dev environment gets updated.

### Cause

The CD workflow (`.github/workflows/cd-dev.yaml`) was only updating `values-dev.yaml` files, not the `values-ubuntu.yaml` files.

### Solution

Update the CD workflow to also update Ubuntu values files.

**File:** `.github/workflows/cd-dev.yaml`

```yaml
- name: Update Product Service image tag
  run: |
    sed -i "s|tag: \"dev.*\"|tag: \"dev-${GITHUB_SHA::7}\"|" charts/product-service/values-dev.yaml
    sed -i "s|tag: \"dev.*\"|tag: \"dev-${GITHUB_SHA::7}\"|" charts/product-service/values-ubuntu.yaml

- name: Update Order Service image tag
  run: |
    sed -i "s|tag: \"dev.*\"|tag: \"dev-${GITHUB_SHA::7}\"|" charts/order-service/values-dev.yaml
    sed -i "s|tag: \"dev.*\"|tag: \"dev-${GITHUB_SHA::7}\"|" charts/order-service/values-ubuntu.yaml

- name: Commit and push
  run: |
    git config --global user.name 'GitHub Actions'
    git config --global user.email 'actions@github.com'
    git add charts/*/values-dev.yaml charts/*/values-ubuntu.yaml
    git diff --staged --quiet || git commit -m "chore: update dev image tags to dev-${GITHUB_SHA::7}"
    git push
```

**Commit:** `2ef149d` - Fix CD workflow to update ubuntu values

---

## 9. OpenTelemetry Loki Exporter Configuration

### Symptom

Logs not appearing in Loki/Grafana from the OpenTelemetry Collector.

### Cause

The OTel Collector was missing the Loki exporter configuration, and later the label configuration format was incorrect.

### Solution

Add Loki exporter and configure the logs pipeline correctly.

**File:** `charts/otel-collector/values-ubuntu.yaml`

```yaml
config:
  exporters:
    loki:
      endpoint: "http://loki.observability:3100/loki/api/v1/push"

  service:
    pipelines:
      logs:
        receivers: [otlp]
        processors: [batch]
        exporters: [loki]
```

The label configuration also needed fixing to use the correct format:

```yaml
# Correct format for resource and attribute labels
resource:
  labels:
    - service.name
    - k8s.namespace.name
    - k8s.pod.name
    - k8s.container.name
attributes:
  labels:
    - level
```

**Commits:**
- `3e5b610` - update loki exporter for local ubuntu
- `9f314d4` - update otel loki

---

## 10. Service Access on MicroK8s - NodePort vs Ingress

### Symptom

Services not accessible from outside the cluster on MicroK8s Ubuntu environment.

### Cause

Initially services were using ClusterIP which is only accessible within the cluster.

### Solution (Evolution)

**Step 1:** Changed to NodePort for external access.

**File:** `charts/*/values-ubuntu.yaml`

```yaml
service:
  type: NodePort
```

**Commit:** `5367ecb` - set NodePort service type for ubuntu environment

**Step 2:** Later migrated to Ingress for better routing using hostnames.

**File:** `charts/*/values-ubuntu.yaml`

```yaml
service:
  type: ClusterIP

ingress:
  enabled: true
  className: nginx
  hosts:
    - host: product.local  # or order.local
      paths:
        - path: /
          pathType: Prefix
```

This required adding `/etc/hosts` entries:
```
127.0.0.1 product.local
127.0.0.1 order.local
```

**Commit:** `1374a02` - add ingress support for ubuntu environment

---

## 11. Vault Agent - Service Account Not Authorized

### Symptom

Services fail to start, stuck in init container. Vault agent logs show authentication errors.

### Error Message (from vault-agent-init container logs)

```
[ERROR] agent.auth.handler: error authenticating:
  error=
  | URL: PUT http://vault-ubuntu.microservices-ubuntu.svc:8200/v1/auth/kubernetes/login
  | Code: 403. Errors:
  |
  | * service account name not authorized
```

### Cause

The Vault Kubernetes auth roles were configured with service account names like `order-service` and `product-service`, but the actual service accounts in the cluster are named with environment suffixes (e.g., `order-service-ubuntu`, `product-service-ubuntu`) based on the Helm release names.

### Diagnosis

Check what service account the pod is using:
```bash
kubectl get pod -l app.kubernetes.io/name=order-service -n microservices-ubuntu -o jsonpath='{.items[0].spec.serviceAccountName}'
```

List all service accounts:
```bash
kubectl get sa -n microservices-ubuntu
```

### Solution

Update the Vault init script to include all possible service account name variations.

**File:** `charts/vault/templates/init-configmap.yaml`

```bash
# Create role for product-service
# Include both short and full names (with environment suffix)
vault write auth/kubernetes/role/product-service \
  bound_service_account_names=product-service,product-service-ubuntu,product-service-dev,product-service-prod,default \
  bound_service_account_namespaces={{ .Release.Namespace }} \
  policies=microservices \
  ttl=1h

# Create role for order-service
# Include both short and full names (with environment suffix)
vault write auth/kubernetes/role/order-service \
  bound_service_account_names=order-service,order-service-ubuntu,order-service-dev,order-service-prod,default \
  bound_service_account_namespaces={{ .Release.Namespace }} \
  policies=microservices \
  ttl=1h
```

After updating, re-run the Vault init job:
```bash
# Delete the old job
kubectl delete job vault-ubuntu-init -n microservices-ubuntu

# Trigger ArgoCD sync
kubectl -n argocd patch application vault-ubuntu --type merge -p '{"metadata":{"annotations":{"argocd.argoproj.io/refresh":"hard"}}}'

# After job completes, restart services
kubectl rollout restart deployment order-service-ubuntu -n microservices-ubuntu
kubectl rollout restart deployment product-service-ubuntu -n microservices-ubuntu
```

---

## 12. Services Cannot Connect to MongoDB - Wrong Hostname

### Symptom

Services crash on startup with MongoDB connection timeout.

### Error Message

```
System.TimeoutException: A timeout occurred after 30000ms selecting a server...
EndPoint: "Unspecified/mongodb:27017"
System.Net.Sockets.SocketException: Name or service not known
```

### Cause

The Vault template in service deployments uses the wrong MongoDB hostname. In Ubuntu environment, services like MongoDB are named with the environment suffix (e.g., `mongodb-ubuntu`), but the values files had `mongodb`.

Similarly, other service references may be wrong:
- `vault.address`: `http://vault:8200` should be `http://vault-ubuntu:8200`
- `vault.mongodbHost`: `mongodb` should be `mongodb-ubuntu`
- `ProductServiceUrl`: `http://product-service:80` should be `http://product-service-ubuntu:80`

### Solution

Update all service hostnames in the Ubuntu values files.

**File:** `charts/order-service/values-ubuntu.yaml`

```yaml
env:
  - name: ProductServiceUrl
    value: "http://product-service-ubuntu:80"

vault:
  enabled: true
  address: "http://vault-ubuntu:8200"
  role: "order-service"
  secretPath: "database/creds/microservices-role"
  mongodbHost: "mongodb-ubuntu"
```

**File:** `charts/product-service/values-ubuntu.yaml`

```yaml
vault:
  enabled: true
  address: "http://vault-ubuntu:8200"
  role: "product-service"
  secretPath: "database/creds/microservices-role"
  mongodbHost: "mongodb-ubuntu"
```

### Key Pattern

In environments where Helm releases include the environment suffix (e.g., `-ubuntu`, `-dev`, `-prod`), all inter-service references must use the full service name.

To find correct service names:
```bash
kubectl get svc -n microservices-ubuntu
```

---

## 13. Vault Database Plugin Not Creating Users

### Symptom

Vault returns credentials but MongoDB authentication fails. Users are never actually created in MongoDB.

### Error Messages

Application logs:
```
MongoDB.Driver.MongoAuthenticationException: Unable to authenticate using sasl protocol mechanism SCRAM-SHA-1.
MongoDB.Driver.MongoCommandException: Command saslStart failed: Authentication failed.
```

Vault logs:
```
[WARN]  MongoDB user was deleted prior to lease revocation: user=v-kubernetes-micr-...
```

### Diagnosis

```bash
# Generate credentials from Vault
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault read database/creds/microservices-role

# Immediately check if user was created in MongoDB (use mongosh for MongoDB 5.0+)
kubectl exec -n microservices-ubuntu deploy/mongodb-ubuntu -- mongosh -u root -p <password> --authenticationDatabase admin --eval "db.getSiblingDB('admin').getUsers()"
```

If the user doesn't appear in MongoDB after generating credentials, Vault isn't actually creating users.

### Cause

The `creation_statements` in the Vault database role was incorrect. MongoDB requires users to be created in the `admin` database with roles granted on the target database.

**Wrong format:**
```json
{ "db": "microservices", "roles": [{ "role": "readWrite" }] }
```

**Correct format:**
```json
{"db": "admin", "roles": [{"role": "readWrite", "db": "microservices"}]}
```

### Solution

Update the Vault role configuration:

**File:** `charts/vault/templates/init-configmap.yaml`

```bash
# Create dynamic credentials role with 1h lease
# Users must be created in 'admin' db with roles granted on target database
vault write database/roles/microservices-role \
  db_name=mongodb \
  creation_statements='{"db": "admin", "roles": [{"role": "readWrite", "db": "microservices"}]}' \
  default_ttl="1h" \
  max_ttl="24h"
```

To fix manually without redeploying:

```bash
# Delete existing config
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault delete database/config/mongodb
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault delete database/roles/microservices-role

# Reconfigure
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault write database/config/mongodb \
  plugin_name=mongodb-database-plugin \
  allowed_roles="microservices-role" \
  connection_url="mongodb://{{username}}:{{password}}@mongodb-ubuntu:27017/admin?authSource=admin" \
  username="root" \
  password="<your-password>"

kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault write database/roles/microservices-role \
  db_name=mongodb \
  creation_statements='{"db": "admin", "roles": [{"role": "readWrite", "db": "microservices"}]}' \
  default_ttl="1h" \
  max_ttl="24h"
```

Then restart services:
```bash
kubectl rollout restart deployment order-service-ubuntu product-service-ubuntu -n microservices-ubuntu
```

---

## Useful Debugging Commands

### Check Pod Status
```bash
kubectl get pods -n microservices-ubuntu
```

### View Pod Logs
```bash
kubectl logs <pod-name> -n microservices-ubuntu
```

### Describe Pod (see events)
```bash
kubectl describe pod <pod-name> -n microservices-ubuntu
```

### Check ArgoCD Application Status
```bash
kubectl get application <app-name> -n argocd -o yaml
```

### Get ArgoCD Sync Error Details
```bash
kubectl get application <app-name> -n argocd -o jsonpath='{.status.operationState.message}'
```

### Check PVC Status
```bash
kubectl get pvc -n microservices-ubuntu
```

### Check Service Accounts
```bash
kubectl get sa -n microservices-ubuntu
```

### Force ArgoCD Sync
```bash
kubectl -n argocd patch application <app-name> --type merge -p '{"operation":{"initiatedBy":{"username":"admin"},"sync":{"force":true}}}'
```

### Hard Refresh ArgoCD Application
```bash
kubectl -n argocd patch application <app-name> --type merge -p '{"metadata":{"annotations":{"argocd.argoproj.io/refresh":"hard"}}}'
```

---

## Vault Debugging Commands

### Check Vault Status
```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault status
```

### View Vault Logs
```bash
kubectl logs vault-ubuntu-0 -n microservices-ubuntu --tail=50
kubectl logs vault-ubuntu-0 -n microservices-ubuntu | grep -i "error\|warn\|mongo"
```

### Check Vault Database Configuration
```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault read database/config/mongodb
```

### Generate Test Credentials
```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault read database/creds/microservices-role
```

### Check Vault Kubernetes Auth Roles
```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault read auth/kubernetes/role/order-service
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault read auth/kubernetes/role/product-service
```

### View Injected Vault Secrets
```bash
# From vault-agent sidecar (always running)
kubectl exec <pod-name> -n microservices-ubuntu -c vault-agent -- cat /vault/secrets/mongodb

# Check vault-agent-init logs
kubectl logs <pod-name> -n microservices-ubuntu -c vault-agent-init
```

### Check Vault Agent Sidecar Logs
```bash
kubectl logs <pod-name> -n microservices-ubuntu -c vault-agent --tail=30
```

### Verify MongoDB Users Created by Vault
```bash
# MongoDB 5.0+ (mongosh)
kubectl exec -n microservices-ubuntu deploy/mongodb-ubuntu -- mongosh -u root -p <password> --authenticationDatabase admin --eval "db.getSiblingDB('admin').getUsers()"

# MongoDB 4.4 (mongo)
kubectl exec -n microservices-ubuntu deploy/mongodb-ubuntu -- mongo -u root -p <password> --authenticationDatabase admin --eval "db.getSiblingDB('admin').getUsers()"
```

### Test MongoDB Connectivity from Vault Pod
```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- sh -c "nc -zv mongodb-ubuntu 27017"
```

---

## 14. Vault Configuration Lost After Cluster Restart

### Symptom

After restarting the Kubernetes cluster (e.g., VM reboot, MicroK8s restart), services are stuck in `Init:0/1` state. The `vault-agent-init` container logs show authentication errors.

### Error Message

```
[ERROR] agent.auth.handler: error authenticating:
  error=
  | URL: PUT http://vault-ubuntu.microservices-ubuntu.svc:8200/v1/auth/kubernetes/login
  | Code: 403. Errors:
  |
  | * permission denied
```

### Diagnosis

Check if Vault has lost its configuration:

```bash
# Check auth methods - should show 'kubernetes/' if configured
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault auth list

# Check secrets engines - should show 'database/' if configured
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- vault secrets list
```

If `kubernetes/` auth or `database/` secrets engine is missing, Vault lost its configuration.

### Cause

Vault in **dev mode** stores all configuration in memory. When the cluster restarts:
1. Vault pod restarts and loses all configuration
2. The init job (which configures Vault) was originally a Helm hook (`post-install,post-upgrade`)
3. Helm hooks only run during actual `helm install` or `helm upgrade` operations
4. ArgoCD "hard refresh" does NOT trigger Helm hooks to re-run

### Solution

**Fix Applied:** The init job now conditionally uses ArgoCD hooks for dev mode:

```yaml
# charts/vault/templates/init-job.yaml
{{- if .Values.vault.server.dev.enabled }}
# Dev mode: Use ArgoCD hooks - runs on every sync (config lost on restart)
argocd.argoproj.io/hook: PostSync
argocd.argoproj.io/hook-delete-policy: BeforeHookCreation
{{- else }}
# Prod mode: Use Helm hooks - runs only on install/upgrade (config persists)
"helm.sh/hook": post-install,post-upgrade
{{- end }}
```

- **Dev/Ubuntu** (`vault.server.dev.enabled: true`): Init job runs on every ArgoCD sync
- **Prod** (`vault.server.dev.enabled: false`): Init job runs only on Helm install/upgrade

**To recover after a cluster restart:**

```bash
# Delete the old init job
kubectl delete job vault-ubuntu-init -n microservices-ubuntu

# Trigger ArgoCD sync (this will now run the init job)
kubectl -n argocd patch application vault-ubuntu --type merge -p '{"operation":{"initiatedBy":{"username":"admin"},"sync":{}}}'

# Wait for init job to complete
kubectl get pods -n microservices-ubuntu -w

# Restart services after init job completes
kubectl rollout restart deployment product-service-ubuntu order-service-ubuntu -n microservices-ubuntu
```

**Manual recovery (if ArgoCD sync doesn't work):**

```bash
kubectl exec -n microservices-ubuntu vault-ubuntu-0 -- sh -c '
vault secrets enable -path=database database 2>/dev/null || true
vault auth enable kubernetes 2>/dev/null || true
vault write auth/kubernetes/config kubernetes_host="https://$KUBERNETES_PORT_443_TCP_ADDR:443"
vault policy write microservices - <<EOF
path "database/creds/microservices-role" { capabilities = ["read"] }
EOF
vault write auth/kubernetes/role/product-service bound_service_account_names=product-service,product-service-ubuntu,default bound_service_account_namespaces=microservices-ubuntu policies=microservices ttl=1h
vault write auth/kubernetes/role/order-service bound_service_account_names=order-service,order-service-ubuntu,default bound_service_account_namespaces=microservices-ubuntu policies=microservices ttl=1h
vault write database/config/mongodb plugin_name=mongodb-database-plugin allowed_roles="microservices-role" connection_url="mongodb://{{username}}:{{password}}@mongodb-ubuntu:27017/admin?authSource=admin" username="root" password="local-dev-password"
vault write database/roles/microservices-role db_name=mongodb creation_statements="{\"db\": \"admin\", \"roles\": [{\"role\": \"readWrite\", \"db\": \"microservices\"}]}" default_ttl="1h" max_ttl="24h"
echo "Done!"
'

# Then restart services
kubectl rollout restart deployment product-service-ubuntu order-service-ubuntu -n microservices-ubuntu
```

### Prevention

For production environments, use Vault with **persistent storage** instead of dev mode. This ensures configuration survives restarts.

See [VAULT-PRODUCTION.md](VAULT-PRODUCTION.md) for complete production setup guide with:
- Azure Key Vault auto-unseal
- Manual initialization steps
- Secure credential management

---

## 15. MongoDB Upgrade - featureCompatibilityVersion Error

### Symptom

After upgrading MongoDB from 4.4 to 7.0+ (e.g., 8.0), the pod crashes on startup.

### Error Message

```json
{"s":"F", "c":"CONTROL", "id":20573, "ctx":"initandlisten", "msg":"Wrong mongod version",
  "attr":{"error":"UPGRADE PROBLEM: Found an invalid featureCompatibilityVersion document
  (ERROR: Invalid feature compatibility version value '4.4'; expected '7.0' or '7.3' or '8.0')"}}
```

### Cause

MongoDB stores a `featureCompatibilityVersion` in the data directory. You cannot skip major versions during upgrade:
- 4.4 → 5.0 → 6.0 → 7.0 → 8.0 (required path)
- 4.4 → 8.0 directly (not supported)

### Solution

**Option 1: Delete data and start fresh (recommended for dev)**

Since dev environments have data seeding, it's easiest to delete the PVC:

```bash
# Delete pod first (PVC delete will hang if pod is using it)
kubectl delete pod -n microservices-ubuntu -l app.kubernetes.io/name=mongodb --force --grace-period=0

# Delete PVC
kubectl delete pvc -n microservices-ubuntu -l app.kubernetes.io/name=mongodb

# If PVC hangs, remove finalizer
kubectl patch pvc <pvc-name> -n microservices-ubuntu -p '{"metadata":{"finalizers":null}}'

# ArgoCD will recreate MongoDB with fresh data
# Then restart services to get fresh connections
kubectl rollout restart deployment product-service-ubuntu order-service-ubuntu -n microservices-ubuntu
```

**Option 2: Incremental upgrade (preserves data)**

If you need to preserve data, upgrade through each major version:

1. Set `image.tag: "5.0"`, sync, wait for healthy
2. Set `image.tag: "6.0"`, sync, wait for healthy
3. Set `image.tag: "7.0"`, sync, wait for healthy
4. Set `image.tag: "8.0"`, sync, wait for healthy

At each step, MongoDB automatically upgrades the `featureCompatibilityVersion`.

### Related Changes

When upgrading from MongoDB 4.4 to 5.0+:
- Remove `shell: mongo` from values file (5.0+ uses `mongosh` by default)
- The deployment template already handles this: `{{ .Values.shell | default "mongosh" }}`

### Prerequisites for MongoDB 5.0+

MongoDB 5.0+ requires CPU with AVX support. Verify before upgrading:

```bash
# Check for AVX support
grep -o 'avx[^ ]*' /proc/cpuinfo | head -1

# Test MongoDB image directly
docker run --rm mongo:8.0 mongod --version
```

If you see "AVX required" errors, your CPU doesn't support it. Either:
- Stay on MongoDB 4.4
- Upgrade VM/hardware to support AVX
