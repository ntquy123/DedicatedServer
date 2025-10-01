# Dedicated Server Deployment Guide

## Giới thiệu
Dự án **Dedicated Server** cung cấp build headless cho trò chơi sử dụng Unity Fusion. Mục tiêu của dự án là giúp đội vận hành dễ dàng cấu hình, triển khai và mở rộng các phiên bản server độc lập để phục vụ nhiều phiên chơi cùng lúc.

## Chuẩn bị môi trường
1. **Máy chủ Linux (khuyến nghị Ubuntu 20.04+)** với quyền sudo.
2. Cài đặt các gói hệ thống cần thiết:
   ```bash
   sudo apt update && sudo apt install -y libstdc++6 libgcc1 unzip
   ```
3. Cấp quyền thực thi cho binary sau khi giải nén bản build Unity:
   ```bash
   chmod +x BanCuLiServer.x86_64
   ```
4. (Tùy chọn khi build lại) Cài đặt Unity Editor có module **Linux Dedicated Server Build Support** và đăng nhập tài khoản cấp quyền.

## Build server (tùy chọn cho đội phát triển)
1. Mở dự án trong Unity Editor với phiên bản đã được kiểm thử nội bộ.
2. Chạy script build hoặc sử dụng `File > Build Settings > Dedicated Server` để xuất binary cho Linux.
3. Đóng gói thành file `.zip` hoặc `.tar.gz` để phân phối.

## Triển khai và vận hành
1. **Tải bản build lên máy chủ** và giải nén vào thư mục làm việc:
   ```bash
   mkdir -p /home/deploy/server && cd /home/deploy/server
   unzip BanCuLiServer.zip
   ```
2. **Khởi động server ở chế độ headless**:
   ```bash
   ./BanCuLiServer.x86_64 -batchmode -nographics -dedicatedServer 1 --roomName DefaultRoom --port 7777
   ```

### Tham số dòng lệnh quan trọng
- `--roomName <Tên phòng>`: đặt tên phiên làm việc (mặc định `DefaultRoom`).
- `--port <Số cổng>`: cổng lắng nghe của server (phải là số hợp lệ, ví dụ `7777`).
- `-batchmode -nographics`: yêu cầu Unity chạy không có giao diện.
- `-dedicatedServer 1`: bật chế độ dedicated server để tối ưu tài nguyên.

## Giám sát và cập nhật
- Kiểm tra log bằng `journalctl` hoặc chuyển hướng stdout sang file để theo dõi trạng thái server.
- Khi cập nhật build mới, dừng tiến trình hiện tại, sao lưu dữ liệu cấu hình và thay thế binary rồi khởi động lại với cùng tham số.

## Liên hệ & tài liệu tham khảo
- **Kênh hỗ trợ vận hành**: gửi email tới `ops@example.com` hoặc liên hệ nhóm `#dedicated-server` trên Slack nội bộ.
- **Tài liệu tham khảo bổ sung**: xem `ServerLauncher.cs` trong thư mục `Assets` để hiểu thêm về cách parse tham số và khởi tạo `NetworkRunner`.
