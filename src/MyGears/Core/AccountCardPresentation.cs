using System.Text.Json.Serialization;
namespace MyGears.Core;
public partial class GameAccount
{
    [JsonIgnore] public string SkinSearchQuery { get; set; } = "";
    [JsonIgnore] public string CardMatchedSkins => string.Join(" • ", SkinSearch.Matches(ValorantData, SkinSearchQuery));
    [JsonIgnore] public string CardEmail => ValorantData?.Profile.AccountInfo?.EmailText ?? "Email: chưa quét";

    private string CardNumber(int? value) => ValorantData?.Profile.DataVersion >= 1 ? value?.ToString("N0") ?? "—" : "—";
    [JsonIgnore] public string CardName => !string.IsNullOrWhiteSpace(ValorantData?.Profile.GameName) ? ValorantData.Profile.GameName : !string.IsNullOrWhiteSpace(Label) ? Label : Username;
    [JsonIgnore] public string CardTag => ValorantData != null ? "#" + ValorantData.Profile.TagLine : Game + " • Chưa quét";
    [JsonIgnore] public string CardLevel => ValorantData != null ? $"LV. {CardNumber(ValorantData.Profile.AccountLevel)}" : "LV. —";
    [JsonIgnore] public string CardRank => ValorantData?.Profile.RankData?.Current?.RankName ?? "CHƯA CÓ DỮ LIỆU";
    [JsonIgnore] public string CardRankIcon => ValorantData?.Profile.RankData?.Current?.Icon ?? "";
    [JsonIgnore] public string CardVp => ValorantData != null ? CardNumber(ValorantData.Profile.ValorantPoints) : "—";
    [JsonIgnore] public string CardRp => ValorantData != null ? CardNumber(ValorantData.Profile.RadianitePoints) : "—";
    [JsonIgnore] public string CardSkins => ValorantData?.CountText("Skins", ValorantData.TotalSkinsCount) ?? "—";
    [JsonIgnore] public string CardPremium => "◇ " + (ValorantData?.CountText("Skins", ValorantData.CountPremium) ?? "—");
    [JsonIgnore] public string CardOther => "◈ " + (ValorantData?.CountText("Skins", ValorantData.CountSelect + ValorantData.CountExclusive + ValorantData.CountUltra) ?? "—");
    [JsonIgnore] public string CardCountry => string.IsNullOrWhiteSpace(ValorantData?.Profile.AccountInfo?.Country) ? "—" : ValorantData.Profile.AccountInfo.Country;
    [JsonIgnore] public string CardUpdated => ValorantData?.ScanTime.ToString("HH:mm dd/MM/yyyy") ?? "Chưa quét";
}
