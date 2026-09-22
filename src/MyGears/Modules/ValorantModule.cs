using System.Windows;
using MyGears.Views.Modules;

namespace MyGears.Modules;

/// <summary>
/// Module Soi Kho Skin & Cửa Hàng Valorant (Valorant Inspector)
/// </summary>
public class ValorantModule : IGearModule
{
    private UIElement? _view;

    public string Id => "valorant";
    public string DisplayNameVi => "Soi Valorant";
    public string DisplayNameEn => "Valorant Inspector";
    public string Icon => "🎯";
    public int Order => 3;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public UIElement GetView() => _view ??= new ValorantModuleView();

    public Task InitializeAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _view = null;
        return ValueTask.CompletedTask;
    }
}
