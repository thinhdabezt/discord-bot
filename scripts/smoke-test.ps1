param(
    [string]$ComposeMode = "prod",
    [int]$BotLogSinceMinutes = 60,
    [string[]]$ExpectedSlashCommands = @(
        "add-x",
        "list-x",
        "remove-x",
        "add-fb",
        "list-fb",
        "remove-fb",
        "add-link",
        "list-links",
        "remove-link"
    )
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

Info "Smoke test for docker compose profile '$ComposeMode'"

$envMap = Parse-EnvFile -Path ".env"
$postgresUser = if ($envMap.ContainsKey("POSTGRES_USER") -and -not [string]::IsNullOrWhiteSpace($envMap["POSTGRES_USER"])) { $envMap["POSTGRES_USER"] } else { "postgres" }
$postgresDb = if ($envMap.ContainsKey("POSTGRES_DB") -and -not [string]::IsNullOrWhiteSpace($envMap["POSTGRES_DB"])) { $envMap["POSTGRES_DB"] } else { "discordbot" }

try {
    $status = docker compose --profile $ComposeMode ps
    $status | Out-Host
    Pass "docker compose ps executed"
}
catch {
    Fail "Unable to read docker compose status"
}

try {
    docker compose exec -T db pg_isready -U $postgresUser -d $postgresDb | Out-Null
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
    $schemaSql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='tracked_feeds' AND column_name IN ('Platform', 'Provider', 'SourceKey');"
    $schemaCountText = docker compose exec -T db psql -U $postgresUser -d $postgresDb -t -A -c $schemaSql 2>&1
    if ($LASTEXITCODE -ne 0 -or (($schemaCountText | Out-String) -match "ERROR:")) {
        Fail "Unable to verify tracked_feeds schema"
    }

    $schemaCount = 0
    [void][int]::TryParse(($schemaCountText | Out-String).Trim(), [ref]$schemaCount)

    if ($schemaCount -eq 3) {
        Pass "tracked_feeds schema includes Platform, Provider, SourceKey"
    }
    else {
        Fail "tracked_feeds schema check failed. Expected 3 tracked columns, got $schemaCount"
    }
}
catch {
    Fail "Unable to verify tracked_feeds schema"
}

try {
    $summarySql = "SELECT to_jsonb(tracked_feeds)->>'Platform' AS platform, to_jsonb(tracked_feeds)->>'Provider' AS provider, COUNT(*) FROM tracked_feeds WHERE (to_jsonb(tracked_feeds)->>'IsActive')::boolean = true GROUP BY 1,2 ORDER BY 1,2;"
    $summary = docker compose exec -T db psql -U $postgresUser -d $postgresDb -c $summarySql 2>&1
    $summaryText = ($summary | Out-String)
    if ($LASTEXITCODE -ne 0 -or $summaryText -match "ERROR:") {
        Fail "Unable to query tracked_feeds summary"
    }
    else {
        $summary | Out-Host
        Pass "Printed active feed summary by platform/provider"
    }
}
catch {
    Fail "Unable to query tracked_feeds summary"
}

try {
    $since = "{0}m" -f [Math]::Max(1, $BotLogSinceMinutes)
    $logs = docker compose --profile $ComposeMode logs --since $since bot
    $logs | Out-Host

    $logText = ($logs | Out-String)
    if ($logText -match "Registered slash command set") {
        Pass "Bot logs contain slash command registration summary"
    }
    else {
        Fail "Could not find slash command registration summary in recent bot logs. Restart bot and rerun smoke test."
    }

    $logLower = $logText.ToLowerInvariant()
    foreach ($command in $ExpectedSlashCommands) {
        if ($logLower.Contains($command.ToLowerInvariant())) {
            Pass "Slash command discovered in logs: /$command"
        }
        else {
            Fail "Missing slash command in logs: /$command"
        }
    }
}
catch {
    Fail "Unable to validate slash command registration logs"
}

try {
    $logs = docker compose --profile $ComposeMode logs bot --tail 120
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
