# PowerShell script to set up Git repository and push to GitHub
# This script helps initialize a git repository and push to a new GitHub repository

param(
    [string]$RepoName = "dtce-api",
    [string]$GitHubUsername = "",
    [string]$Description = "Document Template & Context Extractor API - A .NET-based platform for extracting structured templates and contextual metadata from documents"
)

Write-Host "=== DTCE API - GitHub Repository Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check if git is installed
$gitInstalled = $false
try {
    $null = Get-Command git -ErrorAction Stop
    $gitVersion = git --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $gitInstalled = $true
        Write-Host "✓ Git found: $gitVersion" -ForegroundColor Green
    }
} catch {
    $gitInstalled = $false
}

if (-not $gitInstalled) {
    Write-Host "✗ Git is not installed or not in PATH" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Git from: https://git-scm.com/download/win" -ForegroundColor Yellow
    Write-Host "Or use GitHub Desktop: https://desktop.github.com/" -ForegroundColor Yellow
    exit 1
}

# Check if GitHub CLI is installed (optional)
$hasGh = $false
try {
    $null = Get-Command gh -ErrorAction Stop
    $ghVersion = gh --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $hasGh = $true
        Write-Host "✓ GitHub CLI found" -ForegroundColor Green
    }
} catch {
    Write-Host "ℹ GitHub CLI not found (optional - you can create repo manually)" -ForegroundColor Yellow
}

Write-Host ""

# Get current directory
$projectRoot = $PSScriptRoot
if (-not $projectRoot) {
    $projectRoot = Get-Location
}

Set-Location $projectRoot

# Check if already a git repository
if (Test-Path ".git") {
    Write-Host "ℹ Git repository already initialized" -ForegroundColor Yellow
    $response = Read-Host "Do you want to reinitialize? (y/N)"
    if ($response -ne "y" -and $response -ne "Y") {
        Write-Host "Skipping git initialization" -ForegroundColor Yellow
    } else {
        Remove-Item -Recurse -Force .git -ErrorAction SilentlyContinue
        Write-Host "✓ Removed existing .git directory" -ForegroundColor Green
    }
}

# Initialize git repository if needed
if (-not (Test-Path ".git")) {
    Write-Host "Initializing git repository..." -ForegroundColor Cyan
    git init
    Write-Host "✓ Git repository initialized" -ForegroundColor Green
}

# Check if .gitignore exists
if (Test-Path ".gitignore") {
    Write-Host "✓ .gitignore found" -ForegroundColor Green
} else {
    Write-Host "⚠ .gitignore not found - you should create one!" -ForegroundColor Yellow
}

# Stage all files
Write-Host ""
Write-Host "Staging files..." -ForegroundColor Cyan
git add .

# Check if there are changes to commit
$status = git status --porcelain
if ($status) {
    Write-Host "✓ Files staged" -ForegroundColor Green
    
    # Create initial commit
    Write-Host ""
    Write-Host "Creating initial commit..." -ForegroundColor Cyan
    git commit -m "Initial commit: DTCE API project"
    Write-Host "✓ Initial commit created" -ForegroundColor Green
} else {
    Write-Host "ℹ No changes to commit" -ForegroundColor Yellow
}

# Get GitHub username if not provided
if (-not $GitHubUsername) {
    if ($hasGh) {
        $ghUser = gh api user --jq .login 2>&1
        if ($LASTEXITCODE -eq 0 -and $ghUser) {
            $GitHubUsername = $ghUser.Trim()
            Write-Host "✓ Detected GitHub username: $GitHubUsername" -ForegroundColor Green
        } else {
            Write-Host "⚠ Could not detect GitHub username" -ForegroundColor Yellow
        }
    }
    
    if (-not $GitHubUsername) {
        $GitHubUsername = Read-Host "Enter your GitHub username"
    }
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Create a new repository on GitHub:" -ForegroundColor White
Write-Host "   - Go to: https://github.com/new" -ForegroundColor Gray
Write-Host "   - Repository name: $RepoName" -ForegroundColor Gray
Write-Host "   - Description: $Description" -ForegroundColor Gray
Write-Host "   - Choose Public or Private" -ForegroundColor Gray
Write-Host "   - DO NOT initialize with README, .gitignore, or license" -ForegroundColor Yellow
Write-Host ""

# Try to create repo with GitHub CLI if available
if ($hasGh) {
    $createRepo = Read-Host "Do you want to create the repository using GitHub CLI? (Y/n)"
    if ($createRepo -ne "n" -and $createRepo -ne "N") {
        Write-Host ""
        Write-Host "Creating GitHub repository..." -ForegroundColor Cyan
        
        $visibility = Read-Host "Repository visibility (public/private) [default: private]"
        if (-not $visibility) { $visibility = "private" }
        
        $result = gh repo create $RepoName --description $Description --$visibility --source=. --remote=origin --push 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✓ Repository created and code pushed to GitHub!" -ForegroundColor Green
            Write-Host "  Repository URL: https://github.com/$GitHubUsername/$RepoName" -ForegroundColor Cyan
            exit 0
        } else {
            Write-Host "⚠ Failed to create repository with GitHub CLI" -ForegroundColor Yellow
            Write-Host "  Error: $result" -ForegroundColor Red
        }
    }
}

# Manual instructions
Write-Host "2. After creating the repository, run these commands:" -ForegroundColor White
Write-Host ""
Write-Host "   git remote add origin https://github.com/$GitHubUsername/$RepoName.git" -ForegroundColor Gray
Write-Host "   git branch -M main" -ForegroundColor Gray
Write-Host "   git push -u origin main" -ForegroundColor Gray
Write-Host ""
Write-Host "Or if you prefer SSH:" -ForegroundColor White
Write-Host "   git remote add origin git@github.com:$GitHubUsername/$RepoName.git" -ForegroundColor Gray
Write-Host "   git branch -M main" -ForegroundColor Gray
Write-Host "   git push -u origin main" -ForegroundColor Gray
Write-Host ""

