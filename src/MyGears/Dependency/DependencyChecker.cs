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
            // Parse "HKLM\..." hoặc "HKCU\..."
            var (hive, subKey) = ParseRegistryKey(entry.RegistryKey);

            using var key = hive.OpenSubKey(subKey);
            if (key == null) return false;

            // Nếu không cần check value cụ thể, chỉ cần key tồn tại là đủ
            if (string.IsNullOrWhiteSpace(entry.RegistryValue))
                return true;

            var value = key.GetValue(entry.RegistryValue);
            return value != null && value.ToString() != "0.0.0.0";
        }
        catch
        {
            return false;
        }
    }

    private static (RegistryKey hive, string subKey) ParseRegistryKey(string fullPath)
    {
        var idx = fullPath.IndexOf('\\');
        if (idx < 0) throw new ArgumentException($"Registry key không hợp lệ: {fullPath}");

        var hiveStr = fullPath[..idx].ToUpperInvariant();
        var subKey  = fullPath[(idx + 1)..];

        RegistryKey hive = hiveStr switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE"  => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER"   => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT"   => Registry.ClassesRoot,
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
