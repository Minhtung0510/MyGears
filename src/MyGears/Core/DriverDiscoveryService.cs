using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace MyGears.Core;

/// <summary>
/// Đại diện cho 1 thành phần hoặc driver có thể tick chọn để cài đặt vào máy tính.
/// </summary>
public partial class InstallableComponent : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string StatusBadge { get; set; } = string.Empty;
    public bool IsAlreadyInstalled { get; set; }
    public string? CloudDownloadUrl { get; set; }
    public bool IsFromCloud => !string.IsNullOrEmpty(CloudDownloadUrl);

    [ObservableProperty] private bool _isSelected = true;
}

/// <summary>
/// Quét động toàn bộ các driver và công cụ có trong USB để đưa vào màn hình Setup.
/// </summary>
public static class DriverDiscoveryService
{
    public static List<InstallableComponent> DiscoverComponents()
    {
        var list = new List<InstallableComponent>();
        var usbRoot = UsbPathResolver.UsbRoot;
        if (string.IsNullOrEmpty(usbRoot))
        {
            usbRoot = UsbPathResolver.FindConnectedUsbRoot() ?? AppContext.BaseDirectory;
        }
        var targetRoot = UsbPathResolver.LocalDeployTargetDir;

        // 1. Core Application (Luôn bắt buộc)
        var appDir = Path.Combine(usbRoot, "App");
        if (!Directory.Exists(appDir))
        {
            appDir = AppContext.BaseDirectory;
        }
        long appSizeBytes = GetDirectorySize(appDir);
        bool appExistsOnPc = Directory.Exists(Path.Combine(targetRoot, "App")) &&
                             File.Exists(Path.Combine(targetRoot, "App", "MyGears.exe"));

        list.Add(new InstallableComponent
        {
            Id = "core_app",
            Name = "Ứng dụng MyGears Core (Bắt buộc)",
            Description = appExistsOnPc
                ? "Ứng dụng chính (Đã có trên máy tính — tự động đồng bộ file mới nếu có thay đổi)"
                : "Ứng dụng chính, giao diện điều khiển, kho tài khoản mã hóa AES-256",
            Icon = "⚙️",
            SourcePath = appDir,
            DestinationPath = Path.Combine(targetRoot, "App"),
            SizeBytes = appSizeBytes,
            SizeDisplay = FormatSize(appSizeBytes),
            IsRequired = true,
            IsSelected = true,
            IsAlreadyInstalled = appExistsOnPc,
            StatusBadge = appExistsOnPc ? "✅ Đã có trên máy" : "✨ Bắt buộc"
        });

        // 2. Quét động toàn bộ thư mục trong Download/
        var downloadDir = Path.Combine(usbRoot, "Download");
        if (Directory.Exists(downloadDir))
        {
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            foreach (var subDir in Directory.GetDirectories(downloadDir))
            {
                var folderName = Path.GetFileName(subDir);
                long dirSize = GetDirectorySize(subDir);
                var targetSubDir = Path.Combine(targetRoot, "Download", folderName);

                // Kiểm tra xem đã có thư mục và file trong C:\Users\Public\MyGears\Download\<folderName> chưa
                bool existsInTarget = Directory.Exists(targetSubDir) && Directory.EnumerateFileSystemEntries(targetSubDir).Any();
                bool alreadyExists = existsInTarget;

                string name;
                string desc;
                string icon;

                var lower = folderName.ToLowerInvariant();
                if (lower.Contains("scyrox"))
                {
                    icon = "🖱️";
                    name = "Driver Chuột ScyRox V6";
                    desc = "Phần mềm tùy chỉnh DPI, Polling rate 8K, LOD và gán nút chuột ScyRox V6";

                    // Kiểm tra xem ScyRox đã có file thực thi trên máy hoặc đã cài trong Program Files chưa
                    if (File.Exists(Path.Combine(targetSubDir, "ScyRox.exe")) ||
                        File.Exists(Path.Combine(progFiles, "ScyRox", "Sys64", "ScyRox.exe")) ||
                        File.Exists(Path.Combine(progFiles, "ScyRox", "Sys32", "ScyRox.exe")) ||
                        Directory.Exists(Path.Combine(progFiles, "ScyRox")) ||
                        Directory.Exists(Path.Combine(progFilesX86, "ScyRox")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("webview"))
                {
                    icon = "🌐";
                    name = "Microsoft WebView2 Runtime";
                    desc = "Bộ cài runtime Microsoft để hiển thị giao diện bàn phím hub.fgg.com.cn";

                    // Kiểm tra xem máy đã cài WebView2 Runtime sẵn chưa
                    try
                    {
                        var wvVer = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                        if (!string.IsNullOrEmpty(wvVer))
                        {
                            alreadyExists = true;
                        }
                    }
                    catch
                    {
                        // Chưa cài WebView2
                    }
                }
                else if (lower.Contains("razer"))
                {
                    icon = "🐍";
                    name = $"Driver Razer ({folderName})";
                    desc = "Phần mềm điều khiển thiết bị Razer Gaming / Synapse";
                    if (Directory.Exists(Path.Combine(progFiles, "Razer")) || Directory.Exists(Path.Combine(progFilesX86, "Razer")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("logitech") || lower.Contains("ghub") || lower.Contains("g_hub"))
                {
                    icon = "🎮";
                    name = $"Driver Logitech G ({folderName})";
                    desc = "Phần mềm tùy chỉnh gear Logitech G HUB";
                    if (Directory.Exists(Path.Combine(progFiles, "LGHUB")) || Directory.Exists(Path.Combine(progFilesX86, "LGHUB")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("woot"))
                {
                    icon = "⌨️";
                    name = $"Driver Bàn Phím Wooting ({folderName})";
                    desc = "Tiện ích chỉnh Rapid Trigger, Tachyon Mode và Analog Switch";
                    if (Directory.Exists(Path.Combine(progFiles, "Wooting")) || Directory.Exists(Path.Combine(progFilesX86, "Wooting")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("atk") || lower.Contains("vxe") || lower.Contains("vgn"))
                {
                    icon = "⚡";
                    name = $"Driver ATK / VXE Hub ({folderName})";
                    desc = "Phần mềm điều khiển chuột / bàn phím ATK & VXE";
                    if (Directory.Exists(Path.Combine(progFiles, folderName)) || Directory.Exists(Path.Combine(progFilesX86, folderName)))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("lamzu"))
                {
                    icon = "🐾";
                    name = $"Driver Chuột Lamzu ({folderName})";
                    desc = "Phần mềm tùy chỉnh chuột Lamzu Gaming";
                    if (Directory.Exists(Path.Combine(progFiles, "LAMZU")) || Directory.Exists(Path.Combine(progFilesX86, "LAMZU")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("pulsar"))
                {
                    icon = "💫";
                    name = $"Driver Gear Pulsar ({folderName})";
                    desc = "Phần mềm Pulsar Fusion / chuột bàn phím Pulsar";
                    if (Directory.Exists(Path.Combine(progFiles, "Pulsar")) || Directory.Exists(Path.Combine(progFilesX86, "Pulsar")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("ninjutso") || lower.Contains("sora"))
                {
                    icon = "🥷";
                    name = $"Driver Chuột Ninjutso ({folderName})";
                    desc = "Phần mềm tùy chỉnh chuột Ninjutso Sora";
                    if (Directory.Exists(Path.Combine(progFiles, "Ninjutso")) || Directory.Exists(Path.Combine(progFilesX86, "Ninjutso")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("darmoshark"))
                {
                    icon = "🦈";
                    name = $"Driver Darmoshark ({folderName})";
                    desc = "Phần mềm tùy chỉnh chuột / phím Darmoshark";
                    if (Directory.Exists(Path.Combine(progFiles, "Darmoshark")) || Directory.Exists(Path.Combine(progFilesX86, "Darmoshark")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("steelseries") || lower.Contains("steel"))
                {
                    icon = "🎯";
                    name = $"Driver SteelSeries ({folderName})";
                    desc = "Phần mềm SteelSeries GG / Engine";
                    if (Directory.Exists(Path.Combine(progFiles, "SteelSeries")) || Directory.Exists(Path.Combine(progFilesX86, "SteelSeries")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("corsair") || lower.Contains("icue"))
                {
                    icon = "⛵";
                    name = $"Driver Corsair ({folderName})";
                    desc = "Phần mềm Corsair iCUE Gaming";
                    if (Directory.Exists(Path.Combine(progFiles, "Corsair")) || Directory.Exists(Path.Combine(progFilesX86, "Corsair")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("zowie"))
                {
                    icon = "🔴";
                    name = $"Driver Zowie ({folderName})";
                    desc = "Công cụ điều khiển màn hình / chuột Zowie";
                    if (Directory.Exists(Path.Combine(progFiles, "ZOWIE")) || Directory.Exists(Path.Combine(progFilesX86, "ZOWIE")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("asus") || lower.Contains("rog") || lower.Contains("armoury"))
                {
                    icon = "👁️";
                    name = $"Driver ASUS ROG ({folderName})";
                    desc = "Tiện ích cấu hình ASUS ROG Gear";
                    if (Directory.Exists(Path.Combine(progFiles, "ASUS")) || Directory.Exists(Path.Combine(progFilesX86, "ASUS")))
                    {
                        alreadyExists = true;
                    }
                }
                else if (lower.Contains("fgg") || lower.Contains("fl_esport") || lower.Contains("flesport"))
                {
                    icon = "⌨️";
                    name = $"Driver FL-Esports ({folderName})";
                    desc = "Phần mềm tùy chỉnh bàn phím FL-Esports";
                    if (Directory.Exists(Path.Combine(progFiles, "FL-ESPORTS")) || Directory.Exists(Path.Combine(progFilesX86, "FL-ESPORTS")))
                    {
                        alreadyExists = true;
                    }
                }
                else
                {
                    icon = "📦";
                    name = $"Driver / Công cụ: {folderName}";
                    desc = $"Gói driver hoặc tiện ích trong thư mục Download/{folderName}";
                    if (Directory.Exists(Path.Combine(progFiles, folderName)) || Directory.Exists(Path.Combine(progFilesX86, folderName)))
                    {
                        alreadyExists = true;
                    }
                }

                if (alreadyExists)
                {
                    desc += " (Đã có sẵn trên máy tính, mặc định bỏ qua không cài lại)";
                }

                var cloudUrl = CloudDownloadService.GetCloudUrlForComponent($"driver_{lower}", folderName);

                list.Add(new InstallableComponent
                {
                    Id = $"driver_{lower}",
                    Name = name,
                    Description = desc,
                    Icon = icon,
                    SourcePath = subDir,
                    DestinationPath = targetSubDir,
                    CloudDownloadUrl = cloudUrl,
                    SizeBytes = dirSize,
                    SizeDisplay = FormatSize(dirSize),
                    IsRequired = false,
                    IsSelected = !alreadyExists, // ĐÃ CÓ TRÊN MÁY THÌ BỎ CHỌN, KHÔNG CÀI LẠI
                    IsAlreadyInstalled = alreadyExists,
                    StatusBadge = alreadyExists ? "✅ Đã có trên máy (Bỏ qua)" : "✨ Cài mới"
                });
            }
        }

        // Đảm bảo luôn có các driver thiết yếu từ GitHub Releases nếu USB đã lược bỏ thư mục Download/
        var progFilesCommon = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var progFilesX86Common = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!list.Any(c => c.Id.Contains("scyrox")))
        {
            var targetScyRox = Path.Combine(targetRoot, "Download", "ScyRox");
            bool scyroxExists = File.Exists(Path.Combine(targetScyRox, "ScyRox.exe")) ||
                                File.Exists(Path.Combine(progFilesCommon, "ScyRox", "Sys64", "ScyRox.exe")) ||
                                File.Exists(Path.Combine(progFilesCommon, "ScyRox", "Sys32", "ScyRox.exe")) ||
                                Directory.Exists(Path.Combine(progFilesCommon, "ScyRox")) ||
                                Directory.Exists(Path.Combine(progFilesX86Common, "ScyRox"));

            list.Add(new InstallableComponent
            {
                Id = "driver_scyrox",
                Name = "Driver Chuột ScyRox V6 (GitHub Cloud)",
                Description = scyroxExists
                    ? "Phần mềm tùy chỉnh DPI, Polling rate 8K, LOD (Đã có sẵn trên máy tính, mặc định bỏ qua)"
                    : "Phần mềm tùy chỉnh DPI, Polling rate 8K, LOD và gán nút chuột ScyRox V6 (Tự động tải từ GitHub)",
                Icon = "🖱️",
                SourcePath = string.Empty,
                DestinationPath = targetScyRox,
                CloudDownloadUrl = CloudDownloadService.KnownDownloadUrls["scyrox"],
                SizeBytes = 31 * 1024 * 1024,
                SizeDisplay = "31.0 MB (Cloud)",
                IsRequired = false,
                IsSelected = !scyroxExists,
                IsAlreadyInstalled = scyroxExists,
                StatusBadge = scyroxExists ? "✅ Đã có trên máy (Bỏ qua)" : "☁️ Tải từ GitHub"
            });
        }

        if (!list.Any(c => c.Id.Contains("webview")))
        {
            var targetWv = Path.Combine(targetRoot, "Download", "WebView2Runtime");
            bool wvExists = false;
            try
            {
                var wvVer = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (!string.IsNullOrEmpty(wvVer)) wvExists = true;
            }
            catch { }

            list.Add(new InstallableComponent
            {
                Id = "driver_webview2runtime",
                Name = "Microsoft WebView2 Runtime (GitHub Cloud)",
                Description = wvExists
                    ? "Bộ cài runtime Microsoft (Đã có sẵn trên máy tính, mặc định bỏ qua)"
                    : "Bộ cài runtime Microsoft để hiển thị giao diện bàn phím hub.fgg.com.cn (Tự động tải từ GitHub)",
                Icon = "🌐",
                SourcePath = string.Empty,
                DestinationPath = targetWv,
                CloudDownloadUrl = CloudDownloadService.KnownDownloadUrls["webview2runtime"],
                SizeBytes = 1800 * 1024,
                SizeDisplay = "1.8 MB (Cloud)",
                IsRequired = false,
                IsSelected = !wvExists,
                IsAlreadyInstalled = wvExists,
                StatusBadge = wvExists ? "✅ Đã có trên máy (Bỏ qua)" : "☁️ Tải từ GitHub"
            });
        }

        // 3. Desktop Shortcut
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "MyGears.lnk");
        bool shortcutExists = File.Exists(shortcutPath);

        list.Add(new InstallableComponent
        {
            Id = "desktop_shortcut",
            Name = "Tạo phím tắt ngoài màn hình Desktop",
            Description = shortcutExists
                ? "Biểu tượng MyGears đã có sẵn ngoài màn hình Desktop (mặc định bỏ qua)"
                : "Tạo biểu tượng MyGears trên màn hình chính để mở lại nhanh chóng",
            Icon = "🔗",
            SourcePath = string.Empty,
            DestinationPath = string.Empty,
            SizeBytes = 1024,
            SizeDisplay = "< 1 KB",
            IsRequired = false,
            IsSelected = !shortcutExists, // Đã có phím tắt thì bỏ qua
            IsAlreadyInstalled = shortcutExists,
            StatusBadge = shortcutExists ? "✅ Đã có sẵn" : "Tiện ích"
        });

        return list;
    }

    public static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double mb = bytes / (1024.0 * 1024.0);
        if (mb < 1.0)
        {
            double kb = bytes / 1024.0;
            return $"{kb:0.#} KB";
        }
        return $"{mb:0.#} MB";
    }
}
