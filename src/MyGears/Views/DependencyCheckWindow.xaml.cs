using System.Windows;
using System.Windows.Input;
using MyGears.Core;
using MyGears.ViewModels;

namespace MyGears.Views;

public partial class DependencyCheckWindow : Window
{
    private readonly DependencyCheckViewModel _vm;

    public DependencyCheckWindow()
    {
        InitializeComponent();
        _vm = (DependencyCheckViewModel)DataContext;

        // Hiển thị USB root đã phát hiện được
        UsbRootText.Text = UsbPathResolver.IsInitialized
            ? UsbPathResolver.UsbRoot
            : "⚠️ Chưa khởi tạo";

        // Khi check xong → tự chuyển sang MainWindow
        _vm.AllDoneSuccessfully += OpenMainWindow;

        // Cuộn log xuống dưới cùng khi có log mới
        _vm.Logs.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                LogScrollViewer.ScrollToBottom();
            });
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Tự động chạy check ngay khi window hiển thị
        await _vm.RunCheckCommand.ExecuteAsync(null);
    }

    private int _opened = 0;

    private void OpenMainWindow()
    {
        if (System.Threading.Interlocked.Exchange(ref _opened, 1) != 0) return;
        _vm.AllDoneSuccessfully -= OpenMainWindow;

        Dispatcher.Invoke(() =>
        {
            if (ContinueButton != null) ContinueButton.IsEnabled = false;
            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        });
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        OpenMainWindow();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }
}
