@description('The name of the resource group')
param resourceGroupName string = 'DocumentAPI'

@description('The location for all resources')
param location string = resourceGroup().location

@description('The environment name (e.g., dev, staging, prod)')
param environment string = 'dev'

@description('Application name prefix')
param appNamePrefix string = 'dtce'

var uniqueSuffix = uniqueString(resourceGroup().id, appNamePrefix)
var storageAccountName = toLower('${appNamePrefix}storage${uniqueSuffix}')
var serviceBusNamespaceName = toLower('${appNamePrefix}sb${uniqueSuffix}')
var appServicePlanName = toLower('${appNamePrefix}plan${uniqueSuffix}')
var webClientAppName = toLower('${appNamePrefix}web${uniqueSuffix}')
var apiAppName = toLower('${appNamePrefix}api${uniqueSuffix}')
var functionAppName = toLower('${appNamePrefix}func${uniqueSuffix}')
var containerName = 'dtce-documents'
var jobQueueName = 'job-requests'

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storageAccount.name}/default/${containerName}'
  properties: {
    publicAccess: 'None'
  }
}

resource usersTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: '${storageAccount.name}/default/Users'
}

resource apiKeysTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: '${storageAccount.name}/default/ApiKeys'
}

resource jobStatusTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-01-01' = {
  name: '${storageAccount.name}/default/JobStatus'
}

// Service Bus Namespace and queue
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource serviceBusAuthRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' = {
  name: '${serviceBusNamespace.name}/RootManageSharedAccessKey'
  properties: {
    rights: [
      'Listen'
      'Send'
      'Manage'
    ]
  }
}

resource serviceBusQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  name: '${serviceBusNamespace.name}/${jobQueueName}'
  properties: {
    enablePartitioning: true
    deadLetteringOnMessageExpiration: true
  }
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var serviceBusConnectionString = listKeys(serviceBusAuthRule.id, '2017-04-01').primaryConnectionString

// App Service Plan shared by WebClient, API, Function App
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

// WebClient App (MVC)
resource webClientApp 'Microsoft.Web/sites@2023-01-01' = {
  name: webClientAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: true
      appSettings: [
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment
        }
        {
          name: 'Azure__Storage__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'Azure__Storage__ContainerName'
          value: containerName
        }
        {
          name: 'ApiSettings__BaseUrl'
          value: 'https://${apiAppName}.azurewebsites.net'
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// API Gateway App
resource apiWebApp 'Microsoft.Web/sites@2023-01-01' = {
  name: apiAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET|9.0'
      alwaysOn: true
      appSettings: [
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment
        }
        {
          name: 'Azure__Storage__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'Azure__Storage__ContainerName'
          value: containerName
        }
        {
          name: 'Azure__ServiceBus__ConnectionString'
          value: serviceBusConnectionString
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// Function App hosting Azure Functions
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|9.0'
      alwaysOn: true
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'Azure__Storage__ConnectionString'
          value: storageConnectionString
        }
        {
          name: 'Azure__Storage__ContainerName'
          value: containerName
        }
        {
          name: 'Azure__ServiceBus__ConnectionString'
          value: serviceBusConnectionString
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(functionAppName)
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

// Outputs
output storageAccountName string = storageAccount.name
output storageAccountConnectionString string = storageConnectionString
output storageContainerName string = containerName
output serviceBusNamespaceName string = serviceBusNamespace.name
output serviceBusConnectionString string = serviceBusConnectionString
output serviceBusQueueName string = jobQueueName
output webClientAppName string = webClientApp.name
output webClientUrl string = 'https://${webClientApp.properties.defaultHostName}'
output apiAppName string = apiWebApp.name
output apiUrl string = 'https://${apiWebApp.properties.defaultHostName}'
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'

