using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyGears.Core;
using MyGears.Views;
using MyGears.Views.Modules;
class Program {
 static readonly string SourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
 [STAThread] static void Main() {
  var app = new Application();
  app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MyGears;component/Themes/DarkGamingTheme.xaml", UriKind.Relative) });
  typeof(ValorantApiService).GetField("_metadataLoaded", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
  var now = DateTimeOffset.UtcNow;
  var rank = new ValorantRankData { Current = new() { Title="V26 // ACT V", Tier=13,Rr=14 }, Seasons = [new() {Title="V26 // ACT V",Tier=13,PeakTier=13,Rr=14,Wins=5,Games=7},new() {Title="V26 // ACT IV",Tier=9,PeakTier=9,Rr=75,Wins=23,Games=40}] };
  var scan = new ValorantScanResult { DataVersion=1, Profile = new() {DataVersion=1,ValorantPoints=0,RadianitePoints=145,KingdomCredits=12,AccountLevel=38,RiotId="Tài khoản kiểm thử",RankData=rank,FullRankTitle="V26 // ACT V // GOLD 2 (14 RR)",RankIcon=""}, Store = new() {DataVersion=1,FetchedAt=now,DailyExpiresAt=now.AddHours(8),Bundles=[new() {Name="Bộ sưu tập kiểm thử",Price=5460,ExpiresAt=now.AddDays(2)}],Accessories=[new() {Name="Thẻ người chơi kiểm thử",Price=4500,Currency="KC"}],AccessoryExpiresAt=now.AddDays(3)} };
  for(int i=0;i<4;i++) scan.Store.DailyOffers.Add(new() {Skin=new() {DisplayName=new[]{"Holo Meridian Judge","Prime//2.0 Karambit","Daydreams Operator","Neptune Anchor"}[i],DisplayIcon=Path.Combine(SourceRoot, "prime-full.png")},FinalCost=2175});
  var view = new ValorantModuleView(); view.LoadScanResult(scan);
  foreach(var name in new[]{"PanelInfoView","PanelInventoryView"}) ((FrameworkElement)view.FindName(name)).Visibility=Visibility.Collapsed;
  ((FrameworkElement)view.FindName("PanelStoreView")).Visibility=Visibility.Visible;
  Render(view,1080,820,"store.png");
  scan.Profile.AccountInfo = new ValorantIdentity { Username="test-login",Email="owner.long.address@example.com",EmailVerified=true,Country="VNM",CreatedAt=DateTimeOffset.Parse("2023-06-30T04:48:44Z"),RestrictionCount=1 };
  var infoView = new ValorantModuleView(); infoView.LoadScanResult(scan);
  Render(infoView,1080,820,"info.png");
  if (((TextBlock)infoView.FindName("TxtInfoEmail")).Text != "owner.long.address@example.com") throw new Exception("Email not displayed");
  if (((TextBlock)infoView.FindName("TxtBadgeBanStatus")).Text != "CÓ HẠN CHẾ") throw new Exception("Restriction badge incorrect");
  if (((TextBlock)infoView.FindName("TxtInfoVp")).Text != "0 VP") throw new Exception("Real zero hidden");
  scan.Profile.ValorantPoints=null; scan.InventoryErrors["Skins"]="HTTP 403"; infoView.LoadScanResult(scan);
  if (!((TextBlock)infoView.FindName("TxtInfoVp")).Text.Contains("Chưa có")) throw new Exception("Missing balance shown as zero");
  if (((TextBlock)infoView.FindName("TxtTotalSkinsCount")).Text.Contains("0")) throw new Exception("Failed inventory shown as empty");
  Render(infoView,1080,820,"info-missing.png");
  scan.Profile.ValorantPoints=0;scan.InventoryErrors.Clear();
  var document = System.Xml.Linq.XDocument.Load(Path.Combine(SourceRoot, "MyGears/Views/Modules/AccountsModuleView.xaml"));
  System.Xml.Linq.XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation", x="http://schemas.microsoft.com/winfx/2006/xaml";
  var cardStyle=document.Descendants(presentation+"Style").Single(e=>(string?)e.Attribute(x+"Key")=="AccountIconButton");
  app.Resources.Add("AccountIconButton", System.Windows.Markup.XamlReader.Parse(cardStyle.ToString()));
  var source=document.Descendants(presentation+"ItemsControl").Single(e=>(string?)e.Attribute(x+"Name")=="AccountsItemsControl");
  foreach(var attr in source.DescendantsAndSelf().SelectMany(e=>e.Attributes()).Where(a=>a.Name.LocalName is "MouseEnter" or "MouseLeave" or "PreviewMouseLeftButtonDown" or "PreviewMouseLeftButtonUp" or "Loaded" or "Click" or "MouseLeftButtonUp").ToList()) attr.Remove();
  source.Attribute(x+"Name")!.Remove();
  var cards=(ItemsControl)System.Windows.Markup.XamlReader.Parse(source.ToString());
  cards.ItemsSource=Enumerable.Range(0,6).Select(i=>new GameAccount {Username="test"+i,Label=i==5?"Chưa quét":"Tài khoản "+i,ValorantData=i==5?null:scan}).ToList();
  Render(cards,1220,780,"accounts.png");
  document=System.Xml.Linq.XDocument.Load(Path.Combine(SourceRoot, "MyGears/Views/Modules/AccountsModuleView.xaml"));
  var listPanel = document.Descendants(presentation+"Grid").Single(e=>(string?)e.Attribute(x+"Name")=="PanelAccountsList");
  foreach(var attr in listPanel.DescendantsAndSelf().SelectMany(e=>e.Attributes()).Where(a=>a.Name.LocalName is "MouseEnter" or "MouseLeave" or "PreviewMouseLeftButtonDown" or "PreviewMouseLeftButtonUp" or "Loaded" or "Click" or "MouseLeftButtonUp" or "TextChanged").ToList()) attr.Remove();
  listPanel.SetAttributeValue("Visibility","Visible");
  var searchPanel=(FrameworkElement)System.Windows.Markup.XamlReader.Parse(listPanel.ToString());
  var sample = new ValorantScanResult {DataVersion=1,Profile=new() {DataVersion=1,GameName="Tài khoản kiểm thử",AccountInfo=new() {Email="owner@example.com"}},OwnedSkins=[new() {DisplayName="Prime Vandal",WeaponName="Vandal"}]};
  ((ItemsControl)searchPanel.FindName("AccountsItemsControl")).ItemsSource = Enumerable.Range(0,12).Select(i=>new GameAccount {Username="sample"+i,ValorantData=sample,SkinSearchQuery="prime"}).ToList();
  ((TextBox)searchPanel.FindName("TxtSkinSearch")).Text="prime";
  ((TextBlock)searchPanel.FindName("TxtSkinSearchStatus")).Text="1 tài khoản có skin khớp trong bản quét đã lưu. 2 tài khoản chưa quét; kết quả có thể thiếu.";
  Render(searchPanel,1080,740,"skin-search.png");
  var dialog = new ValorantRankWindow(scan);
  var content=(FrameworkElement)dialog.Content; dialog.Content=null;
  Render(content,650,560,"rank.png");
  scan.Store.DataVersion=0; view.LoadScanResult(scan);
  if (((ItemsControl)view.FindName("ItemsDailyStore")).Items.Count != 0) throw new Exception("Legacy fake store not hidden");
  if (!((TextBlock)view.FindName("TxtStoreStatus")).Text.Contains("Bản quét cũ")) throw new Exception("Legacy warning missing");
  scan.Store.DataVersion=1;scan.Store.DailyExpiresAt=now.AddMinutes(-1);view.LoadScanResult(scan);
  if (((ItemsControl)view.FindName("ItemsDailyStore")).Items.Count != 0) throw new Exception("Expired store visible");
  Console.WriteLine("PASS: WPF views rendered; legacy/expired store hidden.");
  var menu = new ContextMenu();
  foreach (var title in new[]{"Xem kho / Quét Valorant","Sửa tài khoản","Sao chép tài khoản : mật khẩu","Sao chép tên đăng nhập","Sao chép mật khẩu"}) menu.Items.Add(new MenuItem {Header=title});
  menu.Width=290;menu.ApplyTemplate();menu.Measure(new Size(290,300));menu.Arrange(new Rect(0,0,290,menu.DesiredSize.Height));menu.UpdateLayout();
  var menuImage=new RenderTargetBitmap(290,(int)menu.ActualHeight,96,96,PixelFormats.Pbgra32);menuImage.Render(menu);var menuPng=new PngBitmapEncoder();menuPng.Frames.Add(BitmapFrame.Create(menuImage));using(var output=File.Create(Path.Combine(SourceRoot, "ValorantUiCheck/account-menu.png")))menuPng.Save(output);
  var hoverCard=new Border {Width=282,Height=180,CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),RenderTransformOrigin=new Point(.5,.5),Child=new TextBlock {Text="Tài khoản • Hover",Foreground=Brushes.White,Margin=new Thickness(20)}};
  AccountCardAnimation.Apply(hoverCard,true);
  Pump();
  if (((TranslateTransform)((TransformGroup)hoverCard.RenderTransform).Children[1]).Y > -4.9) throw new Exception("Hover lift failed");
  Render(hoverCard,310,210,"hover-card.png");
  AccountCardAnimation.Apply(hoverCard,false);Pump();
  if (Math.Abs(((TranslateTransform)((TransformGroup)hoverCard.RenderTransform).Children[1]).Y)>0.01) throw new Exception("Hover reset failed");
  Console.WriteLine("PASS: hover animation moves card and resets after pointer leaves.");
  var inspectorButton=(Button)view.FindName("BtnCatWeaponSkins");
  if (!CheckAccountMotion.GetScope(inspectorButton)) throw new Exception("Inspector button not in animation scope");
  inspectorButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent});Pump();
  var buttonScale=((TransformGroup)inspectorButton.RenderTransform).Children.OfType<ScaleTransform>().Last();
  if (SystemParameters.ClientAreaAnimation && buttonScale.ScaleX<1.04) throw new Exception("Button mouse enter animation not connected");
  inspectorButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent});Pump();
  if (Math.Abs(buttonScale.ScaleX-1)>0.01) throw new Exception("Button hover does not reset");
  foreach(var tabName in new[]{"TabRadioStore","TabRadioInventory","TabRadioInfo"}) ((RadioButton)view.FindName(tabName)).IsChecked=true;
  Pump();
  if (((FrameworkElement)view.FindName("PanelInfoView")).Visibility!=Visibility.Visible || ((FrameworkElement)view.FindName("PanelInfoView")).Opacity!=1) throw new Exception("Rapid tab transitions did not settle");
  var busy=typeof(ValorantModuleView).GetMethod("SetScanBusy",BindingFlags.Instance|BindingFlags.NonPublic)!;
  busy.Invoke(view,new object[]{true});Pump();
  if (((FrameworkElement)view.FindName("ScanBusyOverlay")).Visibility!=Visibility.Visible) throw new Exception("Missing busy overlay");
  Render(view,1080,820,"scan-busy.png");
  busy.Invoke(view,new object[]{false});
  if (((FrameworkElement)view.FindName("ScanBusyOverlay")).Visibility!=Visibility.Collapsed || ((RotateTransform)view.FindName("ScanSpinnerTransform")).HasAnimatedProperties) throw new Exception("Busy animation not stopped");
  Console.WriteLine("PASS: scoped mouse event animations, reset, rapid tabs, busy start/stop.");
  app.Shutdown();
 }
 static void Pump() {
  var frame=new System.Windows.Threading.DispatcherFrame();
  var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(260)};
  timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
 }
 static void Render(FrameworkElement element,int width,int height,string name) {
  var frame=new Border { Width=width, Height=height, Background=new SolidColorBrush(Color.FromRgb(16,18,23)), Child=element };frame.Measure(new Size(width,height));frame.Arrange(new Rect(0,0,width,height));frame.UpdateLayout();
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(SourceRoot, "ValorantUiCheck", name));png.Save(file);frame.Child=null;
 }
}




