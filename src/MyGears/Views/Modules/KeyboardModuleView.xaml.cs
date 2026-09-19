using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class KeyboardModuleView : UserControl
{
    private bool _webViewReady = false;

    public KeyboardModuleView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitWebViewAsync();
    }

    private int _retryCount = 0;
    private const int MaxRetries = 3;

    public async Task InitWebViewAsync()
    {
        if (_webViewReady) return;

        try
        {
            StatusBadge.Text = "● Đang khởi tạo…";
            StatusBadge.Foreground = Brushes.Orange;

            // UserDataFolder cho WebView2 — lưu trong LocalAppData của máy để tăng tốc độ và không làm phình dung lượng USB
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyGears", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await WebView.EnsureCoreWebView2Async(env);
            _webViewReady = true;

            // Tự động cấp quyền WebHID / WebUSB để web kết nối bàn phím
            WebView.CoreWebView2.PermissionRequested += (s, args) =>
            {
                args.State = CoreWebView2PermissionState.Allow;
            };

            // Bỏ qua lỗi chứng chỉ SSL nếu server Trung Quốc có chứng chỉ nội địa
            WebView.CoreWebView2.ServerCertificateErrorDetected += (s, args) =>
            {
                args.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            };

            // Cấu hình WebView2
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

            // Xử lý sự kiện điều hướng
            WebView.CoreWebView2.NavigationStarting += (_, _) =>
            {
                LoadingTitle.Text = "Đang tải cấu hình bàn phím Nano 68…";
                LoadingDetail.Text = "Đang kết nối tới máy chủ hub.fgg.com.cn…";
                LoadingProgressBar.Visibility = Visibility.Visible;
                BtnRetry.Visibility = Visibility.Collapsed;
                LoadingOverlay.Visibility = Visibility.Visible;
                StatusBadge.Text = "● Đang tải…";
                StatusBadge.Foreground = Brushes.Yellow;
            };

            WebView.CoreWebView2.NavigationCompleted += async (s, args) =>
            {
                if (args.IsSuccess)
                {
                    _retryCount = 0;
                    // Chờ thêm một chút để bundle JS (Vue) render giao diện vào DOM
                    await Task.Delay(1000);
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    StatusBadge.Text = "● Đã kết nối";
                    StatusBadge.Foreground = Brushes.LightGreen;
                }
                else
                {
                    if (_retryCount < MaxRetries)
                    {
                        _retryCount++;
                        LoadingTitle.Text = "Đang thử kết nối lại…";
                        LoadingDetail.Text = $"Máy chủ phản hồi chậm ({args.WebErrorStatus}). Đang tự động thử lại lần {_retryCount}/{MaxRetries}…";
                        LoadingProgressBar.Visibility = Visibility.Visible;
                        BtnRetry.Visibility = Visibility.Collapsed;
                        StatusBadge.Text = $"● Thử lại ({_retryCount}/{MaxRetries})…";
                        StatusBadge.Foreground = Brushes.Orange;

                        await Task.Delay(2500);
                        if (_webViewReady && WebView.CoreWebView2 != null)
                        {
                            WebView.CoreWebView2.Navigate("https://hub.fgg.com.cn/");
                        }
                        return;
                    }

                    LoadingTitle.Text = "Không thể tải trang cấu hình bàn phím";
                    LoadingDetail.Text = $"Lỗi kết nối ({args.WebErrorStatus}). Do máy chủ hub.fgg.com.cn đặt tại Trung Quốc, kết nối mạng quốc tế có thể đang bị nghẽn.\n\nVui lòng bấm 'Thử tải lại' để kết nối lại.";
                    LoadingProgressBar.Visibility = Visibility.Collapsed;
                    BtnRetry.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Visible;
                    StatusBadge.Text = "● Lỗi mạng";
                    StatusBadge.Foreground = Brushes.Red;
                }
            };

            // Bắt đầu điều hướng tới trang web
            WebView.Source = new Uri("https://hub.fgg.com.cn/");
        }
        catch (Exception ex)
        {
            LoadingProgressBar.Visibility = Visibility.Collapsed;
            LoadingTitle.Text = "Không thể khởi động WebView2";
            LoadingDetail.Text = $"Lỗi: {ex.Message}";
            BtnRetry.Visibility = Visibility.Visible;
            StatusBadge.Text = "● Lỗi";
            StatusBadge.Foreground = Brushes.Red;
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _retryCount = 0;
        if (_webViewReady && WebView.CoreWebView2 != null)
        {
            WebView.Reload();
        }
        else
        {
            _ = InitWebViewAsync();
        }
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        _retryCount = 0;
        if (_webViewReady && WebView.CoreWebView2 != null)
        {
            WebView.CoreWebView2.Navigate("https://hub.fgg.com.cn/");
        }
        else
        {
            _ = InitWebViewAsync();
        }
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewReady)
        {
            WebView.ZoomFactor = Math.Min(WebView.ZoomFactor + 0.1, 2.0);
        }
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewReady)
        {
            WebView.ZoomFactor = Math.Max(WebView.ZoomFactor - 0.1, 0.5);
        }
    }

    public void DisposeWebView()
    {
        WebView?.Dispose();
    }
}
