using System.Windows;
using System.Windows.Input;
using MyGears.ViewModels;

namespace MyGears.Views;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _vm;

    public SetupWindow()
    {
        InitializeComponent();
        _vm = (SetupViewModel)DataContext;
        _vm.RequestClose += () =>
        {
            try { DialogResult = true; } catch { }
            Close();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsInstalling && !_vm.IsCompleted)
        {
            var result = MessageBox.Show(
                "Quá trình cài đặt đang diễn ra. Bạn có chắc chắn muốn hủy không?",
                "MyGears Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        Close();
    }
}
