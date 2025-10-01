# Dedicated Server Deployment Guide

## ServerLauncher Overview
`ServerLauncher` chịu trách nhiệm đọc các tham số dòng lệnh để cấu hình server Unity Fusion khi khởi động.

- `--roomName`: được đọc thông qua phương thức `GetArg` để đặt `SessionName` cho server. Nếu tham số không được truyền, giá trị mặc định sẽ là `DefaultRoom`.
- `--port`: được kiểm tra và chuyển đổi sang `ushort`. Nếu hợp lệ, port sẽ được sử dụng khi tạo địa chỉ bind `0.0.0.0:<port>` cho `NetworkRunner`.
- Các log quan trọng trong quá trình khởi tạo:
  - `🟢 Đang chạy Start() - chuẩn bị StartGame`: xác nhận vòng đời `Start()` đã bắt đầu.
  - `🔌 Port sử dụng: ...` và `🏷️ SessionName: ...`: hiển thị cấu hình thực tế nhận từ tham số.
  - `🧪 Khởi tạo StartGame với địa chỉ: 0.0.0.0:<port>`: thông báo địa chỉ bind mong muốn trước khi khởi động Fusion.
  - `📡 Requested bind address: 0.0.0.0:<port>`: xác nhận thông tin bind sau khi gọi `StartGame`.
  - `✅ Fusion Server đã khởi động...` hoặc `❌ StartGame failed...`: phản hồi trạng thái cuối cùng của tiến trình khởi động.
  - `✅ Spawned NetworkManager with RPC`: cho biết prefab `networkManagerPrefab` đã được spawn thành công khi server sẵn sàng.

## Quy trình chạy build Linux trên VPS
1. Truy cập vào thư mục chứa build:
   ```bash
   cd /home/deploy/server
   ```
2. Khởi động server ở chế độ headless:
   ```bash
   ./BanCuLiServer.x86_64 -batchmode -nographics -dedicatedServer 1
   ```

## Lưu ý khi triển khai build mới
Trước khi chạy build vừa upload, đảm bảo gán quyền thực thi cho binary:
```bash
chmod +x BanCuLiServer.x86_64
```
