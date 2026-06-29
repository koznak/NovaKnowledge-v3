param location string
param baseName string
param environmentName string
param keyVaultName string

var storageName = take(replace('st${baseName}${environmentName}', '-', ''), 24)
var serviceBusName = 'sb-${baseName}-${environmentName}'
var postgresName = 'psql-${baseName}-${environmentName}'
var redisName = 'redis-${baseName}-${environmentName}'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storage.name}/default/documents'
  properties: { publicAccess: 'None' }
}

resource blobExports 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storage.name}/default/exports'
  properties: { publicAccess: 'None' }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

resource documentQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'document-uploaded'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' = {
  name: postgresName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: 'novaknowledge'
    administratorLoginPassword: 'ChangeMe123!${uniqueString(resourceGroup().id)}'
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
  }
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: redisName
  location: location
  properties: {
    sku: { name: 'Basic', family: 'C', capacity: 0 }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

var storageKeys = storage.listKeys().keys[0].value
var sbKeys = serviceBus.listKeys().primaryConnectionString
var pgPassword = 'ChangeMe123!${uniqueString(resourceGroup().id)}'
var redisKeys = redis.listKeys().primaryKey

output storageConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storageKeys};EndpointSuffix=core.windows.net'
output serviceBusConnectionString string = sbKeys
output postgresConnectionString string = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=novaknowledge;Username=novaknowledge;Password=${pgPassword};SSL Mode=Require'
output redisConnectionString string = '${redis.properties.hostName}:6380,password=${redisKeys},ssl=True,abortConnect=False'
