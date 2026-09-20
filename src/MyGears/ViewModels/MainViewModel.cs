using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using MyGears.Core;
using MyGears.Modules;
using MyGears.Views;

namespace MyGears.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private IGearModule? _activeModule;
    [ObservableProperty] private string _usbRootDisplay = string.Empty;
    [ObservableProperty] private bool _isRunningFromUsb;
    [ObservableProperty] private bool _isDeployedLocally;
    [ObservableProperty] private bool _isDeploying;
    [ObservableProperty] private string _deployProgressText = string.Empty;
    [ObservableProperty] private bool _isSyncingToUsb;
    [ObservableProperty] private bool _isSelfDestructing;

    // Hardware Specs & Telemetry
    [ObservableProperty] private string _specsCpu = string.Empty;
    [ObservableProperty] private string _specsGpu = string.Empty;
    [ObservableProperty] private string _specsRam = string.Empty;
    [ObservableProperty] private string _specsOs = string.Empty;
    [ObservableProperty] private string _specsHost = string.Empty;
    [ObservableProperty] private string _specsDisk = string.Empty;
    [ObservableProperty] private string _specsNet = string.Empty;
    
    // Live Dynamics (CPU, RAM, Network)
    [ObservableProperty] private double _currentCpuLoad = 0;
    [ObservableProperty] private int _currentRamLoad = 0;
    [ObservableProperty] private double _ramUsedGb = 0;
    [ObservableProperty] private double _totalRamGb = 0;
    [ObservableProperty] private string _ramDetailText = string.Empty;

    // Network Telemetry
    [ObservableProperty] private long _netPingMs = 30;
    [ObservableProperty] private string _netPingText = "30 ms";
    [ObservableProperty] private string _netSpeedText = "↓ 0 KB/s  ↑ 0 KB/s";
    [ObservableProperty] private string _netAdapterText = "Ethernet";

    public ObservableCollection<IGearModule> Modules { get; } = [];

    public MainViewModel()
    {
        // Load tất cả module đã đăng ký
        var allModules = ModuleRegistry.All.OrderBy(m => m.Order);
        foreach (var m in allModules)
            Modules.Add(m);

        // Khôi phục tab cuối dùng từ settings
        var lastTab = SettingsService.Current.Ui.LastActiveTab;
        ActiveModule = Modules.FirstOrDefault(m => m.Id == lastTab) ?? Modules.FirstOrDefault();

        UsbRootDisplay = UsbPathResolver.IsInitialized ? UsbPathResolver.UsbRoot : "?";
        IsRunningFromUsb = UsbPathResolver.IsRunningFromUsb;
        IsDeployedLocally = UsbPathResolver.IsDeployedLocally;

        // Tải thông số cấu hình phần cứng của máy tính
        LoadSystemSpecs();
    }

    public void LoadSystemSpecs()
    {
        try
        {
            var specs = SystemSpecsService.GetSpecs();
            SpecsCpu = $"{specs.CpuName} ({specs.CpuThreads} luồng)";
            SpecsGpu = specs.GpuName;
            TotalRamGb = specs.TotalRamGb;
            RamUsedGb = specs.UsedRamGb;
            SpecsRam = $"{specs.TotalRamGb:F1} GB";
            RamDetailText = $"{specs.UsedRamGb:F1} GB / {specs.TotalRamGb:F1} GB";
            SpecsOs = specs.OsDisplay;
            SpecsHost = specs.HostName;
            SpecsDisk = specs.DiskDisplay;
            SpecsNet = specs.ActiveNetworkAdapter;
            CurrentRamLoad = specs.RamUsagePercent;
        }
        catch { }
    }

    public void UpdateLiveTelemetry()
    {
        try
        {
            var tel = SystemSpecsService.GetLiveTelemetry();
            CurrentCpuLoad = tel.CpuLoadPercent;
            CurrentRamLoad = tel.RamUsagePercent;
            RamUsedGb = tel.RamUsedGb;
            TotalRamGb = tel.RamTotalGb;
            RamDetailText = $"{tel.RamUsedGb:F1} GB / {tel.RamTotalGb:F1} GB";

            NetPingMs = tel.PingMs;
            NetPingText = tel.PingDisplay;
            NetSpeedText = tel.NetworkSpeedDisplay;
            NetAdapterText = tel.ActiveAdapterName;
        }
        catch { }
    }

    [RelayCommand]
    public void DeployToPc()
    {
        var setupWindow = new SetupWindow();
        setupWindow.Owner = Application.Current.MainWindow;
        setupWindow.ShowDialog();
    }

    [RelayCommand]
    public async Task SyncToUsbAsync()
    {
        if (IsSyncingToUsb) return;
        IsSyncingToUsb = true;
        try
        {
            await Task.Delay(250);
            var (success, msg) = DeploymentService.SyncSettingsBackToUsb();
            if (success)
            {
                MessageBox.Show(msg, "MyGears - Lưu cấu hình về USB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(msg, "MyGears - Lưu cấu hình về USB", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsSyncingToUsb = false;
        }
    }

    [RelayCommand]
    public async Task SelfDestructAsync()
    {
        if (IsSelfDestructing) return;
        var result = MessageBox.Show(
            "⚠️ BẠN CÓ CHẮC CHẮN MUỐN TỰ HỦY DỮ LIỆU TRÊN MÁY TÍNH KHÔNG?\n\n" +
            "• Toàn bộ MyGears và driver chuột trên ổ C:\\Users\\Public\\MyGears sẽ bị XÓA SẠCH 100%.\n" +
            "• Phím tắt ngoài Desktop, bộ nhớ cache và Clipboard sẽ được dọn sạch hoàn toàn.\n" +
            "• Không để lại bất kỳ dấu vết nào trên máy tính.\n\n" +
            "(Nếu bạn có cắm lại USB, app sẽ tự động đồng bộ cấu hình và tài khoản về USB trước khi hủy).\n\n" +
            "Bạn có đồng ý TỰ HỦY ngay bây giờ không?",
            "MyGears - Tự Hủy & Dọn Sạch Dữ Liệu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsSelfDestructing = true;
        await Task.Delay(250);

        // 1. Tự động đồng bộ ngược về USB nếu USB đang được cắm
        DeploymentService.SyncSettingsBackToUsb();

        // 2. Kích hoạt tự hủy toàn diện
        SecurityService.TriggerSelfDestruct(UsbPathResolver.LocalDeployTargetDir);
    }

    [RelayCommand]
    public async Task SelectModuleAsync(IGearModule module)
    {
        if (ActiveModule == module) return;

        ActiveModule = module;

        // Khởi tạo module nếu chưa
        if (module.IsAvailable)
            await module.InitializeAsync();

        // Lưu tab đang chọn vào settings
        SettingsService.Current.Ui.LastActiveTab = module.Id;
        await SettingsService.SaveAsync();
    }

    public async Task InitializeAllModulesAsync()
    {
        foreach (var module in Modules.Where(m => m.IsAvailable))
        {
            try { await module.InitializeAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Module:{module.Id}] Init error: {ex.Message}");
            }
        }
    }

    public async Task DisposeAllModulesAsync()
    {
        foreach (var module in Modules)
        {
            try { await module.DisposeAsync(); }
            catch { }
        }
    }
}
