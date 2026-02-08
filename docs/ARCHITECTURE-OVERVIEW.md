# Architecture Overview

## Executive Summary

- This project implements a **GitOps-based microservices platform** with automated deployments, dynamic secrets management, and observability. 
- The architecture follows modern cloud-native best practices but is currently configured for **development/lab use** - production deployment would require additional security hardening.

---

## What We Built

### The Application

Two .NET 9 microservices that work together:

| Service | Purpose | Database |
|---------|---------|----------|
| **ProductService** | Manages product catalog (CRUD operations) | MongoDB |
| **OrderService** | Manages customer orders, references products | MongoDB |

### The Platform

| Component | What It Does | Why We Use It |
|-----------|--------------|---------------|
| **Kubernetes (MicroK8s/AKS)** | Container orchestration | Industry standard, scalable, portable |
| **Helm Charts** | Package and deploy applications | Reusable, version-controlled deployments |
| **ArgoCD** | GitOps continuous deployment | Auto-sync from Git, self-healing |
| **HashiCorp Vault** | Secrets management | Dynamic credentials, no hardcoded passwords |
| **MongoDB** | Document database | Flexible schema, good for microservices |
| **OpenTelemetry** | Observability (traces, metrics, logs) | Unified telemetry, vendor-agnostic |

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              DEVELOPER                                  │
│                                  │                                      │
│                           git push                                      │
│                                  ▼                                      │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                         GITHUB                                    │  │
│  │   Code Repository ──────> GitHub Actions (CI/CD)                  │  │
│  │         │                        │                                │  │
│  │         │                        ▼                                │  │
│  │         │                 Build & Push Images                     │  │
│  │         │                 to Container Registry                   │  │
│  └─────────┼────────────────────────┼────────────────────────────────┘  │
│            │                        │                                   │
│            │ monitors               │ updates image tags                │
│            ▼                        ▼                                   │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                      KUBERNETES CLUSTER                           │  │
│  │                                                                   │  │
│  │   ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐   │  │
│  │   │   ArgoCD    │───>│    Helm     │───>│   Applications      │   │  │
│  │   │  (GitOps)   │    │   Charts    │    │                     │   │  │
│  │   └─────────────┘    └─────────────┘    │  ┌───────────────┐  │   │  │
│  │                                         │  │ProductService │  │   │  │
│  │   ┌─────────────┐                       │  ├───────────────┤  │   │  │
│  │   │    Vault    │──dynamic credentials──│  │ OrderService  │  │   │  │
│  │   │  (Secrets)  │                       │  └───────┬───────┘  │   │  │
│  │   └─────────────┘                       │          │          │   │  │
│  │                                         │          ▼          │   │  │
│  │   ┌─────────────┐                       │  ┌───────────────┐  │   │  │
│  │   │   MongoDB   │<──────────────────────│  │   MongoDB     │  │   │  │
│  │   │ (Database)  │                       │  └───────────────┘  │   │  │
│  │   └─────────────┘                       └─────────────────────┘   │  │
│  │                                                                   │  │
│  │   ┌─────────────────────────────────────────────────────────────┐ │  │
│  │   │                    OBSERVABILITY                            │ │  │
│  │   │  OpenTelemetry ──> Prometheus (metrics)                     │ │  │
│  │   │                ──> Tempo (traces)                           │ │  │
│  │   │                ──> Loki (logs)                              │ │  │
│  │   │                ──> Grafana (visualization)                  │ │  │
│  │   └─────────────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## What's Good (Benefits)

### 1. GitOps Deployment Model
| Benefit | Business Value |
|---------|----------------|
| All changes tracked in Git | Full audit trail, easy rollback |
| Automated deployments | Faster releases, reduced human error |
| Self-healing | ArgoCD auto-corrects manual changes |
| Environment parity | Same process for dev, staging, prod |

### 2. Dynamic Secrets Management (Vault)
| Benefit | Business Value |
|---------|----------------|
| No hardcoded passwords | Reduced risk of credential leaks |
| Short-lived credentials (1 hour) | Limited blast radius if compromised |
| Automatic rotation | No manual password changes needed |
| Audit logging | Track who accessed what secrets |

### 3. Observability Stack
| Benefit | Business Value |
|---------|----------------|
| Distributed tracing | Debug issues across services quickly |
| Centralized logging | Single place to search all logs |
| Custom metrics | Monitor business KPIs |
| Alerting capability | Proactive issue detection |

### 4. Infrastructure as Code
| Benefit | Business Value |
|---------|----------------|
| Reproducible environments | Spin up new environments quickly |
| Version controlled infrastructure | Review changes before applying |
| Terraform for cloud resources | Consistent Azure AKS provisioning |

---

## Current Security Posture

### What IS Secure

| Security Control | Status | Description |
|------------------|--------|-------------|
| Dynamic DB credentials | ✅ Implemented | 1-hour TTL, auto-rotated |
| No secrets in application code | ✅ Implemented | Injected at runtime by Vault |
| Kubernetes RBAC | ✅ Implemented | Services have limited permissions |
| Container image scanning | ✅ Available | GitHub Container Registry |
| Credential auto-revocation | ✅ Implemented | Revoked when pods terminate |

### What is NOT Secure (Lab Configuration)

| Security Gap | Risk Level | Description |
|--------------|------------|-------------|
| Vault in dev mode | 🔴 High | Data in memory, auto-unsealed, root token exposed |
| No TLS encryption | 🔴 High | Traffic unencrypted within cluster |
| Passwords in Git | 🟠 Medium | MongoDB root password in Helm values files |
| No network policies | 🟠 Medium | All pods can communicate with all services |
| No pod security policies | 🟡 Low | Containers could run as root |

---

## Environments

| Environment | Platform | Purpose | Security Level |
|-------------|----------|---------|----------------|
| Ubuntu/MicroK8s | Local VM | Development & testing | Lab only |
| Dev (AKS) | Azure | Integration testing | Lab only |
| Prod (AKS) | Azure | Production workloads | Needs hardening |

---

## Recommended Improvements for Production

### Priority 1: Critical Security (Before Production)

| Improvement | Effort | Impact |
|-------------|--------|--------|
| Enable Vault HA with proper unsealing (Azure Key Vault) | 2-3 days | Eliminates root token exposure |
| Enable TLS for all services | 2-3 days | Encrypts all traffic |
| Move secrets to Azure Key Vault | 1-2 days | Removes passwords from Git |
| Add network policies | 1 day | Limits blast radius of compromise |

### Priority 2: Operational Excellence

| Improvement | Effort | Impact |
|-------------|--------|--------|
| Add Vault audit logging | 1 day | Compliance & forensics |
| Configure alerting rules | 1-2 days | Proactive incident detection |
| Add horizontal pod autoscaling | 1 day | Handle traffic spikes |
| Implement backup strategy | 1-2 days | Disaster recovery |

### Priority 3: Nice to Have

| Improvement | Effort | Impact |
|-------------|--------|--------|
| Add API gateway (Kong/NGINX) | 2-3 days | Rate limiting, API management |
| Implement service mesh (Istio/Linkerd) | 3-5 days | mTLS, traffic management |
| Add chaos engineering | 2-3 days | Resilience testing |

---

## Cost Considerations

### Current Lab Setup (Ubuntu/MicroK8s)
- **Cost:** ~$0 (runs on local VM)
- **Suitable for:** Development, learning, demos

### Azure AKS (Dev/Prod)
| Resource | Estimated Monthly Cost |
|----------|----------------------|
| AKS Cluster (2 nodes, Standard_B2s) | ~$60-80 |
| Azure Key Vault | ~$3-5 |
| Container Registry | ~$5-20 |
| Storage (MongoDB PVCs) | ~$5-10 |
| **Total** | **~$75-120/month** |

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Credential leak (current setup) | Medium | High | Implement Priority 1 items |
| Service outage | Low | Medium | Implement autoscaling & monitoring |
| Data loss | Low | High | Implement backup strategy |
| Supply chain attack | Low | High | Enable image scanning, sign images |

---

## Summary: Go/No-Go for Production

### Current State: ⚠️ NOT Production Ready

The architecture is **sound and follows best practices**, but the current configuration is for **lab/development use only**.

### To Make Production Ready:

1. ✅ Architecture design - **Good**
2. ✅ GitOps workflow - **Good**
3. ✅ Dynamic secrets concept - **Good**
4. ❌ Vault security - **Needs hardening**
5. ❌ TLS encryption - **Not implemented**
6. ❌ Secrets in Git - **Needs remediation**
7. ❌ Network isolation - **Not implemented**

### Estimated Effort to Production Ready: 1-2 Weeks

With Priority 1 items completed, this architecture would be suitable for production workloads handling sensitive data.

---

## Questions?

For technical details, see:
- `SECURITY.md` - Detailed security architecture
- `TROUBLESHOOTING.md` - Common issues and solutions
- `CLAUDE.md` - Development commands and structure
