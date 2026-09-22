using System.Reflection;
using System.Text.Json;
using MyGears.Core;

var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prime-skin-metadata.json"));
using var doc = JsonDocument.Parse(json);
var prime = doc.RootElement;
var expected = prime.GetProperty("chromas")[0].GetProperty("fullRender").GetString();
var service = typeof(ValorantApiService);
var flags = BindingFlags.NonPublic | BindingFlags.Static;
var parsed = service.GetMethod("ParseAndLoadSkinsJson", flags)!.Invoke(null, new object[] { "{\"data\":[" + json + "]}" });
if (!Equals(parsed, true)) throw new Exception("Metadata parse failed");
service.GetField("_metadataLoaded", flags)!.SetValue(null, true);
foreach (var level in prime.GetProperty("levels").EnumerateArray())
{
    var skin = ValorantApiService.GetSkinByUuidOrLevel(level.GetProperty("uuid").GetString()!);
    if (skin.DisplayIcon != expected) throw new Exception("Upgrade preview used instead of full weapon");
}
var saved = new ValorantSkinItem {
    Uuid = prime.GetProperty("uuid").GetString()!,
    LevelUuid = prime.GetProperty("levels")[1].GetProperty("uuid").GetString()!,
    DisplayIcon = prime.GetProperty("levels")[1].GetProperty("displayIcon").GetString()!
};
var variant = new ValorantSkinItem { Uuid = prime.GetProperty("chromas")[1].GetProperty("uuid").GetString()! };
var unknown = new ValorantSkinItem { Uuid = "unknown", DisplayIcon = "keep-existing" };
var scan = new ValorantScanResult {
    OwnedSkins = [saved, unknown],
    Store = new ValorantStoreData { DailyOffers = [new() { Skin = variant }], NightMarketOffers = [new() { Skin = saved }] }
};
await ValorantApiService.RefreshScanSkinImagesAsync(scan);
if (saved.DisplayIcon != expected || variant.DisplayIcon != expected) throw new Exception("Saved scan image was not repaired");
if (unknown.DisplayIcon != "keep-existing" || scan.OwnedSkins.Count != 2) throw new Exception("Inventory changed unexpectedly");
Console.WriteLine("PASS: all four upgrade levels, saved scan, chroma/store images, unknown skin preservation.");

void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
var now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");
var seasonsJson = """
{"data":[
 {"uuid":"old","type":"EAresSeasonType::Act","title":"ACT IV","startTime":"2026-06-24T00:00:00Z","endTime":"2026-08-19T00:00:00Z"},
 {"uuid":"current","type":"EAresSeasonType::Act","title":"ACT V","startTime":"2026-08-19T00:00:00Z","endTime":"2026-10-14T00:00:00Z"}]}
""";
var mmrJson = """
{"QueueSkills":{"competitive":{"SeasonalInfoBySeasonID":{
 "current":{"CompetitiveTier":13,"RankedRating":14,"NumberOfGames":7,"NumberOfWinsWithPlacements":5,"NumberOfWins":3,"WinsByTier":{"14":1}},
 "old":{"CompetitiveTier":9,"RankedRating":75,"NumberOfGames":40,"NumberOfWinsWithPlacements":23}
}}}}
""";
var rank = ValorantApiService.ParseRankData(mmrJson, seasonsJson, now);
Check(rank.Current?.Tier == 13 && rank.Current.Rr == 14 && rank.Current.Title == "ACT V", "Current Act depends on JSON order");
Check(rank.Seasons[0].SeasonId == "current" && rank.PeakTier == 14 && rank.Wins == 28 && rank.Games == 47, "History and peak stats");
var noCurrent = ValorantApiService.ParseRankData("{\"QueueSkills\":{}}", seasonsJson, now);
Check(noCurrent.Current?.Tier == 0 && noCurrent.Current.Title == "ACT V", "Unplayed current season");
var noMetadata = ValorantApiService.ParseRankData(mmrJson, "{\"data\":[]}", now);
Check(noMetadata.Current == null && noMetadata.Error.Length > 0 && noMetadata.Seasons.Count == 2, "Unknown season must not masquerade as current");
var skinId = prime.GetProperty("levels")[0].GetProperty("uuid").GetString();
var storeJson = $$$$"""
{
 "SkinsPanelLayout":{"SingleItemOffers":["offer","unknown-price"],"SingleItemOffersRemainingDurationInSeconds":3600,
 "SingleItemStoreOffers":[{"OfferID":"offer","Cost":{"85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741":3550},"Rewards":[{"ItemID":"{{{{skinId}}}}"}]}]},
 "BonusStore":{"BonusStoreRemainingDurationInSeconds":7200,"BonusStoreOffers":[{"Offer":{"OfferID":"bonus","Rewards":[{"ItemID":"{{{{skinId}}}}"}],"Cost":{"85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741":1775}},"DiscountCosts":{"85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741":0},"DiscountPercent":100}]},
 "AccessoryStore":{"AccessoryStoreRemainingDurationInSeconds":8000,"AccessoryStoreOffers":[{"Offer":{"Rewards":[{"ItemID":"unknown-card"}],"Cost":{"85ca954a-41f2-ce94-9b45-8ca3dd39a00d":4500}}}]},
 "FeaturedBundle":{"Bundles":[{"DataAssetID":"bundle","TotalDiscountedCost":{"85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741":5460},"DurationRemainingInSeconds":1800}]}
}
""";
var shop = ValorantApiService.ParseStoreData(storeJson, now);
Check(shop.DailyOffers.Count == 2 && shop.DailyOffers[0].FinalCost == 3550 && shop.DailyOffers[0].Skin.DisplayName == "Prime Vandal", "Real price/reward mapping");
Check(!shop.DailyOffers[1].HasPrice && shop.DailyOffers[1].PriceText == "Chưa có giá", "No invented prices");
Check(shop.NightMarketOffers[0].FinalCost == 0 && shop.NightMarketOffers[0].HasPrice, "Zero-price discount");
Check(shop.Accessories[0].Price == 4500 && shop.Accessories[0].Currency == "KC" && shop.Bundles[0].Price == 5460, "Extras/currencies");
Check(shop.DailyExpiresAt == now.AddHours(1), "Absolute reset time");
var roundtrip = JsonSerializer.Deserialize<ValorantStoreData>(JsonSerializer.Serialize(shop))!;
Check(roundtrip.DailyExpiresAt == shop.DailyExpiresAt && roundtrip.DataVersion == 1, "Saved expiration");
var empty = ValorantApiService.ParseStoreData("{\"SkinsPanelLayout\":{}}", now);
Check(empty.DailyOffers.Count == 0 && empty.StatusMessage.Length > 0, "No fake fallback skins");
foreach (int status in new[] {401, 403, 429, 500}) {
 var message = (string)service.GetMethod("ApiError", flags)!.Invoke(null, new object[] {"Cửa hàng", status})!;
 Check(message.Length > 0 && (status > 403 || message.Contains("token")), "HTTP error message");
}
Console.WriteLine("PASS: rank chronology, peak, placements, unranked, missing metadata; real store prices, discounts, KC, bundles, expiration, serialization, empty store, HTTP errors.");


using (var request = (System.Net.Http.HttpRequestMessage)service.GetMethod("StorefrontRequest", flags)!.Invoke(null, new object[] { "test-player", "test-access", "test-entitlement", "ap" })!)
{
 Check(request.Method == System.Net.Http.HttpMethod.Post && request.RequestUri!.AbsolutePath == "/store/v3/storefront/test-player", "Storefront v3 POST");
 Check(await request.Content!.ReadAsStringAsync() == "{}" && request.Content.Headers.ContentType!.MediaType == "application/json", "Storefront JSON body");
 Check(request.Headers.Authorization?.Parameter == "test-access" && request.Headers.Contains("X-Riot-Entitlements-JWT") && request.Headers.Contains("X-Riot-ClientVersion") && request.Headers.Contains("X-Riot-ClientPlatform"), "Storefront required headers");
}
Console.WriteLine("PASS: v3 POST request, JSON body, authentication headers, actual VP currency ID.");
var identity = ValorantApiService.ParseAccountIdentity("""
{"sub":"test-player","country":"vnm","email":"owner@example.com","email_verified":true,"phone_number_verified":false,"acct":{"username":"test-login","game_name":"Test","tag_line":"VN1","created_at":1688086124000},"ban":{"restrictions":[{"type":"temporary"}]}}
""");
Check(identity.AccountInfo!.EmailText == "owner@example.com" && identity.AccountInfo.Username == "test-login", "Email and username");
Check(identity.AccountInfo.Country == "VNM" && identity.AccountInfo.CreatedAt!.Value.Year == 2023 && identity.AccountInfo.RestrictionCount == 1, "Country creation restrictions");
var missingInfo = ValorantApiService.ParseAccountIdentity("{\"sub\":\"test-player\",\"acct\":{}}");
Check(missingInfo.AccountInfo!.RestrictionCount == null && !missingInfo.AccountInfo.EmailText.Contains('@') && missingInfo.AccountInfo.CreatedAt == null, "Missing fields must remain unknown");
var maskedInfo = ValorantApiService.ParseAccountIdentity("{\"sub\":\"test-player\",\"email\":\"a***@example.com\",\"ban\":{\"restrictions\":[]}}");
Check(maskedInfo.AccountInfo!.EmailText == "a***@example.com" && maskedInfo.AccountInfo.RestrictionCount == 0, "Preserve masked email and empty restrictions");
var savedInfo = JsonSerializer.Deserialize<ValorantAccountProfile>(JsonSerializer.Serialize(identity))!;
Check(savedInfo.AccountInfo!.Email == "owner@example.com", "Email survives encrypted-vault payload serialization");
Console.WriteLine("PASS: identity/email fields, masked or missing data, creation date, restrictions, persistence.");
File.WriteAllText(Path.Combine(sourceRoot, "SkinImageRegression", "prefill-script.js"), RiotLoginPrefill.BuildScript("test-user", "test-password"));

var walletZero = ValorantApiService.ParseWallet("""{"Balances":{"85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741":0,"e59aa87c-4cbf-517a-5983-6e81511be9b7":145,"85ca954a-41f2-ce94-9b45-8ca3dd39a00d":12}}""");
Check(walletZero.Vp == 0 && walletZero.Rp == 145 && walletZero.Error == "", "Zero is a real balance");
var walletMissing = ValorantApiService.ParseWallet("{}");
Check(walletMissing.Vp == null && walletMissing.Error.Length > 0, "Missing balance is unknown");
var legacyScan = JsonSerializer.Deserialize<ValorantScanResult>("""{"Profile":{"ValorantPoints":0,"AccountLevel":1}}""")!;
Check(legacyScan.Profile.NumberText(0) == "Cần quét lại" && !legacyScan.InventoryKnown("Skins"), "Legacy values need rescan");
var freshScan = new ValorantScanResult { DataVersion = 1, Profile = new() { DataVersion = 1, ValorantPoints = 0 }, ScanTime = DateTime.Now.AddDays(-2) };
Check(freshScan.Profile.NumberText(0) == "0" && freshScan.Profile.NumberText(null).Contains("Chưa") && freshScan.DataStatusText.Contains("24 giờ"), "Zero, missing, stale distinct");
freshScan.InventoryErrors["Skins"] = "HTTP 403";
Check(freshScan.CountText("Skins", 0) == "—" && freshScan.CountText("Cards", 0) == "0", "Failed inventory versus empty inventory");
var savedScan = JsonSerializer.Deserialize<ValorantScanResult>(JsonSerializer.Serialize(freshScan))!;
Check(savedScan.Profile.RadianitePoints == null && !savedScan.InventoryKnown("Skins"), "Unknown survives persistence");
Check(identity.IsBanned == null, "A restriction is not proof of a ban");
var updateTestDir = Path.Combine(Path.Combine(sourceRoot, "SkinImageRegression"), "update-fixtures");
Directory.CreateDirectory(updateTestDir);
var updateSource = Path.Combine(updateTestDir, "source.bin");
var updateTarget = Path.Combine(updateTestDir, "target.bin");
File.WriteAllText(updateSource, "new!"); File.WriteAllText(updateTarget, "old!");
File.SetLastWriteTimeUtc(updateTarget, DateTime.UtcNow.AddDays(1));
Check(!VerifiedAppUpdate.SameContent(updateSource, updateTarget), "Same size/newer timestamp can still differ");
using (var locked = new FileStream(updateTarget, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    bool failed = false;
    try { VerifiedAppUpdate.Install(updateSource, updateTarget); } catch (IOException) { failed = true; }
    Check(failed && File.ReadAllText(updateTarget) == "old!", "Locked target must fail and preserve old file");
}
VerifiedAppUpdate.Install(updateSource, updateTarget);
Check(VerifiedAppUpdate.SameContent(updateSource, updateTarget), "Replacement verified");
VerifiedAppUpdate.Install(updateTarget, updateTarget);
Check(Directory.GetFiles(updateTestDir, "*.tmp").Length == 0, "Staging cleaned on success and failure");
Console.WriteLine("PASS: update integrity, locked target, same-size changes; unknown/zero/legacy/stale/partial data and serialization.");

File.WriteAllText(updateTarget, "local accounts");
VerifiedAppUpdate.CopyInitialData(updateSource, updateTarget);
Check(File.ReadAllText(updateTarget) == "local accounts", "Updates must preserve existing user data");
Console.WriteLine("PASS: existing user data preserved during update.");

var searchScan = new ValorantScanResult { OwnedSkins = [new() {DisplayName="Prime Vandal",WeaponName="Vandal"}, new() {DisplayName="Prime Vandal",WeaponName="Vandal"}, new() {DisplayName="Dao Ánh Sáng",WeaponName="Melee"}], Store = new() {DailyOffers=[new() {Skin=new() {DisplayName="Reaver Vandal"}}]} };
Check(SkinSearch.Matches(searchScan, " VANDAL   prime ").Count == 1, "Case/order/whitespace and duplicates");
Check(SkinSearch.Matches(searchScan, "dao anh sang").Count == 1, "Vietnamese accent insensitive search");
Check(SkinSearch.Matches(searchScan, "Reaver").Count == 0, "Store offers are not owned skins");
Check(SkinSearch.Matches(null, "Prime").Count == 0 && SkinSearch.Matches(searchScan, "  ").Count == 0, "Unscanned and blank query");
searchScan.Profile = new() {Puuid="test-player",AccountInfo=new() {Email="old@example.com",Country="VNM"}};
var originalScanTime=searchScan.ScanTime;
Check(!ValorantApiService.UpdateEmailForAccount(searchScan,new() {Puuid="other",AccountInfo=new() {Email="wrong@example.com"}}), "Wrong account email rejected");
Check(searchScan.Profile.AccountInfo.Email == "old@example.com", "Wrong account leaves email unchanged");
Check(ValorantApiService.UpdateEmailForAccount(searchScan,identity), "Matching account refresh");
Check(searchScan.Profile.AccountInfo.Email == "owner@example.com" && searchScan.Profile.AccountInfo.Country == "VNM" && searchScan.ScanTime == originalScanTime && searchScan.OwnedSkins.Count == 3, "Email refresh preserves inventory and scan date");
Console.WriteLine("PASS: global skin search; email identity guard and snapshot preservation.");

Check(Uri.UnescapeDataString(new Uri(ValorantApiService.RiotAuthUrl).Query).Contains("account email"), "Email scope requested during authorization");
