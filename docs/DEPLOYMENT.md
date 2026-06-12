# Deployment Guide

## Scope
This guide covers two deployment modes:
- docker-prod: full stack with Docker Compose profile prod
- source-run: bot from source with external database (for example Supabase)

## Deployment Artifacts
- [docs/ENV-MATRIX.md](docs/ENV-MATRIX.md): variable matrix with required status and defaults
- [.env.example](.env.example): base local template
- [.env.prod.example](.env.prod.example): production Docker template
- [.env.supabase.example](.env.supabase.example): source-run or external DB template
- [scripts/preflight.ps1](scripts/preflight.ps1): pre-deploy checks
- [scripts/smoke-test.ps1](scripts/smoke-test.ps1): post-deploy checks
- [docs/ops-checklist.md](docs/ops-checklist.md): operations checklist
- [docs/RELEASE-HANDOFF-2026-04-01.md](docs/RELEASE-HANDOFF-2026-04-01.md): phase summary and production handoff for this rollout

## Prerequisites
- Docker Desktop with WSL2 enabled
- Discord bot token with required bot permissions and applications.commands scope
- PowerShell 5.1+ or PowerShell 7+

## Variables You Must Add Manually

### docker-prod minimum
- DISCORD_TOKEN
- POSTGRES_PASSWORD

### source-run minimum
- DISCORD_TOKEN
- CONNECTIONSTRINGS__DEFAULT
- RSSBRIDGE__BASEURL

For the full variable list and defaults, use [docs/ENV-MATRIX.md](docs/ENV-MATRIX.md).

## Setup Environment

### Docker production-like setup
1. Copy [.env.prod.example](.env.prod.example) to .env.
2. Fill real values for required variables.
3. Do not commit .env.

### Source run with external DB
1. Use [.env.supabase.example](.env.supabase.example) as template.
2. Set CONNECTIONSTRINGS__DEFAULT to your managed PostgreSQL connection string.

## Preflight
Run checks before deployment:

```powershell
.\scripts\preflight.ps1 -EnvFile .env
```

This validates Docker availability, required variables, and compose config for prod profile.

## Apply Database Migrations
Apply migrations before starting or upgrading the bot runtime:

```powershell
.\scripts\apply-migrations.ps1 -Mode docker
```

If you need source-run mode:

```powershell
.\scripts\apply-migrations.ps1 -Mode source
```

Notes:
- The script creates a backup dump under `backups/` unless `-SkipBackup` is provided.
- Verify `tracked_feeds` columns include `Platform`, `SourceType`, `Provider`, and `SourceKey` after apply.

## Deploy with Docker

```powershell
docker compose --profile prod up -d --build
docker compose --profile prod ps
```

## Deploy from Source
If you run from source, start dependencies first:

```powershell
docker compose up -d db rss-bridge
dotnet run --project src/DiscordXBot/DiscordXBot.csproj
```

## Smoke Test
After deployment:

```powershell
.\scripts\smoke-test.ps1 -ComposeMode prod
```

If the bot was restarted earlier than the default log window, increase it:

```powershell
.\scripts\smoke-test.ps1 -ComposeMode prod -BotLogSinceMinutes 240
```

Manual checks:
- Slash commands are visible in target guild
- /add-x persists feed mapping
- /add-fb (fanpage) and /add-link persist feed mapping
- /add-ig persists Instagram username mapping
- New tweet is published once only

The smoke script now verifies:
- `tracked_feeds` includes `Platform`, `SourceType`, `Provider`, `SourceKey`
- Active feed summary grouped by `Platform`, `SourceType`, and `Provider`
- Slash command registration summary contains: `add-x/list-x/remove-x`, `add-fb/list-fb/remove-fb`, `add-ig/list-ig/remove-ig`, `add-link/list-links/remove-link`

## Integration Evidence (Facebook + Instagram + Direct RSS)
After you run `/add-fb`, `/add-ig`, and `/add-link` with real test inputs, verify persisted mapping and publish evidence:

```powershell
.\scripts\integration-evidence.ps1 -ComposeMode prod -FanpageSource nasa -FacebookSourceType fanpage -InstagramUsername nasa -DirectRssUrl "https://example.com/feed.xml" -LookbackMinutes 180
```

This script checks:
- `tracked_feeds` contains Facebook source mapping with requested source type (`Platform=Facebook`, `SourceType=Fanpage|Profile`, `Provider=Apify`)
- `tracked_feeds` contains Instagram username mapping (`Platform=Instagram`, `Provider=RssBridge`)
- `tracked_feeds` contains direct RSS mapping (`Provider=DirectRss`)
- `processed_tweets` contains publish evidence for the configured lookback window
- bot logs include publish activity pattern

For personal profile validation, use:

```powershell
.\scripts\integration-evidence.ps1 -ComposeMode prod -FanpageSource 1000xxxx -FacebookSourceType profile -LookbackMinutes 360
```

## Facebook Source Pre-check (Batch)
Before running `/add-fb` on many Facebook sources, pre-check onboarding decisions against Apify config and optional direct RSS mapping:

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSources nasa,facebook,Meta -EnvFile .env
```

You can also read IDs from a file (one source per line, lines starting with `#` are ignored):

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSourcesFile .\fanpages.txt -EnvFile .env
```

Recommendation meanings:
- `use-add-fb`: Apify config is present and the source can be onboarded with `/add-fb`
- `use-add-link`: a mapped direct RSS URL exists and can be used with `/add-link`
- `fix-direct-rss`: a mapped direct RSS URL exists but failed HTTP/XML/item validation
- `configure-apify`: `APIFY__*` config is missing, disabled, or disabled for the requested source type
- `invalid-source`: source cannot be normalized (bad ID/URL format)

Facebook onboarding decision tree:
- If precheck returns `use-add-fb`, register the source with `/add-fb fanpageOrId:<source> channel:<target-channel> sourceType:fanpage`.
- If precheck returns `use-add-link`, run the concrete `/add-link` command produced from the direct RSS map.
- If precheck returns `configure-apify`, set `APIFY__ENABLED=true`, `APIFY__APITOKEN`, and `APIFY__ACTORID`, or provide a direct RSS map entry.
- If precheck returns `fix-direct-rss`, update the mapped URL before using `/add-link`.

Direct RSS validation checklist:
- Open the direct RSS URL in a browser or run `Invoke-WebRequest <direct-rss-url>`.
- Confirm HTTP 200.
- Confirm the XML contains real `<item>` or `<entry>` post entries, not an error page.
- Register only after validation: `/add-link rssUrl:<direct-rss-url> platform:FB channel:<target-channel>` or `/add-link rssUrl:<direct-rss-url> platform:IG channel:<target-channel>`.
- Acceptable direct RSS origins include official website RSS, FetchRSS, RSS.app, or another generated RSS provider. Do not commit private generated feed URLs or provider credentials.
- Store private mappings in ignored file `config/direct-rss-sources.local.csv` with columns `Source,RssUrl,Platform,Notes`.
- Run precheck with mapping validation:

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSources nasa,facebook,Meta -DirectRssMapFile .\config\direct-rss-sources.local.csv -ValidateDirectRss
```

## Apify Primary For Facebook Sources
Configure these variables before using `/add-fb`:
- `APIFY__ENABLED=true`
- `APIFY__APITOKEN=<apify_api_token>`
- `APIFY__ACTORID=apify/facebook-posts-scraper`

Optional controls:
- `APIFY__RESULTSLIMIT=5`
- `APIFY__REQUESTTIMEOUTSECONDS=45`
- `APIFY__POLLINTERVALSECONDS=5`
- `APIFY__MAXPOLLATTEMPTS=24`
- `APIFY__ENABLEFORFANPAGE=true`
- `APIFY__ENABLEFORPROFILE=true`

Runtime order:
- Facebook `/add-fb` rows fetch through Apify directly.
- Facebook `/add-link platform:FB` rows fetch through the direct RSS validator/feed client path.
- Instagram `/add-ig` rows fetch through RSS-Bridge `InstagramBridge`.
- Instagram `/add-link platform:IG` rows fetch through the direct RSS validator/feed client path.
- RSS-Bridge remains for X/Twitter and Instagram.

## Instagram RSS-Bridge Sources
Instagram v1 supports public username feeds only:

```powershell
/add-ig username:nasa channel:<target-channel>
/list-ig
/remove-ig username:nasa
```

RSS-Bridge URL shape:
`?action=display&bridge=InstagramBridge&context=Username&u=<username>&media_type=all&direct_links=on&format=Atom`

Notes:
- No Instagram auth/session/cookie support is configured in bot env.
- If InstagramBridge returns an upstream access error or empty feed, use `/add-link rssUrl:<direct-rss-url> platform:IG channel:<target-channel>` with a tested direct RSS URL.

## Logs and Diagnostics

```powershell
docker compose --profile prod logs -f bot
docker compose --profile prod logs -f db
docker compose --profile prod logs -f rss-bridge
```

## Rollback
If deploy is unhealthy:

```powershell
docker compose --profile prod down
git checkout <last_known_good_tag_or_commit>
docker compose --profile prod up -d --build
```

## Security Notes
- Never commit real secrets in tracked files.
- Rotate credentials immediately if leaked.
- Prefer secret managers for long-term production environments.

