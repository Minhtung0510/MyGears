using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class MouseModuleView : UserControl
{
    // ──────────────────────────────────────────────
    //  Win32 P/Invoke
    // ──────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8 ? SetClassLongPtr64(hWnd, nIndex, dwNewLong) : SetClassLong32(hWnd, nIndex, dwNewLong);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    private const int GWL_STYLE          = -16;
    private const int GCLP_HBRBACKGROUND = -10;
    private const int BLACK_BRUSH        = 4;
    private const int WS_CAPTION         = 0x00C00000;
    private const int WS_THICKFRAME      = 0x00040000;
    private const int WS_BORDER          = 0x00800000;
    private const int WS_CHILD           = 0x40000000;
    private const int WS_POPUP           = unchecked((int)0x80000000);
    private const int SW_HIDE            = 0;
    private const int SW_SHOW            = 5;
    private const uint SWP_NOACTIVATE    = 0x0010;
    private const uint SWP_SHOWWINDOW    = 0x0040;
    private const uint SWP_FRAMECHANGED  = 0x0020;

    // ──────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────

    private Process? _scyroxProcess;
    private IntPtr   _scyroxHwnd = IntPtr.Zero;
    private System.Windows.Threading.DispatcherTimer? _embedTimer;
    private bool _isDetached = false;

    private static readonly string[] ScyRoxExeNames =
        ["ScyRox.exe", "ScyRoxV6.exe", "ScyRox_V6.exe", "scyrox.exe"];

    public MouseModuleView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScyRoxContainer.SizeChanged += OnContainerSizeChanged;

        // Tự động khởi động & nhúng ScyRox ngay khi mở tab Chuột
        LaunchAndEmbedAuto();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _embedTimer?.Stop();
        if (_scyroxHwnd != IntPtr.Zero && !_isDetached)
        {
            ShowWindow(_scyroxHwnd, SW_HIDE);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_scyroxHwnd == IntPtr.Zero || _isDetached) return;

        if (IsVisible)
        {
            ShowWindow(_scyroxHwnd, SW_SHOW);
            ResizeEmbeddedWindow();
        }
        else
        {
            ShowWindow(_scyroxHwnd, SW_HIDE);
        }
    }

    // ──────────────────────────────────────────────
    //  Auto Launch & Embed
    // ──────────────────────────────────────────────

    public void LaunchAndEmbedAuto()
    {
        _isDetached = false;
        BtnToggleDetach.Content = "⤴ Tách cửa sổ";

        // 1. Nếu cửa sổ đã được nhúng trước đó và process còn chạy -> resize lại
        if (_scyroxHwnd != IntPtr.Zero && _scyroxProcess != null && !_scyroxProcess.HasExited)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            ResizeEmbeddedWindow();
            return;
        }

        // 2. Kiểm tra xem ScyRox đã chạy sẵn trên máy chưa
        var existing = Process.GetProcessesByName("ScyRox")
            .Where(p => {
                try { return !p.HasExited; } catch { return false; }
            })
            .ToArray();

        if (existing.Length > 0)
        {
            var p = existing[0];
            try
            {
                p.EnableRaisingEvents = true;
                p.Exited += OnScyRoxExited;
                _scyroxProcess = p;
                StartEmbedTimer();
                return;
            }
            catch
            {
                // Process vừa thoát trong lúc gán, tiếp tục mở mới bên dưới
            }
        }

        // 3. Tìm file ScyRox.exe
        var exePath = FindScyRoxExe();
        if (exePath == null)
        {
            StatusMessage.Text = "⚠️ Chưa tìm thấy file ScyRox.exe trong Download/ScyRox/";
            StatusSubtext.Text = "Hãy chép thư mục ScyRox vào Download/ScyRox/ trên USB.";
            LoadingSpinner.Visibility = Visibility.Collapsed;
            StatusBadge.Text = "● Chưa có file";
            StatusBadge.Foreground = System.Windows.Media.Brushes.OrangeRed;
            return;
        }

        ScyRoxPathText.Text = $"Đường dẫn: {exePath}";
        StatusMessage.Text = "Đang tự động khởi động và nhúng ScyRox V6…";
        LoadingSpinner.Visibility = Visibility.Visible;
        StatusBadge.Text = "● Đang khởi động…";
        StatusBadge.Foreground = System.Windows.Media.Brushes.Yellow;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                UseShellExecute = true,
            };

            _scyroxProcess = Process.Start(psi);
            if (_scyroxProcess == null) return;

            _scyroxProcess.EnableRaisingEvents = true;
            _scyroxProcess.Exited += OnScyRoxExited;

            StartEmbedTimer();
        }
        catch (Exception ex)
        {
            StatusMessage.Text = $"❌ Lỗi khởi động: {ex.Message}";
            LoadingSpinner.Visibility = Visibility.Collapsed;
            StatusBadge.Text = "● Lỗi";
            StatusBadge.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void StartEmbedTimer()
    {
        _embedTimer?.Stop();
        _embedTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        int attempts = 0;
        _embedTimer.Tick += (_, _) =>
        {
            attempts++;
            if (TryEmbedWindow() || attempts > 50)
            {
                _embedTimer.Stop();
            }
        };
        _embedTimer.Start();
    }

    private bool TryEmbedWindow()
    {
        if (_scyroxProcess == null || _scyroxProcess.HasExited) return false;

        try
        {
            _scyroxProcess.Refresh();
            var hwnd = _scyroxProcess.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return false;

            _scyroxHwnd = hwnd;
            EmbedWindow(hwnd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EmbedWindow(IntPtr hwnd)
    {
        var parentWindow = Window.GetWindow(this);
        if (parentWindow == null) return;
        var containerHwnd = new WindowInteropHelper(parentWindow).Handle;

        // Bỏ title bar, border, popup; thêm WS_CHILD
        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_BORDER | WS_POPUP);
        style |= WS_CHILD;
        SetWindowLong(hwnd, GWL_STYLE, style);

        // Đặt parent = main window của MyGears
        SetParent(hwnd, containerHwnd);

        // Đổi cọ vẽ nền cửa sổ sang màu đen để ngăn triệt để nền trắng của Qt
        try { SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, GetStockObject(BLACK_BRUSH)); } catch { }

        // Yêu cầu Windows cập nhật frame
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0004 | SWP_FRAMECHANGED);

        // Resize cho vừa vặn đúng tọa độ container
        ResizeEmbeddedWindow();
        ShowWindow(hwnd, SW_SHOW);

        Dispatcher.Invoke(() =>
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            StatusBadge.Text = "● Đang chạy";
            StatusBadge.Foreground = System.Windows.Media.Brushes.LightGreen;
        });
    }

    private void ResizeEmbeddedWindow()
    {
        if (_scyroxHwnd == IntPtr.Zero || _isDetached) return;
        if (!IsVisible) return;

        try
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            // Tính toạ độ chính xác của ScyRoxContainer so với cửa sổ chính
            var point = ScyRoxContainer.TranslatePoint(new Point(0, 0), parentWindow);
            var size = ScyRoxContainer.RenderSize;

            if (size.Width <= 10 || size.Height <= 10) return;

            // Tính tỷ lệ DPI scaling để khớp chính xác pixel vật lý trên mọi màn hình (100%, 125%, 150%)
            var source = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            int physX = (int)Math.Round(point.X * dpiX);
            int physY = (int)Math.Round(point.Y * dpiY);
            int physW = (int)Math.Round(size.Width * dpiX);
            int physH = (int)Math.Round(size.Height * dpiY);

            // ScyRox được thiết kế kích thước cố định 1340 x 940 px.
            // Nếu cửa sổ mở rộng hơn (như khi toàn màn hình), việc kéo giãn HWND của ScyRox
            // vượt quá 1340x940 sẽ làm lộ nền trắng mặc định (COLOR_BTNFACE) của Qt ở cạnh phải hoặc đáy.
            // Vì vậy ta giới hạn chiều rộng tối đa 1340px và chiều cao tối đa 940px,
            // phần không gian dư thừa xung quanh sẽ được lấp bởi màu nền tối #0A0606 liền mạch tuyệt đối.
            int maxScyRoxW = (int)Math.Round(1340 * dpiX);
            int maxScyRoxH = (int)Math.Round(940 * dpiY);

            int targetW = Math.Min(physW, maxScyRoxW);
            int targetH = Math.Min(physH, maxScyRoxH);

            int targetX = physX + Math.Max(0, (physW - targetW) / 2);
            int targetY = physY;

            SetWindowPos(_scyroxHwnd, IntPtr.Zero,
                targetX, targetY, targetW, targetH,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch { }
    }

    private void OnContainerSizeChanged(object sender, SizeChangedEventArgs e)
        => ResizeEmbeddedWindow();

    // ──────────────────────────────────────────────
    //  Toolbar Actions
    // ──────────────────────────────────────────────

    private async void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        BtnRestart.IsEnabled = false;
        PlaceholderPanel.Visibility = Visibility.Visible;
        StatusMessage.Text = "Đang dừng tiến trình ScyRox cũ…";
        StatusBadge.Text = "● Đang khởi động lại…";
        StatusBadge.Foreground = System.Windows.Media.Brushes.Yellow;

        await Task.Run(() =>
        {
            KillScyRox();
            foreach (var p in Process.GetProcessesByName("ScyRox"))
            {
                try
                {
                    p.Kill(true);
                    p.WaitForExit(1000);
                }
                catch { }
            }
        });

        await Task.Delay(400);
        BtnRestart.IsEnabled = true;
        LaunchAndEmbedAuto();
    }

    private void BtnToggleDetach_Click(object sender, RoutedEventArgs e)
    {
        if (_scyroxHwnd == IntPtr.Zero)
        {
            LaunchAndEmbedAuto();
            return;
        }

        if (!_isDetached)
        {
            // Tách cửa sổ ra desktop
            SetParent(_scyroxHwnd, IntPtr.Zero);
            var style = GetWindowLong(_scyroxHwnd, GWL_STYLE);
            style &= ~WS_CHILD;
            style |= WS_CAPTION | WS_THICKFRAME;
            SetWindowLong(_scyroxHwnd, GWL_STYLE, style);
            ShowWindow(_scyroxHwnd, SW_SHOW);
            _isDetached = true;
            BtnToggleDetach.Content = "⤵ Nhúng lại";
            StatusBadge.Text = "● Đã tách cửa sổ";
            StatusBadge.Foreground = System.Windows.Media.Brushes.SkyBlue;
        }
        else
        {
            // Nhúng lại vào app
            _isDetached = false;
            BtnToggleDetach.Content = "⤴ Tách cửa sổ";
            EmbedWindow(_scyroxHwnd);
        }
    }

    public void KillScyRox()
    {
        _embedTimer?.Stop();
        if (_scyroxProcess != null)
        {
            try
            {
                if (!_scyroxProcess.HasExited)
                {
                    _scyroxProcess.Kill(true);
                    _scyroxProcess.WaitForExit(1500);
                }
            }
            catch { }
            finally
            {
                try { _scyroxProcess.Dispose(); } catch { }
                _scyroxProcess = null;
            }
        }
        _scyroxHwnd = IntPtr.Zero;
    }

    private void OnScyRoxExited(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _scyroxHwnd = IntPtr.Zero;
            PlaceholderPanel.Visibility = Visibility.Visible;
            StatusMessage.Text = "Phần mềm ScyRox đã đóng.";
            StatusSubtext.Text = "Bấm 'Khởi động lại' để mở lại.";
            LoadingSpinner.Visibility = Visibility.Collapsed;
            StatusBadge.Text = "● Đã dừng";
            StatusBadge.Foreground = System.Windows.Media.Brushes.Gray;
        });
    }

    // ──────────────────────────────────────────────
    //  Find ScyRox.exe
    // ──────────────────────────────────────────────

    private static string? FindScyRoxExe()
    {
        var scyroxDir = UsbPathResolver.GetDownloadPath("ScyRox");
        if (Directory.Exists(scyroxDir))
        {
            // 1. Tìm đúng tên ScyRox.exe trong USB
            foreach (var name in ScyRoxExeNames)
            {
                var path = Path.Combine(scyroxDir, name);
                if (File.Exists(path)) return path;
            }

            // 2. Tìm file .exe trong thư mục USB nhưng BỎ QUA setup/installer
            var exes = Directory.GetFiles(scyroxDir, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains("install", StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains("unins", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exes.Count > 0) return exes.First();
        }

        // 3. Fallback: Tìm trong Program Files nếu máy đã cài đặt
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var sys64Path = Path.Combine(progFiles, "ScyRox", "Sys64", "ScyRox.exe");
        if (File.Exists(sys64Path)) return sys64Path;

        var sys32Path = Path.Combine(progFiles, "ScyRox", "Sys32", "ScyRox.exe");
        if (File.Exists(sys32Path)) return sys32Path;

        return null;
    }
}
