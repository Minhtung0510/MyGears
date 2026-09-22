using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MyGears.Core;

namespace MyGears.Views;

public partial class ValorantLoginWindow : Window
{
    private ulong _navigationId;
    private bool _closed;
    private bool _emailScopeFallbackUsed;

    public string? ResultAccessToken { get; private set; }
    public string? ResultIdToken { get; private set; }
    public string? PrefillUsername { get; set; }
    public string? PrefillPassword { get; set; }

    public ValorantLoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CheckAccountMotion.Reveal((FrameworkElement)Content);
        Loaded += ValorantLoginWindow_Loaded;
    }

    private async void ValorantLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitWebView2Async();
    }

    private async Task InitWebView2Async()
    {
        try
        {
            var userDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyGears", "RiotWebView2");
            Directory.CreateDirectory(userDir);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDir);
            await Browser.EnsureCoreWebView2Async(env);

            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // Xóa cookies phiên cũ để tránh dính tài khoản đăng nhập trước
            try
            {
                Browser.CoreWebView2.CookieManager.DeleteAllCookies();
            }
            catch { }

            Browser.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
            Browser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            Browser.CoreWebView2.Navigate(ValorantApiService.RiotAuthUrl);

            TxtStatus.Text = !string.IsNullOrEmpty(PrefillUsername)
                ? $"Đang tải trang đăng nhập cho tài khoản: {PrefillUsername}..."
                : "Đang mở trang đăng nhập Riot Games...";
            LampStatus.Background = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)); // Xanh dương
        }
        catch (Exception)
        {
            PanelWvError.Visibility = Visibility.Visible;
            TxtStatus.Text = "Lỗi khởi tạo WebView2. Vui lòng dán link redirect thủ công bên dưới.";
            LampStatus.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Đỏ
        }
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _navigationId = e.NavigationId;
        if (!_emailScopeFallbackUsed && Uri.TryCreate(e.Uri, UriKind.Absolute, out var redirect)
            && redirect.Host == "localhost" && redirect.AbsolutePath == "/redirect"
            && e.Uri.Contains("error=invalid_scope", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            _emailScopeFallbackUsed = true;
            TxtStatus.Text = "Riot chưa cấp quyền email. Tiếp tục đăng nhập với quyền cơ bản.";
            Dispatcher.BeginInvoke(new Action(() => Browser.CoreWebView2.Navigate(ValorantApiService.RiotAuthUrl.Replace("%20email", ""))));
            return;
        }
        // Khi Riot redirect về http://localhost/redirect#access_token=...
        // Cancel ngay để WebView2 không tải trang localhost (sẽ hiện lỗi kết nối)
        if (e.Uri.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            CheckAndCaptureTokens(e.Uri);
            return;
        }
        CheckAndCaptureTokens(e.Uri);
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (Browser.CoreWebView2 != null)
        {
            CheckAndCaptureTokens(Browser.CoreWebView2.Source);
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess && Browser.CoreWebView2 != null)
        {
            var currentUri = Browser.CoreWebView2.Source;

            // Nếu vừa hoàn tất logout trên Riot -> lập tức chuyển hướng về trang authorize sạch sẽ
            if (currentUri.Contains("auth.riotgames.com/logout", StringComparison.OrdinalIgnoreCase))
            {
                TxtStatus.Text = "Đã đăng xuất phiên cũ thành công! Đang tải trang đăng nhập mới...";
                Browser.CoreWebView2.Navigate(ValorantApiService.RiotAuthUrl);
                return;
            }

            TxtStatus.Text = !string.IsNullOrEmpty(PrefillUsername)
                ? $"Đã tải trang Riot cho tài khoản: {PrefillUsername}"
                : "Đã tải xong trang Riot. Vui lòng đăng nhập tài khoản của bạn.";
            LampStatus.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Xanh lục

            // Tự động điền username & password vào ô input của Riot nếu có
            if (!string.IsNullOrEmpty(PrefillUsername) || !string.IsNullOrEmpty(PrefillPassword))
            {
                try
                {
                    var navigationId = e.NavigationId;
                    int stablePasses = 0;
                    TxtStatus.Text = "Đang chờ ô đăng nhập Riot để tự điền...";
                    for (int attempt = 0; attempt < 120; attempt++)
                    {
                        if (_closed || Browser.CoreWebView2 == null || navigationId != _navigationId) break;
                        var result = await Browser.CoreWebView2.ExecuteScriptAsync(
                            RiotLoginPrefill.BuildScript(PrefillUsername, PrefillPassword));
                        if (result == "\"filled\"")
                        {
                            if (++stablePasses >= 2)
                            {
                                TxtStatus.Text = "Đã điền thông tin. Bạn kiểm tra rồi bấm đăng nhập.";
                                return;
                            }
                        }
                        else stablePasses = 0;
                        if (result == "\"blocked\"")
                        {
                            TxtStatus.Text = "Trang hiện tại không phải trang đăng nhập Riot được hỗ trợ.";
                            return;
                        }
                        if (result == "\"editing\"")
                        {
                            TxtStatus.Text = "Giữ nguyên nội dung bạn đang nhập. Hãy tiếp tục đăng nhập.";
                            return;
                        }
                        await Task.Delay(250);
                    }
                    if (!_closed && navigationId == _navigationId)
                        TxtStatus.Text = "Chưa tìm thấy ô đăng nhập. Bạn có thể dùng Copy TK / Copy MK để dán.";
                }
                catch
                {
                    if (!_closed) TxtStatus.Text = "Chưa tự điền được. Hãy dùng Copy TK / Copy MK để dán.";
                }
            }
        }
    }

    private void BtnCopyTk_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PrefillUsername))
        {
            SecurityService.SafeSetClipboard(PrefillUsername);
            TxtStatus.Text = $"📋 Đã copy Tên đăng nhập: {PrefillUsername}";
        }
    }

    private void BtnCopyMk_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PrefillPassword))
        {
            SecurityService.SafeSetClipboard(PrefillPassword);
            TxtStatus.Text = "🔑 Đã copy Mật khẩu vào bộ nhớ tạm!";
        }
    }

    private void BtnCopyCombo_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(PrefillUsername) && !string.IsNullOrEmpty(PrefillPassword))
        {
            SecurityService.SafeSetClipboard($"{PrefillUsername}:{PrefillPassword}");
            TxtStatus.Text = "📝 Đã copy theo định dạng tk:mk!";
        }
    }

    private void BtnLogoutRiot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Browser.CoreWebView2 != null)
            {
                Browser.CoreWebView2.CookieManager.DeleteAllCookies();
                Browser.CoreWebView2.Navigate("https://auth.riotgames.com/logout");
                TxtStatus.Text = "Đang đăng xuất khỏi auth.riotgames.com/logout để nhập tài khoản mới...";
                LampStatus.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    public static void ForceRestoreCursor()
    {
        try
        {
            ReleaseCapture();
            Mouse.Capture(null);
            Mouse.OverrideCursor = null;
            while (ShowCursor(true) < 0) { }
            SetCursor(LoadCursor(IntPtr.Zero, 32512)); // 32512 = IDC_ARROW
        }
        catch { }
    }

    private void CheckAndCaptureTokens(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return;

        // Riot redirect về playvalorant.com/opt_in#access_token=...
        if (uri.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
        {
            var (access, id) = ValorantApiService.ParseTokensFromUrl(uri);
            if (!string.IsNullOrEmpty(access))
            {
                ResultAccessToken = access;
                ResultIdToken = id;

                TxtStatus.Text = "✅ Đã bắt được Token thành công! Đang chuyển dữ liệu...";
                LampStatus.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

                // Giải phóng ngay chuột khỏi WebView2 để tránh kẹt hoặc mất con trỏ chuột trong Windows
                ForceRestoreCursor();

                // Trì hoãn 120ms để WebView2 hoàn tất vòng lặp message loop trước khi đóng cửa sổ
                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(120);
                    ForceRestoreCursor();
                    DialogResult = true;
                    Close();
                });
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
        ForceRestoreCursor();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ForceRestoreCursor();
        DialogResult = false;
        Close();
    }

    private void BtnConfirmManualUrl_Click(object sender, RoutedEventArgs e)
    {
        var raw = TxtManualUrl.Text.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            MessageBox.Show("Vui lòng dán link redirect từ thanh địa chỉ trình duyệt vào đây.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (access, id) = ValorantApiService.ParseTokensFromUrl(raw);
        if (!string.IsNullOrEmpty(access))
        {
            ResultAccessToken = access;
            ResultIdToken = id;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Không tìm thấy chuỗi access_token trong đường link vừa dán. Vui lòng kiểm tra lại.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnOpenExternalBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ValorantApiService.RiotAuthUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }
}

