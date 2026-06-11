param(
    [string[]]$FanpageSources,
    [string]$FanpageSourcesFile,
    [string]$DirectRssMapFile,
    [string]$EnvFile = ".env",
    [string]$SourceType = "fanpage",
    [int]$TimeoutSec = 60,
    [switch]$ValidateDirectRss,
    [switch]$FailOnUnusable
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
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

function Get-InputSources {
    $sourceSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    if ($FanpageSources) {
        foreach ($source in $FanpageSources) {
            if (-not [string]::IsNullOrWhiteSpace($source)) {
                [void]$sourceSet.Add($source.Trim())
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($FanpageSourcesFile)) {
        if (-not (Test-Path $FanpageSourcesFile)) {
            throw "Fanpage sources file not found: $FanpageSourcesFile"
        }

        Get-Content $FanpageSourcesFile | ForEach-Object {
            $line = $_.Trim()
            if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { return }
            [void]$sourceSet.Add($line)
        }
    }

    return @($sourceSet)
}

function Get-DirectRssMap {
    param([string]$Path)

    $map = @{}
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $map
    }

    if (-not (Test-Path $Path)) {
        throw "Direct RSS map file not found: $Path"
    }

    $rows = Import-Csv -Path $Path
    foreach ($row in $rows) {
        $source = ConvertTo-FanpageSourceKey -InputValue ([string]$row.Source)
        $rssUrl = ([string]$row.RssUrl).Trim()
        $platform = ([string]$row.Platform).Trim()

        if ([string]::IsNullOrWhiteSpace($source) -or [string]::IsNullOrWhiteSpace($rssUrl)) {
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($platform) -and
            $platform -ne "FB" -and
            $platform -ne "Facebook") {
            continue
        }

        $map[$source.ToLowerInvariant()] = $rssUrl
    }

    return $map
}

function Test-DirectRssUrl {
    param(
        [string]$RssUrl,
        [int]$RequestTimeoutSec
    )

    $uri = $null
    if (-not [Uri]::TryCreate($RssUrl, [UriKind]::Absolute, [ref]$uri)) {
        return [pscustomobject]@{ IsValid = $false; Note = "Mapped direct RSS URL is not absolute" }
    }

    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        return [pscustomobject]@{ IsValid = $false; Note = "Mapped direct RSS URL must use http/https" }
    }

    try {
        $response = Invoke-WebRequest -Uri $RssUrl -TimeoutSec $RequestTimeoutSec -UseBasicParsing
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
            return [pscustomobject]@{ IsValid = $false; Note = "Mapped direct RSS returned HTTP $($response.StatusCode)" }
        }

        [xml]$xml = $response.Content
        $entryCount = $xml.SelectNodes("//*[local-name()='item' or local-name()='entry']").Count
        if ($entryCount -le 0) {
            return [pscustomobject]@{ IsValid = $false; Note = "Mapped direct RSS has no item/entry elements" }
        }

        return [pscustomobject]@{ IsValid = $true; Note = "Mapped direct RSS validated" }
    }
    catch {
        return [pscustomobject]@{ IsValid = $false; Note = "Mapped direct RSS validation failed: $($_.Exception.Message)" }
    }
}

function Test-ApifyReady {
    param([hashtable]$EnvMap, [string]$RequestedSourceType)

    $enabled = $EnvMap.ContainsKey("APIFY__ENABLED") -and $EnvMap["APIFY__ENABLED"].ToLowerInvariant() -eq "true"
    $hasToken = $EnvMap.ContainsKey("APIFY__APITOKEN") -and -not [string]::IsNullOrWhiteSpace($EnvMap["APIFY__APITOKEN"])
    $hasActor = $EnvMap.ContainsKey("APIFY__ACTORID") -and -not [string]::IsNullOrWhiteSpace($EnvMap["APIFY__ACTORID"])

    $fanpageEnabled = -not $EnvMap.ContainsKey("APIFY__ENABLEFORFANPAGE") -or $EnvMap["APIFY__ENABLEFORFANPAGE"].ToLowerInvariant() -eq "true"
    $profileEnabled = -not $EnvMap.ContainsKey("APIFY__ENABLEFORPROFILE") -or $EnvMap["APIFY__ENABLEFORPROFILE"].ToLowerInvariant() -eq "true"
    $sourceTypeEnabled = if ($RequestedSourceType -eq "profile") { $profileEnabled } else { $fanpageEnabled }

    return $enabled -and $hasToken -and $hasActor -and $sourceTypeEnabled
}

$normalizedSourceType = $SourceType.Trim().ToLowerInvariant()
if ($normalizedSourceType -eq "page") { $normalizedSourceType = "fanpage" }
if ($normalizedSourceType -eq "personal" -or $normalizedSourceType -eq "personalprofile") { $normalizedSourceType = "profile" }
if ($normalizedSourceType -ne "fanpage" -and $normalizedSourceType -ne "profile") {
    throw "Unsupported SourceType '$SourceType'. Use fanpage or profile."
}

$envMap = Parse-EnvFile -Path $EnvFile
$apifyReady = Test-ApifyReady -EnvMap $envMap -RequestedSourceType $normalizedSourceType
$directRssMap = Get-DirectRssMap -Path $DirectRssMapFile
$sources = Get-InputSources

if ($sources.Count -eq 0) {
    throw "No fanpage sources provided. Use -FanpageSources or -FanpageSourcesFile."
}

Info "Facebook onboarding precheck uses Apify primary for /add-fb and optional direct RSS mapping for /add-link."
if ($apifyReady) {
    Pass "APIFY__* configuration is ready for /add-fb."
}
else {
    Warn "APIFY__* configuration is missing, disabled, or disabled for source type '$normalizedSourceType'."
}

$results = @()
foreach ($source in $sources) {
    $sourceKey = ConvertTo-FanpageSourceKey -InputValue $source
    if ([string]::IsNullOrWhiteSpace($sourceKey)) {
        $results += [pscustomobject]@{
            Source = $source
            SourceKey = ""
            Recommendation = "invalid-source"
            DirectRssUrl = ""
            Note = "Source could not be normalized"
        }
        continue
    }

    $mappedUrl = ""
    $mapKey = $sourceKey.ToLowerInvariant()
    if ($directRssMap.ContainsKey($mapKey)) {
        $mappedUrl = $directRssMap[$mapKey]
    }

    if (-not [string]::IsNullOrWhiteSpace($mappedUrl)) {
        if ($ValidateDirectRss) {
            $validation = Test-DirectRssUrl -RssUrl $mappedUrl -RequestTimeoutSec $TimeoutSec
            $recommendation = if ($validation.IsValid) { "use-add-link" } else { "fix-direct-rss" }
            $note = $validation.Note
        }
        else {
            $recommendation = "use-add-link"
            $note = "Mapped direct RSS available; validation skipped"
        }
    }
    elseif ($apifyReady) {
        $recommendation = "use-add-fb"
        $note = "Apify primary is configured"
    }
    else {
        $recommendation = "configure-apify"
        $note = "Set APIFY__ENABLED=true, APIFY__APITOKEN, and APIFY__ACTORID, or provide a direct RSS map"
    }

    $results += [pscustomobject]@{
        Source = $source
        SourceKey = $sourceKey
        Recommendation = $recommendation
        DirectRssUrl = $mappedUrl
        Note = $note
    }
}

$results | Format-Table Source, SourceKey, Recommendation, DirectRssUrl, Note -AutoSize

Write-Host ""
Write-Host "Suggested commands:" -ForegroundColor Cyan
foreach ($item in $results) {
    switch ($item.Recommendation) {
        "use-add-fb" {
            Write-Host "/add-fb fanpage_or_id:$($item.SourceKey) channel:<target-channel> source_type:$normalizedSourceType"
        }
        "use-add-link" {
            Write-Host "/add-link rss_url:$($item.DirectRssUrl) platform:FB channel:<target-channel>"
        }
        "fix-direct-rss" {
            Warn "$($item.SourceKey): mapped direct RSS failed validation. Fix map entry before using /add-link."
        }
        "configure-apify" {
            Warn "$($item.SourceKey): configure APIFY__* before using /add-fb, or add a valid direct RSS map entry."
        }
        "invalid-source" {
            Warn "$($item.Source): invalid source input."
        }
    }
}

$unusable = @($results | Where-Object { $_.Recommendation -in @("invalid-source", "configure-apify", "fix-direct-rss") })
if ($unusable.Count -gt 0 -and $FailOnUnusable) {
    $failed += $unusable.Count
}

if ($failed -gt 0) {
    Write-Host "Precheck finished with $failed unusable source(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Precheck finished." -ForegroundColor Green
exit 0
