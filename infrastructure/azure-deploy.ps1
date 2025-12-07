param(
    [switch]$Destroy,
    [string]$Location = "eastus",
    [string]$ResourceGroupName = "DocumentAPI",
    [string]$Environment = "dev",
    [string]$AppNamePrefix = "dtce",
    [string]$ConfigPath,
    [string]$SubscriptionId,
    [string]$TenantId,
    [string]$ClientId,
    [string]$ClientSecret,
    [switch]$PromptForCredentials,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')

function Test-AzCli {
    Write-Host "Checking Azure CLI installation..." -ForegroundColor Yellow
    az version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not installed or not available in PATH. Install from https://aka.ms/installazurecliwindows and retry."
    }
    Write-Host "Azure CLI detected." -ForegroundColor Green
}

function ConvertTo-PlainText([Security.SecureString]$secureString) {
    if (-not $secureString) { return $null }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureString)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Read-Configuration([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Configuration file '$Path' not found."
    }
    try {
        return Get-Content $Path -Raw | ConvertFrom-Json
    } catch {
        throw "Failed to parse configuration file '$Path': $_"
    }
}

function Connect-Azure {
    param(
        [string]$TenantId,
        [string]$ClientId,
        [string]$ClientSecret,
        [string]$SubscriptionId
    )

    Write-Host "Signing in to Azure..." -ForegroundColor Yellow

    if ($ClientId -and $TenantId -and $ClientSecret) {
        az login --service-principal -u $ClientId -p $ClientSecret --tenant $TenantId *> $null
    } else {
        az account show *> $null
        if ($LASTEXITCODE -ne 0) {
            az login *> $null
        }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Azure login failed."
    }

    if ($SubscriptionId) {
        az account set --subscription $SubscriptionId *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to set subscription '$SubscriptionId'."
        }
    }

    $subscription = az account show --query "name" -o tsv
    Write-Host "Authenticated. Active subscription: $subscription" -ForegroundColor Green
}

function Publish-DotnetProject {
    param(
        [string]$ProjectPath,
        [string]$PublishDirectory,
        [string]$ZipPath
    )

    if (Test-Path $PublishDirectory) {
        Remove-Item $PublishDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PublishDirectory | Out-Null

    Write-Host "Publishing $(Split-Path $ProjectPath -Leaf)..." -ForegroundColor Yellow
    dotnet publish $ProjectPath -c Release -o $PublishDirectory --no-self-contained *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for project $ProjectPath"
    }

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }
    Compress-Archive -Path (Join-Path $PublishDirectory '*') -DestinationPath $ZipPath -Force
}

function Publish-ZipPackage {
    param(
        [string]$ResourceGroup,
        [string]$AppName,
        [string]$ZipPath,
        [ValidateSet("webapp","functionapp")]
        [string]$AppType
    )

    if ($AppType -eq 'webapp') {
        az webapp deployment source config-zip -g $ResourceGroup -n $AppName --src $ZipPath *> $null
    } else {
        az functionapp deployment source config-zip -g $ResourceGroup -n $AppName --src $ZipPath *> $null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Deployment of package '$ZipPath' to $AppName failed."
    }
    Write-Host "Deployment package applied to $AppName." -ForegroundColor Green
}

function Set-DeploymentResourceGroup {
    param(
        [string]$Name,
        [string]$Location
    )

    $exists = az group exists --name $Name -o tsv
    if ($exists -ne 'true') {
        Write-Host "Creating resource group '$Name' in '$Location'..." -ForegroundColor Yellow
        az group create --name $Name --location $Location *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create resource group '$Name'."
        }
        Write-Host "Resource group created." -ForegroundColor Green
    } else {
        Write-Host "Resource group '$Name' already exists." -ForegroundColor Green
    }
}

function Remove-DeploymentResourceGroup {
    param(
        [string]$Name,
        [switch]$Force
    )

    $exists = az group exists --name $Name -o tsv
    if ($exists -ne 'true') {
        Write-Host "Resource group '$Name' does not exist." -ForegroundColor Yellow
        return
    }

    if (-not $Force) {
        Write-Host "WARNING: This will delete the entire resource group '$Name'." -ForegroundColor Red
        $confirmation = Read-Host "Type 'yes' to confirm"
        if ($confirmation -ne 'yes') {
            Write-Host "Deletion cancelled." -ForegroundColor Yellow
            return
        }
    }

    Write-Host "Deleting resource group '$Name'..." -ForegroundColor Yellow
    az group delete --name $Name --yes --no-wait *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initiate deletion for resource group '$Name'."
    }
    Write-Host "Deletion requested. Use 'az group show --name $Name' to monitor status." -ForegroundColor Green
}

# Load configuration file if supplied
if ($ConfigPath) {
    $config = Read-Configuration $ConfigPath
    if (-not $PSBoundParameters.ContainsKey('Location') -and $config.location) { $Location = $config.location }
    if (-not $PSBoundParameters.ContainsKey('ResourceGroupName') -and $config.resourceGroupName) { $ResourceGroupName = $config.resourceGroupName }
    if (-not $PSBoundParameters.ContainsKey('Environment') -and $config.environment) { $Environment = $config.environment }
    if (-not $PSBoundParameters.ContainsKey('AppNamePrefix') -and $config.appNamePrefix) { $AppNamePrefix = $config.appNamePrefix }
    if (-not $PSBoundParameters.ContainsKey('SubscriptionId') -and $config.subscriptionId) { $SubscriptionId = $config.subscriptionId }
    if (-not $PSBoundParameters.ContainsKey('TenantId') -and $config.tenantId) { $TenantId = $config.tenantId }
    if (-not $PSBoundParameters.ContainsKey('ClientId') -and $config.clientId) { $ClientId = $config.clientId }
    if (-not $PSBoundParameters.ContainsKey('ClientSecret') -and $config.clientSecret) { $ClientSecret = $config.clientSecret }
}

if ($PromptForCredentials) {
    if (-not $TenantId) { $TenantId = Read-Host "Azure Tenant ID" }
    if (-not $ClientId) { $ClientId = Read-Host "Azure Client (Application) ID" }
    if (-not $ClientSecret) { $ClientSecret = ConvertTo-PlainText (Read-Host "Azure Client Secret" -AsSecureString) }
}

Test-AzCli

if ($Destroy) {
    Connect-Azure -TenantId $TenantId -ClientId $ClientId -ClientSecret $ClientSecret -SubscriptionId $SubscriptionId
    Remove-DeploymentResourceGroup -Name $ResourceGroupName -Force:$Force
    return
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "DTCE API Azure Deployment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

Connect-Azure -TenantId $TenantId -ClientId $ClientId -ClientSecret $ClientSecret -SubscriptionId $SubscriptionId
Set-DeploymentResourceGroup -Name $ResourceGroupName -Location $Location

$deploymentName = "dtce-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Write-Host "Deploying infrastructure (Bicep)..." -ForegroundColor Yellow

az deployment group create `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --template-file (Join-Path $scriptDir 'main.bicep') `
    --parameters `
        resourceGroupName=$ResourceGroupName `
        location=$Location `
        environment=$Environment `
        appNamePrefix=$AppNamePrefix *> $null

if ($LASTEXITCODE -ne 0) {
    throw "Infrastructure deployment failed."
}

Write-Host "Infrastructure deployment complete." -ForegroundColor Green

$outputs = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --query "properties.outputs" -o json | ConvertFrom-Json

$webClientAppName = $outputs.webClientAppName.value
$apiAppName = $outputs.apiAppName.value
$functionAppName = $outputs.functionAppName.value
$apiUrl = $outputs.apiUrl.value
$webClientUrl = $outputs.webClientUrl.value

Write-Host "WebClient App: $webClientAppName ($webClientUrl)" -ForegroundColor Cyan
Write-Host "API Gateway App: $apiAppName ($apiUrl)" -ForegroundColor Cyan
Write-Host "Function App: $functionAppName ($($outputs.functionAppUrl.value))" -ForegroundColor Cyan

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dtce-deploy-$([Guid]::NewGuid().ToString('N'))"
New-Item -Path $tempRoot -ItemType Directory | Out-Null

try {
    $webClientPublish = Join-Path $tempRoot 'webclient'
    $apiPublish = Join-Path $tempRoot 'api'
    $funcPublish = Join-Path $tempRoot 'functions'

    $webClientZip = Join-Path $tempRoot 'webclient.zip'
    $apiZip = Join-Path $tempRoot 'api.zip'
    $funcZip = Join-Path $tempRoot 'functions.zip'

    Publish-DotnetProject -ProjectPath (Join-Path $repoRoot 'Dtce.WebClient\Dtce.WebClient.csproj') -PublishDirectory $webClientPublish -ZipPath $webClientZip
    Publish-DotnetProject -ProjectPath (Join-Path $repoRoot 'Dtce.ApiGateway\Dtce.ApiGateway.csproj') -PublishDirectory $apiPublish -ZipPath $apiZip
    Publish-DotnetProject -ProjectPath (Join-Path $repoRoot 'Dtce.AzureFunctions\Dtce.AzureFunctions.csproj') -PublishDirectory $funcPublish -ZipPath $funcZip

    Publish-ZipPackage -ResourceGroup $ResourceGroupName -AppName $webClientAppName -ZipPath $webClientZip -AppType "webapp"
    Publish-ZipPackage -ResourceGroup $ResourceGroupName -AppName $apiAppName -ZipPath $apiZip -AppType "webapp"
    Publish-ZipPackage -ResourceGroup $ResourceGroupName -AppName $functionAppName -ZipPath $funcZip -AppType "functionapp"

    # Ensure WebClient knows the deployed API base URL
    az webapp config appsettings set -g $ResourceGroupName -n $webClientAppName --settings ApiSettings__BaseUrl=$apiUrl *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to update WebClient ApiSettings__BaseUrl."
    }

    Write-Host "Application code deployed successfully." -ForegroundColor Green
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
    }
}

Write-Host "=========================================" -ForegroundColor Green
Write-Host "Deployment complete." -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host "WebClient URL: $webClientUrl" -ForegroundColor White
Write-Host "API URL: $apiUrl" -ForegroundColor White
Write-Host "Function App Name: $functionAppName" -ForegroundColor White
Write-Host ""
Write-Host "Remember to configure environment-specific secrets (e.g. Service Bus access policies) and to run the background worker services (Ingestion, Parsing, Analysis)." -ForegroundColor Yellow
Write-Host "Use '.\infrastructure\azure-deploy.ps1 -Destroy' to tear everything down." -ForegroundColor Yellow

