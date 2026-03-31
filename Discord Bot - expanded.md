Chào bạn, đây là bản kế hoạch triển khai (Implementation Plan) toàn diện và chi tiết nhất cho dự án Discord Bot của bạn. Bản kế hoạch này được thiết kế dựa trên các kỹ năng bạn đã có (C\#, .NET, PostgreSQL) và môi trường Windows 11/WSL2.

## ---

**📋 Lộ trình thực hiện (Roadmap)**

Dự án sẽ chia làm 5 giai đoạn chính:

### **Giai đoạn 1: Thiết lập hạ tầng (Infrastructure) trên WSL2**

Thay vì cài đặt rời rạc, chúng ta sẽ dùng **Docker Compose** để quản lý toàn bộ các dịch vụ phụ trợ.

1. **Tạo file docker-compose.yml:**  
   YAML  
   services:  
     db:  
       image: postgres:latest  
       environment:  
         POSTGRES\_PASSWORD: your\_password  
       ports:  
         \- "5432:5432"  
     rss-bridge:  
       image: rssbridge/rss-bridge  
       ports:  
         \- "3000:80"

2. **Chạy lệnh:** docker compose up \-d trong terminal WSL2.  
3. **Kiểm tra:** Truy cập localhost:3000 trên trình duyệt để đảm bảo RSS-Bridge đã chạy.

### ---

**Giai đoạn 2: Thiết kế Cơ sở dữ liệu (Entity Framework Core)**

Sử dụng kiến trúc Code-First để quản lý DB dễ dàng.

1. **Tạo Model TrackedFeed:**  
   * Id (PK)  
   * GuildId (ulong) \- **Khóa chính để tách biệt các server.**  
   * ChannelId (ulong)  
   * XUsername (string)  
   * LastProcessedId (string) \- Lưu ID bài mới nhất để so sánh.  
2. **Tạo BotDbContext:** Cấu hình quan hệ và các bảng.  
3. **Migration:** Chạy dotnet ef migrations add InitialCreate để tạo bảng trong Postgres.

### ---

**Giai đoạn 3: Phát triển Module Lệnh (Interaction Module)**

Tập trung vào tính năng cá nhân hóa cho từng server.

1. **Lệnh /add-x \[username\] \[channel\]:**  
   * Kiểm tra quyền của người dùng (phải là Admin).  
   * Lưu GuildId (lấy từ Context.Guild.Id) và XUsername vào DB.  
2. **Lệnh /list-x:**  
   * Query DB: \_db.TrackedFeeds.Where(f \=\> f.GuildId \== Context.Guild.Id).  
   * Hiển thị danh sách dưới dạng Embed đẹp mắt.  
3. **Lệnh /remove-x \[username\]:**  
   * Xóa bản ghi dựa trên cặp GuildId và XUsername.

### ---

**Giai đoạn 4: Phát triển "Bộ não" (The Worker Service)**

Đây là phần chạy ngầm để quét bài đăng và gửi tin nhắn.

1. **Luồng xử lý (Workflow):**  
   * **Bước 1 (Gom nhóm):** Lấy tất cả TrackedFeeds, sau đó GroupBy(f \=\> f.XUsername). Việc này giúp bạn chỉ gọi RSS-Bridge **1 lần** cho 1 tài khoản X, dù có 10 server cùng theo dõi tài khoản đó (tiết kiệm tài nguyên).  
   * **Bước 2 (Fetch):** Dùng HttpClient gọi link RSS từ RSS-Bridge.  
   * **Bước 3 (Parse):** Dùng SyndicationFeed để đọc bài mới nhất.  
   * **Bước 4 (Extract Media):** Dùng HtmlAgilityPack để tách link ảnh từ nội dung HTML của RSS.  
   * **Bước 5 (Distribute):** Với mỗi bài mới tìm thấy, duyệt danh sách các Server (Guild) đang theo dõi nó và gửi tin nhắn qua DiscordClient.  
2. **Tần suất quét:** Thiết lập Task.Delay(TimeSpan.FromMinutes(10)) để tránh bị X chặn.

### ---

**Giai đoạn 5: Triển khai và Vận hành (Deployment)**

1. **Cấu hình Secret:** Sử dụng appsettings.json hoặc Environment Variables để lưu Bot Token và Connection String (không bao giờ hardcode token vào code).  
2. **Dockerize Bot:** Tạo một Dockerfile cho ứng dụng C\# của bạn.  
3. **Chạy 24/7:**  
   * Bạn có thể thêm bot vào file docker-compose.yml ở Giai đoạn 1\.  
   * Khi đó, chỉ cần một lệnh docker compose up \-d, toàn bộ hệ thống (Bot \+ DB \+ RSS Bridge) sẽ tự khởi động cùng nhau.

## ---

**🛠️ Danh sách các công cụ/thư viện cần dùng**

| Thành phần | Công nghệ |
| :---- | :---- |
| **Ngôn ngữ** | C\# (.NET 8 hoặc 10\) |
| **Bot Library** | Discord.Net |
| **Database** | PostgreSQL |
| **ORM** | Entity Framework Core |
| **RSS Parser** | System.ServiceModel.Syndication |
| **HTML Parser** | HtmlAgilityPack |
| **Container** | Docker & Docker Compose |

## ---

**💡 Các mẹo nhỏ để Bot "xịn" hơn:**

* **Tính năng Duy nhất (Unique):** Khi người dùng /add-x, hãy kiểm tra xem username đó có tồn tại thật không bằng cách thử gọi RSS-Bridge trước khi lưu vào DB.  
* **Xử lý Media:** X thường có nhiều ảnh trong một bài. Bạn có thể code để bot gửi một chuỗi các ảnh (Multiple Images) hoặc chỉ ảnh đầu tiên.  
* **Logging:** Sử dụng thư viện Serilog để ghi lại lịch sử hoạt động. Nếu RSS-Bridge bị lỗi hoặc X thay đổi cấu trúc, bạn sẽ biết ngay lập tức qua log.