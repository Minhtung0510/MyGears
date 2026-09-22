using System.Windows;
using MyGears.Core;
namespace MyGears.Views;
public partial class ValorantRankWindow : Window
{
    public ValorantRankWindow(ValorantScanResult scan)
    {
        InitializeComponent();
        Loaded += (_, _) => CheckAccountMotion.Reveal((FrameworkElement)Content);
        TxtPlayer.Text = scan.Profile.RiotId;
        TxtUpdated.Text = $"Bản quét: {scan.ScanTime:dd/MM/yyyy HH:mm}";
        var rank = scan.Profile.RankData;
        TxtCurrent.Text = rank?.Current is {} current ? $"{current.Title} • {current.Detail}" : "Chưa có rank hiện tại";
        TxtSummary.Text = rank?.Summary ?? "";
        TxtNotice.Text = rank == null ? "Bản quét cũ chưa có lịch sử. Hãy lấy token mới và quét lại." : rank.Error;
        ItemsHistory.ItemsSource = rank?.Seasons;
    }
}
