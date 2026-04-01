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
| RSSBRIDGE__ENABLENITTERFALLBACK | Optional | Optional | true | true | If enabled, bot falls back to Nitter RSS when TwitterBridge returns synthetic error items. |
| RSSBRIDGE__NITTERBASEURL | Optional | Optional | https://nitter.net | https://nitter.net | Base URL for Nitter fallback endpoint. |
| FEEDPROVIDERS__ENABLEDIRECTRSS | Optional | Optional | true | true | Enables tracking direct RSS URLs (for example FetchRSS). |
| FEEDPROVIDERS__ENABLERSSHUB | Optional | Optional | false | true | Enables RSSHub provider usage for new feed registrations. |
| FEEDPROVIDERS__RSSHUBBASEURL | Optional | Optional | http://rsshub:1200 | http://rsshub:1200 | RSSHub base URL used by resolver for X/FB feeds. |
| FEEDPROVIDERS__DEFAULTXPROVIDER | Optional | Optional | RssBridge | RssBridge | Default provider for /add-x command. |
| FEEDPROVIDERS__DEFAULTFACEBOOKPROVIDER | Optional | Optional | RssHub | RssHub | Default provider for /add-fb fanpage command path. |
| FEEDPROVIDERS__ENABLEFACEBOOKPROFILEALERTS | Optional | Optional | false | true | Enables runtime admin alerts when FB profile feeds show repeated cookie/visibility failures. |
| FEEDPROVIDERS__FACEBOOKPROFILEALERTCHANNELID | Optional | Optional | 0 | 123456789012345678 | Discord channel ID receiving profile health alerts. 0 disables channel delivery. |
| FEEDPROVIDERS__FACEBOOKPROFILEFAILURETHRESHOLD | Optional | Optional | 3 | 3 | Number of consecutive profile fetch failures before alerting. |
| FEEDPROVIDERS__FACEBOOKPROFILEALERTCOOLDOWNMINUTES | Optional | Optional | 180 | 180 | Minimum minutes between repeated alerts for the same profile source. |
| FB_COOKIE | Optional | Optional | empty | c_user=...;xs=... | Cookie used by RSSHub for Facebook personal profile routes. Keep secret. |
| FB_PAGES_LIMIT | Optional | Optional | 3 | 3 | RSSHub fetch page limit for Facebook routes to reduce aggressive crawling. |
| APIFYFALLBACK__ENABLED | Optional | Optional | false | true | Enables Apify fallback when Facebook RSS providers fail repeatedly. |
| APIFYFALLBACK__APIBASEURL | Optional | Optional | https://api.apify.com/v2 | https://api.apify.com/v2 | Base URL for Apify API endpoints. |
| APIFYFALLBACK__APITOKEN | Optional | Optional | empty | apify_api_... | API token for running Apify actor. Keep secret. |
| APIFYFALLBACK__ACTORID | Optional | Optional | apify/facebook-posts-scraper | apify/facebook-posts-scraper | Actor identifier used for fallback scraping. |
| APIFYFALLBACK__RESULTSLIMIT | Optional | Optional | 5 | 5 | Maximum items requested per fallback run. |
| APIFYFALLBACK__REQUESTTIMEOUTSECONDS | Optional | Optional | 45 | 45 | Timeout budget for one fallback run lifecycle. |
| APIFYFALLBACK__POLLINTERVALSECONDS | Optional | Optional | 5 | 5 | Poll interval for asynchronous actor run status. |
| APIFYFALLBACK__MAXPOLLATTEMPTS | Optional | Optional | 24 | 24 | Maximum run status poll attempts before aborting fallback. |
| APIFYFALLBACK__FAILURETHRESHOLD | Optional | Optional | 3 | 3 | Consecutive primary fetch failures required before triggering fallback. |
| APIFYFALLBACK__COOLDOWNMINUTES | Optional | Optional | 180 | 180 | Minimum minutes between fallback attempts per source. |
| APIFYFALLBACK__ENABLEFORFANPAGE | Optional | Optional | true | true | Enables Apify fallback for Facebook fanpage sources. |
| APIFYFALLBACK__ENABLEFORPROFILE | Optional | Optional | true | true | Enables Apify fallback for Facebook profile sources. |
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
