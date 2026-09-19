namespace MyGears.Core;

/// <summary>
/// Model cho settings.json — lưu toàn bộ config cá nhân của user trên USB.
/// Khi cắm USB vào máy khác, tất cả thiết lập được khôi phục tự động.
/// </summary>
public class AppSettings
{
    // ── Chuột ─────────────────────────────────────
    public MouseSettings Mouse { get; set; } = new();

    // ── Bàn phím ──────────────────────────────────
    public KeyboardSettings Keyboard { get; set; } = new();

    // ── UI App ────────────────────────────────────
    public AppUiSettings Ui { get; set; } = new();
}

public class MouseSettings
{
    /// <summary>DPI profile index đang chọn (0-3)</summary>
    public int ActiveDpiProfile { get; set; } = 0;

    /// <summary>Polling rate hiện tại (Hz)</summary>
    public int PollingRate { get; set; } = 1000;

    /// <summary>Màu RGB đèn chuột (hex string, ví dụ "#FF4500")</summary>
    public string RgbColor { get; set; } = "#FF4500";
}

public class KeyboardSettings
{
    /// <summary>Profile keybind đang chọn trên hub.fgg.com.cn</summary>
    public string ActiveProfile { get; set; } = "default";

    /// <summary>Zoom level của WebView2 khi load trang config bàn phím</summary>
    public double WebViewZoom { get; set; } = 1.0;
}

public class AppUiSettings
{
    /// <summary>Tab mở mặc định khi khởi động</summary>
    public string LastActiveTab { get; set; } = "Keyboard";

    /// <summary>Vị trí cửa sổ chính</summary>
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 860;
}
