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

        // Tự động quét thêm các asset driver mới nhất từ GitHub Releases
        _ = RefreshCloudAssetsAsync();
    }

    private async Task RefreshCloudAssetsAsync()
    {
        try
        {
            var cloudAssets = await CloudDownloadService.FetchLatestReleaseAssetsAsync();
            if (cloudAssets == null || cloudAssets.Count == 0) return;

            var targetRoot = UsbPathResolver.LocalDeployTargetDir;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var asset in cloudAssets)
                {
                    // Bỏ qua MyGears-App.zip (đây là app core bundle)
                    if (asset.Name.StartsWith("MyGears-App", StringComparison.OrdinalIgnoreCase)) continue;

                    var baseName = System.IO.Path.GetFileNameWithoutExtension(asset.Name);
                    var id = $"cloud_{baseName.ToLowerInvariant()}";

                    var existing = Components.FirstOrDefault(c =>
                        c.Id == id ||
                        c.Id.EndsWith(baseName.ToLowerInvariant()) ||
                        c.Name.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.CloudDownloadUrl) && c.CloudDownloadUrl.Contains(asset.Name, StringComparison.OrdinalIgnoreCase)));

                    if (existing != null)
                    {
                        existing.CloudDownloadUrl = asset.DownloadUrl;
                        existing.SizeBytes = asset.SizeBytes;
                        existing.SizeDisplay = $"{DriverDiscoveryService.FormatSize(asset.SizeBytes)} (Cloud)";
                    }
                    else
                    {
                        var newComp = DriverDiscoveryService.CreateComponentFromCloudAsset(asset, targetRoot);
                        newComp.PropertyChanged += Comp_PropertyChanged;

                        int insertIndex = Components.Count;
                        var shortcut = Components.FirstOrDefault(c => c.Id == "desktop_shortcut");
                        if (shortcut != null)
                        {
                            var idx = Components.IndexOf(shortcut);
                            if (idx >= 0) insertIndex = idx;
                        }

                        Components.Insert(insertIndex, newComp);
                    }
                }
                UpdateSummary();
            });
        }
        catch { }
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
            ProgressStatusText = "🚀 Đang mở ứng dụng MyGears trên máy tính...";
            IsCompleted = true;
            CompletedMessage = "🎉 Đã cài đặt thành công MyGears vào máy tính!\n\n👉 Ứng dụng đang mở trên máy tính, đang đóng bộ cài USB...";

            // Tự động đóng bộ cài USB để chỉ giữ lại duy nhất 1 cửa sổ ứng dụng chính trên máy tính
            await Task.Delay(900);
            Application.Current.Dispatcher.Invoke(() =>
            {
                RequestClose?.Invoke();
                Application.Current.Shutdown();
            });
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

    [RelayCommand]
    public void LaunchDirectly()
    {
        RequestClose?.Invoke();
        var depWindow = new MyGears.Views.DependencyCheckWindow();
        Application.Current.MainWindow = depWindow;
        depWindow.Show();
    }
}
