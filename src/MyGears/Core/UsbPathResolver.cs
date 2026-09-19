using System.IO;

namespace MyGears.Core;

/// <summary>
/// Tự động phát hiện gốc USB dựa trên vị trí file .exe đang chạy.
/// 
/// Cấu trúc runtime trên USB:
///   [USB Root]/
///   ├── App/         ← AppContext.BaseDirectory trỏ vào đây
///   │   └── MyGears.exe
///   ├── Download/
///   └── src/
/// 
/// Do đó: UsbRoot = thư mục cha của App/ = GetParent(BaseDirectory)
/// </summary>
public static class UsbPathResolver
{
    // ──────────────────────────────────────────────
    //  Public Properties (tất cả là absolute path,
    //  được tính động lúc runtime — không hardcode)
    // ──────────────────────────────────────────────

    /// <summary>Gốc USB, ví dụ D:\</summary>
    public static string UsbRoot { get; private set; } = string.Empty;

    /// <summary>Thư mục App/ chứa .exe đang chạy</summary>
    public static string AppDir { get; private set; } = string.Empty;

    /// <summary>Thư mục Download/ chứa các installer offline</summary>
    public static string DownloadDir { get; private set; } = string.Empty;

    /// <summary>File settings.json</summary>
    public static string SettingsFile { get; private set; } = string.Empty;

    /// <summary>File manifest.json (danh sách dependency)</summary>
    public static string ManifestFile { get; private set; } = string.Empty;

    /// <summary>Đã khởi tạo thành công chưa</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>Đang chạy trực tiếp từ USB (ổ di động hoặc ổ khác C:)</summary>
    public static bool IsRunningFromUsb
    {
        get
        {
            try
            {
                var root = Path.GetPathRoot(AppContext.BaseDirectory);
                if (string.IsNullOrEmpty(root)) return false;
                var drive = new DriveInfo(root);
                return drive.DriveType == DriveType.Removable || !AppContext.BaseDirectory.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Đang chạy từ bộ nhớ máy tính (đã copy vào C:\Users\Public\MyGears)</summary>
    public static bool IsDeployedLocally => !IsRunningFromUsb;

    /// <summary>Đường dẫn mục tiêu khi copy vào máy: C:\Users\Public\MyGears</summary>
    public static string LocalDeployTargetDir
    {
        get
        {
            var pub = Environment.GetEnvironmentVariable("PUBLIC");
            return Path.Combine(!string.IsNullOrEmpty(pub) ? pub : @"C:\Users\Public", "MyGears");
        }
    }

    /// <summary>
    /// Tìm ổ USB gốc khi app đang chạy trên máy (quét các ổ đĩa để tìm ổ có thư mục MyGears/App/Download)
    /// </summary>
    public static string? FindConnectedUsbRoot()
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (drive.RootDirectory.FullName.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)) continue;

                var root = drive.RootDirectory.FullName;
                if ((Directory.Exists(Path.Combine(root, "Download", "ScyRox")) || Directory.Exists(Path.Combine(root, "App")))
                    && (File.Exists(Path.Combine(root, "App", "manifest.json")) || File.Exists(Path.Combine(root, "App", "MyGears.exe"))))
                {
                    return root;
                }
            }
        }
        catch { }
        return null;
    }

    // ──────────────────────────────────────────────
    //  Initialization
    // ──────────────────────────────────────────────

    /// <summary>
    /// Gọi một lần duy nhất khi app khởi động (trong App.OnStartup).
    /// Tự suy ra tất cả đường dẫn từ AppContext.BaseDirectory.
    /// </summary>
    public static void Initialize()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string? root = null;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Download")) &&
                (Directory.Exists(Path.Combine(dir.FullName, "App")) || Directory.Exists(Path.Combine(dir.FullName, "src"))))
            {
                root = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }

        if (string.IsNullOrEmpty(root))
        {
            var parent = Directory.GetParent(baseDir)?.FullName;
            root = !string.IsNullOrEmpty(parent) ? parent : baseDir;
        }

        UsbRoot = root;
        AppDir = Directory.Exists(Path.Combine(UsbRoot, "App"))
            ? Path.Combine(UsbRoot, "App")
            : baseDir;

        DownloadDir = Path.Combine(UsbRoot, "Download");
        SettingsFile = Path.Combine(AppDir, "settings.json");
        ManifestFile = Path.Combine(AppDir, "manifest.json");

        IsInitialized = true;
    }

    // ──────────────────────────────────────────────
    //  Helper Methods
    // ──────────────────────────────────────────────

    /// <summary>
    /// Ghép đường dẫn từ gốc USB.
    /// Ví dụ: GetPath("Download", "WebView2Runtime") → "D:\Download\WebView2Runtime"
    /// </summary>
    public static string GetPath(params string[] relativeParts)
    {
        EnsureInitialized();
        return Path.Combine(new[] { UsbRoot }.Concat(relativeParts).ToArray());
    }

    /// <summary>
    /// Ghép đường dẫn từ thư mục Download/.
    /// Ví dụ: GetDownloadPath("WebView2Runtime", "setup.exe")
    /// </summary>
    public static string GetDownloadPath(params string[] relativeParts)
    {
        EnsureInitialized();
        return Path.Combine(new[] { DownloadDir }.Concat(relativeParts).ToArray());
    }

    /// <summary>
    /// Kiểm tra thư mục Download/ và các thư mục con bắt buộc có tồn tại không.
    /// </summary>
    public static IEnumerable<string> GetMissingDirectories(params string[] expectedSubDirs)
    {
        EnsureInitialized();
        foreach (var sub in expectedSubDirs)
        {
            var path = Path.Combine(DownloadDir, sub);
            if (!Directory.Exists(path))
                yield return path;
        }
    }

    private static void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("UsbPathResolver chưa được khởi tạo. Hãy gọi Initialize() trước.");
    }
}
