# SNHub — EF Core Migration Helper
# Run from the repo root: C:\Mergen\ServiceNowJobs>
#
# Requires: dotnet-ef tool
# Install once: dotnet tool install --global dotnet-ef

param (
    [Parameter(Mandatory=$false)]
    [string]$Name = "",

    [Parameter(Mandatory=$false)]
    [ValidateSet("add", "update", "list", "revert", "script")]
    [string]$Action = "list"
)

$infraProject = "src\Services\Auth\SNHub.Auth.Infrastructure\SNHub.Auth.Infrastructure.csproj"
$startupProject = "src\Services\Auth\SNHub.Auth.API\SNHub.Auth.API.csproj"

function Check-EfTool {
    $ef = dotnet ef --version 2>$null
    if (-not $ef) {
        Write-Error "dotnet-ef not found. Run: dotnet tool install --global dotnet-ef"
        exit 1
    }
}

Check-EfTool

switch ($Action) {
    "list" {
        Write-Host "`n📋 Listing migrations..." -ForegroundColor Cyan
        dotnet ef migrations list `
            --project $infraProject `
            --startup-project $startupProject
    }
    "add" {
        if ([string]::IsNullOrWhiteSpace($Name)) {
            Write-Error "Provide a migration name: .\scripts\migrate.ps1 -Action add -Name YourMigrationName"
            exit 1
        }
        Write-Host "`n➕ Adding migration: $Name" -ForegroundColor Green
        dotnet ef migrations add $Name `
            --project $infraProject `
            --startup-project $startupProject `
            --output-dir Persistence/Migrations
    }
    "update" {
        Write-Host "`n⬆️  Applying migrations to database..." -ForegroundColor Yellow
        dotnet ef database update `
            --project $infraProject `
            --startup-project $startupProject
    }
    "revert" {
        Write-Host "`n⏪ Reverting last migration..." -ForegroundColor Red
        dotnet ef migrations remove `
            --project $infraProject `
            --startup-project $startupProject
    }
    "script" {
        Write-Host "`n📜 Generating SQL migration script..." -ForegroundColor Magenta
        dotnet ef migrations script `
            --project $infraProject `
            --startup-project $startupProject `
            --output migrations-script.sql `
            --idempotent
        Write-Host "Script saved to: migrations-script.sql"
    }
}
