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
.\scripts\smoke-test.ps1 -Profile prod
```

Manual checks:
- Slash commands are visible in target guild
- /add-x persists feed mapping
- New tweet is published once only

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
