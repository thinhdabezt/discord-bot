param(
    [string]$Profile = "prod"
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed++ }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

Info "Smoke test for docker compose profile '$Profile'"

try {
    $status = docker compose --profile $Profile ps
    $status | Out-Host
    Pass "docker compose ps executed"
}
catch {
    Fail "Unable to read docker compose status"
}

try {
    docker compose exec -T db pg_isready -U postgres | Out-Null
    Pass "PostgreSQL is ready"
}
catch {
    Fail "PostgreSQL readiness check failed"
}

try {
    $rss = Invoke-WebRequest -Uri "http://localhost:3000" -TimeoutSec 10 -UseBasicParsing
    if ($rss.StatusCode -eq 200) {
        Pass "RSS-Bridge endpoint is reachable"
    }
    else {
        Fail "RSS-Bridge endpoint returned status $($rss.StatusCode)"
    }
}
catch {
    Fail "RSS-Bridge endpoint is not reachable"
}

try {
    $statusText = ($status | Out-String)
    if ($statusText -match "rsshub") {
        $rssHub = Invoke-WebRequest -Uri "http://localhost:1200" -TimeoutSec 10 -UseBasicParsing
        if ($rssHub.StatusCode -eq 200) {
            Pass "RSSHub endpoint is reachable"
        }
        else {
            Fail "RSSHub endpoint returned status $($rssHub.StatusCode)"
        }
    }
    else {
        Info "RSSHub service not enabled in this profile"
    }
}
catch {
    Fail "RSSHub endpoint is not reachable"
}

try {
    $logs = docker compose --profile $Profile logs bot --tail 120
    $logs | Out-Host

    if ($logs -match "Discord gateway startup requested|Registered slash commands") {
        Pass "Bot logs indicate startup sequence"
    }
    else {
        Info "Bot log pattern not matched. Review logs above."
    }
}
catch {
    Fail "Unable to read bot logs"
}

if ($failed -gt 0) {
    Write-Host "Smoke test finished with $failed failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Smoke test passed." -ForegroundColor Green
exit 0
