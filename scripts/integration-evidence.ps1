param(
    [string]$ComposeMode = "prod",
    [string]$FanpageSource,
    [ValidateSet("fanpage", "profile")]
    [string]$FacebookSourceType = "fanpage",
    [string]$DirectRssUrl,
    [int]$LookbackMinutes = 120,
    [switch]$SkipLogScan
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed++ }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

function Read-EnvMap {
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

function ConvertTo-SqlLiteral {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return $Value.Replace("'", "''")
}

function ConvertTo-FanpageSourceKey {
    param([string]$InputValue)

    if ([string]::IsNullOrWhiteSpace($InputValue)) {
        return ""
    }

    $value = $InputValue.Trim()
    $uri = $null

    if ([Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri)) {
        $query = $uri.Query.TrimStart('?')
        if (-not [string]::IsNullOrWhiteSpace($query)) {
            foreach ($pair in $query.Split('&', [System.StringSplitOptions]::RemoveEmptyEntries)) {
                $parts = $pair.Split('=', 2)
                if ($parts.Length -eq 2 -and $parts[0].Equals('id', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $value = [Uri]::UnescapeDataString($parts[1]).Trim()
                    break
                }
            }
        }

        if ($value -eq $InputValue.Trim()) {
            $segments = $uri.AbsolutePath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) |
                Where-Object {
                    $_ -ine 'posts' -and
                    $_ -ine 'videos' -and
                    $_ -ine 'photos' -and
                    $_ -ine 'profile.php'
                }

            if ($segments.Count -gt 0) {
                $value = $segments[0]
            }
        }
    }

    if ($value.StartsWith('@')) {
        $value = $value.Substring(1)
    }

    $value = [System.Text.RegularExpressions.Regex]::Replace($value, '[^a-zA-Z0-9._-]', '')
    if ($value.Length -lt 2 -or $value.Length -gt 128) {
        return ""
    }

    return $value
}

function Invoke-DbSql {
    param(
        [string]$User,
        [string]$Database,
        [string]$Sql,
        [switch]$Raw
    )

    if ($Raw) {
        return (& docker compose exec -T db psql -U $User -d $Database -c $Sql 2>&1 | Out-String)
    }

    return (& docker compose exec -T db psql -U $User -d $Database -t -A -c $Sql 2>&1 | Out-String)
}

Info "Integration evidence check for profile '$ComposeMode'"

try {
    $status = (& docker compose --profile $ComposeMode ps 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $status -match 'error during connect') {
        Fail "Unable to read docker compose status. Ensure Docker daemon is running."
    }
    else {
        $status | Out-Host
        Pass "docker compose status read successfully"
    }
}
catch {
    Fail "Unable to read docker compose status. Ensure Docker daemon is running."
}

$envMap = Read-EnvMap -Path ".env"
$postgresUser = if ($envMap.ContainsKey("POSTGRES_USER") -and -not [string]::IsNullOrWhiteSpace($envMap["POSTGRES_USER"])) { $envMap["POSTGRES_USER"] } else { "postgres" }
$postgresDb = if ($envMap.ContainsKey("POSTGRES_DB") -and -not [string]::IsNullOrWhiteSpace($envMap["POSTGRES_DB"])) { $envMap["POSTGRES_DB"] } else { "discordbot" }

if ($failed -eq 0 -and [string]::IsNullOrWhiteSpace($FanpageSource) -and [string]::IsNullOrWhiteSpace($DirectRssUrl)) {
    Info "No target feed provided. Pass -FanpageSource and/or -DirectRssUrl to run command-path evidence checks."
}

if ($failed -eq 0 -and -not [string]::IsNullOrWhiteSpace($FanpageSource)) {
    $normalizedFanpage = ConvertTo-FanpageSourceKey -InputValue $FanpageSource
    $sourceTypeValue = if ($FacebookSourceType -eq "profile") { 1 } else { 0 }
    $sourceTypeLabel = if ($FacebookSourceType -eq "profile") { "profile" } else { "fanpage" }

    if ([string]::IsNullOrWhiteSpace($normalizedFanpage)) {
        Fail "Facebook source normalization failed. Check -FanpageSource input."
    }
    else {
        $fanpageSqlValue = ConvertTo-SqlLiteral $normalizedFanpage
        $fanpageSql = "SELECT COUNT(*) FROM tracked_feeds t WHERE (to_jsonb(t)->>'Platform')::int = 1 AND (to_jsonb(t)->>'SourceType')::int = $sourceTypeValue AND (to_jsonb(t)->>'Provider')::int IN (0,2) AND lower(to_jsonb(t)->>'SourceKey') = lower('$fanpageSqlValue');"
        $fanpageCountText = Invoke-DbSql -User $postgresUser -Database $postgresDb -Sql $fanpageSql
        $fanpageText = ($fanpageCountText | Out-String)

        if ($LASTEXITCODE -ne 0 -or $fanpageText -match 'ERROR:') {
            Fail "Failed querying tracked Facebook source mapping."
        }
        else {
            $fanpageCount = 0
            [void][int]::TryParse($fanpageText.Trim(), [ref]$fanpageCount)
            if ($fanpageCount -gt 0) {
                Pass "Facebook $sourceTypeLabel mapping exists for '$normalizedFanpage' (count=$fanpageCount)."
            }
            else {
                Fail "No tracked Facebook $sourceTypeLabel mapping found for '$normalizedFanpage'. Run /add-fb first."
            }
        }
    }
}

if ($failed -eq 0 -and -not [string]::IsNullOrWhiteSpace($DirectRssUrl)) {
    $rssUrlValue = ConvertTo-SqlLiteral $DirectRssUrl.Trim()
    $directSql = "SELECT COUNT(*) FROM tracked_feeds t WHERE (to_jsonb(t)->>'Provider')::int = 1 AND to_jsonb(t)->>'RssUrl' = '$rssUrlValue';"
    $directCountText = Invoke-DbSql -User $postgresUser -Database $postgresDb -Sql $directSql
    $directText = ($directCountText | Out-String)

    if ($LASTEXITCODE -ne 0 -or $directText -match 'ERROR:') {
        Fail "Failed querying direct RSS mapping."
    }
    else {
        $directCount = 0
        [void][int]::TryParse($directText.Trim(), [ref]$directCount)
        if ($directCount -gt 0) {
            Pass "Direct RSS mapping exists for provided URL (count=$directCount)."
        }
        else {
            Fail "No tracked direct RSS mapping found for the provided URL. Run /add-link first."
        }
    }
}

if ($failed -eq 0) {
    $lookback = [Math]::Max(1, $LookbackMinutes)
    $publishSql = "SELECT COUNT(*) FROM processed_tweets p JOIN tracked_feeds t ON (to_jsonb(p)->>'TrackedFeedId')::bigint = (to_jsonb(t)->>'Id')::bigint WHERE (to_jsonb(p)->>'ProcessedAtUtc')::timestamptz >= NOW() - INTERVAL '$lookback minutes' AND ((to_jsonb(t)->>'Platform')::int = 1 OR (to_jsonb(t)->>'Provider')::int = 1);"
    $publishCountText = Invoke-DbSql -User $postgresUser -Database $postgresDb -Sql $publishSql
    $publishText = ($publishCountText | Out-String)

    if ($LASTEXITCODE -ne 0 -or $publishText -match 'ERROR:') {
        Fail "Failed querying publish evidence from processed_tweets."
    }
    else {
        $publishCount = 0
        [void][int]::TryParse($publishText.Trim(), [ref]$publishCount)
        if ($publishCount -gt 0) {
            Pass "Found publish evidence in processed_tweets for fanpage/direct paths within lookback window (count=$publishCount)."
        }
        else {
            Fail "No publish evidence found in processed_tweets within the lookback window."
        }
    }
}

if (-not $SkipLogScan) {
    try {
        $since = "{0}m" -f [Math]::Max(1, $LookbackMinutes)
        $logs = (& docker compose --profile $ComposeMode logs --since $since bot 2>&1 | Out-String)

        if ($LASTEXITCODE -ne 0 -or $logs -match 'error during connect') {
            Fail "Unable to read bot logs for integration evidence."
            $SkipLogScan = $true
        }
        else {
            $logs | Out-Host

            $logText = ($logs | Out-String)
            if ($logText -match "Published tweet") {
                Pass "Bot logs contain publish events within lookback window."
            }
            else {
                Info "No 'Published tweet' pattern found in bot logs for current lookback window."
            }
        }
    }
    catch {
        Fail "Unable to read bot logs for integration evidence."
    }
}

if ($failed -gt 0) {
    Write-Host "Integration evidence check finished with $failed failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Integration evidence check passed." -ForegroundColor Green
exit 0
