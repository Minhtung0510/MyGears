using System.Diagnostics;
using System.IO;
using MyGears.Core;

namespace MyGears.Dependency;

/// <summary>
/// Cài đặt silent dependency từ thư mục Download/ trên USB.
/// Không cần mạng, không cần bấm Next/Next.
/// </summary>
public static class DependencyInstaller
{
    /// <summary>
    /// Cài đặt một dependency từ installer trong Download/.
    /// Báo cáo tiến trình qua progress callback.
    /// </summary>
    /// <param name="entry">Dependency cần cài</param>
    /// <param name="onProgress">Callback log tiến trình (thread-safe)</param>
    /// <returns>true nếu thành công</returns>
    public static async Task<bool> InstallAsync(
        DependencyEntry entry,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        // Ghép đường dẫn installer từ USB root (không hardcode ổ đĩa)
        var installerPath = UsbPathResolver.GetDownloadPath(entry.InstallerSubPath
            .Split('\\', '/'));

        onProgress?.Invoke($"🔍 Tìm installer: {installerPath}");

        if (!File.Exists(installerPath))
        {
            var msg = $"❌ Không tìm thấy installer cho [{entry.DisplayName}]:\n   {installerPath}";
            onProgress?.Invoke(msg);

            if (entry.Optional)
            {
                onProgress?.Invoke($"⚠️ [{entry.DisplayName}] là optional — bỏ qua.");
                return true; // Bỏ qua nếu optional
            }
            return false;
        }

        onProgress?.Invoke($"⏳ Đang cài: {entry.DisplayName} ...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = entry.InstallerArgs,
                UseShellExecute = true,  // Cần để chạy với quyền admin (từ manifest)
                Verb = "runas",          // Đảm bảo elevation nếu chưa được
                CreateNoWindow = false,  // Một số installer cần window
                WindowStyle = ProcessWindowStyle.Minimized,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Không thể khởi động installer.");

            await process.WaitForExitAsync(ct);

            var exitCode = process.ExitCode;

            // Exit code 0 hoặc 3010 (cần restart) đều coi là thành công
            if (exitCode == 0 || exitCode == 3010)
            {
                onProgress?.Invoke($"✅ Cài xong: {entry.DisplayName}" +
                    (exitCode == 3010 ? " (cần khởi động lại máy để hoàn tất)" : ""));
                return true;
            }
            else
            {
                onProgress?.Invoke($"❌ Cài thất bại [{entry.DisplayName}] — Exit code: {exitCode}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            onProgress?.Invoke($"⚠️ Đã hủy cài: {entry.DisplayName}");
            return false;
        }
        catch (Exception ex)
        {
            onProgress?.Invoke($"❌ Lỗi khi cài [{entry.DisplayName}]: {ex.Message}");
            return false;
        }
    }
}
