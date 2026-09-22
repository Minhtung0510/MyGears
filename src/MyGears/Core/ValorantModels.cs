using System.Text.Json.Serialization;

namespace MyGears.Core;

/// <summary>
/// Hồ sơ tài khoản Valorant đầy đủ
/// </summary>
public class ValorantAccountProfile
{
    public string WalletError { get; set; } = "";
    public int DataVersion { get; set; }
    public string NumberText(int? value) => DataVersion < 1 ? "Cần quét lại" : value?.ToString("N0") ?? "Chưa có dữ liệu";
    public ValorantIdentity? AccountInfo { get; set; }
    public string RiotId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public string Puuid { get; set; } = string.Empty;
    public string Region { get; set; } = "AP";
    public int? ValorantPoints { get; set; }
    public int? RadianitePoints { get; set; }
    public int? KingdomCredits { get; set; }
    public int? AccountLevel { get; set; }
    public string RankName { get; set; } = "UNRANKED";
    public string FullRankTitle { get; set; } = "Chưa có dữ liệu rank";
    public ValorantRankData? RankData { get; set; }
    public string RankIcon { get; set; } = "https://media.valorant-api.com/competitivetiers/5641173d-4437-3a4b-d246-dba494f43409/0/largeicon.png";
    public int RankTier { get; set; } = 0;
    public int RankedRating { get; set; } = 0;
    public bool? IsBanned { get; set; }
    public string BanStatus { get; set; } = "Chưa có dữ liệu trạng thái cấm";
    public string SecurityStatus { get; set; } = "ĐÃ XÁC THỰC OAUTH2";
    public string CardIcon { get; set; } = "https://media.valorant-api.com/playercards/9fb348bc-4148-b5a1-f2c1-879e6f642e12/displayicon.png";
    public long EstimatedVndValue { get; set; } = 0;
}

/// <summary>
/// Thông tin một skin súng Valorant
/// </summary>
public class ValorantSkinItem
{
    public string Uuid { get; set; } = string.Empty;
    public string LevelUuid { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WeaponName { get; set; } = string.Empty;
    public string DisplayIcon { get; set; } = string.Empty;
    public string TierName { get; set; } = "Select";
    public string TierColor { get; set; } = "#5A9FE2";
    public string TierIcon { get; set; } = string.Empty;
    public int Cost { get; set; } = 0;
}

/// <summary>
/// Một vật phẩm trong Cửa Hàng Hàng Ngày hoặc Chợ Đêm
/// </summary>
public class ValorantStoreOffer
{
    public ValorantSkinItem Skin { get; set; } = new();
    public int OriginalCost { get; set; }
    public int FinalCost { get; set; }
    public int DiscountPercent { get; set; }
    public bool IsNightMarket { get; set; }
    public bool HasPrice { get; set; } = true;
    public string PriceText => HasPrice ? $"{FinalCost:N0} VP" : "Chưa có giá";
}

/// <summary>
/// Toàn bộ dữ liệu cửa hàng (Daily Store + Night Market)
/// </summary>
public class ValorantStoreData
{
    public int DataVersion { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
    public DateTimeOffset? DailyExpiresAt { get; set; }
    public DateTimeOffset? NightMarketExpiresAt { get; set; }
    public DateTimeOffset? AccessoryExpiresAt { get; set; }
    public string StatusMessage { get; set; } = "";
    public List<ValorantShopExtra> Bundles { get; set; } = [];
    public List<ValorantShopExtra> Accessories { get; set; } = [];
    public List<ValorantStoreOffer> DailyOffers { get; set; } = [];
    public List<ValorantStoreOffer> NightMarketOffers { get; set; } = [];
    public int DailyRemainingSeconds { get; set; }
    public int NightMarketRemainingSeconds { get; set; }
    public bool HasNightMarket => NightMarketOffers.Count > 0;
}

/// <summary>
/// Đại diện cho một vật phẩm trong kho (Buddy, Card, Spray, Agent...)
/// </summary>
public class ValorantItem
{
    public string Uuid { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string DisplayIcon { get; set; } = string.Empty;
    public string BorderColor { get; set; } = "#38BDF8";
    public string TagText { get; set; } = string.Empty;
    public int Cost { get; set; } = 0;
    public string CostDisplay => Cost > 0 ? $"~{Cost:N0} VP" : string.Empty;
}

/// <summary>
/// Kết quả tổng hợp một lần quét tài khoản Valorant
/// </summary>
public class ValorantScanResult
{
    public int DataVersion { get; set; }
    public Dictionary<string, string> InventoryErrors { get; set; } = new();
    public bool InventoryKnown(string category) => DataVersion >= 1 && !InventoryErrors.ContainsKey(category);
    public string CountText(string category, int count) => InventoryKnown(category) ? count.ToString() : "—";
    public string DataStatusText
    {
        get
        {
            if (DataVersion < 1) return "Bản quét cũ — hãy quét lại để xác minh số dư và kho đồ.";
            var issues = new List<string>();
            if (Profile.ValorantPoints == null || Profile.RadianitePoints == null || Profile.KingdomCredits == null) issues.Add(string.IsNullOrEmpty(Profile.WalletError) ? "Số dư chưa đầy đủ" : Profile.WalletError);
            if (Profile.AccountLevel == null) issues.Add("Chưa tải được cấp độ");
            foreach (var item in InventoryErrors) issues.Add($"{item.Key}: {item.Value}");
            if (!string.IsNullOrEmpty(Profile.RankData?.Error)) issues.Add(Profile.RankData.Error);
            var age = DateTime.Now - ScanTime;
            var prefix = age.TotalHours >= 24 ? "Bản quét đã quá 24 giờ — nên quét lại." : "Dữ liệu tại thời điểm quét; chưa tự cập nhật.";
            return prefix + (issues.Count > 0 ? " " + string.Join(" • ", issues) : "");
        }
    }

    public ValorantAccountProfile Profile { get; set; } = new();
    public ValorantStoreData Store { get; set; } = new();
    public List<ValorantSkinItem> OwnedSkins { get; set; } = [];
    public List<ValorantItem> OwnedBuddies { get; set; } = [];
    public List<ValorantItem> OwnedCards { get; set; } = [];
    public List<ValorantItem> OwnedSprays { get; set; } = [];
    public List<ValorantItem> OwnedAgents { get; set; } = [];
    public DateTime ScanTime { get; set; } = DateTime.Now;

    // Phân loại phẩm chất Skin
    public int CountUltra { get; set; } = 0;
    public int CountExclusive { get; set; } = 0;
    public int CountPremium { get; set; } = 0;
    public int CountSelect { get; set; } = 0;

    // Đếm các danh mục phụ kiện kho đồ
    public int CountWeaponSkins => OwnedSkins.Count;
    
    private int _countBuddies;
    public int CountBuddies
    {
        get => OwnedBuddies.Count > 0 ? OwnedBuddies.Count : _countBuddies;
        set => _countBuddies = value;
    }

    private int _countCards;
    public int CountCards
    {
        get => OwnedCards.Count > 0 ? OwnedCards.Count : _countCards;
        set => _countCards = value;
    }

    private int _countSprays;
    public int CountSprays
    {
        get => OwnedSprays.Count > 0 ? OwnedSprays.Count : _countSprays;
        set => _countSprays = value;
    }

    private int _countAgents;
    public int CountAgents
    {
        get => OwnedAgents.Count > 0 ? OwnedAgents.Count : _countAgents;
        set => _countAgents = value;
    }

    public int TotalSkinsCount => OwnedSkins.Count;
    public int TotalEstimatedVpValue => OwnedSkins.Sum(s => s.Cost);
}
