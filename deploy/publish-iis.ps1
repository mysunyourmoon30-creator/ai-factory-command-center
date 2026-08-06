<#
.SYNOPSIS
    Publishes AI.Factory.Web and hosts it under IIS Local.

.DESCRIPTION
    NOT EXECUTED OR VERIFIED in the environment this script was written in - that machine has no
    IIS, no ASP.NET Core Hosting Bundle, and no Administrator rights (recorded as an environment
    finding in docs/00_Project_Status.md's Day 13 acceptance evidence, the same treatment Day 10
    gave the Ollama-absent finding). Written to Microsoft's documented IIS-hosting steps for
    ASP.NET Core: https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/

.PARAMETER Prerequisites
    Before running this script (once per machine), as Administrator:
    1. Install the IIS Windows feature: Web-Server, Web-Asp-Net45, Web-Net-Ext45, Web-ISAPI-Ext,
       Web-ISAPI-Filter (via Install-WindowsFeature or "Turn Windows features on or off").
    2. Install the ASP.NET Core Hosting Bundle (not just the runtime) from
       https://dotnet.microsoft.com/download/dotnet - it installs the ASP.NET Core Module (ANCM)
       v2 into IIS and requires an `iisreset` (or a reboot) afterward.
    3. Run this script itself as Administrator (site/app-pool creation requires elevation).

.PARAMETER SiteName
    IIS site name to create or reuse.

.PARAMETER AppPoolName
    IIS application pool name to create or reuse. Must run with "No Managed Code" - ASP.NET
    Core Module handles the .NET runtime itself, out-of-process; a Managed Code pool conflicts
    with it.

.PARAMETER PhysicalPath
    Folder to publish into and host from.

.PARAMETER Port
    HTTP binding port for the new site.

.PARAMETER ConnectionString
    SQL Server connection string for the hosted app. Written into web.config's
    <environmentVariables> so it reaches the app as AI_FACTORY_CONNECTION_STRING without needing
    ConnectionStrings:AiFactory in appsettings.json (see setup.ps1's comment on why the two
    config sources don't override each other the same way for every entry point).

.EXAMPLE
    .\deploy\publish-iis.ps1
#>
param(
    [string]$SiteName = 'AIFactoryCommandCenter',
    [string]$AppPoolName = 'AIFactoryCommandCenterPool',
    [string]$PhysicalPath = 'C:\inetpub\AIFactoryCommandCenter',
    [int]$Port = 8080,
    [string]$ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=AI_Factory_CommandCenter;Trusted_Connection=True;TrustServerCertificate=True'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must run as Administrator (IIS site/app-pool creation requires elevation)."
}

Import-Module WebAdministration -ErrorAction Stop

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    throw "Repo-local SDK not found at $dotnet. Run from a checkout with .dotnet\dotnet.exe restored (see CLAUDE.md)."
}

Write-Host "Publishing to '$PhysicalPath'..." -ForegroundColor Cyan
& $dotnet publish "$repoRoot\src\AI.Factory.Web" -c Release -o $PhysicalPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Inject the environment variables the ASP.NET Core Module passes to the hosted process. The
# publish step generates web.config from the csproj; this appends an <environmentVariables>
# block rather than overwriting the file, since ANCM settings (processPath, hostingModel) must
# stay intact.
$webConfigPath = Join-Path $PhysicalPath 'web.config'
[xml]$webConfig = Get-Content $webConfigPath
$aspNetCoreNode = $webConfig.configuration.location.'system.webServer'.aspNetCore
$envVarsNode = $webConfig.CreateElement('environmentVariables')
foreach ($pair in @{ 'ASPNETCORE_ENVIRONMENT' = 'Production'; 'AI_FACTORY_CONNECTION_STRING' = $ConnectionString }.GetEnumerator()) {
    $varNode = $webConfig.CreateElement('environmentVariable')
    $varNode.SetAttribute('name', $pair.Key)
    $varNode.SetAttribute('value', $pair.Value)
    [void]$envVarsNode.AppendChild($varNode)
}
[void]$aspNetCoreNode.AppendChild($envVarsNode)
$webConfig.Save($webConfigPath)

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    Write-Host "Creating app pool '$AppPoolName' (No Managed Code - ANCM hosts the runtime out-of-process)..." -ForegroundColor Cyan
    New-WebAppPool -Name $AppPoolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
}

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    Write-Host "Creating site '$SiteName' on port $Port..." -ForegroundColor Cyan
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $Port | Out-Null
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

# The app pool identity (IIS AppPool\<name>) needs read+execute on the published folder, and the
# Data Protection key ring path if one is configured outside the profile directory.
$appPoolIdentity = "IIS AppPool\$AppPoolName"
Write-Host "Granting '$appPoolIdentity' read+execute on '$PhysicalPath'..." -ForegroundColor Cyan
icacls $PhysicalPath /grant "${appPoolIdentity}:(OI)(CI)RX" /T | Out-Null

Write-Host "Restarting site '$SiteName'..." -ForegroundColor Cyan
Restart-WebItem "IIS:\Sites\$SiteName"

Write-Host "Done. Verify at http://localhost:$Port and check the Application event log if it doesn't respond (ANCM logs startup failures there)." -ForegroundColor Green
