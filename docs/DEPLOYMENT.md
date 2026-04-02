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
- New tweet is published once only

The smoke script now verifies:
- `tracked_feeds` includes `Platform`, `SourceType`, `Provider`, `SourceKey`
- Active feed summary grouped by `Platform`, `SourceType`, and `Provider`
- Slash command registration summary contains: `add-x/list-x/remove-x`, `add-fb/list-fb/remove-fb`, `add-link/list-links/remove-link`

## Integration Evidence (Fanpage + Direct RSS)
After you run `/add-fb` and `/add-link` with real test inputs, verify persisted mapping and publish evidence:

```powershell
.\scripts\integration-evidence.ps1 -ComposeMode prod -FanpageSource nasa -FacebookSourceType fanpage -DirectRssUrl "https://example.com/feed.xml" -LookbackMinutes 180
```

This script checks:
- `tracked_feeds` contains Facebook source mapping with requested source type (`Platform=Facebook`, `SourceType=Fanpage|Profile`, `Provider=RssHub` or `Provider=RssBridge`)
- `tracked_feeds` contains direct RSS mapping (`Provider=DirectRss`)
- `processed_tweets` contains publish evidence for the configured lookback window
- bot logs include publish activity pattern

For personal profile validation, use:

```powershell
.\scripts\integration-evidence.ps1 -ComposeMode prod -FanpageSource 1000xxxx -FacebookSourceType profile -LookbackMinutes 360
```

## Facebook Source Pre-check (Batch)
Before running `/add-fb` on many fanpages, pre-check source health against RSS-Bridge:

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSources 10150123547145211,100071458686024
```

You can also read IDs from a file (one source per line, lines starting with `#` are ignored):

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSourcesFile .\fanpages.txt
```

Recommendation meanings:
- `use-add-fb`: feed has usable entries and is suitable for `/add-fb`
- `prefer-add-link`: feed is reachable but only contains bridge error entries; prefer direct RSS via `/add-link`
- `retry-later`: feed has no entries at this moment; retry later before deciding
- `check-rss-bridge`: request failed; verify `rss-bridge` container and URL
- `invalid-source`: source cannot be normalized (bad ID/URL format)

## Facebook Profile Safety Alerts
When enabling profile feeds via RSSHub cookies, configure these env vars in `.env`:
- `FEEDPROVIDERS__ENABLEFACEBOOKPROFILEALERTS=true`
- `FEEDPROVIDERS__FACEBOOKPROFILEALERTCHANNELID=<discord_channel_id>`
- `FEEDPROVIDERS__FACEBOOKPROFILEFAILURETHRESHOLD=3`
- `FEEDPROVIDERS__FACEBOOKPROFILEALERTCOOLDOWNMINUTES=180`
- `FB_COOKIE=<facebook_cookie_value>`

Alert behavior:
- The worker tracks consecutive profile fetch failures (`HTTP 403`, error-only feed, or repeated empty feed after prior success).
- Once threshold is reached, bot sends a warning to admin alert channel.
- Cooldown prevents alert spam for the same profile source.

## Apify Fallback For Facebook Sources
To recover posts when RSSHub/RSS-Bridge fail repeatedly for Facebook fanpage/profile sources, configure:
- `APIFYFALLBACK__ENABLED=true`
- `APIFYFALLBACK__APITOKEN=<apify_api_token>`
- `APIFYFALLBACK__ACTORID=apify/facebook-posts-scraper`

Optional cost controls:
- `APIFYFALLBACK__FAILURETHRESHOLD=3`
- `APIFYFALLBACK__COOLDOWNMINUTES=180`
- `APIFYFALLBACK__RESULTSLIMIT=5`

Behavior:
- Fallback only triggers after consecutive primary provider failures reach threshold.
- Fallback is rate-limited per source by cooldown.
- Fallback works for both fanpage and profile when corresponding `APIFYFALLBACK__ENABLEFOR*` flags are true.

## RSS-Bridge Priority Fallback For Facebook
To insert a lower-cost fallback layer before Apify, configure:
- `RSSBRIDGEFALLBACK__ENABLED=true`
- `RSSBRIDGEFALLBACK__FAILURETHRESHOLD=2`
- `RSSBRIDGEFALLBACK__COOLDOWNMINUTES=60`

Optional scope flags:
- `RSSBRIDGEFALLBACK__ENABLEFORFANPAGE=true`
- `RSSBRIDGEFALLBACK__ENABLEFORPROFILE=false`

Runtime order:
- Primary provider fetch (tracked feed provider)
- RSS-Bridge fallback (if enabled and policy threshold/cooldown allows)
- Apify fallback (if RSS-Bridge did not recover usable posts and Apify policy allows)

Notes:
- With current resolver rules, RSS-Bridge fallback is intended for fanpage sources.
- Keep profile flag off unless RSS-Bridge profile route support is explicitly added.

## Logs and Diagnostics

```powershell
docker compose --profile prod logs -f bot
docker compose --profile prod logs -f db
docker compose --profile prod logs -f rss-bridge
docker compose --profile prod logs -f rsshub
docker compose --profile prod logs -f redis
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
