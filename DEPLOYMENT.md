# DTCE API - Deployment Guide

This guide covers deploying the DTCE (Document Template & Context Extractor) API system in local, production, and Azure environments.

## Table of Contents

1. [Local Deployment](#local-deployment)
2. [Production Deployment](#production-deployment)
3. [Azure Deployment](#azure-deployment)
4. [Configuration Settings](#configuration-settings)
5. [Azure Resource Cleanup](#azure-resource-cleanup)

---

## Local Deployment

### Prerequisites

- .NET 9.0 SDK installed
- PowerShell 5.1 or later (Windows)
- At least 4GB RAM available
- 10GB free disk space

### Step 1: Clone and Build

```powershell
# Clone the repository
git clone <repository-url>
cd "DTCE API"

# Restore dependencies
dotnet restore

# Build all projects
dotnet build
```

### Step 2: Configure Local Settings

The system uses local file system for storage and messaging in Dev mode. Update the following files:

#### `Dtce.ApiGateway/appsettings.Development.json`
```json
{
  "Platform": {
    "Mode": "Dev"
  },
  "Storage": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_storage"
  },
  "Messaging": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_queues"
  }
}
```

#### `Dtce.IngestionService/appsettings.Development.json`
```json
{
  "Platform": {
    "Mode": "Dev"
  },
  "Storage": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_storage"
  },
  "Messaging": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_queues"
  }
}
```

#### `Dtce.ParsingEngine/appsettings.Development.json`
```json
{
  "Platform": {
    "Mode": "Dev"
  },
  "Storage": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_storage"
  },
  "Messaging": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_queues"
  }
}
```

#### `Dtce.AnalysisEngine/appsettings.Development.json`
```json
{
  "Platform": {
    "Mode": "Dev"
  },
  "Storage": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_storage"
  },
  "Messaging": {
    "RootPath": "C:\\Users\\<YourUsername>\\CursorProject\\DTCE API\\local_queues"
  }
}
```

#### `Dtce.WebClient/appsettings.json`
```json
{
  "ApiSettings": {
    "BaseUrl": "http://localhost:5017"
  }
}
```

**Important:** Replace `<YourUsername>` with your actual Windows username. All services must use the same paths for shared storage and messaging.

### Step 3: Start Services

Use the provided PowerShell scripts:

```powershell
# Terminal 1: Start API Gateway
.\start-api-gateway.ps1

# Terminal 2: Start Web Client
.\start-web-client.ps1

# Terminal 3: Start Worker Services (Ingestion, Parsing, Analysis)
.\start-workers.ps1
```

Or start manually:

```powershell
# API Gateway (Port 5017)
cd Dtce.ApiGateway
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5017"

# Web Client (Port 5091)
cd Dtce.WebClient
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run --urls "http://localhost:5091"

# Worker Services (run each in separate terminals)
cd Dtce.IngestionService
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run

cd Dtce.ParsingEngine
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run

cd Dtce.AnalysisEngine
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run
```

### Step 4: Verify Deployment

1. **API Gateway**: http://localhost:5017
2. **Web Client**: http://localhost:5091
3. Check logs for any errors

---

## Production Deployment

### Prerequisites

- Azure subscription
- Azure CLI installed and configured
- Service Principal with Contributor role
- Azure Storage Account
- Azure Service Bus namespace

### Configuration Changes

#### 1. Update Platform Mode

Set `Platform:Mode` to `"Prod"` in all service configuration files:

- `Dtce.ApiGateway/appsettings.json`
- `Dtce.IngestionService/appsettings.json`
- `Dtce.ParsingEngine/appsettings.json`
- `Dtce.AnalysisEngine/appsettings.json`

#### 2. Configure Azure Services

Add Azure connection strings to configuration:

**`Dtce.ApiGateway/appsettings.json`**
```json
{
  "Platform": {
    "Mode": "Prod"
  },
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
      "ContainerName": "dtce-documents"
    },
    "ServiceBus": {
      "ConnectionString": "Endpoint=sb://...servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=..."
    }
  }
}
```

**Worker Services** (`Dtce.IngestionService`, `Dtce.ParsingEngine`, `Dtce.AnalysisEngine`):
```json
{
  "Platform": {
    "Mode": "Prod"
  },
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
      "ContainerName": "dtce-documents"
    },
    "ServiceBus": {
      "ConnectionString": "Endpoint=sb://...servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=..."
    }
  }
}
```

#### 3. Configure CORS (API Gateway)

Update `Dtce.ApiGateway/ApiGatewayHost.cs` to allow your production domain:

```csharp
options.AddDefaultPolicy(policy =>
{
    policy.WithOrigins("https://your-production-domain.com")
          .AllowAnyMethod()
          .AllowAnyHeader()
          .AllowCredentials();
});
```

#### 4. Configure API Key Authentication

In production, API key validation is required. Ensure API keys are properly configured in Azure Table Storage.

---

## Azure Deployment

### Option 1: Using Bicep Template (Recommended)

#### Step 1: Prepare Deployment Settings

Copy and edit the example settings file:

```powershell
cd infrastructure
Copy-Item deploy.settings.example.json deploy.settings.json
```

Edit `deploy.settings.json`:

```json
{
  "subscriptionId": "your-subscription-id",
  "tenantId": "your-tenant-id",
  "clientId": "your-service-principal-client-id",
  "clientSecret": "your-service-principal-secret",
  "location": "eastus",
  "resourceGroupName": "dtce-rg",
  "environment": "prod",
  "appNamePrefix": "dtce"
}
```

#### Step 2: Deploy Infrastructure

```powershell
# Login to Azure
az login

# Set subscription
az account set --subscription "your-subscription-id"

# Run deployment script
.\azure-deploy.ps1
```

The script will:
1. Create a resource group
2. Deploy Azure Storage Account
3. Deploy Azure Service Bus namespace
4. Deploy App Service Plan
5. Deploy API Gateway App Service
6. Deploy Web Client App Service
7. Deploy Azure Functions for worker services
8. Configure all connection strings and settings

#### Step 3: Deploy Application Code

**Deploy API Gateway:**
```powershell
cd Dtce.ApiGateway
dotnet publish -c Release
cd bin\Release\net9.0\publish
az webapp deployment source config-zip --resource-group dtce-rg --name dtce-api-gateway --src publish.zip
```

**Deploy Web Client:**
```powershell
cd Dtce.WebClient
dotnet publish -c Release
cd bin\Release\net9.0\publish
az webapp deployment source config-zip --resource-group dtce-rg --name dtce-web-client --src publish.zip
```

**Deploy Azure Functions:**
```powershell
cd Dtce.AzureFunctions
func azure functionapp publish dtce-functions --dotnet-isolated
```

### Option 2: Manual Azure Portal Deployment

#### Step 1: Create Resource Group

```powershell
az group create --name dtce-rg --location eastus
```

#### Step 2: Create Storage Account

```powershell
az storage account create \
  --name dtcestorage \
  --resource-group dtce-rg \
  --location eastus \
  --sku Standard_LRS

# Create container
az storage container create \
  --name dtce-documents \
  --account-name dtcestorage \
  --auth-mode login
```

#### Step 3: Create Service Bus

```powershell
az servicebus namespace create \
  --resource-group dtce-rg \
  --name dtce-servicebus \
  --location eastus \
  --sku Basic

# Create queues
az servicebus queue create \
  --resource-group dtce-rg \
  --namespace-name dtce-servicebus \
  --name job-requests

az servicebus queue create \
  --resource-group dtce-rg \
  --namespace-name dtce-servicebus \
  --name parsing-jobs

az servicebus queue create \
  --resource-group dtce-rg \
  --namespace-name dtce-servicebus \
  --name analysis-jobs
```

#### Step 4: Create App Service Plan

```powershell
az appservice plan create \
  --name dtce-plan \
  --resource-group dtce-rg \
  --location eastus \
  --sku B1 \
  --is-linux
```

#### Step 5: Create App Services

**API Gateway:**
```powershell
az webapp create \
  --resource-group dtce-rg \
  --plan dtce-plan \
  --name dtce-api-gateway \
  --runtime "DOTNET|9.0"

# Configure app settings
az webapp config appsettings set \
  --resource-group dtce-rg \
  --name dtce-api-gateway \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    Platform__Mode=Prod \
    Azure__Storage__ConnectionString="<storage-connection-string>" \
    Azure__Storage__ContainerName="dtce-documents" \
    Azure__ServiceBus__ConnectionString="<servicebus-connection-string>"
```

**Web Client:**
```powershell
az webapp create \
  --resource-group dtce-rg \
  --plan dtce-plan \
  --name dtce-web-client \
  --runtime "DOTNET|9.0"

# Configure app settings
az webapp config appsettings set \
  --resource-group dtce-rg \
  --name dtce-web-client \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    ApiSettings__BaseUrl="https://dtce-api-gateway.azurewebsites.net" \
    Azure__Storage__ConnectionString="<storage-connection-string>"
```

#### Step 6: Create Function App

```powershell
az functionapp create \
  --resource-group dtce-rg \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 9 \
  --functions-version 4 \
  --name dtce-functions \
  --storage-account dtcestorage

# Configure app settings
az functionapp config appsettings set \
  --resource-group dtce-rg \
  --name dtce-functions \
  --settings \
    Azure__Storage__ConnectionString="<storage-connection-string>" \
    Azure__Storage__ContainerName="dtce-documents" \
    Azure__ServiceBus__ConnectionString="<servicebus-connection-string>"
```

### Step 7: Deploy Code

Follow the deployment steps from Option 1, Step 3.

---

## Configuration Settings

### Environment Variables

| Service | Setting | Local | Production |
|---------|---------|--------|------------|
| All Services | `Platform:Mode` | `Dev` | `Prod` |
| All Services | `Storage:RootPath` | Local path | N/A (uses Azure) |
| All Services | `Messaging:RootPath` | Local path | N/A (uses Azure) |
| All Services | `Azure:Storage:ConnectionString` | N/A | Azure Storage connection string |
| All Services | `Azure:Storage:ContainerName` | N/A | `dtce-documents` |
| All Services | `Azure:ServiceBus:ConnectionString` | N/A | Azure Service Bus connection string |
| API Gateway | `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| Web Client | `ApiSettings:BaseUrl` | `http://localhost:5017` | `https://your-api-gateway.azurewebsites.net` |

### Port Configuration

| Service | Local Port | Production |
|---------|------------|------------|
| API Gateway | 5017 | Auto (Azure App Service) |
| Web Client | 5091 | Auto (Azure App Service) |
| Worker Services | N/A | Azure Functions |

### CORS Configuration

Update `Dtce.ApiGateway/ApiGatewayHost.cs`:

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Local development
        policy.WithOrigins("http://localhost:5091", "https://localhost:7264")
        // Production
        // policy.WithOrigins("https://your-production-domain.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

---

## Azure Resource Cleanup

### Option 1: Delete Entire Resource Group (Recommended)

This deletes all resources in the resource group:

```powershell
az group delete --name dtce-rg --yes --no-wait
```

### Option 2: Delete Individual Resources

```powershell
# Delete App Services
az webapp delete --resource-group dtce-rg --name dtce-api-gateway
az webapp delete --resource-group dtce-rg --name dtce-web-client

# Delete Function App
az functionapp delete --resource-group dtce-rg --name dtce-functions

# Delete App Service Plan
az appservice plan delete --resource-group dtce-rg --name dtce-plan

# Delete Service Bus namespace (deletes all queues)
az servicebus namespace delete --resource-group dtce-rg --name dtce-servicebus

# Delete Storage Account (WARNING: This deletes all data!)
az storage account delete --name dtcestorage --resource-group dtce-rg --yes

# Delete Resource Group (if empty)
az group delete --name dtce-rg --yes
```

### Option 3: Using PowerShell Script

Create a cleanup script `cleanup-azure.ps1`:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName
)

Write-Host "Deleting resource group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "This will delete ALL resources in the resource group!" -ForegroundColor Red

$confirm = Read-Host "Are you sure? (yes/no)"
if ($confirm -ne "yes") {
    Write-Host "Cancelled." -ForegroundColor Green
    exit
}

az group delete --name $ResourceGroupName --yes --no-wait
Write-Host "Deletion initiated. Resources will be deleted shortly." -ForegroundColor Green
```

Run it:
```powershell
.\cleanup-azure.ps1 -ResourceGroupName "dtce-rg"
```

### Important Notes

1. **Data Loss Warning**: Deleting the Storage Account will permanently delete all documents, results, and job history.
2. **Backup First**: Before cleanup, consider backing up important data:
   ```powershell
   # Download all blobs
   az storage blob download-batch \
     --destination ./backup \
     --source dtce-documents \
     --account-name dtcestorage
   ```
3. **Billing**: Resources continue to incur costs until fully deleted. Monitor the Azure portal to confirm deletion.
4. **Soft Delete**: Some resources (like Storage Accounts) may have soft-delete enabled. Check retention policies.

### Verify Deletion

```powershell
# Check if resource group exists
az group exists --name dtce-rg

# List remaining resources
az resource list --resource-group dtce-rg --output table
```

---

## Troubleshooting

### Local Deployment Issues

**Issue: Port already in use**
- Solution: Change ports in `appsettings.json` or stop conflicting services

**Issue: File access denied**
- Solution: Ensure all services use the same absolute paths for `Storage:RootPath` and `Messaging:RootPath`

**Issue: Jobs not processing**
- Solution: Verify all worker services are running and using the same queue paths

### Azure Deployment Issues

**Issue: Deployment fails with authentication error**
- Solution: Ensure service principal has Contributor role on the subscription

**Issue: App Service fails to start**
- Solution: Check Application Insights logs and verify all connection strings are correct

**Issue: Functions not triggering**
- Solution: Verify Service Bus connection string and queue names match configuration

### Common Configuration Mistakes

1. **Mixed Mode**: Don't mix `Dev` and `Prod` modes - all services must use the same mode
2. **Path Mismatch**: In Dev mode, all services must use identical paths for storage and messaging
3. **CORS**: Ensure API Gateway CORS settings include your Web Client URL
4. **Connection Strings**: Verify Azure connection strings are complete and valid

---

## Support

For issues or questions:
1. Check application logs in `Application Insights` (Azure) or console output (local)
2. Review configuration files for typos or missing settings
3. Verify all prerequisites are installed and up to date
