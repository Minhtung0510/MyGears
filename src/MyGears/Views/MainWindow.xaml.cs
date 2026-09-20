using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MyGears.Core;
using MyGears.Modules;
using MyGears.ViewModels;

namespace MyGears.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private DispatcherTimer? _bannerTimer;

    public MainWindow()
    {
        InitializeComponent();
        _vm = (MainViewModel)DataContext;

        // Khôi phục vị trí/kích thước cửa sổ từ settings
        var ui = SettingsService.Current.Ui;
        Left   = ui.WindowLeft;
        Top    = ui.WindowTop;
        Width  = ui.WindowWidth;
        Height = ui.WindowHeight;

        // Theo dõi thay đổi module để update content area
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveModule))
                UpdateContentArea(_vm.ActiveModule);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Mặc định tắt chống chụp màn hình (cho phép chụp ảnh màn hình trên máy cá nhân)
        SecurityService.SetWindowAntiCapture(this, enable: false);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Hiển thị banner với hiệu ứng trượt mượt mà nếu được khởi chạy sau khi cài đặt từ USB
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--from-usb-deploy", StringComparison.OrdinalIgnoreCase)))
        {
            ShowBannerWithAnimation();
        }

        // Hiển thị và khởi tạo module mặc định
        UpdateContentArea(_vm.ActiveModule);
        if (_vm.ActiveModule != null && _vm.ActiveModule.IsAvailable)
        {
            await _vm.ActiveModule.InitializeAsync();
        }

        // Tự động tải trước (pre-warm) các module còn lại trong nền
        // Giúp khi bấm sang Bàn phím thì WebView2 đã tải trước hub.fgg.com.cn
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            foreach (var mod in _vm.Modules)
            {
                if (mod != _vm.ActiveModule && mod.IsAvailable)
                {
                    try { await mod.InitializeAsync(); }
                    catch { }
                }
            }
        });
    }

    // ──────────────────────────────────────────────
    //  Content Area Update & Micro-Animations
    // ──────────────────────────────────────────────

    private void UpdateContentArea(IGearModule? module)
    {
        if (module == null) return;
        // Lấy UIElement của module và đặt vào ContentHost
        ContentHost.Content = module.GetView();

        // Highlight sidebar item tương ứng với thanh trượt mượt
        HighlightSidebarItem(module);

        // Kích hoạt transition Fade-in + Slide nhẹ (200ms CubicEase EaseOut)
        AnimateContentTransition();
    }

    private double _currentGearAngle = 0;

    private void AnimateContentTransition()
    {
        // 1. Gear rotation on tab switch (Bánh răng cơ khí xoay 90 độ khi đổi tab)
        _currentGearAngle += 90.0;
        var gearAnim = new DoubleAnimation
        {
            To = _currentGearAngle,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
        };
        TitlebarGearRotate?.BeginAnimation(RotateTransform.AngleProperty, gearAnim);

        // 2. Fade in
        var fadeAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // 3. Slide in
        var slideAnim = new DoubleAnimation
        {
            From = 14.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // 4. Subtle scale pop
        var scaleAnim = new DoubleAnimation
        {
            From = 0.985,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        ContentHost.BeginAnimation(OpacityProperty, fadeAnim);
        ContentTransform?.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        ContentScaleTransform?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        ContentScaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void HighlightSidebarItem(IGearModule activeModule)
    {
        int index = _vm.Modules.IndexOf(activeModule);
        if (index < 0) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            double targetY = index * 52;
            var container = SidebarItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container != null)
            {
                try
                {
                    var pt = container.TransformToAncestor(SidebarItemsControl).Transform(new Point(0, 0));
                    targetY = pt.Y + 2;
                    if (container.ActualHeight > 0)
                    {
                        SidebarAccentIndicator.Height = container.ActualHeight - 4;
                    }
                }
                catch
                {
                    targetY = index * 52;
                }
            }

            // Animate thanh accent bar trượt mượt mà (CubicEase EaseOut 200ms)
            var anim = new DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            AccentBarTransform.BeginAnimation(TranslateTransform.YProperty, anim);

            // Cập nhật style và màu sắc các mục sidebar
            for (int i = 0; i < _vm.Modules.Count; i++)
            {
                var itemContainer = SidebarItemsControl.ItemContainerGenerator.ContainerFromIndex(i);
                if (itemContainer != null)
                {
                    var btn = FindVisualChild<Button>(itemContainer);
                    if (btn != null)
                    {
                        bool isActive = (i == index);
                        btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
                        btn.Foreground = isActive ? Brushes.White : (Brush)FindResource("TextSecondary");
                    }
                }
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }

    // ──────────────────────────────────────────────
    //  Banner Slide-down & Auto-dismiss Animation
    // ──────────────────────────────────────────────

    private void ShowBannerWithAnimation()
    {
        BannerDeployedNotice.Visibility = Visibility.Visible;
        var slideDown = new DoubleAnimation
        {
            From = -30.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BannerTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        BannerDeployedNotice.BeginAnimation(OpacityProperty, fadeIn);

        // Tự động đóng sau 4.5 giây
        _bannerTimer?.Stop();
        _bannerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4.5)
        };
        _bannerTimer.Tick += (_, _) =>
        {
            _bannerTimer.Stop();
            DismissBannerWithAnimation();
        };
        _bannerTimer.Start();
    }

    private void DismissBannerWithAnimation()
    {
        _bannerTimer?.Stop();
        var slideUp = new DoubleAnimation
        {
            To = -30.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeOut = new DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            BannerDeployedNotice.Visibility = Visibility.Collapsed;
        };
        BannerTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        BannerDeployedNotice.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ──────────────────────────────────────────────
    //  Window Controls
    // ──────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void DismissBanner_Click(object sender, RoutedEventArgs e)
        => DismissBannerWithAnimation();

    // ──────────────────────────────────────────────
    //  On Closing — save window state + settings
    // ──────────────────────────────────────────────

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        // Lưu vị trí cửa sổ
        if (WindowState == WindowState.Normal)
        {
            SettingsService.Current.Ui.WindowLeft   = Left;
            SettingsService.Current.Ui.WindowTop    = Top;
            SettingsService.Current.Ui.WindowWidth  = Width;
            SettingsService.Current.Ui.WindowHeight = Height;
        }

        // Cleanup tất cả modules
        await _vm.DisposeAllModulesAsync();

        // Save settings lần cuối
        SettingsService.Save();
    }
}
