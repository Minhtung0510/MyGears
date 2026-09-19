# ⚙️ MyGears — Portable Gaming Gear Hub & Security Vault

**MyGears** là bộ công cụ quản lý thiết bị chơi game (Chuột, Bàn phím) và bảo mật tài khoản di động dành cho game thủ, được thiết kế tối ưu hóa đặc biệt để mang theo trên USB hoặc chạy trực tiếp từ đám mây (Cloud-Powered).

---

## 🚀 Tính Năng Nổi Bật

1. **🖱️ Module Chuột (ScyRox V6 & Đa Hãng):**
   - Tự động khởi chạy và nhúng trực tiếp giao diện điều khiển ScyRox V6 vào ứng dụng bằng Win32 SetParent.
   - Hỗ trợ đầy đủ tính năng tinh chỉnh Polling rate 8K, DPI, LOD, Macro.
   - Tự động phát hiện driver có sẵn trên máy để bỏ qua không cài lại.

2. **⌨️ Module Bàn Phím (FL-Esports / Nano 68 App Pro):**
   - Tích hợp WebView2 nhúng trực tiếp trang cấu hình `hub.fgg.com.cn`.
   - Tự động cấp quyền WebHID / WebUSB để nhận diện bàn phím ngay lập tức.
   - Cơ chế tự động thử lại khi kết nối mạng quốc tế bị nghẽn.

3. **🔐 Két Sắt Tài Khoản Cấp Quân Sự (Security Vault):**
   - Mã hóa tài khoản & mật khẩu với chuẩn **AES-256 GCM** và khóa dẫn xuất PBKDF2 (100,000 vòng lặp).
   - **Chống chụp trộm màn hình (`WDA_MONITOR`)**: Tự biến thành khối đen tuyền khi có phần mềm bên ngoài cố ý quay lén / chụp trộm.
   - **Tự động xóa Clipboard**: Bộ nhớ tạm tự xóa sạch mật khẩu sau 30 giây.
   - **Cơ chế tự hủy Zero-Trace**: Dọn sạch 100% tàn dư trên máy khi rời đi.

4. **☁️ Cloud Driver Hub & Cài Đặt Tự Động:**
   - Bộ cài độc lập `Cài_Đặt_MyGears.exe` siêu nhẹ.
   - Tự động tải driver chuột và runtime từ GitHub Releases tốc độ cao.
   - Sao chép gia tăng thông minh không ghi đè dữ liệu trùng lặp.

---

## 🛠️ Công Nghệ Sử Dụng

- **Ngôn ngữ & Nền tảng**: C# 12, .NET 8.0 Windows Desktop (WPF).
- **Kiến trúc**: MVVM với CommunityToolkit.Mvvm.
- **Thư viện nhúng Web**: Microsoft.Web.WebView2.
- **Giao diện**: Custom Dark Gaming Theme theo phong cách Sleek Carbon & Amber Glow.

---

## 📦 Bản Quyền & Tác Giả

- Dự án được phát triển bởi **Minhtung0510**.
