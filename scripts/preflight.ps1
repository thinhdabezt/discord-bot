param(
    [string]$EnvFile = ".env"
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed++ }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

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
$requiredVars = @("DISCORD_TOKEN", "POSTGRES_PASSWORD")
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
