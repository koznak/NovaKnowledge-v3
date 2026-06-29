targetScope = 'subscription'

@description('Environment name (dev, staging, prod)')
param environmentName string = 'dev'

@description('Azure region')
param location string = 'eastus2'

@description('Base name prefix for resources')
param baseName string = 'novaknowledge'

var resourceGroupName = 'rg-${baseName}-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: {
    project: 'NovaKnowledge'
    environment: environmentName
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    baseName: baseName
    environmentName: environmentName
  }
}

module security 'modules/security.bicep' = {
  name: 'security'
  scope: rg
  params: {
    location: location
    baseName: baseName
    environmentName: environmentName
  }
}

module data 'modules/data.bicep' = {
  name: 'data'
  scope: rg
  params: {
    location: location
    baseName: baseName
    environmentName: environmentName
    keyVaultName: security.outputs.keyVaultName
  }
}

module ai 'modules/ai.bicep' = {
  name: 'ai'
  scope: rg
  params: {
    location: location
    baseName: baseName
    environmentName: environmentName
    keyVaultName: security.outputs.keyVaultName
  }
}

module compute 'modules/compute.bicep' = {
  name: 'compute'
  scope: rg
  params: {
    location: location
    baseName: baseName
    environmentName: environmentName
    logAnalyticsCustomerId: monitoring.outputs.logAnalyticsCustomerId
    logAnalyticsSharedKey: monitoring.outputs.logAnalyticsSharedKey
    keyVaultUri: security.outputs.keyVaultUri
    postgresConnectionString: data.outputs.postgresConnectionString
    storageConnectionString: data.outputs.storageConnectionString
    serviceBusConnectionString: data.outputs.serviceBusConnectionString
    redisConnectionString: data.outputs.redisConnectionString
    openAiEndpoint: ai.outputs.openAiEndpoint
    searchEndpoint: ai.outputs.searchEndpoint
  }
}

output resourceGroupName string = rg.name
output containerAppsEnvironmentId string = compute.outputs.containerAppsEnvironmentId
output keyVaultUri string = security.outputs.keyVaultUri
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
