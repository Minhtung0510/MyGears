using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using MyGears.Dependency;

namespace MyGears.ViewModels;

// ──────────────────────────────────────────────────────────────────
//  Model cho từng dòng trong danh sách dependency
// ──────────────────────────────────────────────────────────────────
public partial class DependencyItemViewModel : ObservableObject
{
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DependencyStatus _status = DependencyStatus.Pending;
    [ObservableProperty] private string _statusText = "Chờ kiểm tra…";

    public string StatusIcon => Status switch
    {
        DependencyStatus.Pending     => "⏸️",
        DependencyStatus.Checking    => "🔍",
        DependencyStatus.AlreadyOk   => "✅",
        DependencyStatus.Installing  => "⏳",
        DependencyStatus.Success     => "✅",
        DependencyStatus.Failed      => "❌",
        DependencyStatus.Skipped     => "⚠️",
        _ => "❓"
    };

    partial void OnStatusChanged(DependencyStatus value)
        => OnPropertyChanged(nameof(StatusIcon));
}

public enum DependencyStatus
{
    Pending, Checking, AlreadyOk, Installing, Success, Failed, Skipped
}

// ──────────────────────────────────────────────────────────────────
//  ViewModel chính cho DependencyCheckWindow
// ──────────────────────────────────────────────────────────────────
public partial class DependencyCheckViewModel : ObservableObject
{
    [ObservableProperty] private string _overallStatus = "Đang khởi động…";
    [ObservableProperty] private double _progress = 0;
    [ObservableProperty] private bool _isComplete = false;
    [ObservableProperty] private bool _hasErrors = false;

    public ObservableCollection<DependencyItemViewModel> Items { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    // Event để báo khi xong → chuyển sang MainWindow
    public event Action? AllDoneSuccessfully;

    // ──────────────────────────────────────────────
    //  Run
    // ──────────────────────────────────────────────

    [RelayCommand]
    public async Task RunCheckAsync()
    {
        OverallStatus = "🔍 Đang kiểm tra dependency…";
        IsComplete = false;
        HasErrors = false;
        Items.Clear();
        Logs.Clear();

        DependencyManifest manifest;
        try
        {
            AddLog("📦 Đọc manifest.json từ USB…");
            manifest = await DependencyChecker.LoadManifestAsync();
            AddLog($"✅ Tìm thấy {manifest.Dependencies.Count} dependency.");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Lỗi đọc manifest: {ex.Message}");
            OverallStatus = "❌ Không đọc được manifest.json";
            HasErrors = true;
            IsComplete = true;
            return;
        }

        // Tạo danh sách UI items
        foreach (var dep in manifest.Dependencies)
        {
            Items.Add(new DependencyItemViewModel
            {
                DisplayName = dep.DisplayName,
                Status = DependencyStatus.Pending,
                StatusText = "Chờ…"
            });
        }

        int total = manifest.Dependencies.Count;
        int done = 0;
        bool anyFailed = false;

        for (int i = 0; i < manifest.Dependencies.Count; i++)
        {
            var dep = manifest.Dependencies[i];
            var item = Items[i];

            // ── Bước 1: Check ────────────────────────────
            item.Status = DependencyStatus.Checking;
            item.StatusText = "Đang kiểm tra…";
            AddLog($"🔍 Kiểm tra: {dep.DisplayName}");

            bool installed = DependencyChecker.IsInstalled(dep);

            if (installed)
            {
                item.Status = DependencyStatus.AlreadyOk;
                item.StatusText = "Đã có sẵn";
                AddLog($"✅ {dep.DisplayName} — đã cài sẵn trên máy.");
            }
            else
            {
                // ── Bước 2: Cài silent ───────────────────
                item.Status = DependencyStatus.Installing;
                item.StatusText = "Đang cài đặt…";
                AddLog($"⏳ {dep.DisplayName} chưa có → Bắt đầu cài silent…");

                bool success = await DependencyInstaller.InstallAsync(
                    dep,
                    onProgress: msg => Application.Current.Dispatcher.Invoke(() => AddLog(msg)));

                if (success)
                {
                    item.Status = DependencyStatus.Success;
                    item.StatusText = "Đã cài xong";
                }
                else if (dep.Optional)
                {
                    item.Status = DependencyStatus.Skipped;
                    item.StatusText = "Bỏ qua (optional)";
                }
                else
                {
                    item.Status = DependencyStatus.Failed;
                    item.StatusText = "Cài thất bại";
                    anyFailed = true;
                }
            }

            done++;
            Progress = (double)done / total * 100;
        }

        // ── Kết thúc ─────────────────────────────────────────────
        IsComplete = true;
        HasErrors = anyFailed;

        if (anyFailed)
        {
            OverallStatus = "⚠️ Một số dependency cài thất bại. App có thể hoạt động không đầy đủ.";
            AddLog("⚠️ Hoàn tất với lỗi. Bạn có thể tiếp tục, nhưng một số tính năng có thể không hoạt động.");
        }
        else
        {
            OverallStatus = "✅ Tất cả dependency sẵn sàng!";
            AddLog("🚀 Tất cả OK — Chuyển sang giao diện chính…");

            // Auto-chuyển sang MainWindow sau 1.5 giây
            await Task.Delay(1500);
            Application.Current.Dispatcher.Invoke(() => AllDoneSuccessfully?.Invoke());
        }
    }

    // ──────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add($"[{timestamp}] {message}");
            // Limit log lines
            if (Logs.Count > 200) Logs.RemoveAt(0);
        });
    }
}
