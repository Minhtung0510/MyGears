namespace MyGears.Modules;

/// <summary>
/// Đăng ký và quản lý tất cả IGearModule trong app.
/// Thêm gear mới → thêm vào danh sách _modules bên dưới.
/// </summary>
public static class ModuleRegistry
{
    private static readonly List<IGearModule> _modules =
    [
        new KeyboardModule(),
        new MouseModule(),
        new AccountsModule(),
        new HeadsetModule(),
        new OtherModule(),
    ];

    public static IReadOnlyList<IGearModule> All => _modules;

    public static IGearModule? Get(string id)
        => _modules.FirstOrDefault(m => m.Id == id);
}
