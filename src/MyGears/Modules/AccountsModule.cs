using System.Windows;
using MyGears.Views.Modules;

namespace MyGears.Modules;

/// <summary>
/// Module Kho Tài Khoản Game (Mã hóa AES-256, chống chụp màn hình, copy tk:mk, tự hủy)
/// </summary>
public class AccountsModule : IGearModule
{
    private AccountsModuleView? _view;

    public string Id => "accounts";
    public string DisplayNameVi => "Tài Khoản";
    public string DisplayNameEn => "Game Accounts";
    public string Icon => "🔑";
    public int Order => 3;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public UIElement GetView()
    {
        _view ??= new AccountsModuleView();
        return _view;
    }

    public ValueTask DisposeAsync()
    {
        _view = null;
        return ValueTask.CompletedTask;
    }
}
