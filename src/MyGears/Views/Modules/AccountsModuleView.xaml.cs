using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class AccountsModuleView : UserControl
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    private string _currentPin = string.Empty;
    private readonly ObservableCollection<GameAccount> _accounts = [];
    private GameAccount? _editingAccount;
    private GameAccount? _inspectingAccount;

    public AccountsModuleView()
    {
        InitializeComponent();
        AccountsItemsControl.ItemsSource = _accounts;
        _accounts.CollectionChanged += (_, _) => ApplySkinSearch();
        BulkPreviewItemsControl.ItemsSource = _bulkImportItems;
        RefreshVaultState();
        Loaded += (_, _) => UpdateAntiCaptureVisual(SecurityService.IsAntiCaptureActive);

        ValInspectorControl.OnScanCompleted += res =>
        {
            if (_inspectingAccount != null)
            {
                _inspectingAccount.ValorantData = res;
                AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);
            }
        };

        ValInspectorControl.OnScanCleared += () =>
        {
            if (_inspectingAccount != null)
            {
                _inspectingAccount.ValorantData = null;
                AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);
            }
        };
    }

    private void SkinSearch_Changed(object sender, TextChangedEventArgs e) => ApplySkinSearch();
    private void ClearSkinSearch_Click(object sender, RoutedEventArgs e) => TxtSkinSearch.Clear();
    private void ApplySkinSearch()
    {
        if (TxtSkinSearch == null || TxtSkinSearchStatus == null || AccountsItemsControl == null) return;
        var query = TxtSkinSearch.Text.Trim();
        foreach (var account in _accounts) account.SkinSearchQuery = query;
        var filtered = query.Length == 0 ? _accounts.ToList() : _accounts.Where(a => SkinSearch.Matches(a.ValorantData, query).Count > 0).ToList();
        AccountsItemsControl.ItemsSource = filtered;
        var unknown = _accounts.Count(a => (a.Game.Equals("Valorant", StringComparison.OrdinalIgnoreCase) || a.ValorantData != null) && (a.ValorantData == null || !a.ValorantData.InventoryKnown("Skins")));
        TxtSkinSearchStatus.Text = query.Length == 0 ? $"{_accounts.Count} tài khoản • Tìm trong kho skin của các lần quét đã lưu."
            : $"{filtered.Count} tài khoản có skin khớp “{query}” trong bản quét đã lưu.";
        if (unknown > 0) TxtSkinSearchStatus.Text += $" {unknown} tài khoản chưa quét hoặc kho chưa xác minh; kết quả có thể thiếu.";
        PanelEmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshEmail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameAccount account }) return;
        if (account.ValorantData == null || string.IsNullOrEmpty(account.ValorantData.Profile.Puuid))
        {
            BtnInspectValorant_Click(sender, e);
            return;
        }
        var scan = account.ValorantData;
        try
        {
            var login = new MyGears.Views.ValorantLoginWindow { Owner = Window.GetWindow(this), PrefillUsername = account.Username, PrefillPassword = account.Password };
            if (login.ShowDialog() != true || string.IsNullOrEmpty(login.ResultAccessToken)) return;
            var profile = await ValorantApiService.GetUserInfoAsync(login.ResultAccessToken);
            if (!string.Equals(profile.Puuid, scan.Profile.Puuid, StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("Bạn đã đăng nhập tài khoản Riot khác. Chưa lưu thông tin email.", "MyGears"); return; }
            if (string.IsNullOrEmpty(_currentPin) || !_accounts.Contains(account)) return;
            if (!ValorantApiService.UpdateEmailForAccount(scan, profile)) return;
            AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);
            ApplySkinSearch();
            MessageBox.Show($"{profile.AccountInfo?.EmailText}\n{profile.AccountInfo?.EmailStatus}", "Thông tin email từ Riot");
        }
        catch { MessageBox.Show("Chưa lấy được thông tin email. Kiểm tra kết nối và đăng nhập lại; dữ liệu đã lưu được giữ nguyên.", "MyGears"); }
        finally { try { await ValorantApiService.LogoutRiotSessionAsync(); } catch { } }
    }

    private void RefreshVaultState()
    {
        LblPinError.Visibility = Visibility.Collapsed;

        if (!AccountVaultService.IsVaultConfigured)
        {
            // Chưa có vault -> Hiện form tạo PIN mới
            PanelSetupPin.Visibility = Visibility.Visible;
            PanelEnterPin.Visibility = Visibility.Collapsed;
            TxtNewPin.Focus();
        }
        else
        {
            // Đã có vault -> Hiện form nhập PIN
            PanelSetupPin.Visibility = Visibility.Collapsed;
            PanelEnterPin.Visibility = Visibility.Visible;
            TxtUnlockPin.Focus();
        }
    }

    // ──────────────────────────────────────────────
    //  PIN SETUP & UNLOCK
    // ──────────────────────────────────────────────

    private void BtnSetupPin_Click(object sender, RoutedEventArgs e)
    {
        var pin1 = TxtNewPin.Password.Trim();
        var pin2 = TxtConfirmPin.Password.Trim();

        if (pin1.Length < 4)
        {
            ShowError("Mã PIN phải có ít nhất 4 ký tự hoặc số.");
            return;
        }

        if (pin1 != pin2)
        {
            ShowError("Mã PIN xác nhận không khớp. Vui lòng nhập lại.");
            return;
        }

        AccountVaultService.SetupMasterPin(pin1);
        UnlockVault(pin1);
    }

    private void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        AttemptUnlock();
    }

    private void TxtUnlockPin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AttemptUnlock();
    }

    private void AttemptUnlock()
    {
        var pin = TxtUnlockPin.Password.Trim();
        if (string.IsNullOrEmpty(pin))
        {
            ShowError("Vui lòng nhập mã PIN.");
            return;
        }

        if (AccountVaultService.VerifyPin(pin))
        {
            UnlockVault(pin);
        }
        else
        {
            ShowError("Mã PIN không chính xác. Vui lòng thử lại.");
            TriggerPinShakeAnimation();
            TxtUnlockPin.SelectAll();
        }
    }

    private void TriggerPinShakeAnimation()
    {
        var shakeAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(380)
        };

        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(0)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-12, TimeSpan.FromMilliseconds(50)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(12, TimeSpan.FromMilliseconds(100)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-8, TimeSpan.FromMilliseconds(160)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(8, TimeSpan.FromMilliseconds(220)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, TimeSpan.FromMilliseconds(280)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(4, TimeSpan.FromMilliseconds(330)));
        shakeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(380)));

        PinShakeTransform.BeginAnimation(TranslateTransform.XProperty, shakeAnim);
    }

    private void UnlockVault(string pin)
    {
        _currentPin = pin;
        LblPinError.Visibility = Visibility.Collapsed;
        TxtUnlockPin.Clear();
        TxtNewPin.Clear();
        TxtConfirmPin.Clear();

        PanelPinLocked.Visibility = Visibility.Collapsed;
        PanelAccountsList.Visibility = Visibility.Visible;

        LoadAccountsList();
    }

    private void LoadAccountsList()
    {
        _accounts.Clear();
        var list = AccountVaultService.LoadAccounts(_currentPin);
        foreach (var acc in list)
            _accounts.Add(acc);

        ApplySkinSearch();
    }

    private void BtnLockVault_Click(object sender, RoutedEventArgs e)
    {
        _currentPin = string.Empty;
        _accounts.Clear();
        TxtSkinSearch.Clear();
        ApplySkinSearch();
        PanelAccountsList.Visibility = Visibility.Collapsed;
        PanelPinLocked.Visibility = Visibility.Visible;
        RefreshVaultState();
    }

    private void ShowError(string msg)
    {
        LblPinError.Text = msg;
        LblPinError.Visibility = Visibility.Visible;
    }

    // ──────────────────────────────────────────────
    //  COPY ACTIONS (tk:mk & Auto-Wipe Clipboard)
    // ──────────────────────────────────────────────

    private void BtnCopyCombo_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is GameAccount acc)
        {
            // Copy định dạng tk:mk
            var combo = acc.Combo;
            SecurityService.CopyToClipboardWithAutoWipe(
                combo,
                wipeAfterSeconds: 25,
                onTickSecondsRemaining: sec =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToastClipboardWipe.Visibility = Visibility.Visible;
                        TxtClipboardNotice.Text = $"Đã copy theo chuẩn tk:mk ({acc.Username}:••••)! Sẽ tự xóa Clipboard sau {sec}s.";
                    });
                },
                onWiped: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtClipboardNotice.Text = "🛡️ Đã xóa sạch Clipboard an toàn!";
                        Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
                    });
                });
        }
    }

    private void BtnCopySingle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is string text)
        {
            SecurityService.CopyToClipboardWithAutoWipe(
                text,
                wipeAfterSeconds: 25,
                onTickSecondsRemaining: sec =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToastClipboardWipe.Visibility = Visibility.Visible;
                        TxtClipboardNotice.Text = $"Đã copy: {text}! Sẽ tự xóa Clipboard sau {sec}s.";
                    });
                },
                onWiped: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtClipboardNotice.Text = "🛡️ Đã xóa sạch Clipboard an toàn!";
                        Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
                    });
                });
        }
    }

    private void BtnCopyPass_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is string pass)
        {
            SecurityService.CopyToClipboardWithAutoWipe(
                pass,
                wipeAfterSeconds: 25,
                onTickSecondsRemaining: sec =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToastClipboardWipe.Visibility = Visibility.Visible;
                        TxtClipboardNotice.Text = $"Đã copy mật khẩu! Bộ nhớ tạm sẽ tự xóa sau {sec}s.";
                    });
                },
                onWiped: () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtClipboardNotice.Text = "🛡️ Đã xóa sạch mật khẩu khỏi Clipboard an toàn!";
                        Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
                    });
                });
        }
    }

    private void BtnToggleShowPass_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Parent is StackPanel sp)
        {
            var txtMasked = sp.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "TxtMaskedPass");
            var txtReal = sp.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "TxtRealPass");

            if (txtMasked != null && txtReal != null)
            {
                if (txtMasked.Visibility == Visibility.Visible)
                {
                    txtMasked.Visibility = Visibility.Collapsed;
                    txtReal.Visibility = Visibility.Visible;
                    btn.Content = "🙈";
                }
                else
                {
                    txtMasked.Visibility = Visibility.Visible;
                    txtReal.Visibility = Visibility.Collapsed;
                    btn.Content = "👁️";
                }
            }
        }
    }

    private void DismissToast_Click(object sender, RoutedEventArgs e)
    {
        ToastClipboardWipe.Visibility = Visibility.Collapsed;
    }

    private void AccountCard_MouseEnter(object sender, MouseEventArgs e) { if (sender is Border card) MyGears.Views.AccountCardAnimation.Apply(card, true); }
    private void AccountCard_MouseLeave(object sender, MouseEventArgs e) { if (sender is Border card) MyGears.Views.AccountCardAnimation.Apply(card, false); }
    private void AccountCard_Press(object sender, MouseButtonEventArgs e) { if (sender is Border card) MyGears.Views.AccountCardAnimation.Apply(card, true, true); }
    private void AccountCard_Release(object sender, MouseButtonEventArgs e) { if (sender is Border card) MyGears.Views.AccountCardAnimation.Apply(card, card.IsMouseOver); }

    private void AccountCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || sender is not Border card) return;
        var index = card.DataContext is GameAccount account ? _accounts.IndexOf(account) : 0;
        var delay = Math.Min(Math.Max(index, 0), 8) * 22;
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + 220)))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        card.BeginAnimation(OpacityProperty, fade);
    }

    private static void AnimateAccountPage(FrameworkElement page)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        var slide = new TranslateTransform();
        page.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }

    private void AccountCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameAccount acc) return;
        _inspectingAccount = acc;

        // Chỉ chuyển sang màn hình xem khi tài khoản đã có dữ liệu soi
        if (acc.ValorantData != null)
        {
            ShowValorantInspector(acc);
        }
        // Nếu chưa có dữ liệu: Không tự ý mở popup và không tự ý copy đè clipboard!
    }

    private void AccountMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: GameAccount account } button) return;
        var menu = new ContextMenu { PlacementTarget = button };
        void Add(string title, RoutedEventHandler handler, object tag)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, args) => handler(new Button { Tag = tag }, args);
            menu.Items.Add(item);
        }
        Add("Xem kho / Quét Valorant", BtnInspectValorant_Click, account);
        Add("Lấy lại thông tin email từ Riot", RefreshEmail_Click, account);
        if (!string.IsNullOrWhiteSpace(account.ValorantData?.Profile.AccountInfo?.Email))
            Add("Sao chép email Riot", BtnCopySingle_Click, account.ValorantData.Profile.AccountInfo.Email);
        Add("Sửa tài khoản", BtnEditAccount_Click, account);
        Add("Sao chép tài khoản : mật khẩu", BtnCopyCombo_Click, account);
        Add("Sao chép tên đăng nhập", BtnCopySingle_Click, account.Username);
        Add("Sao chép mật khẩu", BtnCopyPass_Click, account.Password);
        menu.IsOpen = true;
    }

    private async void BtnInspectValorant_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not GameAccount acc) return;
        _inspectingAccount = acc;

        // Nếu tài khoản đã có dữ liệu soi -> Mở xem ngay lập tức mà không cần quét lại!
        if (acc.ValorantData != null)
        {
            ShowValorantInspector(acc);
            return;
        }

        // Nếu chưa có dữ liệu -> Khởi chạy quy trình soi
        await StartValorantInspectionFlowAsync(acc);
    }

    private void ShowValorantInspector(GameAccount acc)
    {
        ValInspectorControl.SavedAccountUsername = acc.Username;
        _inspectingAccount = acc;
        TxtInspectorAccountLabel.Text = string.IsNullOrEmpty(acc.Label) ? acc.Username : $"{acc.Label} ({acc.Username})";
        TxtInspectorAccountCombo.Text = $"[{acc.Username}]";

        PanelAccountsList.Visibility = Visibility.Collapsed;
        PanelValorantInspector.Visibility = Visibility.Visible;
        AnimateAccountPage(PanelValorantInspector);

        if (acc.ValorantData != null)
        {
            ValInspectorControl.LoadScanResult(acc.ValorantData);
        }
    }

    private void BtnBackToAccounts_Click(object sender, RoutedEventArgs e)
    {
        PanelValorantInspector.Visibility = Visibility.Collapsed;
        PanelAccountsList.Visibility = Visibility.Visible;
        AnimateAccountPage(PanelAccountsList);
        LoadAccountsList(); // Cập nhật lại huy hiệu & nút bấm cho tài khoản
    }

    private void BtnInspectorCopyCombo_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectingAccount == null) return;
        SecurityService.CopyToClipboardWithAutoWipe(
            _inspectingAccount.Combo,
            wipeAfterSeconds: 45,
            onTickSecondsRemaining: sec =>
            {
                Dispatcher.Invoke(() =>
                {
                    ToastClipboardWipe.Visibility = Visibility.Visible;
                    TxtClipboardNotice.Text = $"📋 Đã copy tk:mk ({_inspectingAccount.Username})! Tự xóa sau {sec}s.";
                });
            },
            onWiped: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtClipboardNotice.Text = "🛡️ Đã xóa sạch tk:mk khỏi Clipboard an toàn!";
                    Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
                });
            });
    }

    private async void BtnInspectorRescan_Click(object sender, RoutedEventArgs e)
    {
        if (_inspectingAccount == null) return;
        await StartValorantInspectionFlowAsync(_inspectingAccount);
    }

    private async Task StartValorantInspectionFlowAsync(GameAccount acc)
    {
        _inspectingAccount = acc;

        // 1. Tự động sao chép định dạng tk:mk vào clipboard để người dùng tự paste nếu cần
        SecurityService.CopyToClipboardWithAutoWipe(
            acc.Combo,
            wipeAfterSeconds: 60,
            onTickSecondsRemaining: sec =>
            {
                Dispatcher.Invoke(() =>
                {
                    ToastClipboardWipe.Visibility = Visibility.Visible;
                    TxtClipboardNotice.Text = $"🎯 Đã copy tk:mk ({acc.Username}) vào bộ nhớ tạm! ({sec}s)";
                });
            },
            onWiped: () =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtClipboardNotice.Text = "🛡️ Đã xóa sạch tk:mk khỏi Clipboard an toàn!";
                    Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
                });
            });

        // 2. Mở cửa sổ đăng nhập Riot
        try
        {
            var loginWin = new ValorantLoginWindow
            {
                Owner = Window.GetWindow(this),
                PrefillUsername = acc.Username,
                PrefillPassword = acc.Password
            };

            if (loginWin.ShowDialog() == true && !string.IsNullOrEmpty(loginWin.ResultAccessToken))
            {
                // Khi bắt được link redirect và token -> cửa sổ đã tự đóng
                ToastClipboardWipe.Visibility = Visibility.Visible;
                TxtClipboardNotice.Text = $"⏳ Đang quét toàn bộ kho skin, rank, ví và cửa hàng cho {acc.Username}...";

                var result = await ValorantApiService.ScanAccountAsync(loginWin.ResultAccessToken, loginWin.ResultIdToken ?? string.Empty);

                // Lưu / Cache dữ liệu vào tài khoản này
                acc.ValorantData = result;

                // Lưu vào file két sắt accounts.enc
                AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);

                // Đăng xuất phiên Riot ngầm để lần quét tiếp theo không bị kẹt hay dính acc cũ
                _ = Task.Run(async () =>
                {
                    try { await ValorantApiService.LogoutRiotSessionAsync(); } catch { }
                });

                // Chuyển sang màn hình Inspector hiển thị chi tiết cho tài khoản
                ShowValorantInspector(acc);
                TxtClipboardNotice.Text = $"🎉 Đã quét và lưu dữ liệu thành công cho tài khoản {result.Profile.RiotId}!";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Có lỗi xảy ra khi quét tài khoản Valorant: {ex.Message}\n\nVui lòng thử lại.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ValorantLoginWindow.ForceRestoreCursor();
        }
    }

    // ──────────────────────────────────────────────
    //  GAME SELECTION (CHIP SELECTOR)
    // ──────────────────────────────────────────────

    private string _selectedGame = "Valorant";

    private void ChipGame_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string game)
        {
            SelectGame(game);
        }
    }

    private void SelectGame(string game)
    {
        _selectedGame = game;
        UpdateChipVisuals();
    }

    private void UpdateChipVisuals()
    {
        SetChipStyle(ChipValorant, _selectedGame.Equals("Valorant", StringComparison.OrdinalIgnoreCase), "#E8321A", "#28110E");
        SetChipStyle(ChipSteam, _selectedGame.StartsWith("Steam", StringComparison.OrdinalIgnoreCase), "#3498DB", "#0E1E28");
        SetChipStyle(ChipRiot, _selectedGame.StartsWith("Riot", StringComparison.OrdinalIgnoreCase), "#E74C3C", "#281111");
        SetChipStyle(ChipEpic, _selectedGame.StartsWith("Epic", StringComparison.OrdinalIgnoreCase), "#F39C12", "#28200E");
        SetChipStyle(ChipOther, _selectedGame.Equals("Khác", StringComparison.OrdinalIgnoreCase) || (!_selectedGame.StartsWith("Valorant") && !_selectedGame.StartsWith("Steam") && !_selectedGame.StartsWith("Riot") && !_selectedGame.StartsWith("Epic")), "#9B59B6", "#200E28");
    }

    private static void SetChipStyle(Button btn, bool isSelected, string activeBorder, string activeBg)
    {
        if (btn.Template?.FindName("Bd", btn) is Border bd && bd.Child is StackPanel sp)
        {
            var txt = sp.Children.OfType<TextBlock>().LastOrDefault();
            if (isSelected)
            {
                bd.Background = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(activeBg)!;
                bd.BorderBrush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom(activeBorder)!;
                bd.BorderThickness = new Thickness(1.5);
                if (txt != null)
                {
                    txt.Foreground = System.Windows.Media.Brushes.White;
                    txt.FontWeight = FontWeights.Bold;
                }
            }
            else
            {
                bd.Background = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom("#181818")!;
                bd.BorderBrush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom("#333333")!;
                bd.BorderThickness = new Thickness(1);
                if (txt != null)
                {
                    txt.Foreground = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom("#AAAAAA")!;
                    txt.FontWeight = FontWeights.Normal;
                }
            }
        }
    }

    // ──────────────────────────────────────────────
    //  ADD / EDIT / DELETE ACCOUNT
    // ──────────────────────────────────────────────

    private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
    {
        _editingAccount = null;
        TxtModalTitle.Text = "Thêm Tài Khoản Game Mới";
        SelectGame("Valorant");
        TxtEditLabel.Text = string.Empty;
        TxtEditUsername.Text = string.Empty;
        TxtEditPassword.Text = string.Empty;

        ModalAccountEditor.Visibility = Visibility.Visible;
        TxtEditLabel.Focus();
    }

    private void BtnEditAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameAccount acc)
        {
            _editingAccount = acc;
            TxtModalTitle.Text = "Chỉnh Sửa Tài Khoản";
            SelectGame(acc.Game);

            TxtEditLabel.Text = acc.Label;
            TxtEditUsername.Text = acc.Username;
            TxtEditPassword.Text = acc.Password;

            ModalAccountEditor.Visibility = Visibility.Visible;
            TxtEditLabel.Focus();
        }
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        ModalAccountEditor.Visibility = Visibility.Collapsed;
        _editingAccount = null;
    }

    private void BtnSaveAccount_Click(object sender, RoutedEventArgs e)
    {
        var game = _selectedGame;
        if (game.Contains('(')) game = game.Split('(')[0].Trim();

        var label = TxtEditLabel.Text.Trim();
        var username = TxtEditUsername.Text.Trim();
        var password = TxtEditPassword.Text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Vui lòng nhập đầy đủ Tên đăng nhập và Mật khẩu.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(label))
            label = $"{game} ({username})";

        if (_editingAccount == null)
        {
            // Thêm mới
            var newAcc = new GameAccount
            {
                Game = game,
                Label = label,
                Username = username,
                Password = password
            };
            _accounts.Add(newAcc);
        }
        else
        {
            // Cập nhật
            _editingAccount.Game = game;
            _editingAccount.Label = label;
            _editingAccount.Username = username;
            _editingAccount.Password = password;

            // Refresh items view
            LoadAccountsList();
        }

        // Lưu và mã hóa với mã PIN hiện tại
        AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);

        ModalAccountEditor.Visibility = Visibility.Collapsed;
        _editingAccount = null;
        PanelEmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameAccount acc)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa tài khoản \"{acc.Label}\" ({acc.Username})?",
                "MyGears - Xác nhận xóa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _accounts.Remove(acc);
                AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);
                PanelEmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void BtnToggleAntiCapture_Click(object sender, RoutedEventArgs e)
    {
        var win = Window.GetWindow(this);
        if (win == null) return;

        bool newState = !SecurityService.IsAntiCaptureActive;
        SecurityService.SetWindowAntiCapture(win, newState);
        UpdateAntiCaptureVisual(newState);
    }

    private void UpdateAntiCaptureVisual(bool isEnabled)
    {
        if (isEnabled)
        {
            BtnToggleAntiCapture.Background = new SolidColorBrush(Color.FromRgb(0x13, 0x25, 0x1A));
            BtnToggleAntiCapture.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            TxtAntiCaptureIcon.Text = "🛡️";
            TxtAntiCaptureStatus.Text = "Chống Chụp: BẬT (Đen khi chụp)";
            TxtAntiCaptureStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            BtnToggleAntiCapture.ToolTip = "Hiện tại ĐANG BẢO VỆ (màn hình sẽ đen kịt khi bị chụp trộm). Bấm để TẮT nếu muốn chụp ảnh màn hình.";
        }
        else
        {
            BtnToggleAntiCapture.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1A, 0x10));
            BtnToggleAntiCapture.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
            TxtAntiCaptureIcon.Text = "📷";
            TxtAntiCaptureStatus.Text = "Chống Chụp: TẮT (Cho phép chụp)";
            TxtAntiCaptureStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
            BtnToggleAntiCapture.ToolTip = "Hiện tại ĐANG CHO PHÉP chụp ảnh màn hình. Bấm để BẬT chống chụp trộm màn hình.";
        }
    }

    // ──────────────────────────────────────────────
    //  BULK IMPORT (NHẬP HÀNG LOẠT TỪ DISCORD / BOT / SHOP)
    // ──────────────────────────────────────────────

    private string _bulkSelectedGame = "Valorant";
    private readonly ObservableCollection<BulkAccountItem> _bulkImportItems = [];

    private void BtnBulkImport_Click(object sender, RoutedEventArgs e)
    {
        TxtBulkInput.Text = string.Empty;
        TxtBulkCommonNote.Text = string.Empty;
        _bulkImportItems.Clear();
        SelectBulkGame("Valorant");
        UpdateBulkStatus();
        ModalBulkImport.Visibility = Visibility.Visible;
        TxtBulkInput.Focus();
    }

    private void BtnCancelBulkImport_Click(object sender, RoutedEventArgs e)
    {
        ModalBulkImport.Visibility = Visibility.Collapsed;
        _bulkImportItems.Clear();
    }

    private void BulkChipGame_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string game)
        {
            SelectBulkGame(game);
        }
    }

    private void SelectBulkGame(string game)
    {
        _bulkSelectedGame = game;
        SetChipStyle(BulkChipValorant, _bulkSelectedGame.Equals("Valorant", StringComparison.OrdinalIgnoreCase), "#E8321A", "#28110E");
        SetChipStyle(BulkChipRiot, _bulkSelectedGame.StartsWith("Riot", StringComparison.OrdinalIgnoreCase), "#E74C3C", "#281111");
        SetChipStyle(BulkChipSteam, _bulkSelectedGame.StartsWith("Steam", StringComparison.OrdinalIgnoreCase), "#3498DB", "#0E1E28");
        SetChipStyle(BulkChipOther, !_bulkSelectedGame.StartsWith("Valorant") && !_bulkSelectedGame.StartsWith("Riot") && !_bulkSelectedGame.StartsWith("Steam"), "#9B59B6", "#200E28");

        var cleanGame = _bulkSelectedGame.Contains('(') ? _bulkSelectedGame.Split('(')[0].Trim() : _bulkSelectedGame;
        foreach (var item in _bulkImportItems)
        {
            item.Game = cleanGame;
        }
    }

    private void TxtBulkInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _bulkImportItems.Clear();
        var rawText = TxtBulkInput.Text;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            UpdateBulkStatus();
            return;
        }

        var cleanGame = _bulkSelectedGame.Contains('(') ? _bulkSelectedGame.Split('(')[0].Trim() : _bulkSelectedGame;
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int idx = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Bỏ qua các dòng rác từ bot/shop
            if (line.StartsWith("Cảm ơn", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Thông tin", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Đơn hàng", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("---") || line.StartsWith("===") || line.StartsWith("***") ||
                line.StartsWith("🎁") || line.StartsWith("⭐") || line.StartsWith("📌"))
            {
                continue;
            }

            string u = string.Empty;
            string p = string.Empty;
            string note = string.Empty;

            // Định dạng 1: Có dấu gạch đứng | (Ví dụ: user | pass | note)
            if (line.Contains('|'))
            {
                var parts = line.Split('|').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (parts.Length >= 2)
                {
                    u = parts[0];
                    p = parts[1];
                    if (parts.Length >= 3)
                    {
                        note = string.Join(" | ", parts.Skip(2));
                    }
                }
            }
            // Định dạng 2: Dấu hai chấm : (Ví dụ: user:pass:note hoặc user:pass - note)
            else if (line.Contains(':'))
            {
                string workingLine = line;

                // Tách ghi chú nếu có dạng " - Ghi chú" hoặc " (Ghi chú)"
                int dashIdx = workingLine.IndexOf(" - ");
                if (dashIdx > 0)
                {
                    note = workingLine[(dashIdx + 3)..].Trim();
                    workingLine = workingLine[..dashIdx].Trim();
                }

                var parts = workingLine.Split(':').Select(s => s.Trim()).ToArray();
                if (parts.Length >= 2)
                {
                    u = parts[0];
                    p = parts[1];
                    if (parts.Length >= 3 && string.IsNullOrEmpty(note))
                    {
                        note = string.Join(":", parts.Skip(2));
                    }
                }
            }

            // Làm sạch username nếu bot chèn từ thừa phía trước
            if (u.Contains(' ')) u = u.Split(' ').Last().Trim();

            // Nếu mật khẩu dính khoảng trắng và chưa có note, tách phần sau làm note
            if (p.Contains(' ') && string.IsNullOrEmpty(note))
            {
                var pParts = p.Split(' ', 2);
                p = pParts[0].Trim();
                if (pParts.Length > 1) note = pParts[1].Trim();
            }

            if (!string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p))
            {
                // Nếu chưa có ghi chú, đặt mặc định kèm số thứ tự hoặc username
                if (string.IsNullOrEmpty(note))
                {
                    note = $"{cleanGame} ({u})";
                }

                _bulkImportItems.Add(new BulkAccountItem
                {
                    Index = idx++,
                    Game = cleanGame,
                    Username = u,
                    Password = p,
                    Label = note
                });
            }
        }

        UpdateBulkStatus();
    }

    private void BtnApplyCommonNote_Click(object sender, RoutedEventArgs e)
    {
        var commonNote = TxtBulkCommonNote.Text.Trim();
        if (string.IsNullOrEmpty(commonNote))
        {
            MessageBox.Show("Vui lòng nhập nội dung ghi chú mẫu vào ô bên cạnh trước khi bấm Áp Dụng.", "MyGears", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int i = 1;
        foreach (var item in _bulkImportItems)
        {
            item.Label = $"{commonNote} #{i++}";
        }
    }

    private void BtnRemoveBulkItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BulkAccountItem item)
        {
            _bulkImportItems.Remove(item);
            for (int i = 0; i < _bulkImportItems.Count; i++)
            {
                _bulkImportItems[i].Index = i + 1;
            }
            UpdateBulkStatus();
        }
    }

    private void UpdateBulkStatus()
    {
        if (_bulkImportItems.Count > 0)
        {
            TxtBulkCountPreview.Text = $"✅ Phát hiện {_bulkImportItems.Count} tài khoản hợp lệ (bấm vào ô Ghi Chú bên dưới để sửa nếu cần)";
            TxtBulkCountPreview.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#2ECC71")!;
            BtnExecuteBulkImport.IsEnabled = true;
            BtnExecuteBulkImport.Content = $"💾 Lưu {_bulkImportItems.Count} Tài Khoản Vào Két Sắt";
        }
        else
        {
            TxtBulkCountPreview.Text = string.IsNullOrWhiteSpace(TxtBulkInput.Text)
                ? "Chưa có dữ liệu"
                : "⚠️ Chưa tìm thấy định dạng user:pass hoặc user|pass";
            TxtBulkCountPreview.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(string.IsNullOrWhiteSpace(TxtBulkInput.Text) ? "#888888" : "#F39C12")!;
            BtnExecuteBulkImport.IsEnabled = false;
            BtnExecuteBulkImport.Content = "💾 Lưu 0 Tài Khoản Vào Két Sắt";
        }
    }

    private void BtnExecuteBulkImport_Click(object sender, RoutedEventArgs e)
    {
        if (_bulkImportItems.Count == 0) return;

        var defaultGame = _bulkSelectedGame.Contains('(') ? _bulkSelectedGame.Split('(')[0].Trim() : _bulkSelectedGame;

        int importedCount = 0;
        foreach (var item in _bulkImportItems)
        {
            if (string.IsNullOrWhiteSpace(item.Username) || string.IsNullOrWhiteSpace(item.Password))
                continue;

            // Tránh thêm trùng lặp tài khoản cùng username và password
            if (_accounts.Any(a => a.Username.Equals(item.Username, StringComparison.OrdinalIgnoreCase) && a.Password == item.Password))
                continue;

            var game = string.IsNullOrWhiteSpace(item.Game) ? defaultGame : item.Game.Trim();
            var label = string.IsNullOrWhiteSpace(item.Label) ? $"{game} ({item.Username})" : item.Label.Trim();

            var newAcc = new GameAccount
            {
                Game = game,
                Label = label,
                Username = item.Username.Trim(),
                Password = item.Password.Trim(),
                CreatedAt = DateTime.Now
            };
            _accounts.Add(newAcc);
            importedCount++;
        }

        // Lưu và mã hóa AES-256 với mã PIN hiện tại
        AccountVaultService.SaveAccounts(_accounts.ToList(), _currentPin);

        ModalBulkImport.Visibility = Visibility.Collapsed;
        _bulkImportItems.Clear();
        PanelEmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Thông báo hoàn tất
        ToastClipboardWipe.Visibility = Visibility.Visible;
        TxtClipboardNotice.Text = $"🎉 Đã lưu thành công {importedCount} tài khoản kèm ghi chú riêng vào két sắt!";
        Task.Delay(3500).ContinueWith(_ => Dispatcher.Invoke(() => ToastClipboardWipe.Visibility = Visibility.Collapsed));
    }
}

/// <summary>
/// Model hỗ trợ nhập hàng loạt tài khoản kèm ghi chú riêng từng tài khoản
/// </summary>
public class BulkAccountItem : System.ComponentModel.INotifyPropertyChanged
{
    private int _index = 1;
    private string _game = "Valorant";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _label = string.Empty;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(nameof(Index)); }
    }

    public string Game
    {
        get => _game;
        set { _game = value; OnPropertyChanged(nameof(Game)); }
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(nameof(Username)); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(nameof(Password)); }
    }

    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(nameof(Label)); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
}
