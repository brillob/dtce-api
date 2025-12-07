# Start all worker services for local development
# This script starts IngestionService, ParsingEngine, and AnalysisEngine

$ErrorActionPreference = "Stop"

Write-Host "`n=== Starting DTCE Worker Services ===" -ForegroundColor Cyan
Write-Host "Starting all three worker services in separate windows..." -ForegroundColor Yellow
Write-Host ""

$workspaceRoot = $PSScriptRoot

# Start IngestionService
Write-Host "Starting IngestionService..." -ForegroundColor Green
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$workspaceRoot\Dtce.IngestionService'; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run"
) -WindowStyle Normal

Start-Sleep -Seconds 2

# Start ParsingEngine
Write-Host "Starting ParsingEngine..." -ForegroundColor Green
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$workspaceRoot\Dtce.ParsingEngine'; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run"
) -WindowStyle Normal

Start-Sleep -Seconds 2

# Start AnalysisEngine
Write-Host "Starting AnalysisEngine..." -ForegroundColor Green
Start-Process powershell -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$workspaceRoot\Dtce.AnalysisEngine'; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run"
) -WindowStyle Normal

Write-Host "`n✓ All worker services started!" -ForegroundColor Green
Write-Host "`nYou should see three new PowerShell windows:" -ForegroundColor Yellow
Write-Host "  1. IngestionService - processes job-requests queue" -ForegroundColor White
Write-Host "  2. ParsingEngine - processes parsing-jobs queue" -ForegroundColor White
Write-Host "  3. AnalysisEngine - processes analysis-jobs queue" -ForegroundColor White
Write-Host "Keep these windows open while testing." -ForegroundColor Cyan
Write-Host ""

