using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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

        // Khởi tạo hệ thống đồng hồ kim analog và buồng bánh răng cơ khí
        InitializeAnalogInstruments();

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

    // ──────────────────────────────────────────────
    //  Analog Instrument & Gearbox Physics Engine
    // ──────────────────────────────────────────────
    private readonly DispatcherTimer _analogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
    private double _needle1Angle = -38.0;
    private double _needle1Target = -38.0;
    private double _needle2Angle = -10.0;
    private double _needle2Target = -10.0;
    private double _needle3Angle = -20.0;
    private double _needle3Target = -20.0;
    private double _gear1Angle = 0.0;
    private double _gearVelocity = 0.4;
    private readonly Random _rnd = new Random();
    private int _metricTickCount = 0;

    private void InitializeAnalogInstruments()
    {
        // Cập nhật giá trị đo lường ban đầu
        _vm.UpdateLiveTelemetry();
        _needle1Target = -42.0 + (_vm.CurrentCpuLoad / 100.0) * 84.0;
        _needle2Target = -42.0 + (_vm.CurrentRamLoad / 100.0) * 84.0;
        double initPing = Math.Clamp(_vm.NetPingMs >= 0 ? _vm.NetPingMs : 30, 0, 100);
        _needle3Target = -42.0 + (initPing / 100.0) * 84.0;
        _needle1Angle = _needle1Target;
        _needle2Angle = _needle2Target;
        _needle3Angle = _needle3Target;

        _analogTimer.Tick += (s, e) =>
        {
            _metricTickCount++;
            // Mỗi 25 ticks (~1 giây) thì lấy mẫu CPU, RAM & Mạng thực tế
            if (_metricTickCount >= 25)
            {
                _metricTickCount = 0;
                _vm.UpdateLiveTelemetry();
                _needle1Target = -42.0 + (_vm.CurrentCpuLoad / 100.0) * 84.0;
                _needle2Target = -42.0 + (_vm.CurrentRamLoad / 100.0) * 84.0;
                double pingVal = Math.Clamp(_vm.NetPingMs >= 0 ? _vm.NetPingMs : 30, 0, 100);
                _needle3Target = -42.0 + (pingVal / 100.0) * 84.0;
            }

            // 1. Kim đo CPU Load (Needle 1): Dao động theo tải CPU thực tế kèm độ rung cơ khí
            if (Math.Abs(_needle1Target - _needle1Angle) > 0.4)
            {
                _needle1Angle += (_needle1Target - _needle1Angle) * 0.18;
            }
            else
            {
                double jitter = (_rnd.NextDouble() - 0.5) * 1.5;
                _needle1Angle = Math.Clamp(_needle1Target + jitter, -42.0, 42.0);
            }
            if (MeterNeedle1Rotate != null)
                MeterNeedle1Rotate.Angle = _needle1Angle;

            // 2. Kim đo RAM Usage (Needle 2): Chỉ dung lượng RAM thực tế đang dùng
            if (Math.Abs(_needle2Target - _needle2Angle) > 0.4)
            {
                _needle2Angle += (_needle2Target - _needle2Angle) * 0.18;
            }
            else
            {
                double needle2Jitter = (_rnd.NextDouble() - 0.5) * 0.6;
                _needle2Angle = Math.Clamp(_needle2Target + needle2Jitter, -42.0, 42.0);
            }
            if (MeterNeedle2Rotate != null)
                MeterNeedle2Rotate.Angle = _needle2Angle;

            // 3. Kim đo Network Ping (Needle 3): Chỉ độ trễ mạng Internet
            if (Math.Abs(_needle3Target - _needle3Angle) > 0.4)
            {
                _needle3Angle += (_needle3Target - _needle3Angle) * 0.18;
            }
            else
            {
                double needle3Jitter = (_rnd.NextDouble() - 0.5) * 1.0;
                _needle3Angle = Math.Clamp(_needle3Target + needle3Jitter, -42.0, 42.0);
            }
            if (MeterNeedle3Rotate != null)
                MeterNeedle3Rotate.Angle = _needle3Angle;

            // 4. Cụm 3 Bánh răng cơ khí ăn khớp: Quay đồng bộ ngược chiều
            _gear1Angle = (_gear1Angle + _gearVelocity) % 360.0;
            if (GearTrain1Rotate != null) GearTrain1Rotate.Angle = _gear1Angle;
            if (GearTrain2Rotate != null) GearTrain2Rotate.Angle = -_gear1Angle * 1.37;
            if (GearTrain3Rotate != null) GearTrain3Rotate.Angle = _gear1Angle * 1.86;

            // Giảm tốc dần về mức quay nhàn rỗi (idle spin = 0.4)
            if (_gearVelocity > 0.4)
            {
                _gearVelocity = Math.Max(0.4, _gearVelocity * 0.94);
            }
        };
        _analogTimer.Start();
    }

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

        // Kích hoạt xoay nhanh cụm bánh răng & giật kim đo analog
        _gearVelocity = 14.0;
        _needle1Target = 24.0;

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
            double targetY = index * 56;
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
                    targetY = index * 56;
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

            // Cập nhật trạng thái hiển thị của các kênh cơ học
            for (int i = 0; i < _vm.Modules.Count; i++)
            {
                var itemContainer = SidebarItemsControl.ItemContainerGenerator.ContainerFromIndex(i);
                if (itemContainer != null)
                {
                    bool isActive = (i == index);
                    var bdChannel = FindChildByName(itemContainer, "BdChannel") as Border;
                    var jewelLamp = FindChildByName(itemContainer, "JewelLamp") as Border;
                    var txtVi = FindChildByName(itemContainer, "TxtDisplayNameVi") as TextBlock;

                    if (bdChannel != null)
                    {
                        bdChannel.BorderBrush = isActive ? (Brush)FindResource("AccentBrass") : (Brush)FindResource("BorderCard");
                    }
                    if (jewelLamp != null)
                    {
                        if (isActive)
                        {
                            jewelLamp.Background = (Brush)FindResource("AccentAmber");
                            jewelLamp.BorderBrush = (Brush)FindResource("AccentGold");
                            jewelLamp.Effect = new DropShadowEffect
                            {
                                Color = (Color)FindResource("AccentAmberColor"),
                                BlurRadius = 10,
                                ShadowDepth = 0,
                                Opacity = 1.0
                            };
                        }
                        else
                        {
                            jewelLamp.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x1A, 0x0D));
                            jewelLamp.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x3A, 0x18));
                            jewelLamp.Effect = null;
                        }
                    }
                    if (txtVi != null)
                    {
                        txtVi.Foreground = isActive ? Brushes.White : (Brush)FindResource("TextSecondary");
                        txtVi.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
                    }
                }
            }
        });
    }

    private static FrameworkElement? FindChildByName(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;
            var sub = FindChildByName(child, name);
            if (sub != null) return sub;
        }
        return null;
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
