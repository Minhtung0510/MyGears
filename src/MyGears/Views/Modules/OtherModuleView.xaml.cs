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
    public OtherModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateStatusDisplay();
    }

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
