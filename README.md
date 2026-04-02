# ---

**Discord Feed Bot (X \+ Facebook)**

Bot Discord tự động lấy feed từ X/Facebook và đăng vào channel theo lịch polling.

Tài liệu này là hướng dẫn end-to-end từ setup đến deploy và vận hành hằng ngày.

## **1\. Tổng quan nhanh**

Bot hỗ trợ 3 nhóm feed:

* X account feed (/add-x)  
* Facebook feed fanpage/profile (/add-fb)  
* Direct RSS feed (/add-link)

Bot có fallback runtime cho Facebook:

* **Primary provider**: (Theo feed đã đăng ký)  
* **RSS-Bridge fallback**: (Ưu tiên trước)  
* **Apify fallback**: (Layer cuối)

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
* rsshub: RSSHub provider  
* redis: Cache cho RSSHub  
* bot: .NET worker \+ Discord gateway

Code chính:

* src/DiscordXBot/Worker.cs: Polling/fetch/fallback/publish  
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
* FEEDPROVIDERS\_\_DEFAULTFACEBOOKPROVIDER  
* APIFYFALLBACK\_\_ENABLED  
* RSSBRIDGEFALLBACK\_\_ENABLED  
* RSSBRIDGEFALLBACK\_\_ENABLEFORPROFILE

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

/add-fb fanpageOrId:000000000000000 channel:\<\#channel\> sourceType:fanpage provider:rssbridge

Thêm profile:

Plaintext

/add-fb fanpageOrId:000000000000000 channel:\<\#channel\> sourceType:profile provider:rsshub

Hoặc profile với RSS-Bridge:

Plaintext

/add-fb fanpageOrId:000000000000000 channel:\<\#channel\> sourceType:profile provider:rssbridge

Liệt kê:

Plaintext

/list-fb

Xóa:

Plaintext

/remove-fb fanpageOrId:000000000000000 \[channel:\<\#channel\>\]

**Lưu ý quan trọng:**

* Profile hiện tại ưu tiên ID số (sourceType=profile).  
* Nếu provider trả lời tạm thời (503/timeout/network), bot vẫn cho add feed và trả về cảnh báo (warning).  
* Runtime sẽ xử lý tiếp bằng retry/fallback chain.

### **7.3 Direct RSS feeds**

Thêm:

Plaintext

/add-link rssUrl:https://example.com/feed.xml platform:FB channel:\<\#channel\>

Liệt kê:

Plaintext

/list-links

Xóa:

Plaintext

/remove-link rssUrl:https://example.com/feed.xml \[channel:\<\#channel\>\]

## **8\. Cấu hình fallback khuyến nghị**

### **8.1 Apify fallback**

Cần bật:

* APIFYFALLBACK\_\_ENABLED=true  
* APIFYFALLBACK\_\_APITOKEN=...  
* APIFYFALLBACK\_\_ACTORID=apify/facebook-posts-scraper

Khuyến nghị:

* APIFYFALLBACK\_\_FAILURETHRESHOLD=3  
* APIFYFALLBACK\_\_COOLDOWNMINUTES=180

### **8.2 RSS-Bridge priority fallback**

Cần bật:

* RSSBRIDGEFALLBACK\_\_ENABLED=true

Khuyến nghị:

* RSSBRIDGEFALLBACK\_\_FAILURETHRESHOLD=2  
* RSSBRIDGEFALLBACK\_\_COOLDOWNMINUTES=60  
* RSSBRIDGEFALLBACK\_\_ENABLEFORFANPAGE=true  
* RSSBRIDGEFALLBACK\_\_ENABLEFORPROFILE=true (Canary trước, mở rộng sau)

Thứ tự runtime:

1. Primary provider fetch  
2. RSS-Bridge fallback  
3. Apify fallback

## **9\. Quy trình verify sau khi add feed**

1. Chạy command trên Discord.  
2. Kiểm tra xem đã có mapping chưa:

PowerShell

.\\scripts\\integration\-evidence.ps1 \-ComposeMode prod \-FanpageSource 100057435399770 \-FacebookSourceType profile \-LookbackMinutes 360

3. Theo dõi log:

PowerShell

docker compose \-\-profile prod logs \-f bot

4. Tìm các marker quan trọng:  
* Using RSS-Bridge fallback for source ...  
* Using Apify fallback for source ...  
* Published tweet ...  
* Polling cycle done ...

## **10\. Vận hành hằng ngày**

### **10.1 Lệnh log nhanh**

PowerShell

docker compose \-\-profile prod logs \-f bot  
docker compose \-\-profile prod logs \-f rss\-bridge  
docker compose \-\-profile prod logs \-f rsshub  
docker compose \-\-profile prod logs \-f db

### **10.2 Kiểm tra sức khỏe batch Facebook trước onboarding**

PowerShell

.\\scripts\\precheck\-fanpages.ps1 \-FanpageSources 10150123547145211,100071458686024

### **10.3 Checklist release**

PowerShell

.\\scripts\\preflight.ps1 \-EnvFile .env  
.\\scripts\\apply\-migrations.ps1 \-Mode docker  
docker compose \-\-profile prod up \-d \-\-build  
.\\scripts\\smoke\-test.ps1 \-ComposeMode prod

## **11\. Troubleshooting nhanh**

### **11.1 /add-fb báo HTTP 503**

Trạng thái mới:

* Command có thể vẫn add được feed nếu lỗi là tạm thời (5xx/timeout/network).  
* Nếu vẫn fail, thử:  
  1. Kiểm tra provider đang dùng (rsshub/rssbridge).  
  2. Kiểm tra service:

  PowerShell  
     docker compose \-\-profile prod ps  
     docker compose \-\-profile prod logs \-\-since 10m rsshub  
     docker compose \-\-profile prod logs \-\-since 10m rss\-bridge

  3. Dùng precheck-fanpages.ps1 để phân loại source.

### **11.2 Không thấy slash command**

1. Kiểm tra DISCORD\_GUILD\_ID.  
2. Kiểm tra log Registered slash command set (...).  
3. Restart bot:

PowerShell

docker compose \-\-profile prod restart bot

### **11.3 Bot không publish dù có feed**

1. Kiểm tra processed\_tweets có bị trùng (duplicate) không.  
2. Kiểm tra media policy và parser output.  
3. Kiểm tra fallback marker trong log.

## **12\. Bảo mật**

1. Không commit các giá trị bí mật (DISCORD\_TOKEN, APIFY token, FB\_COOKIE, DB password).  
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
3. README-ARCHITECTURE.md: Cho luồng dữ liệu và fallback diagram.