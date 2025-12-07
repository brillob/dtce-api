# Start API Gateway for local development
Write-Host "Starting API Gateway..." -ForegroundColor Green
cd "$PSScriptRoot\Dtce.ApiGateway"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --urls "http://localhost:5017"

