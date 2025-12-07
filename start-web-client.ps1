# Start Web Client for local development
Write-Host "Starting Web Client..." -ForegroundColor Green
cd "$PSScriptRoot\Dtce.WebClient"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --urls "http://localhost:5091"

