Mở rộng sang tài khoản cá nhân (Personal Profile) trên Facebook.

### ---

**📋 Giai đoạn 1: Chuẩn bị "Danh tính" (Identity Setup)**

Khác với Fanpage có thể xem công khai, Profile cá nhân thường yêu cầu đăng nhập.

1. **Tạo tài khoản Facebook phụ (Clone):** Tuyệt đối không dùng tài khoản chính để tránh rủi ro bị khóa (checkpoint). Kết bạn hoặc nhấn "Theo dõi" các tài khoản mục tiêu nếu cần.  
2. **Trích xuất Cookie:**  
   * Đăng nhập tài khoản clone trên trình duyệt.  
   * Nhấn F12 \-\> thẻ **Network**.  
   * Tìm một request gửi đến facebook.com, copy toàn bộ chuỗi ở mục **Request Headers** \-\> **Cookie**.  
   * Chuỗi này sẽ giúp RSSHub "giả danh" bạn để đọc bài viết.

### ---

**🛠️ Giai đoạn 2: Cấu hình Hạ tầng (Infrastructure)**

Cập nhật file docker-compose.yml để RSSHub có thể sử dụng danh tính bạn vừa lấy.

1. **Cập nhật RSSHub Service:**  
   YAML  
   rsshub:  
     image: diygod/rsshub  
     environment:  
       \- CACHE\_TYPE=redis  
       \- REDIS\_URL=redis://redis:6379  
       \- FB\_COOKIE=chuỗi\_cookie\_của\_bạn\_ở\_đây  
       \- FB\_PAGES\_LIMIT=3 \# Giới hạn số bài mỗi lần quét để tránh bị nghi ngờ

2. **Khởi động lại:** docker compose up \-d để áp dụng cấu hình mới.

### ---

**💾 Giai đoạn 3: Cập nhật Cơ sở dữ liệu & Model (C\#)**

Bạn cần phân biệt đâu là Page, đâu là Profile để Bot gọi đúng "đường dẫn" (Route).

1. **Cập nhật bảng TrackedFeeds:** Thêm cột SourceType (kiểu Enum hoặc String).  
   * SourceType \= "FB\_PAGE"  
   * SourceType \= "FB\_PROFILE"  
2. **Logic sinh URL trong code C\#:**  
   C\#  
   string rssUrl \= feed.SourceType switch {  
       "FB\_PAGE" \=\> $"http://rsshub:1200/facebook/page/{feed.XUsernameOrId}",  
       "FB\_PROFILE" \=\> $"http://rsshub:1200/facebook/user/{feed.XUsernameOrId}",  
       \_ \=\> feed.RssUrl  
   };

### ---

**🤖 Giai đoạn 4: Cải tiến Slash Command**

Giúp người dùng dễ dàng chọn loại tài khoản khi add.

1. **Lệnh /add-fb:** Thêm một tham số lựa chọn (Option).  
   * Tham số 1: link\_hoac\_id  
   * Tham số 2: loai (Chọn: Fanpage hoặc Trang cá nhân).  
2. **Xác thực ID:** Đối với Profile cá nhân, RSSHub hoạt động tốt nhất với **Numeric ID** (ví dụ: 1000xxxx). Bạn nên tích hợp logic kiểm tra xem chuỗi nhập vào là ID số hay Username.

### ---

**🛡️ Giai đoạn 5: Cơ chế "Sinh tồn" cho Bot (Safety & Maintenance)**

Facebook quét rất gắt các tài khoản dùng Cookie để lấy dữ liệu.

1. **Giãn cách thời gian (Rate Limiting):**  
   * Đối với Fanpage: Quét 15 phút/lần.  
   * Đối với Profile: Nên để **30 \- 60 phút/lần**.  
2. **Cơ chế làm mới Cookie (Cookie Rotation):**  
   * Cookie có thể hết hạn sau vài tuần.  
   * **Kế hoạch:** Nếu Bot nhận thấy RSSHub trả về lỗi 403 hoặc nội dung trống liên tục, hãy thiết lập gửi một tin nhắn cảnh báo vào Channel Admin của bạn: *"Cookie Facebook đã hết hạn, vui lòng cập nhật\!"*.  
3. **Xử lý nội dung (Parsing):**  
   * Bài đăng cá nhân thường có các quyền riêng tư phức tạp. Code C\# cần kiểm tra nếu SyndicationItem bị trống nội dung thì bỏ qua, không gửi tin nhắn rác lên Discord.

### ---

**🚀 Lộ trình triển khai thực tế (Step-by-step)**

1. **Tuần 1:** Thử nghiệm lấy bài từ 1 Profile cá nhân bằng **RSSHub \+ Cookie thủ công** trên trình duyệt để xác nhận Route hoạt động.  
2. **Tuần 2:** Cập nhật Database và lệnh /add-fb trong code C\#.  
3. **Tuần 3:** Triển khai bản Docker mới có kèm Redis để tối ưu Cache (tránh gửi request trùng lặp lên FB).  
4. **Tuần 4:** Theo dõi tỷ lệ "sống" của tài khoản clone và điều chỉnh thời gian quét cho phù hợp.

**Lời khuyên:** Hãy bắt đầu với những Profile có chế độ **"Chuyên nghiệp" (Professional Mode)** trước, vì cấu trúc của chúng gần giống Fanpage và RSSHub hỗ trợ cực kỳ tốt.