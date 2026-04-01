param(
    [string]$ComposeMode = "prod",
    [string]$RssBridgeBaseUrl = "http://localhost:3000",
    [string[]]$FanpageSources,
    [string]$FanpageSourcesFile,
    [int]$TimeoutSec = 60,
    [switch]$SkipDockerCheck,
    [switch]$FailOnUnusable
)

$ErrorActionPreference = "Stop"
$failed = 0

function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:failed++ }
function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }

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

function Get-EntryCountFallback {
    param([string]$Content)

    return ([regex]::Matches($Content, '<entry(\s|>)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
}

function Test-FanpageSource {
    param(
        [string]$InputSource,
        [string]$BaseUrl,
        [int]$RequestTimeoutSec
    )

    $normalizedSource = ConvertTo-FanpageSourceKey -InputValue $InputSource
    if ([string]::IsNullOrWhiteSpace($normalizedSource)) {
        return [pscustomobject]@{
            InputSource = $InputSource
            SourceKey = ""
            HttpStatus = "n/a"
            TotalEntries = 0
            ErrorEntries = 0
            UsableEntries = 0
            Recommendation = "invalid-source"
            Note = "Cannot normalize fanpage source"
        }
    }

    $encodedSource = [Uri]::EscapeDataString($normalizedSource)
    $url = "$BaseUrl/?action=display&bridge=FacebookBridge&context=User&u=$encodedSource&media_type=all&format=Atom"

    try {
        $response = Invoke-WebRequest -Uri $url -TimeoutSec $RequestTimeoutSec -UseBasicParsing
        $content = $response.Content
        $statusCode = [string]$response.StatusCode
        $hasPayloadErrorMarker = $content -match 'Bridge returned error|Unable to find anything useful|Exception'

        $totalEntries = 0
        $errorEntries = 0

        try {
            [xml]$xml = $content
            $entries = @($xml.feed.entry)
            $totalEntries = $entries.Count

            foreach ($entry in $entries) {
                $entryText = ""
                if ($null -ne $entry.title) { $entryText += " " + [string]$entry.title.InnerText }
                if ($null -ne $entry.summary) { $entryText += " " + [string]$entry.summary.InnerText }
                if ($null -ne $entry.content) { $entryText += " " + [string]$entry.content.InnerText }

                if ($entryText -match 'Bridge returned error|Unable to find anything useful|Exception') {
                    $errorEntries++
                }
            }

            if ($hasPayloadErrorMarker -and $errorEntries -eq 0) {
                $errorEntries = [Math]::Max(1, $totalEntries)
            }
        }
        catch {
            $totalEntries = Get-EntryCountFallback -Content $content
            if ($hasPayloadErrorMarker) {
                $errorEntries = [Math]::Max(1, $totalEntries)
            }
        }

        $usableEntries = [Math]::Max(0, $totalEntries - $errorEntries)

        $recommendation = "needs-review"
        $note = "Feed reachable"

        if ($usableEntries -gt 0) {
            $recommendation = "use-add-fb"
            $note = "RSS-Bridge has usable entries"
        }
        elseif ($totalEntries -gt 0 -and $errorEntries -ge $totalEntries) {
            $recommendation = "prefer-add-link"
            $note = "Only bridge error entries found"
        }
        elseif ($hasPayloadErrorMarker) {
            $recommendation = "prefer-add-link"
            $note = "Bridge returned parser error markers"
        }
        elseif ($totalEntries -eq 0) {
            $recommendation = "retry-later"
            $note = "No entries currently available"
        }

        return [pscustomobject]@{
            InputSource = $InputSource
            SourceKey = $normalizedSource
            HttpStatus = $statusCode
            TotalEntries = $totalEntries
            ErrorEntries = $errorEntries
            UsableEntries = $usableEntries
            Recommendation = $recommendation
            Note = $note
        }
    }
    catch {
        $statusText = "request-failed"
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusText = [string]$_.Exception.Response.StatusCode
        }

        return [pscustomobject]@{
            InputSource = $InputSource
            SourceKey = $normalizedSource
            HttpStatus = $statusText
            TotalEntries = 0
            ErrorEntries = 0
            UsableEntries = 0
            Recommendation = "check-rss-bridge"
            Note = $_.Exception.Message
        }
    }
}

Info "Facebook fanpage source pre-check started"

if (-not $SkipDockerCheck) {
    try {
        $status = (& docker compose --profile $ComposeMode ps rss-bridge 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or $status -match 'error during connect') {
            Warn "Could not verify docker compose status for rss-bridge. Continuing with direct HTTP checks."
        }
        else {
            Pass "docker compose status check completed for rss-bridge"
        }
    }
    catch {
        Warn "Could not verify docker compose status for rss-bridge. Continuing with direct HTTP checks."
    }
}

$sources = Get-InputSources
if (-not $sources -or $sources.Count -eq 0) {
    Fail "No fanpage sources provided. Use -FanpageSources and/or -FanpageSourcesFile."
    Write-Host "Example: .\scripts\precheck-fanpages.ps1 -FanpageSources 10150123547145211,100071458686024" -ForegroundColor Yellow
    exit 1
}

Info "Running checks for $($sources.Count) source(s) against $RssBridgeBaseUrl"

$results = foreach ($source in $sources) {
    Test-FanpageSource -InputSource $source -BaseUrl $RssBridgeBaseUrl -RequestTimeoutSec $TimeoutSec
}

$results | Sort-Object Recommendation, SourceKey | Format-Table SourceKey, HttpStatus, TotalEntries, ErrorEntries, UsableEntries, Recommendation, Note -AutoSize

$usableCount = ($results | Where-Object { $_.Recommendation -eq 'use-add-fb' }).Count
$preferLinkCount = ($results | Where-Object { $_.Recommendation -eq 'prefer-add-link' }).Count
$retryCount = ($results | Where-Object { $_.Recommendation -eq 'retry-later' }).Count
$invalidCount = ($results | Where-Object { $_.Recommendation -eq 'invalid-source' }).Count
$checkBridgeCount = ($results | Where-Object { $_.Recommendation -eq 'check-rss-bridge' }).Count

Write-Host "Summary: use-add-fb=$usableCount, prefer-add-link=$preferLinkCount, retry-later=$retryCount, invalid-source=$invalidCount, check-rss-bridge=$checkBridgeCount" -ForegroundColor Cyan

$useAddFbGroup = $results | Where-Object { $_.Recommendation -eq 'use-add-fb' } | Sort-Object SourceKey
$preferAddLinkGroup = $results | Where-Object { $_.Recommendation -eq 'prefer-add-link' } | Sort-Object SourceKey

if ($useAddFbGroup.Count -gt 0 -or $preferAddLinkGroup.Count -gt 0) {
    Write-Host "" 
    Write-Host "Suggested command plan by source:" -ForegroundColor Cyan
}

if ($useAddFbGroup.Count -gt 0) {
    Write-Host "[Group: use-add-fb]" -ForegroundColor Green
    foreach ($item in $useAddFbGroup) {
        Write-Host "/add-fb fanpage_or_id:$($item.SourceKey) channel:<target-channel> provider:rssbridge source_type:fanpage"
    }
}

if ($preferAddLinkGroup.Count -gt 0) {
    Write-Host "[Group: prefer-add-link]" -ForegroundColor Yellow
    foreach ($item in $preferAddLinkGroup) {
        Write-Host "/add-link rss_url:<direct-rss-url-for-$($item.SourceKey)> platform:FB channel:<target-channel>"
    }
}

if ($FailOnUnusable) {
    $unusableCount = ($results | Where-Object { $_.Recommendation -ne 'use-add-fb' }).Count
    if ($unusableCount -gt 0) {
        Fail "FailOnUnusable enabled and found $unusableCount non-usable source(s)."
    }
}

if ($failed -gt 0) {
    Write-Host "Fanpage pre-check finished with $failed failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Fanpage pre-check completed." -ForegroundColor Green
exit 0
