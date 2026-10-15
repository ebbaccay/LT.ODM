<# : batch part (cmd runs these lines; PowerShell sees them as a comment)
@echo off
setlocal
set "DEPLOY_DB_SELF=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ([ScriptBlock]::Create((Get-Content -LiteralPath $env:DEPLOY_DB_SELF -Raw)))"
set "CODE=%ERRORLEVEL%"
endlocal & exit /b %CODE%
: end of batch part #>

# ---------------------------------------------------------------------------------------------------------------------
# LT ODM database deployment: runs every db script in order (docs/installation/02-database.md), stopping at the first
# error. All scripts are idempotent: safe to run again after each release. Double-click it, or run it from a prompt.
#
# Asks for: SQL Server (default localhost), database (default LTODM), SQL login (blank = Windows sign-in), its password
# (hidden, kept only for this run), and whether to trust the server's certificate (self-signed: answer Y).
# Without prompts (e.g. from another script), set: LTODM_DB_SERVER, LTODM_DB_NAME, LTODM_DB_USER, SQLCMDPASSWORD,
# LTODM_DB_TRUST (Y/N) and LTODM_DB_YES=1.
#
# The login needs rights to create and change tables and procedures (e.g. db_owner); the website's own login is not
# enough. Needs sqlcmd (SQL Server command-line tools). The TMS procedures (db\tms and the TMS database scripts) are
# deployed separately.
# ---------------------------------------------------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'
$folder = Split-Path -Parent $env:DEPLOY_DB_SELF
Set-Location $folder   # deploy-auth.sql includes other files by paths relative to this folder
$unattended = $env:LTODM_DB_YES -eq '1'

function Ask([string] $question, [string] $default, [string] $preset) {
    if ($preset) { return $preset }
    if ($unattended) { return $default }
    $answer = Read-Host "$question [$default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { $default } else { $answer.Trim() }
}

function Finish([int] $code) {
    Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
    if (-not $unattended) { Read-Host "`nPress Enter to close" | Out-Null }
    exit $code
}

# The scripts, in order. Keep in line with docs/installation/02-database.md.
$scripts = @(
    'deploy-auth.sql',                                 # sign-in, roles, menu (auth + nav schemas)
    'procedures\usp_Health_Ping.sql',
    'tables\style.tables.sql',                         # Style Library tables (ref, partner, mat, style, staging)
    'procedures\style.import.procedures.sql',          # Settings > Import
    'procedures\style.procedures.sql',                 # Styles screens and home dashboard
    'procedures\ref.procedures.sql',                   # Settings > Reference lists
    'procedures\mat.procedures.sql',                   # Materials
    'procedures\mat.spec.procedures.sql',              # Materials > Description reader
    'procedures\style.ai.procedures.sql',              # AI Studio: BOM check, renders, concept matching, image search
    'procedures\style.tags.procedures.sql',            # picture tags (look filters)
    'procedures\style.assistant.procedures.sql',       # Library assistant
    'procedures\style.image-import.procedures.sql',    # Settings > Import pictures
    'tables\ai.tables.sql',                            # AI connections
    'procedures\ai.procedures.sql',
    'procedures\ai.assistant.procedures.sql',          # Library assistant saved questions
    'seed\nav.menu.sql'                                # menu again: pages added by the scripts above
)

Write-Host "LT ODM database deployment ($folder)" -ForegroundColor Cyan
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host 'sqlcmd was not found. Install the SQL Server command-line tools (sqlcmd) and run this again.' -ForegroundColor Red
    Finish 1
}
$missing = @($scripts | Where-Object { -not (Test-Path $_) })
if ($missing.Count) {
    Write-Host "Missing script files: $($missing -join ', '). Run this from the release's db folder." -ForegroundColor Red
    Finish 1
}

$server = Ask 'SQL Server' 'localhost' $env:LTODM_DB_SERVER
$database = Ask 'Database' 'LTODM' $env:LTODM_DB_NAME
$user = if ($env:LTODM_DB_USER) { $env:LTODM_DB_USER } elseif ($unattended) { '' } else { (Read-Host 'SQL login (blank = Windows sign-in)').Trim() }
if ($user -and -not $env:SQLCMDPASSWORD) {
    $secure = Read-Host "Password for $user" -AsSecureString
    $env:SQLCMDPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$trust = (Ask 'Trust the server certificate (self-signed)? Y/N' 'Y' $env:LTODM_DB_TRUST).ToUpperInvariant().StartsWith('Y')

# sqlcmd arguments as one typed list (Windows PowerShell 5.1 passes splatted one-item lists badly to native programs).
[string[]] $connect = @('-S', $server, '-d', $database)
if ($user) { $connect += @('-U', $user) } else { $connect += '-E' }
$connect += @('-b', '-I')
if ($trust) { $connect += '-C' }
Write-Host ("`nServer {0}, database {1}, {2}; {3} scripts." -f $server, $database, $(if ($user) { "SQL login $user" } else { 'Windows sign-in' }), $scripts.Count)
if (-not $unattended -and (Read-Host 'Continue? Y/N [Y]') -match '^[Nn]') { Finish 1 }

$clock = [Diagnostics.Stopwatch]::StartNew()
foreach ($f in $scripts) {
    Write-Host "`n== $f" -ForegroundColor Cyan
    & sqlcmd ($connect + @('-i', $f))
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nSTOPPED at $f (sqlcmd exit code $LASTEXITCODE). Nothing after it was run; fix the error above and run this again." -ForegroundColor Red
        Finish 1
    }
}
Write-Host ("`nAll {0} scripts ran in {1:N0} s." -f $scripts.Count, $clock.Elapsed.TotalSeconds) -ForegroundColor Green
Finish 0
