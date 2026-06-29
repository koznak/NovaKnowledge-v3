param location string
param baseName string
param environmentName string
param logAnalyticsCustomerId string
param logAnalyticsSharedKey string
@secure()
param keyVaultUri string
@secure()
param postgresConnectionString string
@secure()
param storageConnectionString string
@secure()
param serviceBusConnectionString string
@secure()
param redisConnectionString string
param openAiEndpoint string
param searchEndpoint string

var acrName = take(replace('acr${baseName}${environmentName}', '-', ''), 24)
var caeName = 'cae-${baseName}-${environmentName}'

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: true }
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
  }
}

var services = [
  'gateway'
  'chat-api'
  'orchestration-api'
  'ingestion-api'
  'ingestion-worker'
  'search-api'
  'feedback-api'
  'admin-api'
  'evaluation-service'
]

resource containerApps 'Microsoft.App/containerApps@2024-03-01' = [for svc in services: {
  name: 'ca-${svc}-${environmentName}'
  location: location
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: svc == 'ingestion-worker' ? null : {
        external: svc == 'gateway'
        targetPort: svc == 'evaluation-service' ? 8000 : 8080
        transport: 'auto'
      }
      secrets: [
        { name: 'postgres-conn', value: postgresConnectionString }
        { name: 'storage-conn', value: storageConnectionString }
        { name: 'servicebus-conn', value: serviceBusConnectionString }
        { name: 'redis-conn', value: redisConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: svc
          image: '${acr.properties.loginServer}/${svc}:latest'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ConnectionStrings__NovaKnowledgeDb', secretRef: 'postgres-conn' }
            { name: 'AzureStorage__ConnectionString', secretRef: 'storage-conn' }
            { name: 'ServiceBus__ConnectionString', secretRef: 'servicebus-conn' }
            { name: 'Redis__ConnectionString', secretRef: 'redis-conn' }
            { name: 'AzureOpenAI__Endpoint', value: openAiEndpoint }
            { name: 'AzureSearch__Endpoint', value: searchEndpoint }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
          ]
        }
      ]
      scale: {
        minReplicas: svc == 'gateway' ? 1 : 0
        maxReplicas: svc == 'gateway' ? 3 : 2
      }
    }
  }
}]

output containerAppsEnvironmentId string = cae.id
output acrLoginServer string = acr.properties.loginServer
