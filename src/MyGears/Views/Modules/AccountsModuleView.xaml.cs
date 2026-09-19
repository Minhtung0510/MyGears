using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyGears.Core;

namespace MyGears.Views.Modules;

public partial class AccountsModuleView : UserControl
{
    private string _currentPin = string.Empty;
    private readonly ObservableCollection<GameAccount> _accounts = [];
    private GameAccount? _editingAccount;

    public AccountsModuleView()
    {
        InitializeComponent();
        AccountsItemsControl.ItemsSource = _accounts;
        RefreshVaultState();
        Loaded += (_, _) => UpdateAntiCaptureVisual(SecurityService.IsAntiCaptureActive);
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

        PanelEmptyAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnLockVault_Click(object sender, RoutedEventArgs e)
    {
        _currentPin = string.Empty;
        _accounts.Clear();
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
        if ((sender as FrameworkElement)?.Tag is string text)
        {
            Clipboard.SetDataObject(text, true);
            ToastClipboardWipe.Visibility = Visibility.Visible;
            TxtClipboardNotice.Text = $"Đã copy: {text}";
        }
    }

    private void BtnCopyPass_Click(object sender, RoutedEventArgs e)
    {
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
}
