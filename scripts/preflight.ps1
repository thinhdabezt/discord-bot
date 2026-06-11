param(
    [string]$EnvFile = ".env"
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed++ }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }

function Parse-EnvFile {
    param([string]$Path)

    $map = @{}
    if (-not (Test-Path $Path)) { return $map }

    Get-Content $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { return }
        if ($line -match '^([^=]+)=(.*)$') {
            $map[$matches[1].Trim()] = $matches[2].Trim()
        }
    }

    return $map
}

Info "Preflight checks for docker-prod deployment"

try {
    docker --version | Out-Null
    Pass "Docker is installed"
}
catch {
    Fail "Docker is not installed"
}

try {
    docker info | Out-Null
    Pass "Docker daemon is running"
}
catch {
    Fail "Docker daemon is not running"
}

try {
    docker compose version | Out-Null
    Pass "Docker Compose is available"
}
catch {
    Fail "Docker Compose is not available"
}

if (Test-Path $EnvFile) {
    Pass "$EnvFile exists"
}
else {
    Fail "$EnvFile not found"
}

$envMap = Parse-EnvFile -Path $EnvFile
$requiredVars = @("DISCORD_TOKEN", "POSTGRES_PASSWORD", "RSSBRIDGE__BASEURL", "CONNECTIONSTRINGS__DEFAULT")
$recommendedVars = @("DISCORD_GUILD_ID", "POSTGRES_DB", "POSTGRES_USER")

foreach ($name in $requiredVars) {
    if ($envMap.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($envMap[$name])) {
        Pass "Required var present: $name"
    }
    else {
        Fail "Missing required var: $name"
    }
}

foreach ($name in $recommendedVars) {
    if ($envMap.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($envMap[$name])) {
        Pass "Recommended var present: $name"
    }
    else {
        Info "Recommended var missing: $name (compose defaults may be used)"
    }
}

$enableApify = $false
if ($envMap.ContainsKey("APIFY__ENABLED")) {
    $enableApify = $envMap["APIFY__ENABLED"].ToLowerInvariant() -eq "true"
}

if ($enableApify) {
    if ($envMap.ContainsKey("APIFY__APITOKEN") -and -not [string]::IsNullOrWhiteSpace($envMap["APIFY__APITOKEN"])) {
        Pass "Apify primary enabled and APIFY__APITOKEN is set"
    }
    else {
        Fail "Apify primary enabled but APIFY__APITOKEN is missing"
    }

    if ($envMap.ContainsKey("APIFY__ACTORID") -and -not [string]::IsNullOrWhiteSpace($envMap["APIFY__ACTORID"])) {
        Pass "Apify actor id is set"
    }
    else {
        Fail "Apify primary enabled but APIFY__ACTORID is missing"
    }
}
else {
    Warn "APIFY__ENABLED is not true; /add-fb will reject new Facebook sources. /add-link remains available for direct RSS."
}

$portChecks = @(3000, 55432)
foreach ($p in $portChecks) {
    $inUse = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
    if ($null -eq $inUse) {
        Pass "Port $p is currently free"
    }
    else {
        Info "Port $p is already in use (may be expected if services already running)"
    }
}

try {
    docker compose --profile prod config | Out-Null
    Pass "docker compose configuration is valid for prod profile"
}
catch {
    Fail "docker compose config validation failed for prod profile"
}

if ($failed -gt 0) {
    Write-Host "Preflight finished with $failed failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Preflight passed. Deployment can proceed." -ForegroundColor Green
exit 0
