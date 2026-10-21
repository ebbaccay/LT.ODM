<#
.SYNOPSIS
    Starts LT ODM for development in one go: local AI (OpenVINO), the API and the Angular site, each in its own window.

.DESCRIPTION
    By default the API reads the dev server's database (launch profile https-devserver), while AI jobs run on this PC:
    the dev server database's AI connection points to http://localhost:8000/v1, which here means this PC's OpenVINO.
    Close a window (or Ctrl+C in it) to stop that part.
    -LocalDb uses the local database instead (launch profile https). -NoAi skips OpenVINO (it is also skipped when
    something already listens on port 8000). -ImageOnly (also -WithImage) loads the image model (FLUX.1-schnell) for Style render
    INSTEAD of the text models (both do not fit in 32 GB): text AI is off until you restart without it. -AllowServer lets the
    dev server (LTODMSVR) use this PC's OpenVINO too (start-local.ps1 -AllowServer). -AiServer is the ai-server folder (default: next to the LT.ODM folder).
    Pictures (uploads, AI renders) are saved on THIS PC (Files:Root) even when the database is the dev server's: they do not
    show on the dev server site. To make renders the server keeps, render on the dev server site with -AllowServer instead.

.EXAMPLE
    & "C:\IST\LT ODM\LT.ODM\tools\start-dev.ps1"
.EXAMPLE
    & "C:\IST\LT ODM\LT.ODM\tools\start-dev.ps1" -LocalDb -NoAi
#>
param(
    [switch] $LocalDb,
    [switch] $NoAi,
    [Alias('WithImage')] [switch] $ImageOnly,
    [switch] $AllowServer,
    [string] $AiServer = (Join-Path $PSScriptRoot '..\..\ai-server')
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$api = Join-Path $repo 'src\api\LT.ODM.Api'
$web = Join-Path $repo 'src\web\ltodm-web'
$launch = if ($LocalDb) { 'https' } else { 'https-devserver' }

function Open-Window([string] $title, [string] $folder, [string] $command) {
    $script = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location -LiteralPath '$folder'; $command"
    Start-Process powershell -ArgumentList '-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $script | Out-Null
    Write-Host "Started: $title" -ForegroundColor Cyan
}

if ($NoAi) {
    Write-Host 'AI: skipped (-NoAi).' -ForegroundColor Yellow
} elseif (Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host 'AI: already running on port 8000.' -ForegroundColor Green
} else {
    $startAi = Join-Path $AiServer 'start-local.ps1'
    if (-not (Test-Path $startAi)) { throw "Not found: $startAi (pass -AiServer <folder>, or -NoAi)." }
    Open-Window 'LT ODM - AI (OpenVINO)' (Resolve-Path $AiServer) ("& '$startAi'" + $(if ($ImageOnly) { ' -ImageOnly' } else { '' }) + $(if ($AllowServer) { ' -AllowServer' } else { '' }))
}

Open-Window "LT ODM - API ($launch)" $api "dotnet run --launch-profile $launch"
Open-Window 'LT ODM - Web' $web 'npm start'

Write-Host "`nThe site opens at http://localhost:4200 once Angular has built (the API and AI take a minute to load)." -ForegroundColor Green
