// SNHub Auth Service — Azure Infrastructure
// Deploy: az deployment group create --resource-group snhub-rg --template-file main.bicep

@description('Environment name: dev, staging, prod')
@allowed(['dev', 'staging', 'prod'])
param env string = 'dev'

@description('Azure region')
param location string = resourceGroup().location

@description('PostgreSQL admin password')
@secure()
param dbAdminPassword string

@description('JWT signing secret')
@secure()
param jwtSecret string

var prefix = 'snhub-${env}'
var tags = {
  project: 'SNHub'
  environment: env
  service: 'auth'
}

// ─── Azure Container Registry ──────────────────────────────────────────────
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'snhubacr${env}'
  location: location
  tags: tags
  sku: { name: env == 'prod' ? 'Premium' : 'Basic' }
  properties: {
    adminUserEnabled: true
  }
}

// ─── Azure Key Vault ───────────────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
  }
}

resource kvJwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'JwtSettings--SecretKey'
  properties: { value: jwtSecret }
}

// ─── PostgreSQL Flexible Server ────────────────────────────────────────────
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: '${prefix}-postgres'
  location: location
  tags: tags
  sku: {
    name: env == 'prod' ? 'Standard_D4s_v3' : 'Standard_B1ms'
    tier: env == 'prod' ? 'GeneralPurpose' : 'Burstable'
  }
  properties: {
    administratorLogin: 'snhubadmin'
    administratorLoginPassword: dbAdminPassword
    version: '17'
    storage: { storageSizeGB: env == 'prod' ? 128 : 32 }
    highAvailability: {
      mode: env == 'prod' ? 'ZoneRedundant' : 'Disabled'
    }
    backup: {
      backupRetentionDays: env == 'prod' ? 35 : 7
      geoRedundantBackup: env == 'prod' ? 'Enabled' : 'Disabled'
    }
  }
}

resource snhubDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: postgres
  name: 'snhub_auth'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// ─── Azure Cache for Redis ─────────────────────────────────────────────────
resource redis 'Microsoft.Cache/redis@2023-08-01' = {
  name: '${prefix}-redis'
  location: location
  tags: tags
  properties: {
    sku: {
      name: env == 'prod' ? 'Standard' : 'Basic'
      family: env == 'prod' ? 'C' : 'C'
      capacity: env == 'prod' ? 1 : 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

// ─── Azure Storage (Blob for CVs, profile images) ─────────────────────────
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'snhubstorage${env}'
  location: location
  tags: tags
  sku: { name: env == 'prod' ? 'Standard_ZRS' : 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// ─── Azure Service Bus ─────────────────────────────────────────────────────
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${prefix}-servicebus'
  location: location
  tags: tags
  sku: { name: env == 'prod' ? 'Standard' : 'Basic' }
}

// ─── Application Insights ─────────────────────────────────────────────────
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: env == 'prod' ? 90 : 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-appinsights'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ─── AKS Cluster ──────────────────────────────────────────────────────────
resource aks 'Microsoft.ContainerService/managedClusters@2024-01-01' = {
  name: '${prefix}-aks'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: '${prefix}-aks'
    kubernetesVersion: '1.29'
    agentPoolProfiles: [
      {
        name: 'system'
        count: env == 'prod' ? 3 : 1
        vmSize: env == 'prod' ? 'Standard_D4s_v3' : 'Standard_B2s'
        mode: 'System'
        enableAutoScaling: env == 'prod'
        minCount: env == 'prod' ? 2 : null
        maxCount: env == 'prod' ? 10 : null
        osDiskSizeGB: 100
      }
    ]
    addonProfiles: {
      omsagent: {
        enabled: true
        config: { logAnalyticsWorkspaceResourceID: logAnalytics.id }
      }
    }
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────
output acrLoginServer string = acr.properties.loginServer
output keyVaultUri string = keyVault.properties.vaultUri
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output aksName string = aks.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output redisHost string = redis.properties.hostName
