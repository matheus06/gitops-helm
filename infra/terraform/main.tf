terraform {
  required_version = ">= 1.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }

  backend "azurerm" {
    # Configure your backend here or use -backend-config
    resource_group_name  = "matheus-shared-rg"
    storage_account_name = "matheussharedstorageacc"
    container_name       = "tfstate"
    key                  = "gitops-helm.tfstate"
  }
}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location

  tags = var.tags
}

resource "azurerm_kubernetes_cluster" "main" {
  name                = var.cluster_name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  dns_prefix          = var.cluster_name
  kubernetes_version  = var.kubernetes_version

  default_node_pool {
    name                = "default"
    node_count          = var.node_count
    vm_size             = var.node_vm_size
    enable_auto_scaling = var.enable_auto_scaling
    min_count           = var.enable_auto_scaling ? var.min_node_count : null
    max_count           = var.enable_auto_scaling ? var.max_node_count : null
  }

  identity {
    type = "SystemAssigned"
  }

  # Enable OIDC issuer for workload identity
  oidc_issuer_enabled       = true
  workload_identity_enabled = true

  network_profile {
    network_plugin    = "azure"
    load_balancer_sku = "standard"
  }

  tags = var.tags
}

resource "azurerm_container_registry" "main" {
  count               = var.create_acr ? 1 : 0
  name                = var.acr_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = false

  tags = var.tags
}

resource "azurerm_role_assignment" "aks_acr" {
  count                            = var.create_acr ? 1 : 0
  principal_id                     = azurerm_kubernetes_cluster.main.kubelet_identity[0].object_id
  role_definition_name             = "AcrPull"
  scope                            = azurerm_container_registry.main[0].id
  skip_service_principal_aad_check = true
}

# ============================================================================
# Azure Key Vault for Vault Auto-Unseal
# ============================================================================

data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "vault_unseal" {
  count                       = var.create_vault_keyvault ? 1 : 0
  name                        = var.vault_keyvault_name
  location                    = azurerm_resource_group.main.location
  resource_group_name         = azurerm_resource_group.main.name
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  sku_name                    = "standard"
  soft_delete_retention_days  = 7
  purge_protection_enabled    = true
  enable_rbac_authorization   = true

  tags = var.tags
}

resource "azurerm_key_vault_key" "vault_unseal" {
  count        = var.create_vault_keyvault ? 1 : 0
  name         = "vault-unseal-key"
  key_vault_id = azurerm_key_vault.vault_unseal[0].id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["unwrapKey", "wrapKey"]

  depends_on = [
    azurerm_role_assignment.keyvault_admin
  ]
}

# Grant current user/SP admin access to create the key
resource "azurerm_role_assignment" "keyvault_admin" {
  count                = var.create_vault_keyvault ? 1 : 0
  scope                = azurerm_key_vault.vault_unseal[0].id
  role_definition_name = "Key Vault Administrator"
  principal_id         = data.azurerm_client_config.current.object_id
}

# ============================================================================
# Workload Identity for Vault
# ============================================================================

resource "azurerm_user_assigned_identity" "vault" {
  count               = var.create_vault_keyvault ? 1 : 0
  name                = "vault-identity"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  tags = var.tags
}

# Grant Vault identity access to unwrap/wrap keys (for auto-unseal)
resource "azurerm_role_assignment" "vault_keyvault_crypto" {
  count                = var.create_vault_keyvault ? 1 : 0
  scope                = azurerm_key_vault.vault_unseal[0].id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = azurerm_user_assigned_identity.vault[0].principal_id
}

# Federated credential for Kubernetes ServiceAccount
resource "azurerm_federated_identity_credential" "vault" {
  count               = var.create_vault_keyvault ? 1 : 0
  name                = "vault-federated-credential"
  resource_group_name = azurerm_resource_group.main.name
  parent_id           = azurerm_user_assigned_identity.vault[0].id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = azurerm_kubernetes_cluster.main.oidc_issuer_url
  subject             = "system:serviceaccount:microservices-prod:vault"
}
