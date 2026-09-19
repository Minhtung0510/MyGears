using System.Diagnostics;
using System.IO;

namespace MyGears.Core;

/// <summary>
/// Quản lý việc sao chép/cài đặt MyGears vào máy tính
/// và đồng bộ ngược cấu hình về lại USB khi cần.
/// </summary>
public static class DeploymentService
{
    /// <summary>
    /// Sao chép các thành phần được người dùng tick chọn vào máy tính (C:\Users\Public\MyGears)
    /// với báo cáo tiến trình phần trăm chi tiết.
    /// </summary>
    public static async Task<bool> DeploySelectedComponentsAsync(
        IEnumerable<InstallableComponent> components,
        Action<string, double>? onProgress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                var targetRoot = UsbPathResolver.LocalDeployTargetDir;
                Directory.CreateDirectory(targetRoot);

                // Dọn dẹp/đóng các tiến trình đang mở từ thư mục đích để tránh lỗi file lock
                KillProcessesInDirectory(targetRoot);

                var selected = components.Where(c => c.IsSelected).ToList();

                // Tính tổng dung lượng cần copy để tính % tiến trình
                long totalBytes = selected
                    .Where(c => !string.IsNullOrEmpty(c.SourcePath) && Directory.Exists(c.SourcePath))
                    .Sum(c => DriverDiscoveryService.GetDirectorySize(c.SourcePath));

                if (totalBytes <= 0) totalBytes = 1;
                long copiedBytes = 0;

                onProgress?.Invoke("📁 Đang khởi tạo môi trường trên máy tính…", 0.05);

                // 1. Copy hoặc Tải từng component đã chọn
                foreach (var comp in selected)
                {
                    if (comp.Id == "desktop_shortcut") continue;

                    var dest = !string.IsNullOrEmpty(comp.DestinationPath)
                        ? comp.DestinationPath
                        : Path.Combine(targetRoot, "Download", comp.Id);

                    if (comp.Id == "core_app")
                    {
                        onProgress?.Invoke("⚙️ Đang cài đặt ứng dụng MyGears Core…", 0.08);
                        Directory.CreateDirectory(dest); // C:\Users\Public\MyGears\App

                        var targetExe = Path.Combine(dest, "MyGears.exe");
                        var currentExe = Environment.ProcessPath;

                        // Nếu USB có folder App/ chứa file và khác gốc USB -> Chép folder
                        if (!string.IsNullOrEmpty(comp.SourcePath) && Directory.Exists(comp.SourcePath) &&
                            !comp.SourcePath.TrimEnd('\\', '/').Equals(UsbPathResolver.UsbRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            CopyDirectoryWithProgress(comp.SourcePath, dest, overwrite: true, totalBytes, ref copiedBytes, onProgress);
                        }
                        // Nếu đang chạy dưới dạng 1 file .exe duy nhất trên USB
                        else if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                        {
                            try
                            {
                                File.Copy(currentExe, targetExe, overwrite: true);
                            }
                            catch (IOException) { }
                        }
                        else if (!string.IsNullOrEmpty(comp.CloudDownloadUrl))
                        {
                            CloudDownloadService.DownloadCloudAssetAsync(comp.CloudDownloadUrl, dest, onProgress).GetAwaiter().GetResult();
                        }

                        // Đồng bộ các file cấu hình quan trọng sang C:\Users\Public\MyGears\App
                        var usbRoot = UsbPathResolver.UsbRoot;
                        string[] configFiles = { "accounts.enc", "settings.json", "manifest.json" };
                        foreach (var cf in configFiles)
                        {
                            var srcConfig = Path.Combine(usbRoot, cf);
                            if (!File.Exists(srcConfig) && Directory.Exists(Path.Combine(usbRoot, "App")))
                                srcConfig = Path.Combine(usbRoot, "App", cf);

                            var destConfig = Path.Combine(dest, cf);
                            if (File.Exists(srcConfig))
                            {
                                try { File.Copy(srcConfig, destConfig, overwrite: true); } catch { }
                            }
                        }

                        continue;
                    }

                    // TH1: Có sẵn thư mục offline trên USB -> Chép gia tăng
                    if (!string.IsNullOrEmpty(comp.SourcePath) && Directory.Exists(comp.SourcePath) &&
                        !comp.SourcePath.TrimEnd('\\', '/').Equals(UsbPathResolver.UsbRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(dest);
                        CopyDirectoryWithProgress(comp.SourcePath, dest, overwrite: true, totalBytes, ref copiedBytes, onProgress);
                    }
                    // TH2: Không có trên USB nhưng có link Cloud trên GitHub -> Tải và xả nén trực tiếp
                    else if (!string.IsNullOrEmpty(comp.CloudDownloadUrl))
                    {
                        onProgress?.Invoke($"🌐 Đang tải {comp.Name} từ GitHub…", 0.15);
                        bool ok = CloudDownloadService.DownloadCloudAssetAsync(comp.CloudDownloadUrl, dest, onProgress).GetAwaiter().GetResult();
                        if (!ok)
                        {
                            throw new Exception($"Không thể tải {comp.Name} từ GitHub. Vui lòng kiểm tra kết nối mạng Internet.");
                        }
                    }
                }

                // 2. Tạo Desktop Shortcut nếu được chọn
                if (selected.Any(c => c.Id == "desktop_shortcut"))
                {
                    onProgress?.Invoke("🔗 Đang tạo phím tắt trên màn hình Desktop…", 0.95);
                    var targetAppDir = Path.Combine(targetRoot, "App");
                    var targetExe = Path.Combine(targetAppDir, "MyGears.exe");
                    CreateDesktopShortcut(targetExe);
                }

                // 3. Khởi chạy MyGears từ máy tính
                var localAppExe = Path.Combine(targetRoot, "App", "MyGears.exe");
                if (File.Exists(localAppExe))
                {
                    onProgress?.Invoke("🚀 Đang khởi động MyGears từ máy tính…", 1.0);
                    var psi = new ProcessStartInfo
                    {
                        FileName = localAppExe,
                        Arguments = "--from-usb-deploy",
                        WorkingDirectory = Path.Combine(targetRoot, "App"),
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }

                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"❌ Lỗi sao chép: {ex.Message}", 0.0);
                return false;
            }
        });
    }

    /// <summary>
    /// Sao chép toàn bộ MyGears và các driver mặc định vào máy tính.
    /// </summary>
    public static async Task<bool> DeployToLocalPcAsync(Action<string>? onProgress = null)
    {
        var components = DriverDiscoveryService.DiscoverComponents();
        return await DeploySelectedComponentsAsync(components, (msg, _) => onProgress?.Invoke(msg));
    }

    /// <summary>
    /// Đồng bộ file settings.json và các cấu hình mới nhất từ máy tính về lại USB
    /// </summary>
    public static (bool Success, string Message) SyncSettingsBackToUsb()
    {
        try
        {
            var usbRoot = UsbPathResolver.FindConnectedUsbRoot();
            if (string.IsNullOrEmpty(usbRoot))
            {
                return (false, "Không tìm thấy USB MyGears được cắm vào máy. Vui lòng cắm lại USB rồi thử lại.");
            }

            var destAppDir = Path.Combine(usbRoot, "App");
            if (!Directory.Exists(destAppDir))
                Directory.CreateDirectory(destAppDir);

            // Copy settings.json
            var localSettings = UsbPathResolver.SettingsFile;
            if (File.Exists(localSettings))
            {
                var destSettings = Path.Combine(destAppDir, "settings.json");
                File.Copy(localSettings, destSettings, overwrite: true);
            }

            return (true, $"Đã lưu toàn bộ cấu hình vào USB ({usbRoot}) thành công!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi lưu cấu hình về USB: {ex.Message}");
        }
    }

    private static void CopyDirectoryWithProgress(
        string sourceDir,
        string destinationDir,
        bool overwrite,
        long totalBytes,
        ref long totalCopiedBytes,
        Action<string, double>? onProgress)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);

            // KIỂM TRA TỒN TẠI TRƯỚC: Nếu file đích đã có sẵn và kích thước khớp -> Bỏ qua không chép lại
            if (File.Exists(targetFilePath))
            {
                try
                {
                    var destFi = new FileInfo(targetFilePath);
                    if (destFi.Length == file.Length && destFi.LastWriteTimeUtc >= file.LastWriteTimeUtc.AddSeconds(-2))
                    {
                        totalCopiedBytes += file.Length;
                        continue;
                    }
                }
                catch { }
            }

            onProgress?.Invoke($"Đang chép {file.Name}…", Math.Min(0.98, (double)totalCopiedBytes / totalBytes));
            try
            {
                file.CopyTo(targetFilePath, overwrite);
            }
            catch (IOException)
            {
                // Nếu file đang bị khóa nhưng đã tồn tại trên máy tính, tiếp tục tiến trình
                if (!File.Exists(targetFilePath)) throw;
            }
            totalCopiedBytes += file.Length;
            onProgress?.Invoke($"Đã chép {file.Name}…", Math.Min(0.98, (double)totalCopiedBytes / totalBytes));
        }

        foreach (var subDir in dir.GetDirectories())
        {
            // Bỏ qua thư mục cache .webview2 nếu có
            if (subDir.Name.Equals(".webview2", StringComparison.OrdinalIgnoreCase))
                continue;

            var newDestSubDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectoryWithProgress(subDir.FullName, newDestSubDir, overwrite, totalBytes, ref totalCopiedBytes, onProgress);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);

            // Kiểm tra tồn tại: bỏ qua nếu đã có file trùng kích thước và thời gian
            if (File.Exists(targetFilePath))
            {
                try
                {
                    var destFi = new FileInfo(targetFilePath);
                    if (destFi.Length == file.Length && destFi.LastWriteTimeUtc >= file.LastWriteTimeUtc.AddSeconds(-2))
                    {
                        continue;
                    }
                }
                catch { }
            }

            try
            {
                file.CopyTo(targetFilePath, overwrite);
            }
            catch (IOException)
            {
                if (!File.Exists(targetFilePath)) throw;
            }
        }

        foreach (var subDir in dir.GetDirectories())
        {
            // Bỏ qua thư mục cache .webview2 nếu có
            if (subDir.Name.Equals(".webview2", StringComparison.OrdinalIgnoreCase))
                continue;

            var newDestSubDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestSubDir, overwrite);
        }
    }

    private static void KillProcessesInDirectory(string targetRoot)
    {
        try
        {
            var targetRootLower = targetRoot.TrimEnd('\\', '/').ToLowerInvariant();
            var currentPid = Process.GetCurrentProcess().Id;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == currentPid) continue;

                    string? mainModulePath = null;
                    try { mainModulePath = proc.MainModule?.FileName; } catch { }

                    if (!string.IsNullOrEmpty(mainModulePath) &&
                        mainModulePath.ToLowerInvariant().StartsWith(targetRootLower))
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void CreateDesktopShortcut(string targetExePath)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, "MyGears.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetExePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetExePath);
                shortcut.Description = "MyGears - Portable Gaming Gear Manager";
                shortcut.Save();
            }
        }
        catch { }
    }
}
