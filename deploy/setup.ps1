<#
.SYNOPSIS
    Primary-path deployment setup: applies EF Core migrations, then seeds the canonical demo
    dataset. Matches the locked spec's "primary method" ("dotnet ef database update" + seeders).

.DESCRIPTION
    Idempotent: safe to run against an already-migrated/seeded database. Uses the repo-local
    pinned .NET SDK (.dotnet\dotnet.exe) per CLAUDE.md - the system-wide dotnet on PATH is a
    different major version and fails on this repo.

.PARAMETER ConnectionString
    SQL Server connection string. Defaults to the LocalDB dev target used everywhere else in
    this repo.

.EXAMPLE
    .\deploy\setup.ps1
    .\deploy\setup.ps1 -ConnectionString "Server=.;Database=AI_Factory_CommandCenter;Trusted_Connection=True;TrustServerCertificate=True"
#>
param(
    [string]$ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=AI_Factory_CommandCenter;Trusted_Connection=True;TrustServerCertificate=True'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "Repo-local SDK not found at $dotnet. Run from a checkout with .dotnet\dotnet.exe restored (see CLAUDE.md)."
}

# Two variables, one for each step's different resolution path:
# - AppDbContextFactory (used by `dotnet ef`, the migration step) only ever reads AI_FACTORY_CONNECTION_STRING.
# - The runtime host (used by `dotnet run`, the seed step) resolves ConnectionStrings:AiFactory from config
#   FIRST and only falls back to AI_FACTORY_CONNECTION_STRING if that key is absent - but appsettings.json
#   always defines it, so the env var alone would be silently ignored. ConnectionStrings__AiFactory (double
#   underscore = ASP.NET Core's env-var-to-config-key separator) overrides it, since env vars are read after
#   appsettings.json in the default configuration source order.
$env:AI_FACTORY_CONNECTION_STRING = $ConnectionString
$env:ConnectionStrings__AiFactory = $ConnectionString

Write-Host "Applying EF Core migrations to '$ConnectionString'..." -ForegroundColor Cyan
& $dotnet ef database update --project "$repoRoot\src\AI.Factory.Infrastructure" --startup-project "$repoRoot\src\AI.Factory.Infrastructure"
if ($LASTEXITCODE -ne 0) { throw "Migration failed with exit code $LASTEXITCODE" }

# --seed-production-plans implies --seed-identity/--seed-master-data/--seed-customer-orders (dependency order).
# The web host's seed path also calls Database.MigrateAsync() itself, so this is a redundant but harmless
# double-check of the migration step above, and keeps this script's steps matching the locked doc's wording
# ("primary method: dotnet ef database update") one-to-one.
Write-Host "Seeding canonical demo dataset (identity, master data, customer orders, production plans)..." -ForegroundColor Cyan
& $dotnet run --project "$repoRoot\src\AI.Factory.Web" -- --seed-production-plans
if ($LASTEXITCODE -ne 0) { throw "Seeding failed with exit code $LASTEXITCODE" }

Write-Host "Setup complete: database migrated and canonical demo dataset seeded." -ForegroundColor Green
