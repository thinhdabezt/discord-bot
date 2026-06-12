# ---

**Discord Feed Bot (X \+ Facebook \+ Instagram)**

Bot Discord tự động lấy feed từ X/Facebook/Instagram và đăng vào channel theo lịch polling.

Tài liệu này là hướng dẫn end-to-end từ setup đến deploy và vận hành hằng ngày.

## **1\. Tổng quan nhanh**

Bot hỗ trợ 4 nhóm feed:

* X account feed (/add-x)  
* Facebook feed fanpage/profile (/add-fb)  
* Instagram username feed (/add-ig)  
* Direct RSS feed (/add-link)

Bot dùng Apify cho Facebook và RSS-Bridge cho X/Instagram:

* **Facebook /add-fb**: Apify primary
* **Facebook /add-link**: Direct RSS operator cung cấp
* **Instagram /add-ig**: RSS-Bridge InstagramBridge
* **Instagram /add-link**: Direct RSS operator cung cấp

Script vận hành đã có sẵn:

* scripts/preflight.ps1  
* scripts/apply-migrations.ps1  
* scripts/smoke-test.ps1  
* scripts/integration-evidence.ps1  
* scripts/precheck-fanpages.ps1

Tài liệu chi tiết bổ sung:

* docs/DEPLOYMENT.md  
* docs/ENV-MATRIX.md  
* docs/ops-checklist.md

## **2\. Kiến trúc**

Service chính trong docker-compose.yml:

* db: PostgreSQL  
* rss-bridge: RSS bridge provider  
* bot: .NET worker \+ Discord gateway

Code chính:

* src/DiscordXBot/Worker.cs: Polling/fetch/publish
* src/DiscordXBot/Discord/Commands/\*.cs: Slash command handlers  
* src/DiscordXBot/Services/\*.cs: Resolver, RSS clients, publisher, parser

## **3\. Yêu cầu môi trường**

Cần có:

1. Windows \+ PowerShell (5.1 hoặc 7+)  
2. Docker Desktop (WSL2 enabled)  
3. .NET SDK 8 (Nếu chạy source-run)  
4. Discord bot token và quyền bot phù hợp

## **4\. Setup Discord Bot (Một lần)**

1. Tạo app tại **Discord Developer Portal**.  
2. Tạo bot user và copy token.  
3. Bật quyền bot cần thiết trong guild:  
   * View Channels  
   * Send Messages  
   * Embed Links  
   * Read Message History  
4. Invite bot vào server với scope:  
   * bot  
   * applications.commands

## **5\. Khởi tạo local bằng Docker (Khuyến nghị)**

### **5.1 Tạo file env**

PowerShell

Copy-Item .env.prod.example .env

Cập nhật tối thiểu:

* DISCORD\_TOKEN  
* POSTGRES\_PASSWORD  
* RSSBRIDGE\_\_BASEURL

Khuyến nghị cập nhật thêm:

* DISCORD\_GUILD\_ID  
* APIFY\_\_ENABLED=true  
* APIFY\_\_APITOKEN  
* APIFY\_\_ACTORID

**Lưu ý:**

* Không commit .env.  
* Chỉ commit các file .env.\*.example.
* Khi chạy Docker compose prod, đặt RSSBRIDGE\_\_BASEURL=http://rss-bridge:80 (truy cập nội bộ giữa container).

### **5.2 Preflight**

PowerShell

.\\scripts\\preflight.ps1 \-EnvFile .env

Preflight kiểm tra:

* Docker/Compose  
* Biến bắt buộc  
* Tính hợp lệ của compose profile prod

### **5.3 Apply migration**

PowerShell

.\\scripts\\apply\-migrations.ps1 \-Mode docker

Script sẽ backup DB vào backups/ trừ khi dùng \-SkipBackup.

### **5.4 Deploy**

PowerShell

docker compose \-\-profile prod up \-d \-\-build  
docker compose \-\-profile prod ps

### **5.5 Smoke test**

PowerShell

.\\scripts\\smoke\-test.ps1 \-ComposeMode prod

Nếu bot vừa restart và bạn cần nhiều log hơn:

PowerShell

.\\scripts\\smoke\-test.ps1 \-ComposeMode prod \-BotLogSinceMinutes 240

## **6\. Chạy source-run (Không dùng Docker đầy đủ)**

Dùng khi bạn muốn debug nhanh code:

PowerShell

Copy-Item .env.supabase.example .env

Cập nhật tối thiểu:

* DISCORD\_TOKEN  
* CONNECTIONSTRINGS\_\_DEFAULT  
* RSSBRIDGE\_\_BASEURL

Khởi động dependency cần thiết:

PowerShell

docker compose up \-d db rss\-bridge

Chạy bot:

PowerShell

dotnet run \-\-project src/DiscordXBot/DiscordXBot.csproj

## **7\. Slash commands và cách dùng cụ thể**

Tất cả lệnh cần chạy trong guild và user cần có quyền **Manage Server** hoặc **Manage Channels**.

### **7.1 X feeds**

Thêm:

Plaintext

/add-x username:\<x\_username\> channel:\<\#channel\>

Liệt kê:

Plaintext

/list-x

Xóa:

Plaintext

/remove-x username:\<x\_username\> \[channel:\<\#channel\>\]

### **7.2 Facebook feeds (fanpage/profile)**

Thêm fanpage:

Plaintext

/add-fb fanpageOrId:000000000000000 channel:\<\#channel\> sourceType:fanpage

Thêm profile:

Plaintext

/add-fb fanpageOrId:000000000000000 channel:\<\#channel\> sourceType:profile

Liệt kê:

Plaintext

/list-fb

Xóa:

Plaintext

/remove-fb fanpageOrId:000000000000000 \[channel:\<\#channel\>\]

**Lưu ý quan trọng:**

* Profile hiện tại ưu tiên ID số (sourceType=profile).  
* `/add-fb` yêu cầu Apify config hợp lệ trước khi lưu feed.
* Nếu cần direct RSS cho Facebook, dùng `/add-link platform:FB`.

### **7.3 Instagram feeds**

Thêm:

Plaintext

/add-ig username:\<instagram_username\> channel:\<\#channel\>

Liệt kê:

Plaintext

/list-ig

Xóa:

Plaintext

/remove-ig username:\<instagram_username\> \[channel:\<\#channel\>\]

**Lưu ý quan trọng:**

* Instagram v1 chỉ hỗ trợ username public qua RSS-Bridge.
* Không cấu hình cookie/session Instagram trong bot.
* Nếu InstagramBridge bị upstream block hoặc trả feed rỗng, dùng `/add-link platform:IG` với direct RSS URL đã kiểm tra.

### **7.4 Direct RSS feeds**

Thêm:

Plaintext

/add-link rssUrl:https://example.com/feed.xml platform:FB channel:\<\#channel\>
/add-link rssUrl:https://example.com/feed.xml platform:IG channel:\<\#channel\>

Liệt kê:

Plaintext

/list-links

Xóa:

Plaintext

/remove-link rssUrl:https://example.com/feed.xml \[channel:\<\#channel\>\]

## **8. Cấu hình Facebook qua Apify**

### **8.1 Apify primary**

Cần bật:

* APIFY__ENABLED=true
* APIFY__APITOKEN=...
* APIFY__ACTORID=apify/facebook-posts-scraper

Khuyến nghị:

* APIFY__RESULTSLIMIT=5
* APIFY__REQUESTTIMEOUTSECONDS=45
* APIFY__ENABLEFORFANPAGE=true
* APIFY__ENABLEFORPROFILE=true

Thứ tự runtime:

1. Facebook `/add-fb`: Apify primary
2. Facebook `/add-link`: direct RSS URL operator cung cấp
3. Instagram `/add-ig`: RSS-Bridge InstagramBridge
4. Instagram `/add-link`: direct RSS URL operator cung cấp
5. X/Twitter: RSS-Bridge

## **9. Quy trình verify sau khi add feed**

1. Chạy command trên Discord.  
2. Kiểm tra xem đã có mapping chưa:

PowerShell

.\\scripts\\integration\-evidence.ps1 \-ComposeMode prod \-FanpageSource 100057435399770 \-FacebookSourceType profile \-InstagramUsername nasa \-LookbackMinutes 360

3. Theo dõi log:

PowerShell

docker compose \-\-profile prod logs \-f bot

4. Tìm các marker quan trọng:  
* Apify retrieved ...  
* Published tweet ...  
* Polling cycle done ...

## **10\. Vận hành hằng ngày**

### **10.1 Lệnh log nhanh**

PowerShell

docker compose \-\-profile prod logs \-f bot  
docker compose \-\-profile prod logs \-f rss\-bridge  
docker compose \-\-profile prod logs \-f db

### **10.2 Kiểm tra sức khỏe batch Facebook trước onboarding**

PowerShell

.\\scripts\\precheck\-fanpages.ps1 \-FanpageSources 10150123547145211,100071458686024

Precheck recommendation notes:

* `use-add-fb`: Apify config is present and source is suitable for `/add-fb`.
* `use-add-link`: source has a mapped direct RSS URL and can use `/add-link`.
* `fix-direct-rss`: source has a mapped direct RSS URL, but that URL failed validation.
* `configure-apify`: Apify env is missing or disabled.
* `invalid-source`: source input cannot be normalized.

Facebook source onboarding decision tree:

1. Add known direct RSS URLs to `config/direct-rss-sources.local.csv` using columns `Source,RssUrl,Platform,Notes`.
2. Run `.\scripts\precheck-fanpages.ps1 -FanpageSources <source> -DirectRssMapFile .\config\direct-rss-sources.local.csv -ValidateDirectRss`.
3. If the result is `use-add-fb`, run `/add-fb fanpageOrId:<source> channel:<#channel> sourceType:fanpage`.
4. If the result is `use-add-link`, run the concrete `/add-link` command printed by the script.
5. If the result is `configure-apify`, set `APIFY__ENABLED=true`, `APIFY__APITOKEN`, and `APIFY__ACTORID`, or add a direct RSS map entry.
6. If the result is `fix-direct-rss`, update the mapped RSS URL before running `/add-link`.

Direct RSS validation checklist:

* Open the RSS URL in a browser or run `Invoke-WebRequest <direct-rss-url>`.
* Confirm HTTP 200.
* Confirm the XML contains real `<item>` or `<entry>` posts, not an error page.
* Acceptable origins include official website RSS, FetchRSS, RSS.app, or another generated RSS provider.
* Use `platform:FB` for Facebook direct RSS and `platform:IG` for Instagram direct RSS.
* Do not commit private generated RSS URLs or provider credentials.
* Keep private mappings in `config/direct-rss-sources.local.csv`; use `config/direct-rss-sources.example.csv` as the tracked template.

Facebook provider policy:

* RSS-Bridge is no longer used for Facebook.
* Do not configure Facebook cookies for RSS-Bridge.
* Use Apify for `/add-fb` and `/add-link platform:FB` for operator-provided direct RSS.

Instagram provider policy:

* `/add-ig` uses RSS-Bridge `InstagramBridge` public mode.
* Do not configure Instagram cookies/session in bot env.
* Use `/add-link platform:IG` for operator-provided direct RSS when InstagramBridge is blocked or empty.

### **10.3 Checklist release**

PowerShell

.\\scripts\\preflight.ps1 \-EnvFile .env  
.\\scripts\\apply\-migrations.ps1 \-Mode docker  
docker compose \-\-profile prod up \-d \-\-build  
.\\scripts\\smoke\-test.ps1 \-ComposeMode prod

## **11\. Troubleshooting nhanh**

### **11.1 /add-fb báo thiếu Apify config**

Trạng thái mới:

* `/add-fb` yêu cầu `APIFY__ENABLED=true`, `APIFY__APITOKEN`, và `APIFY__ACTORID`.
* RSS-Bridge không còn nằm trong luồng Facebook.
* Nếu chưa muốn dùng Apify cho source đó, dùng `/add-link platform:FB` với một direct RSS URL đã kiểm tra.

Kiểm tra nhanh:

PowerShell

.\scripts\preflight.ps1 -EnvFile .env
.\scripts\precheck-fanpages.ps1 -FanpageSources <source> -EnvFile .env
### **11.2 Không thấy slash command**

1. Kiểm tra DISCORD\_GUILD\_ID.  
2. Kiểm tra log Registered slash command set (...).  
3. Restart bot:

PowerShell

docker compose \-\-profile prod restart bot

### **11.3 Bot không publish dù có feed**

1. Kiểm tra processed\_tweets có bị trùng (duplicate) không.  
2. Kiểm tra media policy và parser output.  
3. Kiểm tra log fetch/publish marker.

## **12\. Bảo mật**

1. Không commit các giá trị bí mật (DISCORD\_TOKEN, APIFY token, DB password).  
2. Nếu lộ secret, phải rotate (đổi mới) ngay lập tức.  
3. Ưu tiên dùng secret manager cho môi trường production thực tế.

## **13\. Rollback**

PowerShell

docker compose \-\-profile prod down  
git checkout \<last\_known\_good\_commit\>  
docker compose \-\-profile prod up \-d \-\-build

## **14\. File tham chiếu chính**

* docs/DEPLOYMENT.md  
* docs/ENV-MATRIX.md  
* docs/ops-checklist.md  
* scripts/preflight.ps1  
* scripts/smoke-test.ps1  
* scripts/integration-evidence.ps1  
* scripts/precheck-fanpages.ps1

---

Nếu cần, bước tiếp theo bạn nên tạo thêm:

1. README-OPERATIONS.md: Cho on-call/incident.  
2. README-COMMANDS.md: Gồm screenshot các slash command thực tế.  
3. README-ARCHITECTURE.md: Cho luồng dữ liệu và provider diagram.




