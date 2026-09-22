using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MyGears.Core;

namespace MyGears.Dependency;

/// <summary>
/// Kiểm tra xem dependency đã được cài trên máy hay chưa.
/// Hỗ trợ 2 loại check: registry key và file tồn tại.
/// </summary>
public static class DependencyChecker
{
    // ──────────────────────────────────────────────
    //  Load Manifest
    // ──────────────────────────────────────────────

    /// <summary>
    /// Tạo manifest mặc định chứa WebView2 Runtime nếu file trên đĩa bị thiếu.
    /// </summary>
    public static DependencyManifest GetDefaultManifest()
    {
        return new DependencyManifest
        {
            Dependencies = new List<DependencyEntry>
            {
                new DependencyEntry
                {
                    Id = "WebView2Runtime",
                    DisplayName = "Microsoft Edge WebView2 Runtime",
                    CheckType = "registry",
                    RegistryKey = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                    RegistryValue = "pv",
                    InstallerSubPath = @"WebView2Runtime\MicrosoftEdgeWebview2Setup.exe",
                    InstallerArgs = "/silent /install",
                    Optional = false
                }
            }
        };
    }

    /// <summary>
    /// Đọc manifest.json từ App/ trên USB hoặc máy tính.
    /// Nếu chưa tồn tại, tự động sử dụng cấu hình mặc định và tự tạo file.
    /// </summary>
    public static async Task<DependencyManifest> LoadManifestAsync()
    {
        var path = UsbPathResolver.ManifestFile;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var defaultManifest = GetDefaultManifest();
            try
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var jsonStr = JsonSerializer.Serialize(defaultManifest, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(path, jsonStr);
                }
            }
            catch { }

            return defaultManifest;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var manifest = JsonSerializer.Deserialize<DependencyManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest == null || manifest.Dependencies == null || manifest.Dependencies.Count == 0)
                return GetDefaultManifest();

            return manifest;
        }
        catch
        {
            return GetDefaultManifest();
        }
    }

    // ──────────────────────────────────────────────
    //  Check Single Dependency
    // ──────────────────────────────────────────────

    /// <summary>
    /// Kiểm tra một dependency đã được cài chưa.
    /// Trả về true nếu đã cài, false nếu chưa.
    /// </summary>
    public static bool IsInstalled(DependencyEntry entry)
    {
        // Kiểm tra đặc thù WebView2 Runtime bằng API chuẩn Microsoft
        if (entry.Id.Equals("WebView2Runtime", StringComparison.OrdinalIgnoreCase) ||
            entry.DisplayName.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ver = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (!string.IsNullOrEmpty(ver)) return true;
            }
            catch { }
        }

        return entry.CheckType.ToLowerInvariant() switch
        {
            "registry" => CheckByRegistry(entry),
            "file"     => CheckByFile(entry),
            _          => false
        };
    }

    // ──────────────────────────────────────────────
    //  Registry Check
    // ──────────────────────────────────────────────

    private static bool CheckByRegistry(DependencyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RegistryKey))
            return false;

        try
        {
            var (hiveType, subKey) = ParseRegistryKey(entry.RegistryKey);

            // Kiểm tra trên cả 64-bit và 32-bit Registry Views
            if (CheckKeyInView(hiveType, subKey, RegistryView.Registry64, entry.RegistryValue) ||
                CheckKeyInView(hiveType, subKey, RegistryView.Registry32, entry.RegistryValue))
            {
                return true;
            }

            // Nếu key có WOW6432Node, thử bỏ WOW6432Node\ để tìm key 64-bit gốc
            if (subKey.Contains("WOW6432Node\\", StringComparison.OrdinalIgnoreCase))
            {
                var nativeSubKey = subKey.Replace("WOW6432Node\\", "", StringComparison.OrdinalIgnoreCase);
                if (CheckKeyInView(hiveType, nativeSubKey, RegistryView.Registry64, entry.RegistryValue))
                    return true;
            }

            // Fallback: nếu đang check HKLM cho EdgeUpdate, thử check cả HKCU (User-level install)
            if (hiveType == RegistryHive.LocalMachine && subKey.Contains("EdgeUpdate", StringComparison.OrdinalIgnoreCase))
            {
                if (CheckKeyInView(RegistryHive.CurrentUser, subKey, RegistryView.Default, entry.RegistryValue))
                    return true;
                if (subKey.Contains("WOW6432Node\\", StringComparison.OrdinalIgnoreCase))
                {
                    var nativeSubKey = subKey.Replace("WOW6432Node\\", "", StringComparison.OrdinalIgnoreCase);
                    if (CheckKeyInView(RegistryHive.CurrentUser, nativeSubKey, RegistryView.Default, entry.RegistryValue))
                        return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckKeyInView(RegistryHive hive, string subKey, RegistryView view, string? valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            if (key == null) return false;

            if (string.IsNullOrWhiteSpace(valueName))
                return true;

            var val = key.GetValue(valueName);
            return val != null && val.ToString() != "0.0.0.0";
        }
        catch
        {
            return false;
        }
    }

    private static (RegistryHive hive, string subKey) ParseRegistryKey(string fullPath)
    {
        var idx = fullPath.IndexOf('\\');
        if (idx < 0) throw new ArgumentException($"Registry key không hợp lệ: {fullPath}");

        var hiveStr = fullPath[..idx].ToUpperInvariant();
        var subKey  = fullPath[(idx + 1)..];

        RegistryHive hive = hiveStr switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE"  => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER"   => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT"   => RegistryHive.ClassesRoot,
            _ => throw new ArgumentException($"Hive không hỗ trợ: {hiveStr}")
        };

        return (hive, subKey);
    }

    // ──────────────────────────────────────────────
    //  File Check
    // ──────────────────────────────────────────────

    private static bool CheckByFile(DependencyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.CheckPath))
            return false;

        // Expand biến môi trường (%PROGRAMFILES%, %LOCALAPPDATA%...)
        var expandedPath = Environment.ExpandEnvironmentVariables(entry.CheckPath);
        return File.Exists(expandedPath) || Directory.Exists(expandedPath);
    }
}
