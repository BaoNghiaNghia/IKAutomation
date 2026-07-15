🎯 MỤC TIÊU CHÍNH
✅ Xây dựng hệ thống nuôi Facebook trên LDPlayer 9 với C# automation, đảm bảo:

Mỗi LDPlayer instance chạy 1 tài khoản Facebook Lite

Mỗi 2 instance dùng 1 proxy riêng (có user/pass)

Proxy phải thực sự có hiệu lực (check IP Facebook Lite đang dùng)

Có thể reset, thay đổi proxy mỗi phiên chạy

Tự động hóa hoàn toàn bằng C# + ADB + Proxy API

🧱 HẠ TẦNG HIỆN CÓ
✅ LDPlayer 9 đã cài và có thể mở bằng Auto_LDPlayer.LDPlayer.Open(...)

✅ Tool C# (.NET WPF) đang quản lý OCR, ADB, Auto login, post bài...

✅ Danh sách proxy có dạng: ip:port:user:pass, lưu từ API

✅ Mỗi proxy cần gán cho 1–2 LDPlayer theo deviceName

❌ Không dùng xoay IP liên tục → yêu cầu ổn định, trust

🛠 CÁC GIẢI PHÁP ĐÃ KIỂM TRA
Giải pháp gán proxy	Hoạt động?	Ghi chú
✅ SocksDroid (Android app)	⚠️ Có hoạt động, nhưng bất tiện khi tự động hóa	
✅ ProxyDroid	❌ Không ổn định trong LDPlayer 9, app Facebook Lite bypass proxy	
✅ NapsternetV (VPN App)	✅ Ổn định, bypass được cả app Facebook Lite	
❌ ProxyCap / Proxifier	❌ Không tác dụng với app Android trong LDPlayer	
✅ Tinyproxy	✅ Dùng như proxy đầu cuối, log kiểm tra rất chính xác	

✅ CHIẾN LƯỢC GIẢI QUYẾT TỐT NHẤT
Chạy proxy server riêng bằng Tinyproxy trên local hoặc VPS

Có thể định nghĩa nhiều cổng, mỗi cổng là 1 proxy với user/pass khác nhau

Có thể log toàn bộ request để xác minh proxy được dùng

Dùng app VPN Android (NapsternetV) trong mỗi LDPlayer instance

Import file .npv4 cấu hình proxy tương ứng

Tự động hóa mở app và bật VPN bằng adb

Tự động hóa bằng C#

Gán proxy theo deviceName

Cài .apk, push cấu hình .npv4

Tự động bật VPN mỗi khi LDPlayer khởi động

Kiểm tra IP (nếu cần)

Truy cập IP test site bằng trình duyệt LDPlayer

Hoặc log trực tiếp từ Tinyproxy server

