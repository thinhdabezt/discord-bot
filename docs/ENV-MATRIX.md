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
| CONNECTIONSTRINGS__DEFAULT | No | Yes | local appsettings value | Host=db.<project-ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SSL Mode=Require;Trust Server Certificate=true | For external DB or source-run mode. Supabase direct connections require IPv6 support or the IPv4 add-on. |
| RSSBRIDGE__BASEURL | No | Yes | http://localhost:3000 | http://localhost:3000 | RSS-Bridge is used for X/Twitter and Instagram feeds. Facebook `/add-fb` does not use RSS-Bridge. |
| RSSBRIDGE__ENABLENITTERFALLBACK | Optional | Optional | true | true | If enabled, bot falls back to Nitter RSS when TwitterBridge returns synthetic error items. |
| RSSBRIDGE__NITTERBASEURL | Optional | Optional | https://nitter.net | https://nitter.net | Base URL for Nitter fallback endpoint. |
| FEEDPROVIDERS__ENABLEDIRECTRSS | Optional | Optional | true | true | Enables tracking direct RSS URLs through `/add-link`, including `platform:FB` and `platform:IG`. |
| FEEDPROVIDERS__DEFAULTXPROVIDER | Optional | Optional | RssBridge | RssBridge | Default provider for `/add-x`. |
| APIFY__ENABLED | Yes for `/add-fb` | Yes for `/add-fb` | false | true | Enables Apify as the primary Facebook provider. If false, `/add-fb` rejects new Facebook sources. |
| APIFY__APIBASEURL | Optional | Optional | https://api.apify.com/v2 | https://api.apify.com/v2 | Base URL for Apify API endpoints. |
| APIFY__APITOKEN | Yes for `/add-fb` | Yes for `/add-fb` | empty | apify_api_... | API token for running the Facebook actor. Keep secret. |
| APIFY__ACTORID | Yes for `/add-fb` | Yes for `/add-fb` | apify/facebook-posts-scraper | apify/facebook-posts-scraper | Actor identifier used for Facebook scraping. |
| APIFY__RESULTSLIMIT | Optional | Optional | 5 | 5 | Maximum items requested per source per polling cycle. |
| APIFY__REQUESTTIMEOUTSECONDS | Optional | Optional | 45 | 45 | Timeout budget for one actor run lifecycle. |
| APIFY__POLLINTERVALSECONDS | Optional | Optional | 5 | 5 | Poll interval for asynchronous actor run status. |
| APIFY__MAXPOLLATTEMPTS | Optional | Optional | 24 | 24 | Maximum run status poll attempts before aborting. |
| APIFY__ENABLEFORFANPAGE | Optional | Optional | true | true | Enables `/add-fb sourceType:fanpage`. |
| APIFY__ENABLEFORPROFILE | Optional | Optional | true | true | Enables `/add-fb sourceType:profile`. |
| POLLING__INTERVALMINUTES | Optional | Optional | 10 | 10 | Polling interval in minutes. |
| POLLING__MAXITEMSPERFEED | Optional | Optional | 5 | 5 | Max items fetched each cycle. |
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
- APIFY__ENABLED=true, APIFY__APITOKEN, and APIFY__ACTORID if using `/add-fb`

### source-run minimum
- DISCORD_TOKEN
- CONNECTIONSTRINGS__DEFAULT
- RSSBRIDGE__BASEURL
- APIFY__ENABLED=true, APIFY__APITOKEN, and APIFY__ACTORID if using `/add-fb`

## Instagram Notes
- `/add-ig` uses existing `RSSBRIDGE__BASEURL`.
- No Instagram auth/session/cookie env vars are supported in v1.
- Use `/add-link platform:IG` for operator-provided direct RSS fallback.

## Secret Handling
- Do not commit real secrets in .env files.
- Keep only templates in git: .env.example, .env.prod.example, .env.supabase.example.
- Rotate credentials immediately if exposed.
