using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace MyGears.Core;

/// <summary>
/// Đại diện cho 1 app có thể tải và cài đặt từ GitHub Releases
/// </summary>
public partial class DownloadableApp : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeDisplay => FormatSize(SizeBytes);
    public string FileExtension => Path.GetExtension(FileName).ToLowerInvariant();

    /// <summary>Nguồn gốc: "github", "usb", hoặc "dropped"</summary>
    public string Source { get; set; } = "github";

    [ObservableProperty] private string _status = "cloud";       // cloud | downloaded | downloading | installing | installed | error
    [ObservableProperty] private string _statusText = "☁️ Chưa tải";
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isInstalledOnSystem;
    [ObservableProperty] private bool _isInstalled;             // Tương thích ngược
    [ObservableProperty] private string? _localFilePath;
    [ObservableProperty] private string? _installedAppPath;

    public bool HasLocalInstaller => !string.IsNullOrEmpty(LocalFilePath) && File.Exists(LocalFilePath);

    public bool CanShowDownloadAndInstall => !IsDownloaded && !IsInstalledOnSystem && !IsDownloading && !IsInstalling;
    public bool CanShowInstallOffline => IsDownloaded && !IsInstalledOnSystem && !IsDownloading && !IsInstalling;
    public bool CanShowLaunchApp => IsInstalledOnSystem && !IsDownloading && !IsInstalling;
    public bool CanShowReinstall => IsInstalledOnSystem && IsDownloaded && !IsDownloading && !IsInstalling;
    public bool CanShowDeleteFile => IsDownloaded && !IsDownloading && !IsInstalling;

    partial void OnIsDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasLocalInstaller));
        OnPropertyChanged(nameof(CanShowDownloadAndInstall));
        OnPropertyChanged(nameof(CanShowInstallOffline));
        OnPropertyChanged(nameof(CanShowLaunchApp));
        OnPropertyChanged(nameof(CanShowReinstall));
        OnPropertyChanged(nameof(CanShowDeleteFile));
    }

    partial void OnIsInstalledOnSystemChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowDownloadAndInstall));
        OnPropertyChanged(nameof(CanShowInstallOffline));
        OnPropertyChanged(nameof(CanShowLaunchApp));
        OnPropertyChanged(nameof(CanShowReinstall));
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowDownloadAndInstall));
        OnPropertyChanged(nameof(CanShowInstallOffline));
        OnPropertyChanged(nameof(CanShowLaunchApp));
        OnPropertyChanged(nameof(CanShowReinstall));
        OnPropertyChanged(nameof(CanShowDeleteFile));
    }

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowDownloadAndInstall));
        OnPropertyChanged(nameof(CanShowInstallOffline));
        OnPropertyChanged(nameof(CanShowLaunchApp));
        OnPropertyChanged(nameof(CanShowReinstall));
        OnPropertyChanged(nameof(CanShowDeleteFile));
    }

    public string Icon
    {
        get
        {
            return FileExtension switch
            {
                ".exe" => "⚡",
                ".msi" => "📦",
                ".zip" => "🗜️",
                _ => "📄"
            };
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public void SetStatus(string status, string statusText)
    {
        Status = status;
        StatusText = statusText;
        IsDownloading = status == "downloading";
        IsInstalling = status == "installing";
        if (status == "installed")
        {
            IsInstalledOnSystem = true;
            IsInstalled = true;
        }
        else if (status == "downloaded")
        {
            IsDownloaded = true;
        }
    }
}

/// <summary>
/// Dịch vụ quản lý tải app từ GitHub Releases, cài đặt tự động và xóa file.
/// </summary>
public static class AppDownloadService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    /// <summary>Thư mục lưu file tải về (Ưu tiên thư mục Download trên USB nếu có)</summary>
    public static string DownloadDir
    {
        get
        {
            try
            {
                if (!string.IsNullOrEmpty(UsbPathResolver.DownloadDir) && Directory.Exists(UsbPathResolver.DownloadDir))
                    return UsbPathResolver.DownloadDir;

                var usbRoot = UsbPathResolver.FindConnectedUsbRoot() ?? UsbPathResolver.UsbRoot;
                if (!string.IsNullOrEmpty(usbRoot) && Directory.Exists(usbRoot))
                {
                    var usbDl = Path.Combine(usbRoot, "Download");
                    Directory.CreateDirectory(usbDl);
                    return usbDl;
                }
            }
            catch { }

            var dir = Path.Combine(UsbPathResolver.LocalDeployTargetDir, "Downloads");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Tìm file đã tải xem có trong thư mục Download của USB hoặc máy không
    /// </summary>
    public static string? FindExistingFile(string fileName)
    {
        var dirsToSearch = new List<string>();

        if (!string.IsNullOrEmpty(UsbPathResolver.DownloadDir) && Directory.Exists(UsbPathResolver.DownloadDir))
            dirsToSearch.Add(UsbPathResolver.DownloadDir);

        if (!string.IsNullOrEmpty(DownloadDir) && Directory.Exists(DownloadDir) && !dirsToSearch.Contains(DownloadDir))
            dirsToSearch.Add(DownloadDir);

        if (Directory.Exists(@"d:\Download") && !dirsToSearch.Contains(@"d:\Download"))
            dirsToSearch.Add(@"d:\Download");

        var usbRoot = UsbPathResolver.FindConnectedUsbRoot() ?? UsbPathResolver.UsbRoot;
        if (!string.IsNullOrEmpty(usbRoot))
        {
            var pUsb = Path.Combine(usbRoot, "Download");
            if (Directory.Exists(pUsb) && !dirsToSearch.Contains(pUsb))
                dirsToSearch.Add(pUsb);
        }

        var pLocal = Path.Combine(UsbPathResolver.LocalDeployTargetDir, "Download");
        if (Directory.Exists(pLocal) && !dirsToSearch.Contains(pLocal))
            dirsToSearch.Add(pLocal);

        var pLocalDls = Path.Combine(UsbPathResolver.LocalDeployTargetDir, "Downloads");
        if (Directory.Exists(pLocalDls) && !dirsToSearch.Contains(pLocalDls))
            dirsToSearch.Add(pLocalDls);

        var userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(userDownloads) && !dirsToSearch.Contains(userDownloads))
            dirsToSearch.Add(userDownloads);

        var targetNorm = NormalizeNameForMatch(fileName);

        foreach (var dir in dirsToSearch)
        {
            try
            {
                // 1. Khớp chính xác
                var exact = Path.Combine(dir, fileName);
                if (File.Exists(exact)) return exact;

                // 2. Khớp fuzzy (Install.VALORANT.exe <-> Install VALORANT.exe)
                foreach (var file in Directory.GetFiles(dir))
                {
                    var curName = Path.GetFileName(file);
                    if (NormalizeNameForMatch(curName).Equals(targetNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static string NormalizeNameForMatch(string filename)
    {
        return filename.ToLowerInvariant()
            .Replace(".", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace(" ", "");
    }

    /// <summary>
    /// Kiểm tra ứng dụng đã được tải hoặc cài đặt trên Windows (hàm tiện ích tương thích ngược)
    /// </summary>
    public static (bool IsInstalled, string? DetectedPath) CheckAppInstalledOrDownloaded(string name, string fileName)
    {
        var (isSystemInstalled, systemPath) = CheckInstalledOnSystem(name, fileName);
        if (isSystemInstalled) return (true, systemPath);

        var localFile = FindExistingFile(fileName);
        if (!string.IsNullOrEmpty(localFile)) return (true, localFile);

        return (false, null);
    }

    /// <summary>
    /// Kiểm tra xem ứng dụng đã thực sự được cài đặt vào hệ điều hành Windows hay chưa.
    /// Quét Registry, Desktop Shortcuts, Start Menu, các thư mục cài đặt mặc định và tiến trình đang chạy.
    /// </summary>
    public static (bool IsInstalled, string? DetectedPath) CheckInstalledOnSystem(string name, string fileName)
    {
        // 1. Nếu là MyGears: Chính là ứng dụng đang chạy
        if (name.Contains("MyGears", StringComparison.OrdinalIgnoreCase) || fileName.Contains("MyGears", StringComparison.OrdinalIgnoreCase))
        {
            return (true, Environment.ProcessPath ?? AppContext.BaseDirectory);
        }

        var cleanName = name.Replace("Install", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 2. Kiểm tra các tiến trình đang chạy trên Windows
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (IsMatchAppName(proc.ProcessName, cleanName, fileName))
                    {
                        string? mainMod = null;
                        try { mainMod = proc.MainModule?.FileName; } catch { }
                        return (true, !string.IsNullOrEmpty(mainMod) ? mainMod : proc.ProcessName);
                    }
                }
                catch { }
            }
        }
        catch { }

        // 3. Kiểm tra các đường dẫn mặc định quen thuộc của các app game phổ biến
        try
        {
            // Discord
            if (cleanName.Contains("Discord", StringComparison.OrdinalIgnoreCase))
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var discordUpdate = Path.Combine(localApp, "Discord", "Update.exe");
                if (File.Exists(discordUpdate)) return (true, discordUpdate);

                var discordDir = Path.Combine(localApp, "Discord");
                if (Directory.Exists(discordDir))
                {
                    var exe = Directory.GetFiles(discordDir, "Discord.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (exe != null) return (true, exe);
                }
            }

            // VALORANT / Riot Games
            if (cleanName.Contains("VALORANT", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("Riot", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var drv in DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName))
                {
                    var valExe = Path.Combine(drv, "Riot Games", "VALORANT", "live", "VALORANT.exe");
                    if (File.Exists(valExe)) return (true, valExe);

                    var valDir = Path.Combine(drv, "Riot Games", "VALORANT");
                    if (Directory.Exists(valDir)) return (true, valDir);

                    var riotClient = Path.Combine(drv, "Riot Games", "Riot Client", "RiotClientServices.exe");
                    if (File.Exists(riotClient)) return (true, riotClient);
                }
            }

            // FxSound
            if (cleanName.Contains("FxSound", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("Sound", StringComparison.OrdinalIgnoreCase))
            {
                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var p1 = Path.Combine(prog, "FxSound LLC", "FxSound", "FxSound.exe");
                var p2 = Path.Combine(progX86, "FxSound LLC", "FxSound", "FxSound.exe");
                if (File.Exists(p1)) return (true, p1);
                if (File.Exists(p2)) return (true, p2);
            }

            // UniKey
            if (cleanName.Contains("UniKey", StringComparison.OrdinalIgnoreCase))
            {
                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var u1 = Path.Combine(prog, "UniKey", "UniKeyNT.exe");
                if (File.Exists(u1)) return (true, u1);
            }

            // ScyRox
            if (cleanName.Contains("ScyRox", StringComparison.OrdinalIgnoreCase))
            {
                var prog = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var progX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var s1 = Path.Combine(prog, "ScyRox", "Sys64", "ScyRox.exe");
                var s2 = Path.Combine(prog, "ScyRox", "Sys32", "ScyRox.exe");
                if (File.Exists(s1)) return (true, s1);
                if (File.Exists(s2)) return (true, s2);
                if (Directory.Exists(Path.Combine(prog, "ScyRox")) || Directory.Exists(Path.Combine(progX86, "ScyRox")))
                    return (true, Path.Combine(prog, "ScyRox"));
            }
        }
        catch { }

        // 4. Kiểm tra Windows Registry (Uninstall keys)
        try
        {
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

                foreach (var regKey in new[] { hklm, hkcu })
                {
                    foreach (var regPath in registryPaths)
                    {
                        using var sub = regKey.OpenSubKey(regPath);
                        if (sub == null) continue;

                        foreach (var keyName in sub.GetSubKeyNames())
                        {
                            using var appKey = sub.OpenSubKey(keyName);
                            if (appKey == null) continue;

                            var dispName = appKey.GetValue("DisplayName")?.ToString();
                            if (string.IsNullOrEmpty(dispName)) continue;

                            if (IsMatchAppName(dispName, cleanName, fileName))
                            {
                                var loc = appKey.GetValue("InstallLocation")?.ToString();
                                var icon = appKey.GetValue("DisplayIcon")?.ToString();

                                if (!string.IsNullOrEmpty(icon))
                                {
                                    var cleanIcon = icon.Split(',')[0].Trim('\"');
                                    if (File.Exists(cleanIcon)) return (true, cleanIcon);
                                }

                                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                                {
                                    var firstExe = Directory.GetFiles(loc, "*.exe").FirstOrDefault();
                                    if (firstExe != null) return (true, firstExe);
                                    return (true, loc);
                                }

                                return (true, dispName);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 5. Kiểm tra shortcuts Desktop / Start Menu
        try
        {
            var shortcutDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            foreach (var sDir in shortcutDirs.Where(Directory.Exists))
            {
                var lnkFiles = Directory.GetFiles(sDir, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnkFiles)
                {
                    var lnkName = Path.GetFileNameWithoutExtension(lnk);
                    if (IsMatchAppName(lnkName, cleanName, fileName))
                        return (true, lnk);
                }
            }
        }
        catch { }

        return (false, null);
    }

    /// <summary>
    /// Cập nhật toàn bộ trạng thái (Đã có file tải, Đã cài vào Windows, Trạng thái chữ & màu) cho 1 app
    /// </summary>
    public static void UpdateAppStatus(DownloadableApp app)
    {
        // 1. Quét file bộ cài offline
        var localFile = FindExistingFile(app.FileName);
        app.LocalFilePath = localFile;
        app.IsDownloaded = !string.IsNullOrEmpty(localFile) && File.Exists(localFile);

        // 2. Quét xem app đã được cài trên Windows chưa
        var (isInstalled, detectedPath) = CheckInstalledOnSystem(app.Name, app.FileName);
        app.IsInstalledOnSystem = isInstalled;
        app.InstalledAppPath = detectedPath;
        app.IsInstalled = isInstalled; // Tương thích ngược

        // 3. Quyết định Status & StatusText
        if (app.IsDownloading)
        {
            app.Status = "downloading";
            app.StatusText = "⏳ Đang tải…";
        }
        else if (app.IsInstalling)
        {
            app.Status = "installing";
            app.StatusText = "🔧 Đang cài đặt…";
        }
        else if (app.IsInstalledOnSystem)
        {
            app.Status = "installed";
            app.StatusText = "✅ Đã cài vào máy";
        }
        else if (app.IsDownloaded)
        {
            app.Status = "downloaded";
            app.StatusText = "📦 Đã có bộ cài";
        }
        else
        {
            app.Status = "cloud";
            app.StatusText = "☁️ Chưa tải";
        }
    }

    private static bool IsMatchAppName(string actual, string expectedCleanName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expectedCleanName)) return false;

        var a = actual.ToLowerInvariant();
        var e = expectedCleanName.ToLowerInvariant();
        var f = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace(".", " ").Replace("-", " ").Replace("_", " ");

        if (a.Contains(e) || e.Contains(a)) return true;
        if (!string.IsNullOrEmpty(f) && (a.Contains(f) || f.Contains(a))) return true;

        if (e.Contains("valorant") && a.Contains("valorant")) return true;
        if (e.Contains("discord") && a.Contains("discord")) return true;
        if (e.Contains("fxsound") && a.Contains("fxsound")) return true;
        if (e.Contains("nvidia") && a.Contains("nvidia")) return true;
        if (e.Contains("scyrox") && a.Contains("scyrox")) return true;
        if (e.Contains("unikey") && a.Contains("unikey")) return true;

        return false;
    }

    /// <summary>
    /// Danh sách file đã bỏ qua (dependency hệ thống, không phải app người dùng)
    /// </summary>
    private static readonly HashSet<string> SkippedAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebView2Runtime.zip",
    };

    /// <summary>
    /// Fetch danh sách app từ GitHub Releases mới nhất
    /// </summary>
    public static async Task<List<DownloadableApp>> FetchAppsFromGitHubReleasesAsync()
    {
        var result = new List<DownloadableApp>();
        try
        {
            var apiUrl = $"https://api.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd("MyGearsApp");

            using var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return result;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var asset in assetsElem.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                long size = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

                // Bỏ qua các file dependency hệ thống
                if (SkippedAssets.Contains(name)) continue;

                // Chỉ lấy file cài đặt (.exe, .msi, .zip)
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext != ".exe" && ext != ".msi" && ext != ".zip") continue;

                var app = new DownloadableApp
                {
                    Name = Path.GetFileNameWithoutExtension(name).Replace("-", " ").Replace("_", " "),
                    FileName = name,
                    DownloadUrl = url,
                    SizeBytes = size,
                    Source = "github"
                };

                // Cập nhật trạng thái tải & cài đặt chi tiết
                UpdateAppStatus(app);

                result.Add(app);
            }
        }
        catch
        {
            // Lỗi mạng → bỏ qua lỗi GitHub, vẫn tiếp tục quét file offline trên USB
        }

        // Quét thêm tất cả file đã tải / kéo thả có sẵn trong thư mục Download trên USB
        try
        {
            var dirsToScan = new List<string>();
            if (!string.IsNullOrEmpty(DownloadDir) && Directory.Exists(DownloadDir))
                dirsToScan.Add(DownloadDir);
            if (!string.IsNullOrEmpty(UsbPathResolver.DownloadDir) && Directory.Exists(UsbPathResolver.DownloadDir) && !dirsToScan.Contains(UsbPathResolver.DownloadDir))
                dirsToScan.Add(UsbPathResolver.DownloadDir);

            foreach (var dir in dirsToScan)
            {
                foreach (var filePath in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext != ".exe" && ext != ".msi" && ext != ".zip") continue;

                    var fileName = Path.GetFileName(filePath);
                    if (SkippedAssets.Contains(fileName)) continue;

                    // Nếu đã có trong danh sách GitHub thì bỏ qua (đã được link trạng thái ở trên)
                    if (result.Any(a => a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var fi = new FileInfo(filePath);
                    var localApp = new DownloadableApp
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName).Replace("-", " ").Replace("_", " "),
                        FileName = fileName,
                        DownloadUrl = string.Empty,
                        SizeBytes = fi.Length,
                        Source = "usb",
                        LocalFilePath = filePath
                    };

                    UpdateAppStatus(localApp);
                    result.Add(localApp);
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Tải file từ GitHub về máy tính, sau đó tự động chạy installer
    /// </summary>
    public static async Task<bool> DownloadAndInstallAsync(
        DownloadableApp app,
        Action<string, double>? onProgress = null)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(DownloadDir);

            var destFile = Path.Combine(DownloadDir, app.FileName);
            tempPath = destFile + ".tmp";

            app.SetStatus("downloading", "⏳ Đang tải…");
            onProgress?.Invoke($"🌐 Đang kết nối tải {app.FileName}…", 0.02);

            // Tải file
            using (var response = await HttpClient.GetAsync(app.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

                var buffer = new byte[16384];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double progress = (double)totalRead / totalBytes.Value;
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        double mbTotal = totalBytes.Value / (1024.0 * 1024.0);
                        app.DownloadProgress = progress;
                        app.ProgressText = $"{mbRead:0.#} / {mbTotal:0.#} MB ({progress:P0})";
                        onProgress?.Invoke($"⬇ Đang tải {app.FileName}: {mbRead:0.#} / {mbTotal:0.#} MB", Math.Min(0.80, progress * 0.80));
                    }
                    else
                    {
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        app.ProgressText = $"{mbRead:0.#} MB…";
                        onProgress?.Invoke($"⬇ Đang tải {app.FileName}: {mbRead:0.#} MB…", 0.5);
                    }
                }
            }

            // Move từ .tmp sang file chính
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(tempPath, destFile);
            tempPath = null;

            app.LocalFilePath = destFile;
            app.DownloadProgress = 1.0;

            // Tự động chạy installer
            app.IsInstalling = true;
            app.SetStatus("installing", "🔧 Đang cài đặt…");
            onProgress?.Invoke($"🔧 Đang khởi chạy cài đặt {app.FileName}…", 0.90);

            bool installOk = await RunInstallerAsync(destFile, app.FileExtension);

            await Task.Delay(1500); // Đợi hệ thống Windows ghi nhận Registry / Shortcut
            app.IsInstalling = false;
            UpdateAppStatus(app);

            if (app.IsInstalledOnSystem)
            {
                onProgress?.Invoke($"✅ Đã cài đặt thành công {app.Name} vào máy tính!", 1.0);
            }
            else
            {
                onProgress?.Invoke($"✅ Đã tải bộ cài và hoàn tất tiến trình cài đặt {app.Name}.", 1.0);
            }

            return true;
        }
        catch (Exception ex)
        {
            app.IsInstalling = false;
            app.SetStatus("error", $"❌ Lỗi: {ex.Message}");
            app.DownloadProgress = 0;
            app.ProgressText = string.Empty;
            onProgress?.Invoke($"❌ Lỗi tải {app.FileName}: {ex.Message}", 0.0);

            // Dọn file tạm
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            return false;
        }
    }

    /// <summary>
    /// Chạy trực tiếp file installer offline đã có trên máy/USB mà không cần tải lại
    /// </summary>
    public static async Task<bool> InstallExistingOfflineAppAsync(
        DownloadableApp app,
        Action<string, double>? onProgress = null)
    {
        if (string.IsNullOrEmpty(app.LocalFilePath) || !File.Exists(app.LocalFilePath))
        {
            var found = FindExistingFile(app.FileName);
            if (!string.IsNullOrEmpty(found))
            {
                app.LocalFilePath = found;
                app.IsDownloaded = true;
            }
            else
            {
                onProgress?.Invoke($"❌ Không tìm thấy file bộ cài offline {app.FileName}.", 0);
                return false;
            }
        }

        try
        {
            app.IsInstalling = true;
            app.SetStatus("installing", "🔧 Đang cài đặt…");
            onProgress?.Invoke($"🔧 Đang khởi chạy cài đặt {app.FileName}…", 0.5);

            bool ok = await RunInstallerAsync(app.LocalFilePath, app.FileExtension);

            await Task.Delay(1500); // Đợi Windows ghi nhận
            app.IsInstalling = false;
            UpdateAppStatus(app);

            if (app.IsInstalledOnSystem)
            {
                onProgress?.Invoke($"✅ Đã cài đặt {app.Name} vào máy tính thành công!", 1.0);
            }
            else
            {
                onProgress?.Invoke($"✅ Đã hoàn tất tiến trình cài đặt {app.Name}.", 1.0);
            }

            return ok;
        }
        catch (Exception ex)
        {
            app.IsInstalling = false;
            UpdateAppStatus(app);
            onProgress?.Invoke($"❌ Lỗi chạy cài đặt: {ex.Message}", 0);
            return false;
        }
    }

    /// <summary>
    /// Mở trực tiếp ứng dụng đã được cài đặt trên Windows
    /// </summary>
    public static bool LaunchInstalledApp(DownloadableApp app)
    {
        try
        {
            if (!string.IsNullOrEmpty(app.InstalledAppPath))
            {
                var target = app.InstalledAppPath;
                if (File.Exists(target) || Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        WorkingDirectory = Path.GetDirectoryName(target),
                        UseShellExecute = true
                    });
                    return true;
                }
            }

            // Fallback: Tìm lại đường dẫn hệ thống
            var (isInstalled, detectedPath) = CheckInstalledOnSystem(app.Name, app.FileName);
            if (isInstalled && !string.IsNullOrEmpty(detectedPath) && (File.Exists(detectedPath) || Directory.Exists(detectedPath)))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = detectedPath,
                    WorkingDirectory = Path.GetDirectoryName(detectedPath),
                    UseShellExecute = true
                });
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Chạy installer theo loại file: .exe, .msi, .zip
    /// </summary>
    public static async Task<bool> RunInstallerAsync(string filePath, string extension)
    {
        try
        {
            switch (extension)
            {
                case ".exe":
                    var exeProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    if (exeProcess != null)
                    {
                        await exeProcess.WaitForExitAsync();
                    }
                    return true;

                case ".msi":
                    var msiProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "msiexec",
                        Arguments = $"/i \"{filePath}\"",
                        UseShellExecute = true
                    });
                    if (msiProcess != null)
                    {
                        await msiProcess.WaitForExitAsync();
                    }
                    return true;

                case ".zip":
                    return await HandleZipInstallerAsync(filePath);

                default:
                    // Mở bằng trình xử lý mặc định của Windows
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Giải nén file .zip → Tìm setup.exe hoặc file .exe đầu tiên → Chạy
    /// </summary>
    private static async Task<bool> HandleZipInstallerAsync(string zipPath)
    {
        try
        {
            var extractDir = Path.Combine(
                Path.GetDirectoryName(zipPath)!,
                Path.GetFileNameWithoutExtension(zipPath));

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);

            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true));

            // Tìm file setup theo thứ tự ưu tiên
            string? installerPath = null;

            // 1. Tìm setup.exe
            var setupExe = Directory.GetFiles(extractDir, "setup.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (setupExe != null) installerPath = setupExe;

            // 2. Tìm install.exe
            if (installerPath == null)
            {
                var installExe = Directory.GetFiles(extractDir, "install.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (installExe != null) installerPath = installExe;
            }

            // 3. Tìm file .msi
            if (installerPath == null)
            {
                var msiFile = Directory.GetFiles(extractDir, "*.msi", SearchOption.AllDirectories).FirstOrDefault();
                if (msiFile != null) installerPath = msiFile;
            }

            // 4. Tìm file .exe bất kỳ (chọn file lớn nhất)
            if (installerPath == null)
            {
                var exeFiles = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                {
                    installerPath = exeFiles
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(fi => fi.Length)
                        .First()
                        .FullName;
                }
            }

            if (installerPath != null)
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true
                });
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
                return true;
            }

            // Không tìm thấy installer → Mở thư mục giải nén
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{extractDir}\"",
                UseShellExecute = true
            });
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Xóa file app đã tải
    /// </summary>
    public static bool DeleteDownloadedApp(DownloadableApp app)
    {
        try
        {
            if (!string.IsNullOrEmpty(app.LocalFilePath) && File.Exists(app.LocalFilePath))
            {
                File.Delete(app.LocalFilePath);

                // Nếu là .zip, xóa luôn thư mục giải nén
                if (app.FileExtension == ".zip")
                {
                    var extractDir = Path.Combine(
                        Path.GetDirectoryName(app.LocalFilePath)!,
                        Path.GetFileNameWithoutExtension(app.LocalFilePath));
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                }
            }

            var extraPath = FindExistingFile(app.FileName);
            if (!string.IsNullOrEmpty(extraPath) && File.Exists(extraPath))
            {
                try { File.Delete(extraPath); } catch { }
            }

            app.LocalFilePath = null;
            app.IsDownloaded = false;
            app.DownloadProgress = 0;
            app.ProgressText = string.Empty;

            // Cập nhật lại trạng thái dựa trên việc máy còn cài hay không
            UpdateAppStatus(app);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Upload 1 file cài đặt từ máy / USB lên GitHub Releases mới nhất của repo
    /// </summary>
    public static async Task<(bool Success, string Message)> UploadAssetToGitHubReleaseAsync(
        string filePath,
        string token,
        Action<string, double>? onProgress = null)
    {
        if (!File.Exists(filePath))
            return (false, "File không tồn tại trên đĩa.");

        if (string.IsNullOrWhiteSpace(token))
            return (false, "Chưa cấu hình GitHub Token.");

        var fileName = Path.GetFileName(filePath);

        try
        {
            onProgress?.Invoke($"🌐 Đang kiểm tra Release mới nhất trên GitHub…", 0.05);

            // 1. Lấy thông tin release mới nhất
            var releaseApiUrl = $"https://api.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/latest";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, releaseApiUrl);
            getReq.Headers.UserAgent.ParseAdd("MyGearsApp");
            getReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var releaseResp = await HttpClient.SendAsync(getReq);
            if (!releaseResp.IsSuccessStatusCode)
            {
                return (false, $"Không thể kết nối GitHub (HTTP {releaseResp.StatusCode}). Bạn vui lòng kiểm tra lại Token.");
            }

            var releaseJson = await releaseResp.Content.ReadAsStringAsync();
            using var releaseDoc = JsonDocument.Parse(releaseJson);
            var root = releaseDoc.RootElement;

            long releaseId = root.GetProperty("id").GetInt64();

            // Kiểm tra xem asset đã tồn tại chưa, nếu có thì xóa trước để upload đè
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    var aName = asset.GetProperty("name").GetString() ?? "";
                    if (aName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        long assetId = asset.GetProperty("id").GetInt64();
                        onProgress?.Invoke($"🗑️ Đang xóa bản cũ ({fileName}) trên GitHub…", 0.15);

                        var delUrl = $"https://api.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/assets/{assetId}";
                        using var delReq = new HttpRequestMessage(HttpMethod.Delete, delUrl);
                        delReq.Headers.UserAgent.ParseAdd("MyGearsApp");
                        delReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        await HttpClient.SendAsync(delReq);
                        break;
                    }
                }
            }

            // 2. Upload file
            onProgress?.Invoke($"☁️ Đang upload {fileName} lên GitHub Releases…", 0.30);

            var uploadUrl = $"https://uploads.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/{releaseId}/assets?name={Uri.EscapeDataString(fileName)}";

            await using var fileStream = File.OpenRead(filePath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var uploadReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadReq.Headers.UserAgent.ParseAdd("MyGearsApp");
            uploadReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            uploadReq.Content = content;

            var uploadResp = await HttpClient.SendAsync(uploadReq);
            if (uploadResp.IsSuccessStatusCode)
            {
                onProgress?.Invoke($"✅ Đã upload {fileName} lên GitHub Releases thành công!", 1.0);
                return (true, $"Đã đẩy {fileName} lên GitHub Releases thành công!");
            }
            else
            {
                var respErr = await uploadResp.Content.ReadAsStringAsync();
                return (false, $"Lỗi upload (HTTP {uploadResp.StatusCode}): {respErr}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi upload: {ex.Message}");
        }
    }

    /// <summary>
    /// Xóa một asset khỏi GitHub Releases mới nhất của repo (có cơ chế tự động thử lại nếu mạng chập chờn)
    /// </summary>
    public static async Task<(bool Success, string Message)> DeleteAssetFromGitHubReleaseAsync(
        string fileName,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Chưa cấu hình GitHub Token.");

        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var releaseApiUrl = $"https://api.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/latest";
                using var getReq = new HttpRequestMessage(HttpMethod.Get, releaseApiUrl);
                getReq.Headers.UserAgent.ParseAdd("MyGearsApp");
                getReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var releaseResp = await HttpClient.SendAsync(getReq);
                if (!releaseResp.IsSuccessStatusCode)
                {
                    retries--;
                    if (retries == 0)
                        return (false, $"Không thể kết nối GitHub (HTTP {releaseResp.StatusCode}).");
                    await Task.Delay(1500);
                    continue;
                }

                var releaseJson = await releaseResp.Content.ReadAsStringAsync();
                using var releaseDoc = JsonDocument.Parse(releaseJson);
                var root = releaseDoc.RootElement;

                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        var aName = asset.GetProperty("name").GetString() ?? "";
                        if (aName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            long assetId = asset.GetProperty("id").GetInt64();
                            var delUrl = $"https://api.github.com/repos/{CloudDownloadService.GitHubRepoOwner}/{CloudDownloadService.GitHubRepoName}/releases/assets/{assetId}";
                            using var delReq = new HttpRequestMessage(HttpMethod.Delete, delUrl);
                            delReq.Headers.UserAgent.ParseAdd("MyGearsApp");
                            delReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                            using var delResp = await HttpClient.SendAsync(delReq);
                            if (delResp.IsSuccessStatusCode || delResp.StatusCode == System.Net.HttpStatusCode.NoContent)
                            {
                                return (true, $"Đã xóa {fileName} khỏi GitHub Releases thành công!");
                            }
                            else
                            {
                                return (false, $"Lỗi xóa trên GitHub (HTTP {delResp.StatusCode}).");
                            }
                        }
                    }
                }

                // Không tìm thấy asset trên release -> đã bị xóa trước đó rồi
                return (true, $"File không tồn tại trên GitHub Releases (đã xóa).");
            }
            catch (Exception ex)
            {
                retries--;
                if (retries == 0)
                    return (false, $"Lỗi kết nối khi xóa trên GitHub: {ex.Message}");

                await Task.Delay(1500);
            }
        }

        return (false, "Không thể kết nối đến máy chủ GitHub.");
    }
}
