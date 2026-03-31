# Environment Variable Matrix

This matrix documents which variables are required for each deployment mode.

## Modes
- docker-prod: Run full stack with Docker Compose profile prod.
- source-run: Run bot from source with dotnet run.

## Variables

| Variable | Required docker-prod | Required source-run | Default | Example | Notes |
|---|---|---|---|---|---|
| DISCORD_TOKEN | Yes | Yes | none | YOUR_DISCORD_BOT_TOKEN | Bot token from Discord Developer Portal. Never commit real value. |
| DISCORD_GUILD_ID | Recommended | Recommended | empty | 123456789012345678 | Guild-scoped slash command registration in development. |
| POSTGRES_DB | Recommended | No | discordbot | discordbot | Used by local Docker PostgreSQL service. |
| POSTGRES_USER | Recommended | No | postgres | postgres | Used by local Docker PostgreSQL service. |
| POSTGRES_PASSWORD | Yes | No | postgres | ChangeMe_StrongPassword | Must be changed for production. |
| CONNECTIONSTRINGS__DEFAULT | No | Yes | local appsettings value | Host=localhost;Port=55432;Database=discordbot;Username=postgres;Password=... | For external DB or source-run mode. |
| RSSBRIDGE__BASEURL | No | Yes | http://localhost:3000 | http://localhost:3000 | In docker-prod the bot uses internal URL from compose. |
| POLLING__INTERVALMINUTES | Optional | Optional | 10 | 10 | Polling interval in minutes. |
| POLLING__MAXITEMSPERFEED | Optional | Optional | 5 | 5 | Max feed items fetched each cycle. |
| RETRY__MAXRETRIES | Optional | Optional | 3 | 3 | Retry count for RSS fetch path. |
| RETRY__PUBLISHMAXRETRIES | Optional | Optional | 2 | 2 | Retry count for Discord publish path. |
| RETRY__INITIALDELAYSECONDS | Optional | Optional | 2 | 2 | Base delay for exponential backoff. |
| PUBLISH__MAXCONCURRENTPUBLISHES | Optional | Optional | 2 | 2 | Concurrency guard for publishes. |
| PUBLISH__INTERPUBLISHDELAYMS | Optional | Optional | 200 | 200 | Delay between successful publish operations. |
| PUBLISH__MAXADDITIONALIMAGES | Optional | Optional | 3 | 3 | Additional images sent after the first embed image. |

## Minimum Variables You Must Add Manually

### docker-prod minimum
- DISCORD_TOKEN
- POSTGRES_PASSWORD

### source-run minimum
- DISCORD_TOKEN
- CONNECTIONSTRINGS__DEFAULT
- RSSBRIDGE__BASEURL

## Secret Handling
- Do not commit real secrets in .env files.
- Keep only templates in git: .env.example, .env.prod.example, .env.supabase.example.
- Rotate credentials immediately if exposed.
