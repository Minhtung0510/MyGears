@echo off
chcp 65001 > nul
echo ===================================================
echo   MyGears - Build & Publish sang d:\App
echo ===================================================

where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Không tìm thấy dotnet SDK trên máy này.
    echo Vui lòng cài đặt .NET 8.0 SDK để biên dịch:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo [1/2] Đang biên dịch và xuất bản MyGears (Single-File Self-Contained)...
dotnet publish "%~dp0MyGears\MyGears.csproj" -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o "%~dp0..\App"

if %ERRORLEVEL% equ 0 (
    echo.
    echo [2/2] XUẤT BẢN THÀNH CÔNG!
    echo File MyGears.exe đã được cập nhật vào d:\App\MyGears.exe
) else (
    echo.
    echo [ERROR] Có lỗi xảy ra trong quá trình biên dịch!
)

pause
