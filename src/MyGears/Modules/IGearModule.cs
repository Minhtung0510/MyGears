using System.Windows;

namespace MyGears.Modules;

/// <summary>
/// Interface cho mỗi "Gear" module trong MyGears.
/// Để thêm gear mới, chỉ cần tạo một class implement interface này
/// và đăng ký trong ModuleRegistry — không sửa code lõi.
/// </summary>
public interface IGearModule : IAsyncDisposable
{
    /// <summary>ID nội bộ duy nhất, ví dụ "keyboard", "mouse"</summary>
    string Id { get; }

    /// <summary>Tên hiển thị tiếng Việt</summary>
    string DisplayNameVi { get; }

    /// <summary>Tên hiển thị tiếng Anh</summary>
    string DisplayNameEn { get; }

    /// <summary>Unicode icon hoặc path đến icon file</summary>
    string Icon { get; }

    /// <summary>Thứ tự hiển thị trong sidebar (nhỏ hơn = trên hơn)</summary>
    int Order { get; }

    /// <summary>
    /// Trả về UIElement (UserControl) để hiển thị trong vùng nội dung chính.
    /// Được gọi một lần khi tab được chọn lần đầu (lazy init).
    /// </summary>
    UIElement GetView();

    /// <summary>
    /// Khởi tạo module (có thể async — load WebView2, launch process con...).
    /// Được gọi sau khi dependency check hoàn tất.
    /// </summary>
    Task InitializeAsync();

    /// <summary>Module có thể sử dụng không (dependency đã cài chưa)</summary>
    bool IsAvailable { get; }

    /// <summary>Lý do không available (hiển thị trong UI nếu IsAvailable = false)</summary>
    string? UnavailableReason { get; }
}
