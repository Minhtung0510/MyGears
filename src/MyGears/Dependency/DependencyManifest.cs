using System.Text.Json.Serialization;

namespace MyGears.Dependency;

/// <summary>
/// Model cho App/manifest.json — khai báo danh sách dependency cần kiểm tra.
/// </summary>
public class DependencyManifest
{
    [JsonPropertyName("dependencies")]
    public List<DependencyEntry> Dependencies { get; set; } = [];
}

public class DependencyEntry
{
    /// <summary>ID nội bộ, dùng để match với thư mục trong Download/</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Tên hiển thị trong UI</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Loại check: "registry" hoặc "file"</summary>
    [JsonPropertyName("checkType")]
    public string CheckType { get; set; } = "registry";

    // ── Registry check ──────────────────────────────

    /// <summary>Ví dụ: "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{...}"</summary>
    [JsonPropertyName("registryKey")]
    public string? RegistryKey { get; set; }

    /// <summary>Tên value cần kiểm tra (nếu null → chỉ check key tồn tại)</summary>
    [JsonPropertyName("registryValue")]
    public string? RegistryValue { get; set; }

    // ── File check ───────────────────────────────────

    /// <summary>
    /// Đường dẫn file để check (có thể dùng biến môi trường như %PROGRAMFILES%).
    /// Nếu checkType = "file" và file tồn tại → dependency đã có.
    /// </summary>
    [JsonPropertyName("checkPath")]
    public string? CheckPath { get; set; }

    // ── Installer ────────────────────────────────────

    /// <summary>
    /// Đường dẫn installer TƯƠNG ĐỐI tính từ thư mục Download/ trên USB.
    /// Ví dụ: "WebView2Runtime\\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
    /// </summary>
    [JsonPropertyName("installerSubPath")]
    public string InstallerSubPath { get; set; } = string.Empty;

    /// <summary>Tham số cài đặt silent, ví dụ "/silent /install" hoặc "/S"</summary>
    [JsonPropertyName("installerArgs")]
    public string InstallerArgs { get; set; } = string.Empty;

    /// <summary>
    /// Nếu true, bỏ qua khi installer không tìm thấy (dependency optional).
    /// </summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}
