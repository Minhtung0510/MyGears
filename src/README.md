# MyGears 1.3.0

Ứng dụng Windows WPF / .NET 8. Phần này chỉ chứa MyGears, không chứa USB boot, kho tài khoản cá nhân hoặc các bộ cài bên ngoài.

## Tính năng

- Kho tài khoản mã hóa, khóa PIN, nhập hàng loạt và sao chép có thời hạn.
- Quét tài khoản Valorant: thông tin Riot, email khi được cấp quyền, rank, cửa hàng, kho vật phẩm.
- Tìm skin trên các tài khoản đã quét; phân biệt dữ liệu thiếu, cũ và lỗi.
- Giao diện tối với menu, thanh cuộn và animation cho phần kiểm tra tài khoản.
- Đóng gói single-file; cập nhật bản cài có xác minh SHA-256 và giữ dữ liệu hiện có.

Dữ liệu Riot phụ thuộc quyền truy cập và thời điểm quét. Không lưu access token vào repository. CAPTCHA/MFA do người dùng thực hiện.

## Build (Windows, .NET 8 SDK)

Chạy tại thư mục gốc repository:

```powershell
dotnet restore src/MyGears/MyGears.csproj -r win-x64
dotnet build src/MyGears/MyGears.csproj -c Release -r win-x64 --no-restore
dotnet publish src/MyGears/MyGears.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src/artifacts/publish
```

## Kiểm tra

```powershell
dotnet run --project src/SkinImageRegression/SkinImageRegression.csproj
node src/SkinImageRegression/prefill-test.cjs
dotnet run --project src/ValorantUiCheck/ValorantUiCheck.csproj
```

Build Release win-x64 trước khi chạy kiểm tra WPF. Các kiểm tra dùng dữ liệu giả lập, không đăng nhập tài khoản thật. Ảnh kiểm tra sinh trong `src/ValorantUiCheck` và không được commit. Ảnh/metadata Prime Vandal trong fixture lấy từ danh mục công khai valorant-api.com để kiểm tra ảnh skin.

## Dữ liệu không đưa lên Git

`accounts.enc`, cấu hình runtime cá nhân, token, cache WebView, log, `bin`, `obj`, bản sao lưu EXE và toàn bộ USB boot. Hai file trong `MyGears/RuntimeDefaults` chỉ là cấu hình mặc định của ứng dụng.
