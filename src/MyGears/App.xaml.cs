using System.Windows;
using MyGears.Core;
using MyGears.Views;

namespace MyGears;

public partial class App : Application
{
    private static System.Threading.Mutex? _singleInstanceMutex;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnLastWindowClose;

        // Bắt lỗi toàn cục để không bao giờ bị crash âm thầm
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"Lỗi hệ thống:\n\n{args.ExceptionObject}",
                "MyGears - System Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "last_ui_error.log"), $"{args.Exception.Message}\n\n{args.Exception.StackTrace}\n\nInner:\n{args.Exception.InnerException}"); } catch {}
            MessageBox.Show(
                $"Lỗi giao diện:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "MyGears - UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Khởi tạo USB path resolver — bước đầu tiên, quan trọng nhất
        UsbPathResolver.Initialize();

        // Load settings từ USB
        await SettingsService.LoadAsync();

        // Kiểm tra điều kiện khởi chạy:
        // 1) Chạy từ USB HOẶC có cờ --setup -> Mở màn hình Setup
        // 2) Chạy từ máy tính (hoặc có cờ --app / --dashboard) -> Mở trực tiếp Dashboard
        bool forceApp = e.Args.Contains("--app") || e.Args.Contains("--dashboard") || e.Args.Contains("--from-usb-deploy");

        // Bảo vệ Single Instance: Tránh mở 2 cửa sổ ứng dụng song song
        if (forceApp || (!UsbPathResolver.IsRunningFromUsb && !e.Args.Contains("--setup")))
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, "Global\\MyGears_MainApp_SingleInstance_Mutex_2026", out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(currentProc.ProcessName))
                    {
                        if (p.Id != currentProc.Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            var runningPath = p.MainModule?.FileName;
                            if (!string.Equals(runningPath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                                MessageBox.Show($"MyGears đang mở từ:\n{runningPath}\n\nBạn vừa mở:\n{Environment.ProcessPath}\n\nHãy đóng bản đang chạy trước khi mở bản này. Để cập nhật bản cài trên máy, chạy MyGears.exe --setup từ thư mục App nguồn.", "MyGears — Đang mở bản khác", MessageBoxButton.OK, MessageBoxImage.Information);
                            ShowWindowAsync(p.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                }
                catch { }
                Shutdown();
                return;
            }
        }

        if ((UsbPathResolver.IsRunningFromUsb || e.Args.Contains("--setup")) && !forceApp)
        {
            var setupWindow = new SetupWindow();
            MainWindow = setupWindow;
            setupWindow.Show();
            return;
        }

        // Hiển thị màn hình Dependency Check trước
        var depWindow = new DependencyCheckWindow();
        MainWindow = depWindow;
        depWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cleanup tất cả module khi thoát
        base.OnExit(e);
    }
}
