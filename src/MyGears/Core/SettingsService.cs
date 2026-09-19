using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyGears.Core;

/// <summary>
/// Đọc và ghi settings.json từ App/ trên USB.
/// Đường dẫn file được lấy từ UsbPathResolver — không hardcode.
/// </summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Current { get; private set; } = new();

    // ──────────────────────────────────────────────
    //  Load
    // ──────────────────────────────────────────────

    public static async Task LoadAsync()
    {
        var path = UsbPathResolver.SettingsFile;
        if (!File.Exists(path))
        {
            // Lần đầu chạy — tạo settings mặc định
            Current = new AppSettings();
            await SaveAsync();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            Current = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            if (Current.Ui.WindowWidth < 1250) Current.Ui.WindowWidth = 1360;
            if (Current.Ui.WindowHeight < 800) Current.Ui.WindowHeight = 860;
        }
        catch (Exception ex)
        {
            // File hỏng hoặc format cũ → reset về mặc định
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Lỗi đọc settings: {ex.Message}");
            Current = new AppSettings();
        }
    }

    // ──────────────────────────────────────────────
    //  Save
    // ──────────────────────────────────────────────

    public static async Task SaveAsync()
    {
        var path = UsbPathResolver.SettingsFile;
        try
        {
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Lỗi ghi settings: {ex.Message}");
        }
    }

    /// <summary>Ghi settings đồng bộ (dùng trong OnExit)</summary>
    public static void Save()
    {
        var path = UsbPathResolver.SettingsFile;
        try
        {
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Lỗi ghi settings: {ex.Message}");
        }
    }
}
