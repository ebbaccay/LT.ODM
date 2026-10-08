<#
.SYNOPSIS
    Builds a deployable LT ODM release: the published API with the Angular build in its wwwroot (one IIS site).

.DESCRIPTION
    1. Runs the API tests (skip with -SkipTests).
    2. Publishes the API (Release) to <Output>\api.
    3. Builds the web app (production) and copies it into <Output>\api\wwwroot.
    4. Copies the db scripts to <Output>\db.
    Nothing on a server is touched. See docs/installation/03-iis-deployment.md for deploying the output.

.EXAMPLE
    .\tools\build-release.ps1 -Output D:\Releases\ltodm-2026-10-07
#>
param(
    [Parameter(Mandatory)] [string] $Output,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $Output 'api'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

if (Test-Path $Output) { throw "$Output already exists. Choose a new folder per release." }

if (-not $SkipTests) {
    Step 'API tests'
    dotnet test (Join-Path $root 'tests/LT.ODM.Api.Tests/LT.ODM.Api.Tests.csproj') -c Release --nologo
    Check 'dotnet test'
}

Step 'Publish API'
dotnet publish (Join-Path $root 'src/api/LT.ODM.Api') -c Release -o $api --nologo
Check 'dotnet publish'
# Developer settings never go to a server.
Remove-Item (Join-Path $api 'appsettings.Development.json') -ErrorAction SilentlyContinue

Step 'Build web app'
$web = Join-Path $root 'src/web/ltodm-web'
Push-Location $web
try {
    npm ci
    Check 'npm ci'
    npx ng build --configuration production
    Check 'ng build'
}
finally { Pop-Location }

Step 'Web app -> api\wwwroot'
$dist = Join-Path $web 'dist/ltodm-web/browser'
if (-not (Test-Path (Join-Path $dist 'index.html'))) { throw "No index.html in $dist." }
$wwwroot = Join-Path $api 'wwwroot'
New-Item -ItemType Directory -Force $wwwroot | Out-Null
Copy-Item (Join-Path $dist '*') $wwwroot -Recurse
# Source maps help debugging but are not needed on a server.
Get-ChildItem $wwwroot -Filter '*.map' -Recurse | Remove-Item

Step 'Database scripts'
Copy-Item (Join-Path $root 'db') (Join-Path $Output 'db') -Recurse

Write-Host "`nRelease ready in $Output" -ForegroundColor Green
Write-Host "Deploy: run the db scripts (docs/installation/02-database.md), then copy api\ to the site folder"
Write-Host "and merge your web.config changes (docs/installation/03-iis-deployment.md)."
