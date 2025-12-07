# Azure Infrastructure Deployment

This directory contains the infrastructure as code (IaC) templates and deployment scripts for the DTCE API project.

## Prerequisites

1. **Azure CLI** - Install from [https://aka.ms/installazurecliwindows](https://aka.ms/installazurecliwindows)
2. **Azure Subscription** - You need an active Azure subscription
3. **Azure Login** - You must be logged in to Azure CLI (`az login`)

## Resources Created

The deployment creates the following Azure resources in the `DocumentAPI` resource group:

- **Storage Account** - For blob storage and table storage
  - Blob container: `documents`
  - Table storage (created automatically when accessed)
- **Service Bus Namespace** - For message queuing
- **App Service Plan** - Linux-based Basic tier plan
- **Web App (API)** - For hosting the Dtce.ApiGateway REST API
- **Function App** - For hosting the Dtce.AzureFunctions application

## Deployment

### Unified Script (Recommended)

Use the unified script for both deployment and destruction:

**Deploy Infrastructure:**

```powershell
.\infrastructure\azure-deploy.ps1
```

**Destroy Infrastructure:**

```powershell
.\infrastructure\azure-deploy.ps1 -Destroy
```

Or with force flag (no confirmation prompt):

```powershell
.\infrastructure\azure-deploy.ps1 -Destroy -Force
```

**Configuration file example (`infrastructure/deploy.settings.json`):**

```json
{
  "subscriptionId": "00000000-0000-0000-0000-000000000000",
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "clientId": "00000000-0000-0000-0000-000000000000",
  "clientSecret": "<service-principal-secret>",
  "location": "eastus",
  "resourceGroupName": "DocumentAPI",
  "environment": "prod",
  "appNamePrefix": "dtce"
}
```

Run with the config file:

```powershell
.\infrastructure\azure-deploy.ps1 -ConfigPath .\infrastructure\deploy.settings.json
```

Use `-PromptForCredentials` to enter tenant/client/secret interactively without storing them on disk.

> Copy `deploy.settings.example.json` to `deploy.settings.json` and fill in your Azure identifiers when using a service principal.

> The unified script now deploys the infrastructure **and** pushes freshly published application packages (WebClient, API Gateway, Azure Functions) via ZIP deployment.

**Parameters:**
- `-Destroy` (optional): If set, destroys all resources instead of deploying
- `-Force` (optional): Skip confirmation prompt (only for destroy)
- `-Location` (optional): Azure region (default: `eastus`)
- `-ResourceGroupName` (optional): Resource group name (default: `DocumentAPI`)
- `-Environment` (optional): Environment name (default: `dev`)
- `-AppNamePrefix` (optional): Prefix for resource names (default: `dtce`)

## Manual Deployment (Alternative)

If you prefer to deploy manually using Azure CLI:

```powershell
# Create resource group
az group create --name DocumentAPI --location eastus

# Deploy Bicep template
az deployment group create `
    --resource-group DocumentAPI `
    --template-file infrastructure/main.bicep `
    --parameters resourceGroupName=DocumentAPI location=eastus environment=dev appNamePrefix=dtce
```

## Manual Destruction (Alternative)

To manually delete the resource group:

```powershell
az group delete --name DocumentAPI --yes
```

## Outputs

After deployment, the script will display:
- Storage Account Name
- Service Bus Namespace Name
- Web App Name and URL
- Function App Name and URL
- Connection strings (stored in App Settings)

## Next Steps

After deploying the infrastructure:

1. **Configure Application Settings**: Update your application configuration files with the connection strings from the deployment outputs
2. **Deploy Application Code**: 
   - Deploy the WebClient to the Web App
   - Deploy the AzureFunctions to the Function App
3. **Test**: Verify that all services are working correctly

## Notes

- Resource names are automatically generated with unique suffixes to ensure uniqueness
- All resources are created in the same resource group for easy management
- The deployment uses Bicep templates for infrastructure as code
- Storage account connection strings and Service Bus connection strings are automatically configured in the App Service and Function App settings

