# Discord Feed Bot (X + Facebook)

Bot Discord tu dong lay feed tu X/Facebook va dang vao channel theo lich polling.

Tai lieu nay la huong dan end-to-end tu setup den deploy va van hanh hang ngay.

## 1. Tong quan nhanh

Bot ho tro 3 nhom feed:
- X account feed (`/add-x`)
- Facebook feed fanpage/profile (`/add-fb`)
- Direct RSS feed (`/add-link`)

Bot co fallback runtime cho Facebook:
- Primary provider (theo feed da dang ky)
- RSS-Bridge fallback (uu tien truoc)
- Apify fallback (layer cuoi)

Script van hanh da co san:
- `scripts/preflight.ps1`
- `scripts/apply-migrations.ps1`
- `scripts/smoke-test.ps1`
- `scripts/integration-evidence.ps1`
- `scripts/precheck-fanpages.ps1`

Tai lieu chi tiet bo sung:
- `docs/DEPLOYMENT.md`
- `docs/ENV-MATRIX.md`
- `docs/ops-checklist.md`

## 2. Kien truc

Service chinh trong `docker-compose.yml`:
- `db`: PostgreSQL
- `rss-bridge`: RSS bridge provider
- `rsshub`: RSSHub provider
- `redis`: cache cho RSSHub
- `bot`: .NET worker + Discord gateway

Code chinh:
- `src/DiscordXBot/Worker.cs`: polling/fetch/fallback/publish
- `src/DiscordXBot/Discord/Commands/*.cs`: slash command handlers
- `src/DiscordXBot/Services/*.cs`: resolver, RSS clients, publisher, parser

## 3. Yeu cau moi truong

Can co:
1. Windows + PowerShell (5.1 hoac 7+)
2. Docker Desktop (WSL2 enabled)
3. .NET SDK 8 (neu chay source-run)
4. Discord bot token va quyen bot phu hop

## 4. Setup Discord Bot (mot lan)

1. Tao app tai Discord Developer Portal.
2. Tao bot user va copy token.
3. Bat quyen bot can thiet trong guild:
- View Channels
- Send Messages
- Embed Links
- Read Message History
4. Invite bot vao server voi scope:
- `bot`
- `applications.commands`

## 5. Khoi tao local bang Docker (khuyen nghi)

### 5.1 Tao file env

```powershell
Copy-Item .env.prod.example .env
```

Cap nhat toi thieu:
- `DISCORD_TOKEN`
- `POSTGRES_PASSWORD`

Khuyen nghi cap nhat them:
- `DISCORD_GUILD_ID`
- `FEEDPROVIDERS__DEFAULTFACEBOOKPROVIDER`
- `APIFYFALLBACK__ENABLED`
- `RSSBRIDGEFALLBACK__ENABLED`
- `RSSBRIDGEFALLBACK__ENABLEFORPROFILE`

Luu y:
- Khong commit `.env`.
- Chi commit cac file `.env.*.example`.

### 5.2 Preflight

```powershell
.\scripts\preflight.ps1 -EnvFile .env
```

Preflight kiem tra:
- Docker/Compose
- Bien bat buoc
- Tinh hop le compose profile `prod`

### 5.3 Apply migration

```powershell
.\scripts\apply-migrations.ps1 -Mode docker
```

Script se backup DB vao `backups/` tru khi dung `-SkipBackup`.

### 5.4 Deploy

```powershell
docker compose --profile prod up -d --build
docker compose --profile prod ps
```

### 5.5 Smoke test

```powershell
.\scripts\smoke-test.ps1 -ComposeMode prod
```

Neu bot vua restart va ban can nhieu log hon:

```powershell
.\scripts\smoke-test.ps1 -ComposeMode prod -BotLogSinceMinutes 240
```

## 6. Chay source-run (khong full docker)

Dung khi ban muon debug nhanh code:

```powershell
Copy-Item .env.supabase.example .env
```

Cap nhat toi thieu:
- `DISCORD_TOKEN`
- `CONNECTIONSTRINGS__DEFAULT`
- `RSSBRIDGE__BASEURL`

Khoi dong dependency can thiet:

```powershell
docker compose up -d db rss-bridge
```

Chay bot:

```powershell
dotnet run --project src/DiscordXBot/DiscordXBot.csproj
```

## 7. Slash commands va cach dung cu the

Tat ca lenh can chay trong guild va user can co quyen Manage Server hoac Manage Channels.

### 7.1 X feeds

Them:
```text
/add-x username:<x_username> channel:<#channel>
```

Liet ke:
```text
/list-x
```

Xoa:
```text
/remove-x username:<x_username> [channel:<#channel>]
```

### 7.2 Facebook feeds (fanpage/profile)

Them fanpage:
```text
/add-fb fanpageOrId:100072247413815 channel:<#channel> sourceType:fanpage provider:rssbridge
```

Them profile:
```text
/add-fb fanpageOrId:100057435399770 channel:<#channel> sourceType:profile provider:rsshub
```

Hoac profile voi RSS-Bridge:
```text
/add-fb fanpageOrId:100057435399770 channel:<#channel> sourceType:profile provider:rssbridge
```

Liet ke:
```text
/list-fb
```

Xoa:
```text
/remove-fb fanpageOrId:100057435399770 [channel:<#channel>]
```

Luu y quan trong:
- Profile hien tai uu tien ID so (`sourceType=profile`).
- Neu provider tra loi tam thoi (503/timeout/network), bot van cho add feed va tra warning.
- Runtime se xu ly tiep bang retry/fallback chain.

### 7.3 Direct RSS feeds

Them:
```text
/add-link rssUrl:https://example.com/feed.xml platform:FB channel:<#channel>
```

Liet ke:
```text
/list-links
```

Xoa:
```text
/remove-link rssUrl:https://example.com/feed.xml [channel:<#channel>]
```

## 8. Cau hinh fallback khuyen nghi

### 8.1 Apify fallback

Can bat:
- `APIFYFALLBACK__ENABLED=true`
- `APIFYFALLBACK__APITOKEN=...`
- `APIFYFALLBACK__ACTORID=apify/facebook-posts-scraper`

Khuyen nghi:
- `APIFYFALLBACK__FAILURETHRESHOLD=3`
- `APIFYFALLBACK__COOLDOWNMINUTES=180`

### 8.2 RSS-Bridge priority fallback

Can bat:
- `RSSBRIDGEFALLBACK__ENABLED=true`

Khuyen nghi:
- `RSSBRIDGEFALLBACK__FAILURETHRESHOLD=2`
- `RSSBRIDGEFALLBACK__COOLDOWNMINUTES=60`
- `RSSBRIDGEFALLBACK__ENABLEFORFANPAGE=true`
- `RSSBRIDGEFALLBACK__ENABLEFORPROFILE=true` (canary truoc, mo rong sau)

Thu tu runtime:
1. Primary provider fetch
2. RSS-Bridge fallback
3. Apify fallback

## 9. Quy trinh verify sau khi add feed

1. Chay command tren Discord.
2. Kiem tra da co mapping:
```powershell
.\scripts\integration-evidence.ps1 -ComposeMode prod -FanpageSource 100057435399770 -FacebookSourceType profile -LookbackMinutes 360
```
3. Theo doi log:
```powershell
docker compose --profile prod logs -f bot
```
4. Tim marker quan trong:
- `Using RSS-Bridge fallback for source ...`
- `Using Apify fallback for source ...`
- `Published tweet ...`
- `Polling cycle done ...`

## 10. Van hanh hang ngay

### 10.1 Lenh log nhanh

```powershell
docker compose --profile prod logs -f bot
docker compose --profile prod logs -f rss-bridge
docker compose --profile prod logs -f rsshub
docker compose --profile prod logs -f db
```

### 10.2 Kiem tra suc khoe batch Facebook truoc onboarding

```powershell
.\scripts\precheck-fanpages.ps1 -FanpageSources 10150123547145211,100071458686024
```

### 10.3 Checklist release

```powershell
.\scripts\preflight.ps1 -EnvFile .env
.\scripts\apply-migrations.ps1 -Mode docker
docker compose --profile prod up -d --build
.\scripts\smoke-test.ps1 -ComposeMode prod
```

## 11. Troubleshooting nhanh

### 11.1 `/add-fb` bao HTTP 503

Trang thai moi:
- Command co the van add duoc feed neu loi la tam thoi (5xx/timeout/network).
- Neu van fail, thu:
1. Kiem tra provider dang dung (`rsshub`/`rssbridge`).
2. Kiem tra service:
```powershell
docker compose --profile prod ps
docker compose --profile prod logs --since 10m rsshub
docker compose --profile prod logs --since 10m rss-bridge
```
3. Dung `precheck-fanpages.ps1` de phan loai source.

### 11.2 Khong thay slash command

1. Kiem tra `DISCORD_GUILD_ID`.
2. Kiem tra log `Registered slash command set (...)`.
3. Restart bot:
```powershell
docker compose --profile prod restart bot
```

### 11.3 Bot khong publish du co feed

1. Kiem tra `processed_tweets` co duplicate khong.
2. Kiem tra media policy va parser output.
3. Kiem tra fallback marker trong log.

## 12. Bao mat

1. Khong commit cac gia tri secret (`DISCORD_TOKEN`, `APIFY token`, `FB_COOKIE`, DB password).
2. Neu lo secret, rotate ngay.
3. Uu tien dung secret manager cho production thuc te.

## 13. Rollback

```powershell
docker compose --profile prod down
git checkout <last_known_good_commit>
docker compose --profile prod up -d --build
```

## 14. File tham chieu chinh

- `docs/DEPLOYMENT.md`
- `docs/ENV-MATRIX.md`
- `docs/ops-checklist.md`
- `scripts/preflight.ps1`
- `scripts/smoke-test.ps1`
- `scripts/integration-evidence.ps1`
- `scripts/precheck-fanpages.ps1`

---

Neu can, buoc tiep theo nen tao them:
1. `README-OPERATIONS.md` cho on-call/incident.
2. `README-COMMANDS.md` gom screenshot slash command.
3. `README-ARCHITECTURE.md` cho luong du lieu va fallback diagram.
