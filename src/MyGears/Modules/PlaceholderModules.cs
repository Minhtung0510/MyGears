using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyGears.Modules;

/// <summary>Placeholder module cho Tai nghe — sẽ implement sau.</summary>
public class HeadsetModule : IGearModule
{
    public string Id => "headset";
    public string DisplayNameVi => "Tai nghe";
    public string DisplayNameEn => "Headset";
    public string Icon => "🎧";
    public int Order => 4;
    public bool IsAvailable => false;
    public string? UnavailableReason => "Chức năng sắp ra mắt / Coming soon";

    public UIElement GetView() => CreatePlaceholder();
    public Task InitializeAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static UIElement CreatePlaceholder() => new Grid
    {
        Children =
        {
            new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "🎧", FontSize = 64, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Tai nghe", FontSize = 24, Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 8) },
                    new TextBlock { Text = "Sắp ra mắt / Coming soon", FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        HorizontalAlignment = HorizontalAlignment.Center },
                }
            }
        }
    };
}

/// <summary>Module Khác — Công cụ, quản lý ẩn hiện thư mục USB và thông tin hệ thống.</summary>
public class OtherModule : IGearModule
{
    private UIElement? _view;

    public string Id => "other";
    public string DisplayNameVi => "Khác";
    public string DisplayNameEn => "Other";
    public string Icon => "⚙️";
    public int Order => 5;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public UIElement GetView() => _view ??= new Views.Modules.OtherModuleView();
    public Task InitializeAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync()
    {
        _view = null;
        return ValueTask.CompletedTask;
    }
}
