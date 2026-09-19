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
    /// Đọc manifest.json từ App/ trên USB.
    /// Đường dẫn lấy từ UsbPathResolver — không hardcode.
    /// </summary>
    public static async Task<DependencyManifest> LoadManifestAsync()
    {
        var path = UsbPathResolver.ManifestFile;

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Không tìm thấy manifest.json tại: {path}\n" +
                "Hãy đảm bảo file này tồn tại trong thư mục App/ trên USB.");

        var json = await File.ReadAllTextAsync(path);
        var manifest = JsonSerializer.Deserialize<DependencyManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return manifest ?? new DependencyManifest();
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
