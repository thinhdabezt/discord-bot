# Deployment Guide

## Prerequisites
- Docker Desktop with WSL2 enabled
- A Discord bot token with `applications.commands` and bot permissions
- Optional: Supabase PostgreSQL if not using local Postgres container

## Environment
Copy `.env.example` values into your runtime environment.

Required variables:
- `DISCORD_TOKEN`
- `DISCORD_GUILD_ID` for development command registration
- `CONNECTIONSTRINGS__DEFAULT`
- `RSSBRIDGE__BASEURL`

## Local Infrastructure
Run helper services:

```powershell
docker compose up -d db rss-bridge
```

Validate services:

```powershell
docker compose ps
```

## Run Bot from Source

```powershell
dotnet run --project src/DiscordXBot/DiscordXBot.csproj
```

## Run Bot in Docker
The bot service uses `prod` profile:

```powershell
docker compose --profile prod up -d --build
```

## Smoke Checks
- Bot logs show gateway startup success
- Slash commands are registered in the target guild
- `/add-x` works and persists into database
- Worker publishes a new tweet only once

## Rollback
If a deploy fails:

```powershell
docker compose --profile prod down
git checkout <last_known_good_tag>
docker compose --profile prod up -d --build
```
