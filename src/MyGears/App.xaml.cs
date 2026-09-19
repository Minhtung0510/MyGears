using System.Windows;
using MyGears.Core;
using MyGears.Views;

namespace MyGears;

public partial class App : Application
{
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
