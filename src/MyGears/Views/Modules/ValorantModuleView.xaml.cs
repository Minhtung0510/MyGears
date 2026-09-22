using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class ValorantModuleView : UserControl
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private bool _isScanning;
    private int _toastVersion;
    private FrameworkElement? _visibleTab;
    private ValorantScanResult? _currentScan;
    public string SavedAccountUsername { get; set; } = "";

    private void CopyInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && FindName(name) is TextBlock value)
        {
            try { Clipboard.SetText(value.Text); ShowToast("Đã sao chép."); }
            catch { ShowToast("Chưa sao chép được. Hãy thử lại."); }
        }
    }
    private readonly ObservableCollection<ValorantStoreOffer> _dailyOffers = [];
    private readonly ObservableCollection<ValorantStoreOffer> _nightMarketOffers = [];
    private readonly ObservableCollection<ValorantItem> _displayedInventoryItems = [];

    private string _activeCategory = "weapons";
    private string _currentWeaponFilter = "All";
    private DispatcherTimer? _countdownTimer;


    public ValorantModuleView()
    {
        InitializeComponent();
        Unloaded += (_, _) => ScanSpinnerTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        Loaded += (_, _) => { if (_isScanning) SetScanBusy(true); };

        ItemsDailyStore.ItemsSource = _dailyOffers;
        ItemsNightMarket.ItemsSource = _nightMarketOffers;
        ItemsOwnedSkins.ItemsSource = _displayedInventoryItems;

        Loaded += ValorantModuleView_Loaded;
        Unloaded += (_, _) => _countdownTimer?.Stop();
    }

    private void ValorantModuleView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_currentScan == null)
        {
            ResetToEmptyState();
        }
        else StartCountdownTimer();
    }

    private void ResetToEmptyState()
    {
        _currentScan = null;
        _countdownTimer?.Stop();
        TxtStoreCountdown.Text = "--:--:--";

        TxtProfileRiotId.Text = "Chưa Kết Nối";
        TxtProfileRegion.Text = "REGION: AP (vnm)";
        TxtProfileLastUpdated.Text = "LẦN CUỐI CẬP NHẬT: Chưa quét";
        TxtBadgeBanStatus.Text = "CHƯA KIỂM TRA";
        TxtDataStatus.Text = "Chưa có bản quét.";

        TxtFullRankTitle.Text = "Chưa có dữ liệu rank";
        TxtTotalSkinsCount.Text = "0 SKINS";
        TxtCountUltra.Text = "0";
        TxtCountExclusive.Text = "0";
        TxtCountPremium.Text = "0";
        TxtCountSelect.Text = "0";

        TxtMetricLevel.Text = "--";

        // Tab Thông Tin
        TxtInfoRiotId.Text = "--";
        TxtInfoRegion.Text = "AP (vnm)";
        TxtInfoLevel.Text = "LV --";
        TxtInfoRank.Text = "UNRANKED";
        TxtInfoBanStatus.Text = "Chưa có dữ liệu trạng thái cấm";
        TxtInfoVp.Text = "-- VP";
        TxtInfoRp.Text = "-- RP";
        TxtInfoKc.Text = "-- KC";
        TxtInfoTotalVp.Text = "-- VP";
        TxtInfoVnd.Text = "-- VNĐ";

        TxtCountWeapons.Text = "0";
        TxtCountBuddies.Text = "0";
        TxtCountCards.Text = "0";
        TxtCountSprays.Text = "0";
        TxtCountAgents.Text = "0";

        _dailyOffers.Clear();
        _nightMarketOffers.Clear();
        _displayedInventoryItems.Clear();

        UpdateViewVisibility();
    }

    public event Action<ValorantScanResult>? OnScanCompleted;
    public event Action? OnScanCleared;

    private async void BtnClearScan_Click(object sender, RoutedEventArgs e)
    {
        ResetToEmptyState();
        // Đăng xuất phiên Riot cũ để lần quét tiếp theo không bị dính tài khoản cũ
        try { await ValorantApiService.LogoutRiotSessionAsync(); } catch { }
        OnScanCleared?.Invoke();
        ShowToast("Đã xóa bản quét, đăng xuất phiên Riot và đặt lại trạng thái ban đầu!");
    }

    // ──────────────────────────────────────────────
    //  ĐĂNG NHẬP & QUÉT DỮ LIỆU
    // ──────────────────────────────────────────────

    private async void BtnOneClickLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var loginWin = new ValorantLoginWindow
            {
                Owner = Window.GetWindow(this)
            };

            if (loginWin.ShowDialog() == true && !string.IsNullOrEmpty(loginWin.ResultAccessToken))
            {
                await ExecuteScanAsync(loginWin.ResultAccessToken, loginWin.ResultIdToken ?? string.Empty);
            }
        }
        finally
        {
            // Đảm bảo chuột luôn luôn được khôi phục dù trong bất kỳ tình huống nào
            try
            {
                ReleaseCapture();
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;
                while (ShowCursor(true) < 0) { }
            }
            catch { }
        }
    }

    /// <summary>
    /// Được gọi từ AccountsModuleView khi bấm nút 🎯 Soi Valorant trên 1 tài khoản.
    /// Tự động mở cửa sổ đăng nhập Riot với username đã điền sẵn.
    /// Mật khẩu đã được copy vào clipboard trước đó.
    /// </summary>
    public async void PromptLoginWithAccount(string username, string password)
    {
        try
        {
            // Xóa bản quét cũ và đăng xuất phiên Riot trước khi đăng nhập tài khoản mới
            ResetToEmptyState();
            try { await ValorantApiService.LogoutRiotSessionAsync(); } catch { }

            var loginWin = new ValorantLoginWindow
            {
                Owner = Window.GetWindow(this),
                PrefillUsername = username,
                PrefillPassword = password
            };

            if (loginWin.ShowDialog() == true && !string.IsNullOrEmpty(loginWin.ResultAccessToken))
            {
                await ExecuteScanAsync(loginWin.ResultAccessToken, loginWin.ResultIdToken ?? string.Empty);
            }
        }
        finally
        {
            try
            {
                ReleaseCapture();
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;
                while (ShowCursor(true) < 0) { }
            }
            catch { }
        }
    }

    private void BtnToggleManualDrawer_Click(object sender, RoutedEventArgs e)
    {
        DrawerManualInput.Visibility = DrawerManualInput.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (DrawerManualInput.Visibility == Visibility.Visible) MyGears.Views.CheckAccountMotion.Reveal(DrawerManualInput);
    }

    private async void BtnScanManualUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtManualRedirectUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Vui lòng dán link redirect từ Riot Games vào ô.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (access, id) = ValorantApiService.ParseTokensFromUrl(url);
        if (string.IsNullOrEmpty(access))
        {
            MessageBox.Show("Không tìm thấy access_token trong đường link vừa dán. Vui lòng kiểm tra lại.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DrawerManualInput.Visibility = Visibility.Collapsed;
        TxtManualRedirectUrl.Text = string.Empty;
        await ExecuteScanAsync(access, id ?? string.Empty);
    }

    private void SetScanBusy(bool busy)
    {
        _isScanning = busy;
        ScanBusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanSpinnerTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        if (busy)
        {
            MyGears.Views.CheckAccountMotion.Reveal(ScanBusyOverlay);
            if (SystemParameters.ClientAreaAnimation)
                ScanSpinnerTransform.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
    }

    private async Task ExecuteScanAsync(string accessToken, string idToken)
    {
        if (_isScanning) return;
        SetScanBusy(true);

        try
        {
            var result = await ValorantApiService.ScanAccountAsync(accessToken, idToken, "ap");
            LoadScanResult(result);
            OnScanCompleted?.Invoke(result);
            ShowToast($"Đã lưu bản quét {result.Profile.RiotId}. Xem trạng thái dữ liệu phía trên.");

            // Sau khi đã lấy đầy đủ dữ liệu → đăng xuất phiên Riot để lần quét sau không bị dính acc cũ
            _ = Task.Run(async () =>
            {
                try { await ValorantApiService.LogoutRiotSessionAsync(); } catch { }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Có lỗi xảy ra khi kết nối máy chủ Riot: {ex.Message}\n\nVui lòng kiểm tra kết nối mạng hoặc thử đăng nhập lại.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetScanBusy(false);
            try
            {
                ReleaseCapture();
                Mouse.Capture(null);
                Mouse.OverrideCursor = null;
                while (ShowCursor(true) < 0) { }
            }
            catch { }
        }
    }

    private async Task RefreshSkinImagesAsync(ValorantScanResult data)
    {
        try
        {
            await ValorantApiService.RefreshScanSkinImagesAsync(data);
            if (!ReferenceEquals(_currentScan, data)) return;
            ApplyFilterAndSearch();
            ItemsDailyStore.Items.Refresh();
            ItemsNightMarket.Items.Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Skin image refresh failed: {ex.Message}");
        }
    }

    public void LoadScanResult(ValorantScanResult data)
    {
        _currentScan = data;
        _visibleTab = null;
        _ = RefreshSkinImagesAsync(data);

        // 1. Header Profile & Status
        TxtProfileRiotId.Text = string.IsNullOrEmpty(data.Profile.RiotId) ? "Người Chơi" : data.Profile.RiotId;
        TxtProfileRegion.Text = $"REGION: {data.Profile.Region}";
        TxtProfileLastUpdated.Text = $"LẦN QUÉT: {data.ScanTime:dd/MM/yyyy HH:mm}";
        TxtDataStatus.Text = data.DataStatusText;

        try
        {
            if (!string.IsNullOrEmpty(data.Profile.RankIcon))
            {
                var bmp = new BitmapImage(new Uri(data.Profile.RankIcon));
                ImgProfileRankAvatar.Source = bmp;
                ImgOverviewRankIcon.Source = bmp;
            }
        }
        catch { }

        // 2. Hàng thống kê tổng quan (Rank, Tier, Mini metrics)
        TxtFullRankTitle.Text = data.Profile.RankData == null ? "Bản quét cũ — cần quét lại rank" : data.Profile.FullRankTitle;
        TxtTotalSkinsCount.Text = $"{data.CountText("Skins", data.TotalSkinsCount)} SKINS";
        TxtCountUltra.Text = data.CountText("Skins", data.CountUltra);
        TxtCountExclusive.Text = data.CountText("Skins", data.CountExclusive);
        TxtCountPremium.Text = data.CountText("Skins", data.CountPremium);
        TxtCountSelect.Text = data.CountText("Skins", data.CountSelect);

        TxtMetricLevel.Text = data.Profile.NumberText(data.Profile.AccountLevel);

        // 3. Tab 1: THÔNG TIN TÀI KHOẢN & SỐ DƯ VÍ
        TxtInfoRiotId.Text = data.Profile.RiotId;
        TxtInfoRegion.Text = data.Profile.Region;
        TxtInfoLevel.Text = $"LV {data.Profile.NumberText(data.Profile.AccountLevel)}";
        TxtInfoRank.Text = data.Profile.RankData == null ? "Cần quét lại rank" : data.Profile.RankName;
        TxtInfoBanStatus.Text = data.Profile.BanStatus;
        var info = data.Profile.AccountInfo;
        TxtInfoUsername.Text = !string.IsNullOrWhiteSpace(info?.Username) ? info.Username
            : !string.IsNullOrWhiteSpace(SavedAccountUsername) ? SavedAccountUsername : "Riot không cung cấp tên đăng nhập";
        TxtInfoUsername.ToolTip = string.IsNullOrWhiteSpace(info?.Username) ? "Tên đăng nhập đã lưu trong app" : "Tên đăng nhập do Riot cung cấp";
        TxtInfoEmail.Text = info?.EmailText ?? "Bản quét cũ — hãy quét lại";
        TxtInfoEmailStatus.Text = (info?.FetchedAt is { } fetched ? $"Email ({fetched.ToLocalTime():dd/MM HH:mm}): " : "Email: ") + (info?.EmailStatus ?? "Chưa có dữ liệu xác minh")
            + (!string.IsNullOrWhiteSpace(info?.Email) && info.Email.Contains('*') ? " • Địa chỉ đã được Riot che một phần" : "");
        TxtInfoPhone.Text = info?.PhoneText ?? "Bản quét cũ — hãy quét lại";
        var createdLocal = info?.CreatedAt?.ToLocalTime();
        TxtCreatedTime.Text = createdLocal?.ToString("HH:mm:ss") ?? "—";
        TxtCreatedDay.Text = createdLocal?.ToString("dd") ?? "—";
        TxtInfoCreated.Text = createdLocal?.ToString("MM / yyyy") ?? "Chưa có dữ liệu";
        TxtInfoCreated.ToolTip = info?.CreatedText ?? "Bản quét cũ — hãy quét lại";
        TxtInfoRegion.Text = $"{(string.IsNullOrWhiteSpace(info?.Country) ? "Chưa rõ quốc gia" : info.Country)} / {data.Profile.Region}";
        TxtInfoBanStatus.Text = info?.BanText ?? "Bản quét cũ — hãy quét lại";
        TxtBadgeBanStatus.Text = info?.RestrictionCount is > 0 ? "CÓ HẠN CHẾ"
            : info?.RestrictionCount == 0 ? "KHÔNG CÓ HẠN CHẾ" : "CHƯA RÕ";
        var statusColor = (Brush)new BrushConverter().ConvertFromString(info?.RestrictionCount is > 0 ? "#FF6677" : info?.RestrictionCount == 0 ? "#34D399" : "#F6C768")!;
        TxtBadgeBanStatus.Foreground = statusColor;
        BadgeBanStatus.BorderBrush = statusColor;
        TxtInfoBanStatus.Foreground = statusColor;

        TxtInfoVp.Text = $"{data.Profile.NumberText(data.Profile.ValorantPoints)} VP";
        TxtInfoRp.Text = $"{data.Profile.NumberText(data.Profile.RadianitePoints)} RP";
        TxtInfoKc.Text = $"{data.Profile.NumberText(data.Profile.KingdomCredits)} KC";
        TxtInfoTotalVp.Text = data.InventoryKnown("Skins") ? $"~{data.TotalEstimatedVpValue:N0} VP" : "Chưa đủ dữ liệu";
        TxtInfoVnd.Text = data.InventoryKnown("Skins") ? $"~{data.Profile.EstimatedVndValue:N0} VNĐ" : "Cần quét lại";

        TxtCountWeapons.Text = data.CountText("Skins", data.CountWeaponSkins);
        TxtCountBuddies.Text = data.CountText("Buddies", data.CountBuddies);
        TxtCountCards.Text = data.CountText("Cards", data.CountCards);
        TxtCountSprays.Text = data.CountText("Sprays", data.CountSprays);
        TxtCountAgents.Text = data.CountText("Agents", data.CountAgents);

        // Cập nhật số lượng trên các nút Category Pills ở Tab Kho Đồ
        BtnCatWeaponSkins.Content = $"WEAPON SKINS ({data.CountText("Skins", data.CountWeaponSkins)})";
        BtnCatBuddies.Content = $"BUDDIES ({data.CountText("Buddies", data.CountBuddies)})";
        BtnCatCards.Content = $"CARDS ({data.CountText("Cards", data.CountCards)})";
        BtnCatSprays.Content = $"SPRAYS ({data.CountText("Sprays", data.CountSprays)})";
        BtnCatAgents.Content = $"AGENTS ({data.CountText("Agents", data.CountAgents)})";

        // 4. Tab 2: Cửa hàng Daily Store & Chợ Đêm
        _dailyOffers.Clear();
        foreach (var offer in data.Store.DailyOffers)
        {
            _dailyOffers.Add(offer);
        }

        _nightMarketOffers.Clear();
        if (data.Store.HasNightMarket)
        {
            PanelNightMarket.Visibility = Visibility.Visible;
            foreach (var nm in data.Store.NightMarketOffers)
            {
                _nightMarketOffers.Add(nm);
            }
        }
        else
        {
            PanelNightMarket.Visibility = Visibility.Collapsed;
        }

        StartCountdownTimer();

        // 5. Tab 3: Kho đồ
        UpdateCategoryPillStyles();
        ApplyFilterAndSearch();

        // 6. Cập nhật giao diện
        UpdateViewVisibility();
    }

    // ──────────────────────────────────────────────
    //  ĐẾM NGƯỢC THỜI GIAN RESET CỬA HÀNG
    // ──────────────────────────────────────────────

    private void BtnRankHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_currentScan == null) { ShowToast("Hãy quét tài khoản trước để xem rank."); return; }
        var dialog = new MyGears.Views.ValorantRankWindow(_currentScan) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void StartCountdownTimer()
    {
        _countdownTimer?.Stop();
        if (_currentScan == null) return;
        var store = _currentScan.Store;
        ItemsBundles.ItemsSource = store.Bundles;
        ItemsAccessories.ItemsSource = store.Accessories;
        void Update()
        {
            var now = DateTimeOffset.UtcNow;
            bool legacy = store.DataVersion < 1;
            bool dailyValid = !legacy && store.DailyExpiresAt > now;
            if (!dailyValid) _dailyOffers.Clear();
            if (legacy || store.NightMarketExpiresAt <= now || store.NightMarketExpiresAt == null) _nightMarketOffers.Clear();
            PanelNightMarket.Visibility = _nightMarketOffers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var bundles = legacy ? new List<ValorantShopExtra>() : store.Bundles.Where(b => b.ExpiresAt > now).ToList();
            var accessories = legacy || store.AccessoryExpiresAt <= now || store.AccessoryExpiresAt == null ? new List<ValorantShopExtra>() : store.Accessories;
            if (ItemsBundles.Items.Count != bundles.Count) ItemsBundles.ItemsSource = bundles;
            if (ItemsAccessories.Items.Count != accessories.Count) ItemsAccessories.ItemsSource = accessories;
            TxtBundleEmpty.Visibility = bundles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtAccessoryEmpty.Visibility = accessories.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtNightExpiry.Text = store.NightMarketExpiresAt.HasValue ? $"Hết hạn: {store.NightMarketExpiresAt.Value.ToLocalTime():dd/MM HH:mm}" : "";
            TxtStoreStatus.Text = legacy ? "Bản quét cũ chưa xác thực giá và thời hạn. Hãy lấy token mới và quét lại cửa hàng."
                : !string.IsNullOrEmpty(store.StatusMessage) ? store.StatusMessage
                : !dailyValid ? "Cửa hàng đã hết hạn hoặc chưa có thời gian reset. Hãy quét lại để lấy cửa hàng mới."
                : $"Dữ liệu lần quét: {store.FetchedAt?.ToLocalTime():dd/MM/yyyy HH:mm}.";
            if (dailyValid) {
                var remaining = store.DailyExpiresAt!.Value - now;
                TxtStoreCountdown.Text = $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
            else TxtStoreCountdown.Text = "Cần quét lại";
        }
        Update();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => Update();
        _countdownTimer.Start();
    }
    // ──────────────────────────────────────────────
    //  BỘ LỌC & TÌM KIẾM KHO SKIN / VẬT PHẨM
    // ──────────────────────────────────────────────

    private void BtnCatPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            MyGears.Views.CheckAccountMotion.Reveal(ScrollInventorySkins);
            _activeCategory = tag;
            UpdateCategoryPillStyles();
            ApplyFilterAndSearch();
        }
    }

    private void UpdateCategoryPillStyles()
    {
        var pills = new[] { BtnCatWeaponSkins, BtnCatBuddies, BtnCatCards, BtnCatSprays, BtnCatAgents };
        foreach (var p in pills)
        {
            if (p == null) continue;
            bool isActive = (p.Tag as string) == _activeCategory;
            if (isActive)
            {
                p.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#F39C12")!;
                p.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#0E111A")!;
                p.FontWeight = FontWeights.Bold;
            }
            else
            {
                p.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#141824")!;
                p.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#E0E6ED")!;
                p.FontWeight = FontWeights.Normal;
            }
        }

        if (PanelWeaponSubFilter != null && TxtCategoryHeaderLabel != null)
        {
            if (_activeCategory == "weapons")
            {
                PanelWeaponSubFilter.Visibility = Visibility.Visible;
                TxtCategoryHeaderLabel.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelWeaponSubFilter.Visibility = Visibility.Collapsed;
                TxtCategoryHeaderLabel.Visibility = Visibility.Visible;
                TxtCategoryHeaderLabel.Text = _activeCategory switch
                {
                    "buddies" => "Danh sách Phụ Kiện Súng (Buddies) đã sở hữu",
                    "cards" => "Danh sách Thẻ Người Chơi (Cards) đã mở khóa",
                    "sprays" => "Danh sách Hình Phun Sơn (Sprays) đã sở hữu",
                    "agents" => "Danh sách Đặc Vụ (Agents) sẵn sàng chiến đấu",
                    _ => "Danh sách vật phẩm đã mở khóa"
                };
            }
        }
    }

    private void FilterWeapon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string weapon)
        {
            MyGears.Views.CheckAccountMotion.Reveal(ScrollInventorySkins);
            _currentWeaponFilter = weapon;
            ApplyFilterAndSearch();
        }
    }

    private void TxtSearchSkin_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilterAndSearch();
    }

    private void ApplyFilterAndSearch()
    {
        if (_currentScan == null) return;

        var query = TxtSearchSkin?.Text.Trim().ToLowerInvariant() ?? "";
        _displayedInventoryItems.Clear();

        if (_activeCategory == "weapons")
        {
            var queryList = _currentScan.OwnedSkins.AsEnumerable();

            if (!_currentWeaponFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                queryList = queryList.Where(s => s.WeaponName.Contains(_currentWeaponFilter, StringComparison.OrdinalIgnoreCase) ||
                                                 s.DisplayName.Contains(_currentWeaponFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(query))
            {
                queryList = queryList.Where(s => s.DisplayName.ToLowerInvariant().Contains(query) ||
                                                 s.WeaponName.ToLowerInvariant().Contains(query));
            }

            foreach (var s in queryList)
            {
                _displayedInventoryItems.Add(new ValorantItem
                {
                    Uuid = s.Uuid,
                    DisplayName = s.DisplayName,
                    CategoryName = s.WeaponName,
                    DisplayIcon = s.DisplayIcon,
                    BorderColor = s.TierColor,
                    TagText = s.TierName,
                    Cost = s.Cost
                });
            }

            TxtInvTotalCount.Text = _currentScan.InventoryKnown("Skins") ? $"{_displayedInventoryItems.Count} Skins" : "Kho skin chưa xác minh";
            TxtInvTotalValue.Text = _currentScan.InventoryKnown("Skins") ? $"Ước tính ~{_displayedInventoryItems.Sum(s => s.Cost):N0} VP" : "Chưa đủ dữ liệu định giá";
        }
        else if (_activeCategory == "buddies")
        {
            var list = _currentScan.OwnedBuddies.AsEnumerable();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(b => b.DisplayName.ToLowerInvariant().Contains(query));

            foreach (var item in list) _displayedInventoryItems.Add(item);
            TxtInvTotalCount.Text = $"{_displayedInventoryItems.Count} Phụ Kiện (Buddies)";
            TxtInvTotalValue.Text = "";
        }
        else if (_activeCategory == "cards")
        {
            var list = _currentScan.OwnedCards.AsEnumerable();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(c => c.DisplayName.ToLowerInvariant().Contains(query));

            foreach (var item in list) _displayedInventoryItems.Add(item);
            TxtInvTotalCount.Text = $"{_displayedInventoryItems.Count} Thẻ (Player Cards)";
            TxtInvTotalValue.Text = "";
        }
        else if (_activeCategory == "sprays")
        {
            var list = _currentScan.OwnedSprays.AsEnumerable();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(s => s.DisplayName.ToLowerInvariant().Contains(query));

            foreach (var item in list) _displayedInventoryItems.Add(item);
            TxtInvTotalCount.Text = $"{_displayedInventoryItems.Count} Hình Sơn (Sprays)";
            TxtInvTotalValue.Text = "";
        }
        else if (_activeCategory == "agents")
        {
            var list = _currentScan.OwnedAgents.AsEnumerable();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(a => a.DisplayName.ToLowerInvariant().Contains(query) || a.CategoryName.ToLowerInvariant().Contains(query));

            foreach (var item in list) _displayedInventoryItems.Add(item);
            TxtInvTotalCount.Text = $"{_displayedInventoryItems.Count} Đặc Vụ (Agents)";
            TxtInvTotalValue.Text = "";
        }

        if (PanelEmptyInventory != null && ScrollInventorySkins != null)
        {
            if (_displayedInventoryItems.Count == 0)
            {
                PanelEmptyInventory.Visibility = Visibility.Visible;
                ScrollInventorySkins.Visibility = Visibility.Collapsed;
                if (TxtEmptyInventoryNotice != null)
                {
                    var category = _activeCategory == "weapons" ? "Skins" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(_activeCategory);
                    TxtEmptyInventoryNotice.Text = !_currentScan.InventoryKnown(category)
                        ? "Chưa xác minh được kho đồ. Hãy quét lại; không thể kết luận tài khoản không có vật phẩm."
                        : "Không tìm thấy vật phẩm phù hợp bộ lọc hiện tại.";
                }
            }
            else
            {
                PanelEmptyInventory.Visibility = Visibility.Collapsed;
                ScrollInventorySkins.Visibility = Visibility.Visible;
            }
        }
    }

    // ──────────────────────────────────────────────
    //  CHUYỂN 3 TAB CHÍNH (THÔNG TIN, CỬA HÀNG, KHO ĐỒ)
    // ──────────────────────────────────────────────

    private void TabRadio_Checked(object sender, RoutedEventArgs e)
    {
        UpdateViewVisibility();
    }

    private void UpdateViewVisibility()
    {
        if (PanelEmptyState == null || PanelInfoView == null || PanelStoreView == null || PanelInventoryView == null) return;

        if (_currentScan == null)
        {
            if (PanelEmptyState.Visibility != Visibility.Visible) MyGears.Views.CheckAccountMotion.Reveal(PanelEmptyState);
            _visibleTab = null;
            PanelEmptyState.Visibility = Visibility.Visible;
            PanelInfoView.Visibility = Visibility.Collapsed;
            PanelStoreView.Visibility = Visibility.Collapsed;
            PanelInventoryView.Visibility = Visibility.Collapsed;
            return;
        }

        PanelEmptyState.Visibility = Visibility.Collapsed;
        PanelInfoView.Visibility = TabRadioInfo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelStoreView.Visibility = TabRadioStore.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelInventoryView.Visibility = TabRadioInventory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FrameworkElement current = TabRadioInfo.IsChecked == true ? PanelInfoView : TabRadioStore.IsChecked == true ? PanelStoreView : PanelInventoryView;
        if (_visibleTab != current) { _visibleTab = current; MyGears.Views.CheckAccountMotion.Reveal(current); }
    }

    // ──────────────────────────────────────────────
    //  XUẤT ẢNH PNG
    // ──────────────────────────────────────────────

    private void BtnExportCardPng_Click(object sender, RoutedEventArgs e)
    {
        if (_currentScan == null)
        {
            MessageBox.Show("Vui lòng đăng nhập hoặc quét tài khoản trước khi xuất ảnh.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Render giao diện hiện tại
            var width = (int)ActualWidth;
            var height = (int)ActualHeight;
            if (width <= 0) width = 1100;
            if (height <= 0) height = 650;

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            var sfd = new SaveFileDialog
            {
                Title = "Lưu Ảnh Tổng Hợp Tài Khoản Valorant",
                Filter = "PNG Image (*.png)|*.png",
                FileName = $"MyGears_Valorant_{(_currentScan.Profile.RiotId.Replace('#', '_'))}_{DateTime.Now:yyyyMMdd_HHmm}.png"
            };

            if (sfd.ShowDialog() == true)
            {
                using var fs = File.OpenWrite(sfd.FileName);
                encoder.Save(fs);
                ShowToast($"✅ Đã xuất ảnh thành công vào: {Path.GetFileName(sfd.FileName)}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi xuất ảnh: {ex.Message}", "MyGears", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ShowToast(string msg)
    {
        TxtToastMessage.Text = msg;
        ToastNotice.Visibility = Visibility.Visible;
        var version = ++_toastVersion;
        MyGears.Views.CheckAccountMotion.Reveal(ToastNotice);
        await Task.Delay(3500);
        if (version == _toastVersion) ToastNotice.Visibility = Visibility.Collapsed;
    }
}

