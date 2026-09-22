using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace MyGears.Core;

/// <summary>
/// Cung cấp các tính năng bảo mật cấp quân sự cho MyGears:
/// 1. Chống chụp trộm màn hình (WDA_EXCLUDEFROMCAPTURE)
/// 2. Mã hóa AES-256 dữ liệu tài khoản bằng mã PIN
/// 3. Tự động xóa sạch Clipboard (bộ nhớ tạm) sau 25 giây
/// 4. Tự hủy & dọn sạch 100% tàn dư trên máy tính (Zero-Trace)
/// </summary>
public static class SecurityService
{
    // ──────────────────────────────────────────────
    //  1. CHỐNG CHỤP TRỘM MÀN HÌNH
    // ──────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public static bool IsAntiCaptureActive { get; private set; } = false;

    /// <summary>
    /// Bật hoặc tắt chế độ chống chụp màn hình.
    /// enable = true: Cửa sổ thành khối đen khi chụp màn hình (chống phần mềm chụp lén).
    /// enable = false: Cho phép chụp màn hình bình thường.
    /// </summary>
    public static bool SetWindowAntiCapture(Window window, bool enable = true)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.EnsureHandle();
            if (hwnd == IntPtr.Zero) return false;

            if (enable)
            {
                // Thử WDA_EXCLUDEFROMCAPTURE (Win 10 version 2004+ và Win 11)
                bool ok = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                if (!ok)
                {
                    // Fallback cho Win 10 bản cũ hơn
                    ok = SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
                }
                IsAntiCaptureActive = ok;
                return ok;
            }
            else
            {
                // WDA_NONE: Cho phép chụp màn hình bình thường
                bool ok = SetWindowDisplayAffinity(hwnd, WDA_NONE);
                IsAntiCaptureActive = false;
                return ok;
            }
        }
        catch
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────
    //  2. MÃ HÓA AES-256 VỚI MÃ PIN CÁ NHÂN
    // ──────────────────────────────────────────────

    private const int KeySize = 256;
    private const int DerivationIterations = 100_000;

    /// <summary>
    /// Tạo mã băm an toàn từ mã PIN để kiểm tra tính hợp lệ
    /// </summary>
    public static (string Hash, string Salt) HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, DerivationIterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Kiểm tra mã PIN nhập vào có khớp với hash đã lưu hay không
    /// </summary>
    public static bool VerifyPin(string pin, string savedHash, string savedSalt)
    {
        try
        {
            var salt = Convert.FromBase64String(savedSalt);
            using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, DerivationIterations, HashAlgorithmName.SHA256);
            var testHash = pbkdf2.GetBytes(32);
            var originalHash = Convert.FromBase64String(savedHash);
            return CryptographicOperations.FixedTimeEquals(testHash, originalHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mã hóa chuỗi văn bản bằng AES-256 dựa trên mã PIN
    /// Cấu trúc: [16 bytes Salt] + [16 bytes IV] + [Ciphertext]
    /// </summary>
    public static string Encrypt(string plainText, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, DerivationIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(KeySize / 8);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        using var ms = new MemoryStream();
        ms.Write(salt, 0, salt.Length);
        ms.Write(iv, 0, iv.Length);
        ms.Write(cipherBytes, 0, cipherBytes.Length);

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Giải mã chuỗi AES-256 bằng mã PIN
    /// </summary>
    public static string Decrypt(string cipherTextBase64, string pin)
    {
        var allBytes = Convert.FromBase64String(cipherTextBase64);
        if (allBytes.Length < 32)
            throw new InvalidOperationException("Dữ liệu mã hóa không hợp lệ.");

        var salt = new byte[16];
        var iv = new byte[16];
        var cipherBytes = new byte[allBytes.Length - 32];

        Buffer.BlockCopy(allBytes, 0, salt, 0, 16);
        Buffer.BlockCopy(allBytes, 16, iv, 0, 16);
        Buffer.BlockCopy(allBytes, 32, cipherBytes, 0, cipherBytes.Length);

        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, DerivationIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(KeySize / 8);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    // ──────────────────────────────────────────────
    //  3. TỰ ĐỘNG XÓA BỘ NHỚ TẠM (CLIPBOARD AUTO-WIPE)
    // ──────────────────────────────────────────────

    private static CancellationTokenSource? _clipboardWipeCts;

    /// <summary>
    /// Đưa nội dung vào Clipboard với cơ chế thử lại nếu clipboard đang bị tiến trình khác chiếm quyền
    /// </summary>
    public static bool SafeSetClipboard(string text, int retries = 5, int delayMs = 50)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (COMException)
            {
                if (i == retries - 1) return false;
                Thread.Sleep(delayMs);
            }
            catch (ExternalException)
            {
                if (i == retries - 1) return false;
                Thread.Sleep(delayMs);
            }
            catch
            {
                if (i == retries - 1) return false;
                Thread.Sleep(delayMs);
            }
        }
        return false;
    }

    /// <summary>
    /// Copy nội dung (ví dụ tk:mk) vào Clipboard và bắt đầu đếm ngược tự xóa
    /// </summary>
    public static void CopyToClipboardWithAutoWipe(
        string textToCopy,
        int wipeAfterSeconds = 25,
        Action<int>? onTickSecondsRemaining = null,
        Action? onWiped = null)
    {
        try
        {
            // Đưa vào Clipboard an toàn với cơ chế retry chống xung đột
            SafeSetClipboard(textToCopy);

            // Hủy đếm ngược cũ nếu đang chạy
            _clipboardWipeCts?.Cancel();
            _clipboardWipeCts?.Dispose();
            _clipboardWipeCts = new CancellationTokenSource();

            var token = _clipboardWipeCts.Token;

            // Bắt đầu đếm ngược ngầm
            Task.Run(async () =>
            {
                for (int sec = wipeAfterSeconds; sec > 0; sec--)
                {
                    if (token.IsCancellationRequested) return;
                    onTickSecondsRemaining?.Invoke(sec);
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) return;

                // Xóa sạch bộ nhớ tạm
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Kiểm tra nếu clipboard vẫn là nội dung vừa copy thì xóa
                        if (Clipboard.ContainsText() && Clipboard.GetText() == textToCopy)
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch { }
                });

                onWiped?.Invoke();
            }, token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Security] Clipboard error: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    //  4. TỰ HỦY & DỌN DẸP SẠCH MÁY TÍNH (ZERO-TRACE)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Đóng ứng dụng, tắt driver chuột, xóa sạch thư mục trên C:\, xóa shortcut desktop,
    /// xóa cache WebView2 và xóa clipboard — không để lại bất kỳ tàn dư nào trên máy tính.
    /// </summary>
    public static void TriggerSelfDestruct(string deployedRootPath)
    {
        try
        {
            // Kiểm tra an toàn bắt buộc: chặn tuyệt đối rủi ro xóa nhầm thư mục gốc ổ đĩa
            if (string.IsNullOrWhiteSpace(deployedRootPath))
            {
                MessageBox.Show("Đường dẫn tự hủy không hợp lệ. Đã hủy thao tác để bảo vệ an toàn máy tính.", "MyGears Security", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fullPath = Path.GetFullPath(deployedRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootDrive = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Chặn tuyệt đối nếu đường dẫn là thư mục gốc của phân vùng (C:\, D:\, ...)
            if (string.Equals(fullPath, rootDrive, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("CẢNH BÁO AN TOÀN: Tuyệt đối không thể tự hủy trên thư mục gốc của ổ đĩa!", "MyGears Security", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Chặn nếu đường dẫn không chứa thư mục định danh MyGears hoặc quá ngắn
            if (fullPath.Length < 8 || !fullPath.Contains("MyGears", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("CẢNH BÁO AN TOÀN: Thư mục mục tiêu không thuộc phạm vi cài đặt của MyGears!", "MyGears Security", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 1. Xóa sạch Clipboard ngay lập tức
            try { Clipboard.Clear(); } catch { }

            // 2. Tắt các tiến trình con liên quan (ScyRox)
            try
            {
                foreach (var p in Process.GetProcessesByName("ScyRox"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            // 3. Xóa Desktop Shortcut
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var lnk = Path.Combine(desktop, "MyGears.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }

            // 4. Xóa thư mục WebView2 Cache trong %LOCALAPPDATA%
            try
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var webView2Dir = Path.Combine(localApp, "MyGears");
                if (Directory.Exists(webView2Dir))
                    Directory.Delete(webView2Dir, true);
            }
            catch { }

            // 5. Khởi chạy lệnh xóa thư mục cài đặt ngầm bằng CMD độc lập (sau khi MyGears tắt)
            // Lệnh: timeout 1 giây để app thoát hoàn toàn -> rmdir /s /q -> exit
            var cmdScript = $"/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{fullPath}\" & exit";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdScript,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            // 6. Tắt ngay MyGears
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tự hủy: {ex.Message}", "MyGears", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
