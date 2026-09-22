using System.Text.Json;
namespace MyGears.Core;

public class ValorantIdentity
{
    public string Username { get; set; } = "";
    public DateTimeOffset? FetchedAt { get; set; }
    public string Email { get; set; } = "";
    public bool? EmailVerified { get; set; }
    public string Phone { get; set; } = "";
    public bool? PhoneVerified { get; set; }
    public string Country { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public int? RestrictionCount { get; set; }
    public string EmailText => !string.IsNullOrWhiteSpace(Email) ? Email : "Riot không cung cấp địa chỉ email";
    public string EmailStatus => EmailVerified switch { true => "Đã xác minh", false => "Chưa xác minh", _ => "Chưa có trạng thái xác minh" };
    public string PhoneText => !string.IsNullOrWhiteSpace(Phone) ? Phone : PhoneVerified == true ? "Đã xác minh • Riot không cung cấp số" : "Chưa có dữ liệu số điện thoại";
    public string CreatedText => CreatedAt.HasValue ? CreatedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "Riot không cung cấp ngày tạo";
    public string BanText => RestrictionCount.HasValue ? RestrictionCount > 0 ? $"Riot trả về {RestrictionCount} hạn chế" : "Không có hạn chế trong dữ liệu Riot" : "Chưa có dữ liệu trạng thái cấm";
}

public static partial class ValorantApiService
{
    public static bool UpdateEmailForAccount(ValorantScanResult scan, ValorantAccountProfile incoming)
    {
        if (string.IsNullOrWhiteSpace(scan.Profile.Puuid) || !string.Equals(scan.Profile.Puuid, incoming.Puuid, StringComparison.OrdinalIgnoreCase) || incoming.AccountInfo == null) return false;
        scan.Profile.AccountInfo ??= new ValorantIdentity();
        scan.Profile.AccountInfo.Email = incoming.AccountInfo.Email;
        scan.Profile.AccountInfo.EmailVerified = incoming.AccountInfo.EmailVerified;
        scan.Profile.AccountInfo.FetchedAt = incoming.AccountInfo.FetchedAt;
        return true;
    }

    private static bool? OptionalBool(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    public static ValorantAccountProfile ParseAccountIdentity(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var acct = Prop(root, "acct");
        var details = new ValorantIdentity {
            FetchedAt = DateTimeOffset.UtcNow,
            Username = Str(root, "username"), Email = Str(root, "email"), EmailVerified = OptionalBool(Prop(root, "email_verified")),
            Phone = Str(root, "phone_number"), PhoneVerified = OptionalBool(Prop(root, "phone_number_verified")), Country = Str(root, "country").ToUpperInvariant()
        };
        if (string.IsNullOrWhiteSpace(details.Username)) details.Username = Str(acct, "username");
        var created = Prop(acct, "created_at");
        if (created.ValueKind == JsonValueKind.Number && created.TryGetInt64(out var milliseconds)) {
            try { details.CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); } catch (ArgumentOutOfRangeException) { }
        } else if (created.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(created.GetString(), out var date)) details.CreatedAt = date;
        var restrictions = Prop(Prop(root, "ban"), "restrictions");
        if (restrictions.ValueKind == JsonValueKind.Array) details.RestrictionCount = restrictions.GetArrayLength();
        var puuid = Str(root, "sub");
        if (string.IsNullOrWhiteSpace(puuid)) throw new JsonException("Riot không trả về định danh tài khoản.");
        return new ValorantAccountProfile {
            Puuid = puuid, GameName = Str(acct, "game_name"), TagLine = Str(acct, "tag_line"),
            AccountInfo = details, IsBanned = details.RestrictionCount == 0 ? false : null, BanStatus = details.BanText
        };
    }
}
