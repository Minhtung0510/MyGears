using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGears.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace MyGears.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    public ObservableCollection<InstallableComponent> Components { get; } = new();

    [ObservableProperty] private string _selectedSummaryText = "Đang tính toán…";
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private double _installProgress;
    [ObservableProperty] private string _progressStatusText = "Sẵn sàng cài đặt…";
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private string _completedMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";

    public event Action? RequestClose;

    public SetupViewModel()
    {
        LoadComponents();
    }

    public void LoadComponents()
    {
        Components.Clear();
        var discovered = DriverDiscoveryService.DiscoverComponents();
        foreach (var comp in discovered)
        {
            comp.PropertyChanged += Comp_PropertyChanged;
            Components.Add(comp);
        }
        UpdateSummary();
    }

    private void Comp_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallableComponent.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        int skippedCount = Components.Count(c => c.IsAlreadyInstalled && !c.IsSelected);
        long totalBytes = selected.Sum(c => c.SizeBytes);
        string sizeStr = DriverDiscoveryService.FormatSize(totalBytes);

        if (skippedCount > 0)
        {
            SelectedSummaryText = $"Đã chọn {selected.Count} mục ({sizeStr}) • Bỏ qua {skippedCount} mục đã có sẵn";
        }
        else
        {
            SelectedSummaryText = $"Đã chọn {selected.Count} / {Components.Count} mục • Tổng cộng: {sizeStr}";
        }
    }

    [RelayCommand]
    public void SelectOnlyNew()
    {
        if (IsInstalling) return;
        foreach (var comp in Components)
        {
            if (comp.IsRequired)
            {
                comp.IsSelected = true;
            }
            else
            {
                // Chỉ chọn những mục chưa cài đặt trên máy
                comp.IsSelected = !comp.IsAlreadyInstalled;
            }
        }
    }

    [RelayCommand]
    public void SelectAll()
    {
        if (IsInstalling) return;
        foreach (var comp in Components)
        {
            comp.IsSelected = true;
        }
    }

    [RelayCommand]
    public void DeselectOptional()
    {
        if (IsInstalling) return;
        foreach (var comp in Components)
        {
            if (!comp.IsRequired)
            {
                comp.IsSelected = false;
            }
        }
    }

    [RelayCommand]
    public async Task StartInstallAsync()
    {
        if (IsInstalling) return;

        var selected = Components.Where(c => c.IsSelected).ToList();
        if (!selected.Any(c => c.Id == "core_app"))
        {
            MessageBox.Show(
                "Mục 'Ứng dụng MyGears Core' là bắt buộc để ứng dụng có thể hoạt động.",
                "MyGears Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsInstalling = true;
        HasError = false;
        InstallProgress = 0.05;
        ProgressStatusText = "Đang chuẩn bị sao chép…";

        bool success = await DeploymentService.DeploySelectedComponentsAsync(
            Components,
            (status, progress) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ProgressStatusText = status;
                    InstallProgress = Math.Clamp(progress, 0.0, 1.0);
                });
            });

        if (success)
        {
            InstallProgress = 1.0;
            ProgressStatusText = "Hoàn tất 100%!";
            IsCompleted = true;
            CompletedMessage = "🎉 Đã cài đặt thành công MyGears vào máy tính!\n\n👉 Bạn hãy RÚT USB RA và cắm Chuột / Bàn phím vào máy để bắt đầu chơi game nhé!";
        }
        else
        {
            IsInstalling = false;
            HasError = true;
            ErrorMessage = "Đã xảy ra lỗi trong quá trình cài đặt. Vui lòng kiểm tra quyền ghi hoặc thử lại.";
        }
    }

    [RelayCommand]
    public void FinishAndExit()
    {
        RequestClose?.Invoke();
        Application.Current.Shutdown();
    }
}
