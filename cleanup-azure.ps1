# Azure Resource Cleanup Script for DTCE API
# This script deletes all Azure resources created for the DTCE API deployment

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [switch]$Force,
    
    [switch]$BackupFirst
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "=========================================" -ForegroundColor Red
Write-Host "DTCE API - Azure Resource Cleanup" -ForegroundColor Red
Write-Host "=========================================" -ForegroundColor Red
Write-Host ""

# Check if resource group exists
$exists = az group exists --name $ResourceGroupName -o tsv
if ($exists -ne 'true') {
    Write-Host "Resource group '$ResourceGroupName' does not exist." -ForegroundColor Yellow
    exit 0
}

# List resources in the group
Write-Host "Resources in resource group '$ResourceGroupName':" -ForegroundColor Cyan
az resource list --resource-group $ResourceGroupName --output table
Write-Host ""

# Backup option
if ($BackupFirst) {
    Write-Host "Backing up storage account data..." -ForegroundColor Yellow
    
    # Get storage account name
    $storageAccounts = az storage account list --resource-group $ResourceGroupName --query "[].name" -o tsv
    if ($storageAccounts) {
        $storageAccount = $storageAccounts[0]
        $backupDir = ".\backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        
        Write-Host "Downloading blobs from storage account: $storageAccount" -ForegroundColor Yellow
        az storage blob download-batch `
            --destination $backupDir `
            --source dtce-documents `
            --account-name $storageAccount `
            --auth-mode login
        
        Write-Host "Backup completed to: $backupDir" -ForegroundColor Green
    } else {
        Write-Host "No storage account found to backup." -ForegroundColor Yellow
    }
    Write-Host ""
}

# Confirmation
if (-not $Force) {
    Write-Host "WARNING: This will DELETE ALL resources in the resource group '$ResourceGroupName'!" -ForegroundColor Red
    Write-Host "This includes:" -ForegroundColor Red
    Write-Host "  - App Services (API Gateway, Web Client)" -ForegroundColor Red
    Write-Host "  - Function Apps" -ForegroundColor Red
    Write-Host "  - Storage Accounts (ALL DATA WILL BE LOST)" -ForegroundColor Red
    Write-Host "  - Service Bus namespaces" -ForegroundColor Red
    Write-Host "  - App Service Plans" -ForegroundColor Red
    Write-Host ""
    
    $confirmation = Read-Host "Type 'DELETE' to confirm deletion"
    if ($confirmation -ne 'DELETE') {
        Write-Host "Deletion cancelled." -ForegroundColor Green
        exit 0
    }
}

# Delete resource group
Write-Host "Initiating deletion of resource group '$ResourceGroupName'..." -ForegroundColor Yellow
az group delete --name $ResourceGroupName --yes --no-wait

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Deletion initiated successfully." -ForegroundColor Green
    Write-Host "Resources are being deleted asynchronously." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To monitor deletion status, run:" -ForegroundColor Cyan
    Write-Host "  az group show --name $ResourceGroupName" -ForegroundColor White
    Write-Host ""
    Write-Host "To check if deletion is complete:" -ForegroundColor Cyan
    Write-Host "  az group exists --name $ResourceGroupName" -ForegroundColor White
} else {
    Write-Host "Failed to initiate deletion." -ForegroundColor Red
    exit 1
}

