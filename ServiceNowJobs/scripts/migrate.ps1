# SNHub — Run all EF Core migrations
# Usage: .\scripts\migrate.ps1
# Prerequisites: Docker running (postgres container), dotnet 10 SDK installed

param(
    [string]$Environment = "Development",
    [string]$Service = "all"
)

$services = @{
    "auth"          = "src/Services/Auth/SNHub.Auth.Infrastructure"
    "users"         = "src/Services/Users/SNHub.Users.Infrastructure"
    "jobs"          = "src/Services/Jobs/SNHub.Jobs.Infrastructure"
    "applications"  = "src/Services/Applications/SNHub.Applications.Infrastructure"
    "profiles"      = "src/Services/Profiles/SNHub.Profiles.Infrastructure"
    "notifications" = "src/Services/Notifications/SNHub.Notifications.Infrastructure"
}

$dbContexts = @{
    "auth"          = "AuthDbContext"
    "users"         = "UsersDbContext"
    "jobs"          = "JobsDbContext"
    "applications"  = "ApplicationsDbContext"
    "profiles"      = "ProfilesDbContext"
    "notifications" = "NotificationsDbContext"
}

$apiProjects = @{
    "auth"          = "src/Services/Auth/SNHub.Auth.API"
    "users"         = "src/Services/Users/SNHub.Users.API"
    "jobs"          = "src/Services/Jobs/SNHub.Jobs.API"
    "applications"  = "src/Services/Applications/SNHub.Applications.API"
    "profiles"      = "src/Services/Profiles/SNHub.Profiles.API"
    "notifications" = "src/Services/Notifications/SNHub.Notifications.API"
}

$toRun = if ($Service -eq "all") { $services.Keys } else { @($Service) }

foreach ($svc in $toRun) {
    Write-Host "`n>>> Running migration: $svc" -ForegroundColor Cyan
    $infraPath = $services[$svc]
    $startupPath = $apiProjects[$svc]
    $ctx = $dbContexts[$svc]
    
    dotnet ef database update `
        --project $infraPath `
        --startup-project $startupPath `
        --context $ctx
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Migration failed for $svc" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ $svc migrated successfully" -ForegroundColor Green
}

Write-Host "`n✅ All migrations complete!" -ForegroundColor Green
