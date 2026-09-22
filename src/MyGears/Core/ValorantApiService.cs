using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyGears.Core;

/// <summary>
/// Dịch vụ kết nối Riot Games API & Valorant-API.com
/// </summary>
public static partial class ValorantApiService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly Dictionary<string, ValorantSkinItem> _skinsByUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantSkinItem> _skinsByLevelUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantItem> _buddiesByUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantItem> _buddiesByLevelUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantItem> _cardsByUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantItem> _spraysByUuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ValorantItem> _agentsByUuid = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] BaseAgentUuids = new[]
    {
        "320b2a48-4d9b-a075-30f1-1f93a9b638fa", // Sova
        "eb93336a-449b-9c1b-0a54-a891f7921d69", // Phoenix
        "9f0d8ba9-4140-b941-57d3-a7ad57c6b417", // Brimstone
        "569fdd95-4d10-43ab-ca70-79becc718b46", // Sage
        "add6443a-41bd-e414-f6ad-e58d267f4e95"  // Jett
    };

    private static bool _metadataLoaded = false;
    private static readonly object _lock = new();

    public const string RiotAuthUrl =
        "https://auth.riotgames.com/authorize?redirect_uri=http://localhost/redirect&client_id=riot-client&response_type=token%20id_token&nonce=1&scope=openid%20link%20ban%20lol_region%20account%20email";

    private const string ClientPlatformBase64 =
        "ewogICAgInBsYXRmb3JtVHlwZSI6ICJQQyIsCiAgICAicGxhdGZvcm1PUyI6ICJXaW5kb3dzIiwKICAgICJwbGF0Zm9ybU9TVmVyc2lvbiI6ICIxMC4wLjE5MDQyLjEuMjU2LjY0Yml0IiwKICAgICJwbGF0Zm9ybUNoaXBzZXQiOiAiVW5rbm93biIKfQ==";

    private static string _clientVersion = "release-13.05-shipping-11-5350494";

    // ──────────────────────────────────────────────
    //  BÓC TÁCH TOKEN TỪ LINK REDIRECT
    // ──────────────────────────────────────────────

    public static (string? AccessToken, string? IdToken) ParseTokensFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, null);

        string? access = null;
        string? id = null;

        var clean = url.Replace("#", "&");
        var matchAccess = Regex.Match(clean, @"access_token=([^&]+)");
        if (matchAccess.Success)
            access = Uri.UnescapeDataString(matchAccess.Groups[1].Value);

        var matchId = Regex.Match(clean, @"id_token=([^&]+)");
        if (matchId.Success)
            id = Uri.UnescapeDataString(matchId.Groups[1].Value);

        return (access, id);
    }

    // ──────────────────────────────────────────────
    //  ĐỒNG BỘ METADATA TỪ VALORANT-API.COM
    // ──────────────────────────────────────────────

    public static async Task EnsureAllMetadataLoadedAsync()
    {
        if (_metadataLoaded) return;

        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGears");
        Directory.CreateDirectory(cacheDir);

        // Đồng bộ phiên bản Riot Client mới nhất
        try
        {
            var verJson = await _httpClient.GetStringAsync("https://valorant-api.com/v1/version");
            using var vDoc = JsonDocument.Parse(verJson);
            if (vDoc.RootElement.TryGetProperty("data", out var vData) &&
                vData.TryGetProperty("riotClientVersion", out var rcv) && !string.IsNullOrEmpty(rcv.GetString()))
            {
                _clientVersion = rcv.GetString()!;
            }
        }
        catch { }

        var tSkins = LoadResourceCachedAsync("https://valorant-api.com/v1/weapons/skins", "valorant_skins_cache.json", s => ParseAndLoadSkinsJson(s));
        var tBuddies = LoadResourceCachedAsync("https://valorant-api.com/v1/buddies", "valorant_buddies_cache.json", ParseAndLoadBuddiesJson);
        var tCards = LoadResourceCachedAsync("https://valorant-api.com/v1/playercards", "valorant_cards_cache.json", ParseAndLoadCardsJson);
        var tSprays = LoadResourceCachedAsync("https://valorant-api.com/v1/sprays", "valorant_sprays_cache.json", ParseAndLoadSpraysJson);
        var tAgents = LoadResourceCachedAsync("https://valorant-api.com/v1/agents?isPlayableCharacter=true", "valorant_agents_cache.json", ParseAndLoadAgentsJson);

        await Task.WhenAll(tSkins, tBuddies, tCards, tSprays, tAgents);
        _metadataLoaded = _skinsByUuid.Count > 0 && _buddiesByUuid.Count > 0 && _cardsByUuid.Count > 0 && _spraysByUuid.Count > 0 && _agentsByUuid.Count > 0;
    }

    public static Task EnsureSkinsMetadataLoadedAsync() => EnsureAllMetadataLoadedAsync();

    private static async Task LoadResourceCachedAsync(string url, string cacheFileName, Action<string> parseAction)
    {
        var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGears");
        var cacheFile = Path.Combine(cacheDir, cacheFileName);

        try
        {
            if (File.Exists(cacheFile) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalDays < 3)
            {
                var cachedJson = await File.ReadAllTextAsync(cacheFile);
                parseAction(cachedJson);
                return;
            }

            var response = await _httpClient.GetStringAsync(url);
            parseAction(response);
            _ = File.WriteAllTextAsync(cacheFile, response);
        }
        catch
        {
            if (File.Exists(cacheFile))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile);
                    parseAction(cachedJson);
                }
                catch { }
            }
        }
    }

    private static bool ParseAndLoadSkinsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return false;

            lock (_lock)
            {
                _skinsByUuid.Clear();
                _skinsByLevelUuid.Clear();

                foreach (var item in dataArr.EnumerateArray())
                {
                    var skinUuid = item.GetProperty("uuid").GetString() ?? "";
                    var displayName = item.GetProperty("displayName").GetString() ?? "";
                    var displayIcon = item.TryGetProperty("displayIcon", out var dIcon) && dIcon.GetString() != null
                        ? dIcon.GetString()! : "";

                    string tierName = "Select";
                    string tierColor = "#5A9FE2";
                    int cost = 875;

                    if (item.TryGetProperty("contentTierUuid", out var tierProp) && tierProp.GetString() != null)
                    {
                        var tUuid = tierProp.GetString()!.ToLowerInvariant();
                        (tierName, tierColor, cost) = tUuid switch
                        {
                            "12683d76-48d7-84a3-4e09-6985794f0445" => ("Select", "#5A9FE2", 875),
                            "0cebb8be-46d7-c12a-d306-e9907ab5a366" => ("Deluxe", "#27A6D8", 1275),
                            "60bca009-4182-7998-dee7-b8a2558dc369" => ("Premium", "#D1548D", 1775),
                            "411e4a55-4e59-775b-3772-b23046bbbfdd" => ("Exclusive", "#F9D35E", 2175),
                            "e046854e-406c-37f4-6607-19a9ba8426fc" => ("Ultra", "#F1A83B", 2475),
                            _ => ("Select", "#5A9FE2", 875)
                        };
                    }

                    var weaponName = "Vũ khí";
                    var nameParts = displayName.Split(' ');
                    if (nameParts.Length > 1) weaponName = nameParts.Last();

                    // ── Bước 1: Pre-scan levels để lấy icon Level 1 ──
                    string level1Icon = "";
                    var levelsList = new List<(string Uuid, string Icon)>();
                    if (item.TryGetProperty("levels", out var levelsArr))
                    {
                        foreach (var lvl in levelsArr.EnumerateArray())
                        {
                            var lvlUuid = lvl.GetProperty("uuid").GetString() ?? "";
                            if (!string.IsNullOrEmpty(lvlUuid))
                            {
                                var lvlIcon = lvl.TryGetProperty("displayIcon", out var lIcon) && lIcon.GetString() != null
                                    ? lIcon.GetString()! : "";
                                levelsList.Add((lvlUuid, lvlIcon));
                                if (string.IsNullOrEmpty(level1Icon) && !string.IsNullOrEmpty(lvlIcon))
                                    level1Icon = lvlIcon;
                            }
                        }
                    }

                    // ── Bước 2: Pre-scan chromas để lấy fullRender của chroma đầu tiên (base skin) ──
                    // fullRender = ảnh toàn bộ súng HD (1920x1080) - đây là ảnh đúng nhất
                    string bestIcon = "";
                    var chromasList = new List<(string Uuid, string BestIcon)>();
                    if (item.TryGetProperty("chromas", out var chromasArr))
                    {
                        foreach (var chr in chromasArr.EnumerateArray())
                        {
                            var chrUuid = chr.GetProperty("uuid").GetString() ?? "";
                            if (!string.IsNullOrEmpty(chrUuid))
                            {
                                string chrBestIcon = "";
                                // fullRender > displayIcon (displayIcon là ảnh swatch nhỏ, không phải ảnh súng)
                                if (chr.TryGetProperty("fullRender", out var frProp) && frProp.GetString() != null)
                                    chrBestIcon = frProp.GetString()!;
                                if (string.IsNullOrEmpty(chrBestIcon) && chr.TryGetProperty("displayIcon", out var cIcon) && cIcon.GetString() != null)
                                    chrBestIcon = cIcon.GetString()!;
                                chromasList.Add((chrUuid, chrBestIcon));

                                // Chroma đầu tiên = base skin, lấy icon tốt nhất từ đây
                                if (string.IsNullOrEmpty(bestIcon) && !string.IsNullOrEmpty(chrBestIcon))
                                    bestIcon = chrBestIcon;
                            }
                        }
                    }

                    // ── Bước 3: Xác định icon cuối cho skin ──
                    // Ưu tiên: fullRender chroma 1 > level1Icon > displayIcon gốc
                    if (string.IsNullOrEmpty(displayIcon)) displayIcon = level1Icon;
                    string skinIcon = !string.IsNullOrEmpty(bestIcon) ? bestIcon
                                    : !string.IsNullOrEmpty(displayIcon) ? displayIcon
                                    : level1Icon;

                    var skinModel = new ValorantSkinItem
                    {
                        Uuid = skinUuid,
                        DisplayName = displayName,
                        WeaponName = weaponName,
                        DisplayIcon = skinIcon,
                        TierName = tierName,
                        TierColor = tierColor,
                        Cost = cost
                    };
                    _skinsByUuid[skinUuid] = skinModel;

                    // ── Bước 4: Map tất cả level UUIDs ──
                    foreach (var (lvlUuid, lvlIcon) in levelsList)
                    {
                        // Upgrade icons can be close-ups of VFX, animations or finishers.
                        // Inventory/store cards represent the whole skin, not an upgrade.
                        var finalLvlIcon = skinIcon;
                        var lvlItem = new ValorantSkinItem
                        {
                            Uuid = skinUuid,
                            LevelUuid = lvlUuid,
                            DisplayName = displayName,
                            WeaponName = weaponName,
                            DisplayIcon = finalLvlIcon,
                            TierName = tierName,
                            TierColor = tierColor,
                            Cost = cost
                        };
                        _skinsByLevelUuid[lvlUuid] = lvlItem;
                    }

                    // ── Bước 5: Map tất cả chroma UUIDs ──
                    foreach (var (chrUuid, chrBestIcon) in chromasList)
                    {
                        // Chroma dùng: fullRender của chính nó > skinIcon (fullRender chroma 1)
                        var finalChrIcon = !string.IsNullOrEmpty(chrBestIcon) ? chrBestIcon : skinIcon;
                        var chrItem = new ValorantSkinItem
                        {
                            Uuid = skinUuid,
                            LevelUuid = chrUuid,
                            DisplayName = displayName,
                            WeaponName = weaponName,
                            DisplayIcon = finalChrIcon,
                            TierName = tierName,
                            TierColor = tierColor,
                            Cost = cost
                        };
                        _skinsByLevelUuid[chrUuid] = chrItem;
                    }
                }
            }
            return _skinsByUuid.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseAndLoadBuddiesJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return;

            lock (_lock)
            {
                _buddiesByUuid.Clear();
                _buddiesByLevelUuid.Clear();

                foreach (var item in dataArr.EnumerateArray())
                {
                    var uuid = item.GetProperty("uuid").GetString() ?? "";
                    var name = item.GetProperty("displayName").GetString() ?? "";
                    var icon = item.TryGetProperty("displayIcon", out var ic) && ic.GetString() != null ? ic.GetString()! : "";

                    var buddy = new ValorantItem
                    {
                        Uuid = uuid,
                        DisplayName = name,
                        DisplayIcon = icon,
                        CategoryName = "Phụ kiện súng",
                        TagText = "BUDDY",
                        BorderColor = "#EAB308"
                    };

                    _buddiesByUuid[uuid] = buddy;

                    if (item.TryGetProperty("levels", out var lvls))
                    {
                        foreach (var lvl in lvls.EnumerateArray())
                        {
                            var lUuid = lvl.GetProperty("uuid").GetString() ?? "";
                            if (!string.IsNullOrEmpty(lUuid))
                                _buddiesByLevelUuid[lUuid] = buddy;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static void ParseAndLoadCardsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return;

            lock (_lock)
            {
                _cardsByUuid.Clear();
                foreach (var item in dataArr.EnumerateArray())
                {
                    var uuid = item.GetProperty("uuid").GetString() ?? "";
                    var name = item.GetProperty("displayName").GetString() ?? "";
                    string icon = "";
                    if (item.TryGetProperty("largeArt", out var la) && la.GetString() != null) icon = la.GetString()!;
                    else if (item.TryGetProperty("displayIcon", out var di) && di.GetString() != null) icon = di.GetString()!;

                    _cardsByUuid[uuid] = new ValorantItem
                    {
                        Uuid = uuid,
                        DisplayName = name,
                        DisplayIcon = icon,
                        CategoryName = "Thẻ người chơi",
                        TagText = "CARD",
                        BorderColor = "#38BDF8"
                    };
                }
            }
        }
        catch { }
    }

    private static void ParseAndLoadSpraysJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return;

            lock (_lock)
            {
                _spraysByUuid.Clear();
                foreach (var item in dataArr.EnumerateArray())
                {
                    var uuid = item.GetProperty("uuid").GetString() ?? "";
                    var name = item.GetProperty("displayName").GetString() ?? "";
                    string icon = "";
                    if (item.TryGetProperty("displayIcon", out var di) && di.GetString() != null) icon = di.GetString()!;
                    else if (item.TryGetProperty("fullTransparentIcon", out var fi) && fi.GetString() != null) icon = fi.GetString()!;

                    _spraysByUuid[uuid] = new ValorantItem
                    {
                        Uuid = uuid,
                        DisplayName = name,
                        DisplayIcon = icon,
                        CategoryName = "Hình phun sơn",
                        TagText = "SPRAY",
                        BorderColor = "#A855F7"
                    };
                }
            }
        }
        catch { }
    }

    private static void ParseAndLoadAgentsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return;

            lock (_lock)
            {
                _agentsByUuid.Clear();
                foreach (var item in dataArr.EnumerateArray())
                {
                    var uuid = item.GetProperty("uuid").GetString() ?? "";
                    var name = item.GetProperty("displayName").GetString() ?? "";
                    string role = "Đặc vụ";
                    if (item.TryGetProperty("role", out var rObj) && rObj.ValueKind == JsonValueKind.Object &&
                        rObj.TryGetProperty("displayName", out var rd) && rd.GetString() != null)
                    {
                        role = rd.GetString()!;
                    }

                    string icon = "";
                    if (item.TryGetProperty("displayIcon", out var di) && di.GetString() != null) icon = di.GetString()!;
                    else if (item.TryGetProperty("bustPortrait", out var bp) && bp.GetString() != null) icon = bp.GetString()!;

                    _agentsByUuid[uuid] = new ValorantItem
                    {
                        Uuid = uuid,
                        DisplayName = name,
                        DisplayIcon = icon,
                        CategoryName = $"Đặc vụ ({role})",
                        TagText = role.ToUpperInvariant(),
                        BorderColor = "#10B981"
                    };
                }
            }
        }
        catch { }
    }

    private static void LoadFallbackSkins()
    {
        lock (_lock)
        {
            var primeVandal = new ValorantSkinItem
            {
                Uuid = "prime-vandal",
                DisplayName = "Prime Vandal",
                WeaponName = "Vandal",
                DisplayIcon = "https://media.valorant-api.com/weaponskins/9feab64a-4712-4c28-5777-628d06d44521/displayicon.png",
                TierName = "Premium",
                TierColor = "#D1548D",
                Cost = 1775
            };
            _skinsByUuid[primeVandal.Uuid] = primeVandal;
        }
    }

    public static ValorantSkinItem GetSkinByUuidOrLevel(string id)
    {
        if (string.IsNullOrEmpty(id)) return new ValorantSkinItem { DisplayName = "Skin Valorant" };
        if (_skinsByLevelUuid.TryGetValue(id, out var lvlSkin)) return lvlSkin;
        if (_skinsByUuid.TryGetValue(id, out var skin)) return skin;
        return new ValorantSkinItem { Uuid = id, DisplayName = $"Skin ({id[..Math.Min(8, id.Length)]})", TierColor = "#5A9FE2" };
    }

    public static async Task RefreshScanSkinImagesAsync(ValorantScanResult scan)
    {
        await EnsureSkinsMetadataLoadedAsync();
        // Saved scans contain the old URL. Resolve by parent UUID first so a
        // deduplicated inventory card always shows the base skin, regardless of
        // which owned upgrade/chroma Riot returned first.
        lock (_lock)
        {
            foreach (var item in scan.OwnedSkins
                .Concat(scan.Store.DailyOffers.Select(offer => offer.Skin))
                .Concat(scan.Store.NightMarketOffers.Select(offer => offer.Skin)))
            {
                if (!_skinsByUuid.TryGetValue(item.Uuid, out var skin))
                {
                    if (!_skinsByLevelUuid.TryGetValue(item.LevelUuid, out var level) &&
                        !_skinsByLevelUuid.TryGetValue(item.Uuid, out level)) continue;
                    if (!_skinsByUuid.TryGetValue(level.Uuid, out skin)) continue;
                }
                if (!string.IsNullOrWhiteSpace(skin.DisplayIcon))
                    item.DisplayIcon = skin.DisplayIcon;
            }
        }
    }

    // ──────────────────────────────────────────────
    //  GỌI API RIOT GAMES
    // ──────────────────────────────────────────────

    public static async Task<string> GetEntitlementsTokenAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://entitlements.auth.riotgames.com/api/token/v1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var res = await _httpClient.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("entitlements_token").GetString() ?? string.Empty;
    }

    public static async Task<ValorantAccountProfile> GetUserInfoAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://auth.riotgames.com/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await _httpClient.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return ParseAccountIdentity(await res.Content.ReadAsStringAsync());
    }
    public static (int? Vp, int? Rp, int? Kc, string Error) ParseWallet(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var balances = Prop(doc.RootElement, "Balances");
        int? Read(string id)
        {
            var value = Prop(balances, id);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var amount) && amount >= 0 ? amount : null;
        }
        var vp = Read(VpCurrency);
        var rp = Read("e59aa87c-4cbf-517a-5983-6e81511be9b7");
        var kc = Read(KcCurrency);
        return (vp, rp, kc, vp == null || rp == null || kc == null ? "Riot chưa trả về đủ số dư." : "");
    }

    public static async Task<(int? Vp, int? Rp, int? Kc, string Error)> GetWalletAsync(string puuid, string accessToken, string entitlementsToken, string region = "ap")
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://pd.{region}.a.pvp.net/store/v1/wallet/{puuid}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Riot-Entitlements-JWT", entitlementsToken);
            req.Headers.Add("X-Riot-ClientPlatform", ClientPlatformBase64);
            req.Headers.Add("X-Riot-ClientVersion", _clientVersion);
            using var res = await _httpClient.SendAsync(req);
            if (!res.IsSuccessStatusCode) return (null, null, null, ApiError("Số dư", (int)res.StatusCode));
            return ParseWallet(await res.Content.ReadAsStringAsync());
        }
        catch (JsonException) { return (null, null, null, "Dữ liệu số dư không hợp lệ."); }
        catch { return (null, null, null, "Không kết nối được để tải số dư. Hãy quét lại."); }
    }

    public static async Task<int?> GetAccountLevelAsync(string puuid, string accessToken, string entitlementsToken, string region = "ap")
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://pd.{region}.a.pvp.net/account-xp/v1/players/{puuid}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Riot-Entitlements-JWT", entitlementsToken);
            req.Headers.Add("X-Riot-ClientPlatform", ClientPlatformBase64);
            req.Headers.Add("X-Riot-ClientVersion", _clientVersion);

            using var res = await _httpClient.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Progress", out var prog))
            {
                if (prog.TryGetProperty("Level", out var lvlProp))
                    return lvlProp.GetInt32();
            }
            return null;
        }
        catch { return null; }
    }

    public static string GetRankName(int tier) => tier switch
    {
        3 => "IRON 1", 4 => "IRON 2", 5 => "IRON 3",
        6 => "BRONZE 1", 7 => "BRONZE 2", 8 => "BRONZE 3",
        9 => "SILVER 1", 10 => "SILVER 2", 11 => "SILVER 3",
        12 => "GOLD 1", 13 => "GOLD 2", 14 => "GOLD 3",
        15 => "PLATINUM 1", 16 => "PLATINUM 2", 17 => "PLATINUM 3",
        18 => "DIAMOND 1", 19 => "DIAMOND 2", 20 => "DIAMOND 3",
        21 => "ASCENDANT 1", 22 => "ASCENDANT 2", 23 => "ASCENDANT 3",
        24 => "IMMORTAL 1", 25 => "IMMORTAL 2", 26 => "IMMORTAL 3",
        27 => "RADIANT",
        _ => "UNRANKED"
    };

    public static string GetRankIconUrl(int tier)
    {
        return $"https://media.valorant-api.com/competitivetiers/5641173d-4437-3a4b-d246-dba494f43409/{tier}/largeicon.png";
    }

    private static bool TryGetProp(JsonElement element, string propName, out JsonElement value)
    {
        if (element.TryGetProperty(propName, out value)) return true;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    public static async Task<string> GetAccountRegionAsync(string accessToken, string idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return "";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, "https://riot-geo.pas.si.riotgames.com/pas/v1/product/valorant");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { id_token = idToken }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req);
            if (!res.IsSuccessStatusCode) return "";

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (TryGetProp(doc.RootElement, "affinities", out var aff))
            {
                if (TryGetProp(aff, "live", out var liveProp) && liveProp.GetString() != null)
                {
                    var live = liveProp.GetString()!.ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(live)) return live;
                }
            }
        }
        catch { }
        return "";
    }

    public static async Task<List<string>> GetOwnedEntitlementItemIdsAsync(string puuid, string accessToken, string entitlementsToken, string itemTypeId, string region = "ap")
    {
        var list = new List<string>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://pd.{region}.a.pvp.net/store/v1/entitlements/{puuid}/{itemTypeId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Add("X-Riot-Entitlements-JWT", entitlementsToken);
            req.Headers.Add("X-Riot-ClientPlatform", ClientPlatformBase64);
            req.Headers.Add("X-Riot-ClientVersion", _clientVersion);

            using var res = await _httpClient.SendAsync(req);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("Entitlements", out _) && !doc.RootElement.TryGetProperty("EntitlementsByTypes", out _))
                throw new JsonException("Thiếu dữ liệu kho đồ.");
            void ExtractItems(JsonElement arr)
            {
                if (arr.ValueKind != JsonValueKind.Array) throw new JsonException("Dữ liệu kho không hợp lệ.");
                foreach (var item in arr.EnumerateArray())
                {
                    string? id = null;
                    if (TryGetProp(item, "ItemID", out var idProp) && idProp.GetString() != null)
                        id = idProp.GetString();
                    else if (TryGetProp(item, "ItemId", out var idProp2) && idProp2.GetString() != null)
                        id = idProp2.GetString();

                    if (!string.IsNullOrEmpty(id))
                        list.Add(id);
                }
            }

            if (TryGetProp(doc.RootElement, "Entitlements", out var ents))
                ExtractItems(ents);

            if (TryGetProp(doc.RootElement, "EntitlementsByTypes", out var byTypes))
            {
                if (byTypes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in byTypes.EnumerateArray())
                    {
                        if (TryGetProp(t, "Entitlements", out var subEnts))
                            ExtractItems(subEnts);
                    }
                }
                else if (byTypes.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in byTypes.EnumerateObject())
                    {
                        if (TryGetProp(p.Value, "Entitlements", out var subEnts))
                            ExtractItems(subEnts);
                    }
                }
            }
        }
        catch { throw; }
        return list;
    }

    public static async Task<int> GetEntitlementCountAsync(string puuid, string accessToken, string entitlementsToken, string itemTypeId, string region = "ap")
    {
        var items = await GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, itemTypeId, region);
        return items.Count;
    }

    public static async Task<List<string>> GetOwnedSkinLevelUuidsAsync(string puuid, string accessToken, string entitlementsToken, string region = "ap")
    {
        var levels = await GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "e7c63390-eda7-46e0-bb7a-a6abdacd2433", region);
        var chromas = await GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "3ad1b2b2-acdb-4524-852f-954a76ddae0a", region);
        return levels.Concat(chromas).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<ValorantStoreData> GetStorefrontAsync(string puuid, string accessToken, string entitlementsToken, string region = "ap")
    {
        try
        {
            using var req = StorefrontRequest(puuid, accessToken, entitlementsToken, region);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return new() { DataVersion = 1, StatusMessage = ApiError("Cửa hàng", (int)response.StatusCode) };
            var store = ParseStoreData(await response.Content.ReadAsStringAsync(), DateTimeOffset.UtcNow);
            if (store.Bundles.Count > 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await _httpClient.GetStringAsync("https://valorant-api.com/v1/bundles"));
                    var catalog = Rows(Prop(doc.RootElement, "data")).ToDictionary(b => Str(b, "uuid"), StringComparer.OrdinalIgnoreCase);
                    foreach (var bundle in store.Bundles)
                        if (catalog.TryGetValue(bundle.Name, out var metadata))
                        {
                            bundle.Name = Str(metadata, "displayName");
                            bundle.Icon = Str(metadata, "displayIcon");
                        }
                }
                catch { /* Keep store data if the public image catalog is unavailable. */ }
            }
            return store;
        }
        catch { return new() { DataVersion = 1, StatusMessage = "Không tải được cửa hàng. Kiểm tra kết nối và quét lại." }; }
    }
    public static async Task<ValorantScanResult> ScanAccountAsync(string accessToken, string idToken, string defaultRegion = "ap")
    {
        await EnsureSkinsMetadataLoadedAsync();

        // Tự động nhận diện region nếu có idToken, nếu không dùng defaultRegion
        string region = defaultRegion;
        var regionVerified = false;
        if (!string.IsNullOrEmpty(idToken))
        {
            var detected = await GetAccountRegionAsync(accessToken, idToken);
            if (!string.IsNullOrEmpty(detected)) { region = detected; regionVerified = true; }
        }

        var entitlementsToken = await GetEntitlementsTokenAsync(accessToken);
        var userInfo = await GetUserInfoAsync(accessToken);
        var puuid = userInfo.Puuid;
        var gameName = userInfo.GameName;
        var tagLine = userInfo.TagLine;
        var isBanned = userInfo.IsBanned;
        var banStatus = userInfo.BanStatus;

        var inventoryErrors = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        async Task<List<string>> ReadInventory(string category, Func<Task<List<string>>> fetch)
        {
            try { return await fetch(); }
            catch (HttpRequestException ex) { inventoryErrors[category] = ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode.Value}" : "Lỗi kết nối"; }
            catch (TaskCanceledException) { inventoryErrors[category] = "Quá thời gian chờ"; }
            catch { inventoryErrors[category] = "Dữ liệu không hợp lệ"; }
            return [];
        }
        // Chạy song song các tác vụ gọi API
        var walletTask = GetWalletAsync(puuid, accessToken, entitlementsToken, region);
        var levelTask = GetAccountLevelAsync(puuid, accessToken, entitlementsToken, region);
        var rankTask = GetRankDataAsync(puuid, accessToken, entitlementsToken, region);
        var storeTask = GetStorefrontAsync(puuid, accessToken, entitlementsToken, region);
        var skinsTask = ReadInventory("Skins", () => GetOwnedSkinLevelUuidsAsync(puuid, accessToken, entitlementsToken, region));

        // ItemTypeIDs chuẩn từ Riot Games
        var buddiesTask = ReadInventory("Buddies", () => GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "dd3bf334-87f3-40bd-b043-682a57a8dc3a", region)); // Buddies
        var cardsTask = ReadInventory("Cards", () => GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "3f296c07-64c3-494c-923b-fe692a4fa1bd", region));   // Cards
        var spraysTask = ReadInventory("Sprays", () => GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "d5f120f8-ff8c-4aac-92ea-f2b5acbe9475", region));  // Sprays
        var agentsTask = ReadInventory("Agents", () => GetOwnedEntitlementItemIdsAsync(puuid, accessToken, entitlementsToken, "01bb38e1-da47-4e6a-9b3d-945fe4655707", region));  // Agents

        await Task.WhenAll(walletTask, levelTask, rankTask, storeTask, skinsTask, buddiesTask, cardsTask, spraysTask, agentsTask);

        var (vp, rp, kc, walletError) = walletTask.Result;
        var level = levelTask.Result;
        var rankData = rankTask.Result;
        var tier = rankData.Current?.Tier ?? 0;
        var rr = rankData.Current?.Rr ?? 0;
        var rankName = rankData.Current?.RankName ?? "CHƯA CÓ DỮ LIỆU";
        var fullTitle = rankData.Current != null ? $"{rankData.Current.Title} // {rankName} ({rr} RR)" : "Chưa tải được rank — hãy quét lại";
        var iconUrl = GetRankIconUrl(tier);
        var storeData = storeTask.Result;
        var ownedSkinUuids = skinsTask.Result;

        var ownedSkins = new List<ValorantSkinItem>();
        var seenBaseSkins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int countUltra = 0;
        int countExclusive = 0;
        int countPremium = 0;
        int countSelect = 0;

        foreach (var lvlId in ownedSkinUuids)
        {
            var skin = GetSkinByUuidOrLevel(lvlId);
            if (!string.IsNullOrEmpty(skin.Uuid) && seenBaseSkins.Add(skin.Uuid))
            {
                // Bỏ qua các skin súng mặc định (Standard / Melee thường)
                if (!skin.DisplayName.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) &&
                    !skin.DisplayName.Equals("Melee", StringComparison.OrdinalIgnoreCase))
                {
                    ownedSkins.Add(skin);
                    if (skin.TierName.Equals("Ultra", StringComparison.OrdinalIgnoreCase)) countUltra++;
                    else if (skin.TierName.Equals("Exclusive", StringComparison.OrdinalIgnoreCase)) countExclusive++;
                    else if (skin.TierName.Equals("Premium", StringComparison.OrdinalIgnoreCase)) countPremium++;
                    else countSelect++;
                }
            }
        }

        // Map Buddies: Gom theo Parent Buddy Uuid (vì Riot trả về 2 instances cho mỗi buddy)
        var ownedBuddies = new List<ValorantItem>();
        var seenBuddies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bId in buddiesTask.Result)
        {
            ValorantItem? buddy = null;
            if (_buddiesByLevelUuid.TryGetValue(bId, out var b1)) buddy = b1;
            else if (_buddiesByUuid.TryGetValue(bId, out var b2)) buddy = b2;

            if (buddy != null)
            {
                if (seenBuddies.Add(buddy.Uuid))
                    ownedBuddies.Add(buddy);
            }
            else
            {
                if (seenBuddies.Add(bId))
                {
                    ownedBuddies.Add(new ValorantItem
                    {
                        Uuid = bId,
                        DisplayName = $"Phụ kiện ({bId[..Math.Min(8, bId.Length)]})",
                        CategoryName = "Phụ kiện súng",
                        TagText = "BUDDY",
                        BorderColor = "#EAB308"
                    });
                }
            }
        }

        // Map Cards: Thẻ người chơi
        var ownedCards = new List<ValorantItem>();
        var seenCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cId in cardsTask.Result)
        {
            if (_cardsByUuid.TryGetValue(cId, out var card))
            {
                if (seenCards.Add(card.Uuid))
                    ownedCards.Add(card);
            }
            else
            {
                if (seenCards.Add(cId))
                {
                    ownedCards.Add(new ValorantItem
                    {
                        Uuid = cId,
                        DisplayName = $"Thẻ ({cId[..Math.Min(8, cId.Length)]})",
                        CategoryName = "Thẻ người chơi",
                        TagText = "CARD",
                        BorderColor = "#38BDF8"
                    });
                }
            }
        }

        // Map Sprays: Hình phun sơn
        var ownedSprays = new List<ValorantItem>();
        var seenSprays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sId in spraysTask.Result)
        {
            if (_spraysByUuid.TryGetValue(sId, out var spray))
            {
                if (seenSprays.Add(spray.Uuid))
                    ownedSprays.Add(spray);
            }
            else
            {
                if (seenSprays.Add(sId))
                {
                    ownedSprays.Add(new ValorantItem
                    {
                        Uuid = sId,
                        DisplayName = $"Hình sơn ({sId[..Math.Min(8, sId.Length)]})",
                        CategoryName = "Hình phun sơn",
                        TagText = "SPRAY",
                        BorderColor = "#A855F7"
                    });
                }
            }
        }

        // Map Agents: Bao gồm 5 đặc vụ mặc định mở khóa sẵn cho toàn bộ tài khoản Valorant
        var ownedAgents = new List<ValorantItem>();
        var allAgentUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseId in BaseAgentUuids)
            allAgentUuids.Add(baseId);
        foreach (var aId in agentsTask.Result)
            allAgentUuids.Add(aId);

        foreach (var aId in allAgentUuids)
        {
            if (_agentsByUuid.TryGetValue(aId, out var agent))
            {
                ownedAgents.Add(agent);
            }
            else
            {
                ownedAgents.Add(new ValorantItem
                {
                    Uuid = aId,
                    DisplayName = $"Đặc vụ ({aId[..Math.Min(8, aId.Length)]})",
                    CategoryName = "Đặc vụ",
                    TagText = "AGENT",
                    BorderColor = "#10B981"
                });
            }
        }

        if (_skinsByUuid.Count == 0 || ownedSkinUuids.Any(id => !_skinsByLevelUuid.ContainsKey(id) && !_skinsByUuid.ContainsKey(id)))
            inventoryErrors.TryAdd("Skins", "Danh mục skin chưa đầy đủ; số lượng và định giá chưa xác minh");
        if (_buddiesByUuid.Count == 0) inventoryErrors.TryAdd("Buddies", "Chưa tải được danh mục");
        if (_cardsByUuid.Count == 0 || cardsTask.Result.Any(id => !_cardsByUuid.ContainsKey(id))) inventoryErrors.TryAdd("Cards", "Chưa tải được danh mục");
        if (_spraysByUuid.Count == 0 || spraysTask.Result.Any(id => !_spraysByUuid.ContainsKey(id))) inventoryErrors.TryAdd("Sprays", "Chưa tải được danh mục");
        if (_agentsByUuid.Count == 0) inventoryErrors.TryAdd("Agents", "Chưa tải được danh mục");
        // Sắp xếp skin theo phẩm chất VIP (Ultra -> Exclusive -> Premium -> Deluxe -> Select)
        var totalVp = ownedSkins.Sum(s => s.Cost);
        var vndVal = (long)totalVp * 140;

        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGears", "valorant_scan_diag.log");
            var diag = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RiotId={gameName}#{tagLine}, Region={region}, Level={level}, VP={vp}, RP={rp}, KC={kc}, RawSkinUuids={ownedSkinUuids.Count}, FilteredSkins={ownedSkins.Count}, Buddies={ownedBuddies.Count}, Cards={ownedCards.Count}, Sprays={ownedSprays.Count}, Agents={ownedAgents.Count}, DailyStoreOffers={storeData.DailyOffers.Count}\n";
            File.AppendAllText(logPath, diag);
        }
        catch { }

        return new ValorantScanResult
        {
            Profile = new ValorantAccountProfile
            {
                Puuid = puuid,
                GameName = gameName,
                TagLine = tagLine,
                RiotId = $"{gameName}#{tagLine}",
                Region = region.ToUpperInvariant() + (regionVerified ? "" : " (chưa xác minh)"),
                AccountInfo = userInfo.AccountInfo,
                DataVersion = 1,
                WalletError = walletError,
                ValorantPoints = vp,
                RadianitePoints = rp,
                KingdomCredits = kc,
                AccountLevel = level > 0 ? level : null,
                RankTier = tier,
                RankedRating = rr,
                RankName = rankName,
                FullRankTitle = fullTitle,
                RankData = rankData,
                RankIcon = iconUrl,
                EstimatedVndValue = vndVal,
                IsBanned = isBanned,
                BanStatus = banStatus,
                SecurityStatus = "ĐÃ XÁC THỰC OAUTH2"
            },
            DataVersion = 1,
            InventoryErrors = new(inventoryErrors),
            Store = storeData,
            OwnedSkins = ownedSkins,
            OwnedBuddies = ownedBuddies,
            OwnedCards = ownedCards,
            OwnedSprays = ownedSprays,
            OwnedAgents = ownedAgents,
            CountUltra = countUltra,
            CountExclusive = countExclusive,
            CountPremium = countPremium,
            CountSelect = countSelect,
            CountBuddies = ownedBuddies.Count,
            CountCards = ownedCards.Count,
            CountSprays = ownedSprays.Count,
            CountAgents = ownedAgents.Count,
            ScanTime = DateTime.Now
        };
    }

    /// <summary>
    /// Đăng xuất khỏi phiên Riot Games (gọi auth.riotgames.com/logout)
    /// Đảm bảo lần quét tiếp theo không bị kẹt hay tự động đăng nhập vào tài khoản cũ
    /// </summary>
    public static async Task LogoutRiotSessionAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://auth.riotgames.com/logout");
            req.Headers.Add("User-Agent", "RiotClient/96.0.0.3957 (Windows; 10; x64)");
            await _httpClient.SendAsync(req);
        }
        catch { }
    }

    // ──────────────────────────────────────────────
    //  CHẾ ĐỘ MẪU (DEMO MODE ĐỂ TEST GIAO DIỆN)
    // ──────────────────────────────────────────────

    public static ValorantScanResult GetDemoScanResult()
    {
        var sampleSkins = new List<ValorantSkinItem>
        {
            new() { Uuid = "kuro-vandal", DisplayName = "Kuronami Vandal", WeaponName = "Vandal", DisplayIcon = "https://media.valorant-api.com/weaponskins/b00d603a-4467-3725-d913-c28892f39c89/displayicon.png", TierName = "Exclusive", TierColor = "#F9D35E", Cost = 2375 },
            new() { Uuid = "prime-vandal", DisplayName = "Prime Vandal", WeaponName = "Vandal", DisplayIcon = "https://media.valorant-api.com/weaponskins/9feab64a-4712-4c28-5777-628d06d44521/displayicon.png", TierName = "Premium", TierColor = "#D1548D", Cost = 1775 },
            new() { Uuid = "reaver-phantom", DisplayName = "Reaver Phantom", WeaponName = "Phantom", DisplayIcon = "https://media.valorant-api.com/weaponskins/4a29a0a0-47b7-58b2-5182-da8bb56b5952/displayicon.png", TierName = "Premium", TierColor = "#D1548D", Cost = 1775 },
            new() { Uuid = "kuronami-blade", DisplayName = "Kuronami no Yaiba", WeaponName = "Cận chiến", DisplayIcon = "https://media.valorant-api.com/weaponskins/679ae11e-450b-8d07-ee24-9ea9cc83e9b1/displayicon.png", TierName = "Exclusive", TierColor = "#F9D35E", Cost = 5350 },
            new() { Uuid = "elderflame-vandal", DisplayName = "Elderflame Vandal", WeaponName = "Vandal", DisplayIcon = "https://media.valorant-api.com/weaponskins/c4b8b6a2-4a0b-8e10-c08c-6689d0689b96/displayicon.png", TierName = "Ultra", TierColor = "#F1A83B", Cost = 2475 },
            new() { Uuid = "glitchpop-dagger", DisplayName = "Glitchpop Dagger", WeaponName = "Cận chiến", DisplayIcon = "https://media.valorant-api.com/weaponskins/5c0d12e6-4bb6-4654-2dc6-92895fbebeea/displayicon.png", TierName = "Exclusive", TierColor = "#F9D35E", Cost = 4350 },
            new() { Uuid = "ion-sheriff", DisplayName = "Ion Sheriff", WeaponName = "Sheriff", DisplayIcon = "https://media.valorant-api.com/weaponskins/a1c17042-4f35-94f7-dc21-499691b01c38/displayicon.png", TierName = "Premium", TierColor = "#D1548D", Cost = 1775 },
            new() { Uuid = "oni-phantom", DisplayName = "Oni Phantom", WeaponName = "Phantom", DisplayIcon = "https://media.valorant-api.com/weaponskins/0816999b-4395-fc7c-7201-1e967a507c91/displayicon.png", TierName = "Premium", TierColor = "#D1548D", Cost = 1775 }
        };

        var storeOffers = new List<ValorantStoreOffer>
        {
            new() { Skin = sampleSkins[0], OriginalCost = 2375, FinalCost = 2375 },
            new() { Skin = sampleSkins[1], OriginalCost = 1775, FinalCost = 1775 },
            new() { Skin = sampleSkins[2], OriginalCost = 1775, FinalCost = 1775 },
            new() { Skin = sampleSkins[3], OriginalCost = 5350, FinalCost = 5350 }
        };

        var nmOffers = new List<ValorantStoreOffer>
        {
            new() { Skin = sampleSkins[6], OriginalCost = 1775, FinalCost = 1065, DiscountPercent = 40, IsNightMarket = true },
            new() { Skin = sampleSkins[7], OriginalCost = 1775, FinalCost = 1242, DiscountPercent = 30, IsNightMarket = true }
        };

        return new ValorantScanResult
        {
            Profile = new ValorantAccountProfile
            {
                Puuid = "demo-puuid",
                GameName = "NightMarket_VIP",
                TagLine = "VN1",
                RiotId = "NightMarket_VIP#VN1",
                Region = "AP",
                ValorantPoints = 3450,
                RadianitePoints = 185
            },
            Store = new ValorantStoreData
            {
                DailyOffers = storeOffers,
                NightMarketOffers = nmOffers,
                DailyRemainingSeconds = 64200,
                NightMarketRemainingSeconds = 432000
            },
            OwnedSkins = sampleSkins,
            ScanTime = DateTime.Now
        };
    }
}


