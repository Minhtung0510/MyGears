using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class OtherModuleView : UserControl
{
    private readonly ObservableCollection<DownloadableApp> _apps = new();
    private bool _appsFetched;

    public OtherModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateStatusDisplay();

            // Bind danh sách app
            AppListControl.ItemsSource = _apps;

            // Hiển thị cấu hình Token GitHub
            UpdateTokenStatusDisplay();

            // Tự động fetch lần đầu
            if (!_appsFetched)
            {
                _appsFetched = true;
                _ = RefreshAppsAsync();
            }
        };
    }

    // ──────────────────────────────────────────────
    //  App Download Manager
    // ──────────────────────────────────────────────

    private async Task RefreshAppsAsync()
    {
        AppLoadingPanel.Visibility = Visibility.Visible;
        AppEmptyPanel.Visibility = Visibility.Collapsed;

        try
        {
            BtnRefreshApps.IsEnabled = false;
            var apps = await AppDownloadService.FetchAppsFromGitHubReleasesAsync();

            _apps.Clear();
            foreach (var app in apps)
                _apps.Add(app);

            TxtAppCount.Text = $"{_apps.Count} app";
            AppEmptyPanel.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowAppActionMessage($"❌ Lỗi kết nối GitHub: {ex.Message}", isSuccess: false);
        }
        finally
        {
            AppLoadingPanel.Visibility = Visibility.Collapsed;
            BtnRefreshApps.IsEnabled = true;
        }
    }

    private async void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
        ShowAppActionMessage("🔄 Đã đồng bộ từ GitHub Releases và quét tất cả file trên USB!", isSuccess: true);
    }

    private async void DownloadAndInstallApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DownloadableApp app) return;
        if (app.IsDownloading) return;

        ShowAppActionMessage($"⬇ Đang tải {app.Name}…", isSuccess: true);

        bool success = await AppDownloadService.DownloadAndInstallAsync(app, (msg, progress) =>
        {
            Dispatcher.Invoke(() => ShowAppActionMessage(msg, isSuccess: true));
        });

        if (success)
        {
            ShowAppActionMessage($"✅ Đã tải và cài đặt {app.Name} thành công!", isSuccess: true);
        }
        else
        {
            ShowAppActionMessage($"❌ Lỗi khi tải {app.Name}. Vui lòng thử lại.", isSuccess: false);
        }
    }

    private async void InstallOfflineApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DownloadableApp app) return;
        if (app.IsInstalling || app.IsDownloading) return;

        ShowAppActionMessage($"⚡ Đang khởi chạy cài đặt {app.Name}…", isSuccess: true);

        bool success = await AppDownloadService.InstallExistingOfflineAppAsync(app, (msg, progress) =>
        {
            Dispatcher.Invoke(() => ShowAppActionMessage(msg, isSuccess: true));
        });

        if (success)
        {
            ShowAppActionMessage($"✅ Đã hoàn tất tiến trình cài đặt {app.Name}!", isSuccess: true);
        }
        else
        {
            ShowAppActionMessage($"❌ Quá trình cài đặt {app.Name} chưa hoàn tất hoặc bị hủy.", isSuccess: false);
        }
    }

    private void LaunchApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DownloadableApp app) return;

        bool launched = AppDownloadService.LaunchInstalledApp(app);
        if (launched)
        {
            ShowAppActionMessage($"🚀 Đã mở ứng dụng {app.Name}!", isSuccess: true);
        }
        else
        {
            ShowAppActionMessage($"⚠️ Không thể khởi chạy tự động {app.Name}. Bạn có thể mở từ Desktop hoặc Start Menu.", isSuccess: false);
        }
    }

    private async void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DownloadableApp app) return;

        var result = MessageBox.Show(
            $"Bạn có chắc muốn xóa file này?\n\n📄 {app.FileName}\n\nThao tác này sẽ xóa file trên máy/USB VÀ xóa vĩnh viễn khỏi GitHub Releases!",
            "MyGears - Xóa File & GitHub Release",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        ShowAppActionMessage($"🗑️ Đang xóa {app.FileName} trên máy và GitHub Releases…", isSuccess: true);

        // 1. Xóa file trên máy/USB
        bool localDeleted = AppDownloadService.DeleteDownloadedApp(app);

        // 2. Xóa trên GitHub Releases nếu đã có Token
        var token = SettingsService.Current.GitHubToken?.Trim();
        bool gitDeleted = false;
        string gitMsg = string.Empty;

        if (!string.IsNullOrWhiteSpace(token))
        {
            (gitDeleted, gitMsg) = await AppDownloadService.DeleteAssetFromGitHubReleaseAsync(app.FileName, token);
        }

        // 3. Cập nhật danh sách hiển thị
        if (gitDeleted)
        {
            _apps.Remove(app);
            TxtAppCount.Text = $"{_apps.Count} app";
            AppEmptyPanel.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowAppActionMessage($"🗑️ Đã xóa {app.FileName} trên máy tính và trên GitHub Releases thành công!", isSuccess: true);
        }
        else if (localDeleted)
        {
            if (app.Source != "github")
            {
                _apps.Remove(app);
                TxtAppCount.Text = $"{_apps.Count} app";
                AppEmptyPanel.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ShowAppActionMessage($"🗑️ Đã xóa file {app.FileName} khỏi USB.", isSuccess: true);
            }
            else
            {
                AppDownloadService.UpdateAppStatus(app);
                ShowAppActionMessage($"🗑️ Đã xóa file bộ cài {app.FileName} trên máy. (App vẫn sẵn sàng để tải lại từ GitHub).", isSuccess: true);
            }
        }
        else
        {
            ShowAppActionMessage($"⚠️ Đã xóa trên máy, nhưng GitHub phản hồi: {gitMsg}", isSuccess: false);
        }
    }

    // ──────────────────────────────────────────────
    //  Drag & Drop Handlers
    // ──────────────────────────────────────────────

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x24, 0x36));
            DropZoneBorder.BorderBrush = (Brush)FindResource("AccentGold");
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x18));
        DropZoneBorder.BorderBrush = (Brush)FindResource("AccentBrass");
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x18));
        DropZoneBorder.BorderBrush = (Brush)FindResource("AccentBrass");

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        await ProcessDroppedFilesAsync(files);
    }

    private async Task ProcessDroppedFilesAsync(string[] files)
    {
        var token = SettingsService.Current.GitHubToken?.Trim();
        int addedCount = 0;

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".exe" && ext != ".msi" && ext != ".zip")
            {
                ShowAppActionMessage($"⚠️ Bỏ qua {Path.GetFileName(file)}: Chỉ hỗ trợ file .exe, .msi, .zip", isSuccess: false);
                continue;
            }

            var fileName = Path.GetFileName(file);
            var destPath = Path.Combine(AppDownloadService.DownloadDir, fileName);

            try
            {
                ShowAppActionMessage($"⏳ Đang lưu {fileName} vào USB ({AppDownloadService.DownloadDir})…", isSuccess: true);

                if (!string.Equals(file, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() => File.Copy(file, destPath, overwrite: true));
                }

                // Cập nhật hoặc thêm vào danh sách
                var existing = _apps.FirstOrDefault(a => a.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.LocalFilePath = destPath;
                    existing.SizeBytes = new FileInfo(destPath).Length;
                    AppDownloadService.UpdateAppStatus(existing);
                }
                else
                {
                    var app = new DownloadableApp
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName).Replace("-", " ").Replace("_", " "),
                        FileName = fileName,
                        DownloadUrl = string.Empty,
                        SizeBytes = new FileInfo(destPath).Length,
                        Source = "dropped",
                        LocalFilePath = destPath
                    };
                    AppDownloadService.UpdateAppStatus(app);
                    _apps.Add(app);
                }

                addedCount++;
                TxtAppCount.Text = $"{_apps.Count} app";
                AppEmptyPanel.Visibility = Visibility.Collapsed;

                // Tự động đẩy file lên GitHub Releases
                if (!string.IsNullOrWhiteSpace(token))
                {
                    ShowAppActionMessage($"☁️ Đang tự động đẩy {fileName} lên GitHub Releases…", isSuccess: true);
                    var (okUpload, msgUpload) = await AppDownloadService.UploadAssetToGitHubReleaseAsync(
                        destPath,
                        token,
                        (statusMsg, _) => Dispatcher.Invoke(() => ShowAppActionMessage(statusMsg, isSuccess: true)));

                    if (okUpload)
                    {
                        ShowAppActionMessage($"✅ Đã lưu vào USB và đẩy {fileName} lên GitHub Releases thành công!", isSuccess: true);
                        await RefreshAppsAsync();
                    }
                    else
                    {
                        ShowAppActionMessage($"⚠️ Đã lưu vào USB, nhưng lỗi đẩy lên GitHub: {msgUpload}", isSuccess: false);
                    }
                }
                else
                {
                    ShowAppActionMessage($"✅ Đã lưu {fileName} vào USB! (Vui lòng nhập GitHub Token bên dưới nếu bạn muốn tự động đẩy lên Git).", isSuccess: true);
                }
            }
            catch (Exception ex)
            {
                ShowAppActionMessage($"❌ Lỗi xử lý {fileName}: {ex.Message}", isSuccess: false);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  GitHub Token Management
    // ──────────────────────────────────────────────

    private void UpdateTokenStatusDisplay()
    {
        var token = SettingsService.Current.GitHubToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            PbGitHubToken.Password = token;
            TxtTokenStatus.Text = "✅ Đã cấu hình Token (Sẵn sàng tự động upload)";
            TxtTokenStatus.Foreground = (Brush)FindResource("VintageEmerald");
        }
        else
        {
            TxtTokenStatus.Text = "⚠️ Chưa có Token (Nhập Token để tự động upload)";
            TxtTokenStatus.Foreground = (Brush)FindResource("AccentAmber");
        }
    }

    private async void BtnSaveToken_Click(object sender, RoutedEventArgs e)
    {
        var token = PbGitHubToken.Password?.Trim() ?? string.Empty;
        SettingsService.Current.GitHubToken = token;
        await SettingsService.SaveAsync();

        UpdateTokenStatusDisplay();

        if (!string.IsNullOrWhiteSpace(token))
            ShowAppActionMessage("✅ Đã lưu GitHub Token vào USB! Giờ bạn có thể kéo thả file để tự động đẩy lên GitHub.", isSuccess: true);
        else
            ShowAppActionMessage("🗑️ Đã xóa GitHub Token.", isSuccess: true);
    }

    private void BtnGetTokenGuide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = "https://github.com/settings/tokens/new?scopes=repo&description=MyGears-App";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            ShowAppActionMessage("🌐 Đã mở trang tạo GitHub Token. Bạn chỉ cần bấm 'Generate token' ở cuối trang và copy mã ghp_... dán vào đây.", isSuccess: true);
        }
        catch (Exception ex)
        {
            ShowAppActionMessage($"❌ Lỗi mở trình duyệt: {ex.Message}", isSuccess: false);
        }
    }

    private void ShowAppActionMessage(string msg, bool isSuccess)
    {
        TxtAppActionMsg.Text = msg;
        TxtAppActionMsg.Foreground = isSuccess
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
        TxtAppActionMsg.Visibility = Visibility.Visible;
    }

    // ──────────────────────────────────────────────
    //  USB Folder Management (Existing)
    // ──────────────────────────────────────────────

    private string GetUsbTargetRoot()
    {
        if (UsbPathResolver.IsRunningFromUsb)
            return UsbPathResolver.UsbRoot;

        return UsbPathResolver.FindConnectedUsbRoot() ?? UsbPathResolver.UsbRoot;
    }

    private void UpdateStatusDisplay()
    {
        try
        {
            var usb = GetUsbTargetRoot();
            var appDir = Path.Combine(usb, "App");
            if (Directory.Exists(appDir))
            {
                var attr = File.GetAttributes(appDir);
                bool isHidden = attr.HasFlag(FileAttributes.Hidden);

                if (isHidden)
                {
                    TxtHiddenStatus.Text = "🔒 Đang Ẩn (Gọn gàng trên USB)";
                    StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x2E, 0x1B));
                    StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    TxtHiddenStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                }
                else
                {
                    TxtHiddenStatus.Text = "👁️ Đang Hiện (Hiển thị)";
                    StatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x20, 0x10));
                    StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    TxtHiddenStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                }
            }
            else
            {
                TxtHiddenStatus.Text = "Chưa kết nối USB";
                TxtHiddenStatus.Foreground = Brushes.Gray;
            }
        }
        catch
        {
            TxtHiddenStatus.Text = "Không xác định";
        }
    }

    private void SetFolderHidden(string path, bool hidden)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return;

        try
        {
            var attr = File.GetAttributes(path);
            if (hidden)
                attr |= FileAttributes.Hidden;
            else
                attr &= ~FileAttributes.Hidden;

            File.SetAttributes(path, attr);
        }
        catch { }
    }

    private void BtnUnhideFolders_Click(object sender, RoutedEventArgs e)
    {
        var usb = GetUsbTargetRoot();
        if (string.IsNullOrEmpty(usb) || !Directory.Exists(usb))
        {
            ShowActionMessage("⚠️ Không tìm thấy ổ USB để thực hiện thao tác.", isSuccess: false);
            return;
        }

        SetFolderHidden(Path.Combine(usb, "Download"), hidden: false);
        SetFolderHidden(Path.Combine(usb, "App"), hidden: false);
        SetFolderHidden(Path.Combine(usb, "src"), hidden: false);
        SetFolderHidden(Path.Combine(usb, "autorun.inf"), hidden: false);

        UpdateStatusDisplay();
        ShowActionMessage("✅ Đã HIỆN tất cả thư mục (Download, App và src) trên USB!", isSuccess: true);
    }

    private void BtnHideFolders_Click(object sender, RoutedEventArgs e)
    {
        var usb = GetUsbTargetRoot();
        if (string.IsNullOrEmpty(usb) || !Directory.Exists(usb))
        {
            ShowActionMessage("⚠️ Không tìm thấy ổ USB để thực hiện thao tác.", isSuccess: false);
            return;
        }

        SetFolderHidden(Path.Combine(usb, "Download"), hidden: true);
        SetFolderHidden(Path.Combine(usb, "App"), hidden: true);
        SetFolderHidden(Path.Combine(usb, "src"), hidden: true);
        SetFolderHidden(Path.Combine(usb, "autorun.inf"), hidden: true);

        UpdateStatusDisplay();
        ShowActionMessage("🔒 Đã ẨN tất cả thư mục! Giờ USB chỉ hiện file Cài Đặt.", isSuccess: true);
    }

    private void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusDisplay();
        ShowActionMessage("🔄 Đã cập nhật trạng thái mới nhất.", isSuccess: true);
    }

    private void ShowActionMessage(string msg, bool isSuccess)
    {
        TxtFolderActionMsg.Text = msg;
        TxtFolderActionMsg.Foreground = isSuccess
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
        TxtFolderActionMsg.Visibility = Visibility.Visible;
    }

    private void OpenDownloadDir_Click(object sender, MouseButtonEventArgs e)
    {
        OpenFolderInExplorer(UsbPathResolver.DownloadDir);
    }

    private void OpenUsbRootDir_Click(object sender, MouseButtonEventArgs e)
    {
        var usb = GetUsbTargetRoot();
        OpenFolderInExplorer(usb);
    }

    private void OpenAppDir_Click(object sender, MouseButtonEventArgs e)
    {
        OpenFolderInExplorer(UsbPathResolver.AppDir);
    }

    private void OpenLocalDeployDir_Click(object sender, MouseButtonEventArgs e)
    {
        OpenFolderInExplorer(UsbPathResolver.LocalDeployTargetDir);
    }

    private static void OpenFolderInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"Thư mục chưa tồn tại:\n{path}", "MyGears", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "MyGears - Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

