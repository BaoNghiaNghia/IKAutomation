# Xác thực đổi vị trí cùng lãnh thổ

## Điều kiện

- LDPlayer ở độ phân giải 1280x720, giao diện game tiếng Việt.
- Bật `WorldMapNavigation.RequireVerifiedSameTerritory=true` và giữ
  `WorldMapNavigation.AllowLegacyTerritoryFallback=false`.
- Chỉ chạy một thiết bị cho lần xác thực đầu tiên.

## Quy trình

1. Mở bản đồ thế giới và chạy một chu kỳ farm trên một thiết bị.
2. Theo dõi Tiến trình farm: màu nhà, ứng viên X/Y, kết quả màu đích và trạng
   thái rollback phải được ghi rõ.
3. Nếu màu không đủ tin cậy hoặc khác màu, thiết bị phải khôi phục đúng X/Y
   gốc và không được gửi tap di chuyển cuối cùng.
4. Sau khi một thiết bị ổn định, thử lần lượt 6, 12 rồi 25 thiết bị; không
   thay đổi ngưỡng màu chỉ vì tỉ lệ `Unknown` tăng.

## Tiêu chí dừng

Dừng phiên farm và kiểm tra chẩn đoán khi có bất kỳ điều kiện nào: di chuyển
không xác minh, rollback không xác minh, hoặc tap di chuyển cuối cùng khi màu
nhà/đích không cùng nhóm. Các chỉ số cần theo dõi là số ứng viên đã thử, tỉ lệ
thành công cùng lãnh thổ, tỉ lệ `Unknown`, số rollback lỗi và số di chuyển
không an toàn; bốn chỉ số lỗi phải bằng 0.
