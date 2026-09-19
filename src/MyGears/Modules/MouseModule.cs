using System.Windows;
using MyGears.Views.Modules;

namespace MyGears.Modules;

/// <summary>
/// Module Chuột — launch ScyRox V6 như process con và nhúng cửa sổ của nó
/// vào trong khung UI của MyGears bằng Win32 SetParent.
/// Đường dẫn ScyRox.exe được lấy từ USB root — không hardcode.
/// </summary>
public class MouseModule : IGearModule
{
    private MouseModuleView? _view;

    public string Id => "mouse";
    public string DisplayNameVi => "Chuột";
    public string DisplayNameEn => "Mouse";
    public string Icon => "🖱️";
    public int Order => 2;
    public bool IsAvailable { get; private set; } = true;
    public string? UnavailableReason { get; private set; }

    public UIElement GetView()
    {
        _view ??= new MouseModuleView();
        return _view;
    }

    public async Task InitializeAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _view ??= new MouseModuleView();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_view != null)
            await Application.Current.Dispatcher.InvokeAsync(() => _view.KillScyRox());
    }
}
