using System.Windows;
using System.Windows.Controls;
using MyGears.Views.Modules;

namespace MyGears.Modules;

/// <summary>
/// Module Bàn phím — nhúng WebView2 load trang hub.fgg.com.cn để config
/// bàn phím Nano 68 App Pro trực tiếp trong app, không mở trình duyệt ngoài.
/// </summary>
public class KeyboardModule : IGearModule
{
    private KeyboardModuleView? _view;

    public string Id => "keyboard";
    public string DisplayNameVi => "Bàn phím";
    public string DisplayNameEn => "Keyboard";
    public string Icon => "⌨️";
    public int Order => 1;
    public bool IsAvailable { get; private set; } = true;
    public string? UnavailableReason { get; private set; }

    public UIElement GetView()
    {
        _view ??= new KeyboardModuleView();
        return _view;
    }

    public async Task InitializeAsync()
    {
        // WebView2 init xảy ra trong UserControl — gọi qua dispatcher
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            _view ??= new KeyboardModuleView();
            await _view.InitWebViewAsync();
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_view != null)
            await Application.Current.Dispatcher.InvokeAsync(() => _view.DisposeWebView());
    }
}
