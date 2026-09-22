using System.Text.Json;

namespace MyGears.Core;

public class ValorantRankSeason
{
    public string SeasonId { get; set; } = "";
    public string Title { get; set; } = "Act chưa xác định";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public int Tier { get; set; }
    public int PeakTier { get; set; }
    public int Rr { get; set; }
    public int Wins { get; set; }
    public int Games { get; set; }
    public int Losses => Math.Max(0, Games - Wins);
    public double WinRate => Games > 0 ? Math.Round(100.0 * Wins / Games) : 0;
    public string RankName => ValorantApiService.GetRankName(Tier);
    public string Icon => ValorantApiService.GetRankIconUrl(Tier);
    public string Detail => $"{RankName} • {Rr} RR";
    public string Record => $"{Wins} W / {Losses} L • {Games} trận • {WinRate}%";
}

public class ValorantRankData
{
    public List<ValorantRankSeason> Seasons { get; set; } = [];
    public ValorantRankSeason? Current { get; set; }
    public string Error { get; set; } = "";
    public int PeakTier => Seasons.Select(s => s.PeakTier).DefaultIfEmpty(0).Max();
    public string PeakTitle => Seasons.OrderByDescending(s => s.PeakTier).ThenByDescending(s => s.Start).FirstOrDefault()?.Title ?? "—";
    public int Wins => Seasons.Sum(s => s.Wins);
    public int Games => Seasons.Sum(s => s.Games);
    public string Summary => $"Cao nhất: {ValorantApiService.GetRankName(PeakTier)} ({PeakTitle})\n{Wins} thắng / {Math.Max(0, Games - Wins)} thua • {Games} trận • {(Games > 0 ? Math.Round(100.0 * Wins / Games) : 0)}% thắng";
}

public class ValorantShopExtra
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Category { get; set; } = "";
    public int? Price { get; set; }
    public string Currency { get; set; } = "VP";
    public DateTimeOffset? ExpiresAt { get; set; }
    public string PriceText => Price.HasValue ? $"{Price:N0} {Currency}" : "Chưa có giá";
    public string ExpiryText => ExpiresAt.HasValue ? $"Hết hạn: {ExpiresAt.Value.ToLocalTime():dd/MM HH:mm}" : "";
}

public static partial class ValorantApiService
{
    private static JsonElement Prop(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : default;
    private static string Str(JsonElement e, string name) => Prop(e, name).ValueKind == JsonValueKind.String ? Prop(e, name).GetString()! : "";
    private static int Int(JsonElement e, string name) => Prop(e, name).TryInt();
    private static int TryInt(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;
    private static IEnumerable<JsonElement> Rows(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static DateTimeOffset Date(JsonElement e, string name) => DateTimeOffset.TryParse(Str(e, name), out var date) ? date : default;

    public static ValorantRankData ParseRankData(string mmrJson, string seasonsJson, DateTimeOffset now)
    {
        using var mmr = JsonDocument.Parse(mmrJson);
        using var metadata = JsonDocument.Parse(seasonsJson);
        var acts = Rows(Prop(metadata.RootElement, "data")).Where(s => Str(s, "type").EndsWith("::Act"))
            .ToDictionary(s => Str(s, "uuid"), StringComparer.OrdinalIgnoreCase);
        var currentAct = acts.Values.Where(s => Date(s, "startTime") <= now && Date(s, "endTime") > now)
            .OrderByDescending(s => Date(s, "startTime")).FirstOrDefault();
        var root = mmr.RootElement;
        var comp = Prop(Prop(root, "QueueSkills"), "competitive");
        var seasons = Prop(comp, "SeasonalInfoBySeasonID");
        var result = new ValorantRankData();
        if (Prop(root, "QueueSkills").ValueKind != JsonValueKind.Object)
            throw new JsonException("Thiếu dữ liệu xếp hạng.");
        if (seasons.ValueKind == JsonValueKind.Object)
        foreach (var entry in seasons.EnumerateObject())
        {
            var value = entry.Value;
            acts.TryGetValue(entry.Name, out var act);
            int tier = Int(value, "CompetitiveTier");
            int peak = tier;
            var byTier = Prop(value, "WinsByTier");
            if (byTier.ValueKind == JsonValueKind.Object)
                foreach (var win in byTier.EnumerateObject())
                    if (win.Value.TryInt() > 0 && int.TryParse(win.Name, out var wonTier)) peak = Math.Max(peak, wonTier);
            var row = new ValorantRankSeason {
                SeasonId = entry.Name, Title = Str(act, "title"), Start = Date(act, "startTime"), End = Date(act, "endTime"),
                Tier = tier, PeakTier = peak, Rr = Int(value, "RankedRating"), Games = Int(value, "NumberOfGames"),
                Wins = Prop(value, "NumberOfWinsWithPlacements").ValueKind == JsonValueKind.Number ? Int(value, "NumberOfWinsWithPlacements") : Int(value, "NumberOfWins")
            };
            if (string.IsNullOrEmpty(row.Title)) row.Title = "Act " + entry.Name;
            row.Wins = Math.Clamp(row.Wins, 0, Math.Max(0, row.Games));
            if (row.Games > 0 || tier > 0) result.Seasons.Add(row);
            if (entry.Name.Equals(Str(currentAct, "uuid"), StringComparison.OrdinalIgnoreCase)) result.Current = row;
        }
        if (currentAct.ValueKind == JsonValueKind.Object)
            result.Current ??= new ValorantRankSeason { SeasonId = Str(currentAct, "uuid"), Title = Str(currentAct, "title"), Start = Date(currentAct, "startTime"), End = Date(currentAct, "endTime") };
        else
            result.Error = "Chưa xác định được Act hiện tại. Lịch sử bên dưới là dữ liệu đã nhận.";
        result.Seasons = result.Seasons.OrderByDescending(s => s.Start).ThenBy(s => s.SeasonId).ToList();
        return result;
    }

    private static string ApiError(string part, int status) => status is 401 or 403
        ? $"{part}: phiên Riot hết hạn hoặc không được cấp quyền. Hãy lấy token mới và quét lại."
        : $"Không tải được {part} (HTTP {status}). Hãy thử quét lại.";

    private static System.Net.Http.HttpRequestMessage PlayerRequest(string url, string access, string entitlement)
    {
        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        req.Headers.Add("X-Riot-Entitlements-JWT", entitlement);
        req.Headers.Add("X-Riot-ClientPlatform", ClientPlatformBase64);
        req.Headers.Add("X-Riot-ClientVersion", _clientVersion);
        return req;
    }

    public static async Task<ValorantRankData> GetRankDataAsync(string puuid, string access, string entitlement, string region)
    {
        try {
            using var req = PlayerRequest($"https://pd.{region}.a.pvp.net/mmr/v1/players/{puuid}", access, entitlement);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return new() { Error = ApiError("Rank", (int)response.StatusCode) };
            string seasons;
            try { seasons = await _httpClient.GetStringAsync("https://valorant-api.com/v1/seasons"); }
            catch { seasons = "{\"data\":[]}"; }
            return ParseRankData(await response.Content.ReadAsStringAsync(), seasons, DateTimeOffset.UtcNow);
        }
        catch { return new() { Error = "Không tải được rank. Kiểm tra kết nối và quét lại." }; }
    }

    private static System.Net.Http.HttpRequestMessage StorefrontRequest(string puuid, string access, string entitlement, string region)
    {
        var request = PlayerRequest($"https://pd.{region}.a.pvp.net/store/v3/storefront/{puuid}", access, entitlement);
        request.Method = System.Net.Http.HttpMethod.Post;
        request.Content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private const string VpCurrency = "85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741";
    private const string KcCurrency = "85ca954a-41f2-ce94-9b45-8ca3dd39a00d";
    private static int? Cost(JsonElement costs, string currency) => Prop(costs, currency).ValueKind == JsonValueKind.Number ? Math.Max(0, Int(costs, currency)) : null;
    private static string RewardId(JsonElement offer) => Rows(Prop(offer, "Rewards")).Select(r => Str(r, "ItemID")).FirstOrDefault() ?? Str(offer, "OfferID");
    private static DateTimeOffset? Expiry(DateTimeOffset now, int seconds) => seconds > 0 ? now.AddSeconds(seconds) : null;

    public static ValorantStoreData ParseStoreData(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new ValorantStoreData { DataVersion = 1, FetchedAt = now };
        var panel = Prop(root, "SkinsPanelLayout");
        if (panel.ValueKind != JsonValueKind.Object) throw new JsonException("Thiếu dữ liệu cửa hàng.");
        result.DailyRemainingSeconds = Int(panel, "SingleItemOffersRemainingDurationInSeconds");
        result.DailyExpiresAt = Expiry(now, result.DailyRemainingSeconds);
        var detailed = Rows(Prop(panel, "SingleItemStoreOffers")).Where(o => Str(o, "OfferID") != "")
            .GroupBy(o => Str(o, "OfferID")).ToDictionary(g => g.Key, g => g.First());
        var ids = Rows(Prop(panel, "SingleItemOffers")).Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        if (ids.Count == 0) ids.AddRange(detailed.Keys);
        foreach (var id in ids.Distinct()) {
            detailed.TryGetValue(id, out var offer);
            var skin = GetSkinByUuidOrLevel(offer.ValueKind == JsonValueKind.Object ? RewardId(offer) : id);
            var price = Cost(Prop(offer, "Cost"), VpCurrency);
            result.DailyOffers.Add(new() { Skin = skin, OriginalCost = price ?? 0, FinalCost = price ?? 0, HasPrice = price.HasValue });
        }
        var bonus = Prop(root, "BonusStore");
        result.NightMarketRemainingSeconds = Int(bonus, "BonusStoreRemainingDurationInSeconds");
        result.NightMarketExpiresAt = Expiry(now, result.NightMarketRemainingSeconds);
        foreach (var row in Rows(Prop(bonus, "BonusStoreOffers"))) {
            var offer = Prop(row, "Offer");
            var price = Cost(Prop(row, "DiscountCosts"), VpCurrency);
            result.NightMarketOffers.Add(new() { Skin = GetSkinByUuidOrLevel(RewardId(offer)), OriginalCost = Cost(Prop(offer, "Cost"), VpCurrency) ?? 0,
                FinalCost = price ?? 0, HasPrice = price.HasValue, DiscountPercent = Int(row, "DiscountPercent"), IsNightMarket = true });
        }
        var accessories = Prop(root, "AccessoryStore");
        result.AccessoryExpiresAt = Expiry(now, Int(accessories, "AccessoryStoreRemainingDurationInSeconds"));
        foreach (var row in Rows(Prop(accessories, "AccessoryStoreOffers"))) {
            var offer = Prop(row, "Offer"); var id = RewardId(offer);
            ValorantItem? item = null;
            if (_cardsByUuid.TryGetValue(id, out var card)) item = card;
            else if (_spraysByUuid.TryGetValue(id, out var spray)) item = spray;
            else if (_buddiesByLevelUuid.TryGetValue(id, out var buddy)) item = buddy;
            else if (_buddiesByUuid.TryGetValue(id, out buddy)) item = buddy;
            result.Accessories.Add(new() { Name = item?.DisplayName ?? $"Vật phẩm ({id})", Icon = item?.DisplayIcon ?? "", Category = item?.CategoryName ?? "Phụ kiện",
                Currency = "KC", Price = Cost(Prop(offer, "Cost"), KcCurrency), ExpiresAt = result.AccessoryExpiresAt });
        }
        var featured = Prop(root, "FeaturedBundle");
        var bundles = Rows(Prop(featured, "Bundles")).ToList();
        if (bundles.Count == 0 && Prop(featured, "Bundle").ValueKind == JsonValueKind.Object) bundles.Add(Prop(featured, "Bundle"));
        foreach (var bundle in bundles) {
            var id = Str(bundle, "DataAssetID");
            result.Bundles.Add(new() { Name = id, Category = "Bộ sưu tập", Price = Cost(Prop(bundle, "TotalDiscountedCost"), VpCurrency),
                ExpiresAt = Expiry(now, Int(bundle, "DurationRemainingInSeconds") > 0 ? Int(bundle, "DurationRemainingInSeconds") : Int(featured, "BundleRemainingDurationInSeconds")) });
        }
        if (result.DailyOffers.Count == 0) result.StatusMessage = "Riot chưa trả về skin hằng ngày. Hãy thử quét lại.";
        return result;
    }
}
