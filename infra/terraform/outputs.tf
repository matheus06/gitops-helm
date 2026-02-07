output "resource_group_name" {
  description = "The name of the resource group"
  value       = azurerm_resource_group.main.name
}

output "cluster_name" {
  description = "The name of the AKS cluster"
  value       = azurerm_kubernetes_cluster.main.name
}

output "cluster_id" {
  description = "The ID of the AKS cluster"
  value       = azurerm_kubernetes_cluster.main.id
}

output "kube_config_command" {
  description = "Command to configure kubectl"
  value       = "az aks get-credentials --resource-group ${azurerm_resource_group.main.name} --name ${azurerm_kubernetes_cluster.main.name}"
}

output "acr_login_server" {
  description = "The login server of the ACR"
  value       = var.create_acr ? azurerm_container_registry.main[0].login_server : null
}

# ============================================================================
# Vault Auto-Unseal Outputs
# ============================================================================

output "vault_keyvault_name" {
  description = "The name of the Azure Key Vault for Vault unseal"
  value       = var.create_vault_keyvault ? azurerm_key_vault.vault_unseal[0].name : null
}

output "vault_keyvault_key_name" {
  description = "The name of the unseal key in Key Vault"
  value       = var.create_vault_keyvault ? azurerm_key_vault_key.vault_unseal[0].name : null
}

output "vault_identity_client_id" {
  description = "The client ID of the Vault managed identity"
  value       = var.create_vault_keyvault ? azurerm_user_assigned_identity.vault[0].client_id : null
}

output "aks_oidc_issuer_url" {
  description = "The OIDC issuer URL for workload identity"
  value       = azurerm_kubernetes_cluster.main.oidc_issuer_url
}
