using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RollCore;

// Independent RollCore library introduced in v17.8-preview4a.
// FastCore Web and future desktop UI should reference this shared core instead of duplicating logic.

public sealed class CardOpportunityFilter
{
    public bool Enabled { get; init; }
    public TermList Terms { get; init; } = new();
    public List<string> Rarities { get; init; } = new();
    public int MinCount { get; init; }
    public bool HasFilter => Enabled && (Terms.HasTerms || Rarities.Count > 0);

    public static Dictionary<string, CardOpportunityFilter> ParseAll(JsonElement? root)
    {
        var result = new Dictionary<string, CardOpportunityFilter>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in new[] { "own", "colorless", "other" })
        {
            var cfg = root.Prop(cat);
            result[cat] = FromJson(cfg);
        }
        return result;
    }

    private static CardOpportunityFilter FromJson(JsonElement? cfg)
    {
        if (cfg is null || cfg.Value.ValueKind != JsonValueKind.Object) return new CardOpportunityFilter();
        return new CardOpportunityFilter
        {
            Enabled = cfg.Value.Prop("enabled").Bool(false),
            Terms = TermList.FromJson(cfg),
            Rarities = NormalizeRarities(cfg.Value.Prop("rarities").StringList()),
            MinCount = 0,
        };
    }

    private static List<string> NormalizeRarities(IEnumerable<string> raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["普通"] = "Common",
            ["罕见"] = "Uncommon",
            ["稀有"] = "Rare",
            ["common"] = "Common",
            ["uncommon"] = "Uncommon",
            ["rare"] = "Rare",
        };
        var outList = new List<string>();
        foreach (var item in raw)
        {
            var k = (item ?? "").Trim();
            if (k.Length == 0) continue;
            if (new[] { "auto", "any", "all", "none", "不限制", "任意", "自动" }.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase))) continue;
            var v = map.TryGetValue(k, out var mapped) ? mapped : k;
            if (!outList.Contains(v, StringComparer.OrdinalIgnoreCase)) outList.Add(v);
        }
        return outList;
    }
}


public sealed class CardEvent
{
    public string Source { get; init; } = "";
    public string Category { get; init; } = "";
    public string Type { get; init; } = "";
    public string Method { get; init; } = "";
    public List<string> Cards { get; init; } = new();
    public List<List<string>> Options { get; init; } = new();
    public Dictionary<string, string?> Rarities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PotionEvent
{
    public string Source { get; init; } = "";
    public List<string> Potions { get; init; } = new();
}

public sealed class RelicEvent
{
    public string Source { get; init; } = "";
    public string Method { get; init; } = "";
    public List<string> Relics { get; init; } = new();
}


public sealed class OpeningRoute
{
    public string Kind { get; init; } = "direct";
    public string? DirectRelic { get; init; }
    public List<string> PickOrder { get; init; } = new();
    public string? BonesCurse { get; init; }
    public List<CardEvent> CardOpportunities { get; init; } = new();
    public List<PotionEvent> PotionOpportunities { get; init; } = new();
    public List<RelicEvent> RelicOpportunities { get; init; } = new();
    public List<string> Potions { get; init; } = new();
    public List<string> PredictedRelics { get; init; } = new();
}



public sealed class NeowRouteFilter
{
    public string RouteMode { get; init; } = "any";
    public TermList InitialRelics { get; init; } = new();
    public TermList BonesRelics { get; init; } = new();
    public TermList GeneratedRelics { get; init; } = new();
    public TermList Cards { get; init; } = new();
    public TermList Potions { get; init; } = new();
    public Dictionary<string, TermList> SourceCardFilters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TermList> SourcePotionFilters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TermList> SourceRelicFilters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CurseAny { get; init; } = new();
    public List<string> CurseBlacklist { get; init; } = new();

    public bool HasFilter => !string.Equals(RouteMode, "any", StringComparison.OrdinalIgnoreCase)
        || InitialRelics.HasTerms || BonesRelics.HasTerms || GeneratedRelics.HasTerms
        || Cards.HasTerms || Potions.HasTerms
        || SourceCardFilters.Values.Any(x => x.HasTerms)
        || SourcePotionFilters.Values.Any(x => x.HasTerms)
        || SourceRelicFilters.Values.Any(x => x.HasTerms)
        || CurseAny.Count > 0 || CurseBlacklist.Count > 0;

    public static NeowRouteFilter FromJson(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind != JsonValueKind.Object) return new NeowRouteFilter();
        var route = e.Value.Prop("route").Str("any") ?? "any";
        var curse = e.Value.Prop("curse");
        return new NeowRouteFilter
        {
            RouteMode = string.IsNullOrWhiteSpace(route) ? "any" : route.Trim().ToLowerInvariant(),
            InitialRelics = TermList.FromJson(e.Value.Prop("initial_relics")),
            BonesRelics = TermList.FromJson(e.Value.Prop("bones_relics")),
            GeneratedRelics = TermList.FromJson(e.Value.Prop("generated_relics")),
            Cards = TermList.FromJson(e.Value.Prop("cards")),
            Potions = TermList.FromJson(e.Value.Prop("potions")),
            SourceCardFilters = SourceTermListsFromJson(e.Value.Prop("source_cards")),
            SourcePotionFilters = SourceTermListsFromJson(e.Value.Prop("source_potions")),
            SourceRelicFilters = SourceTermListsFromJson(e.Value.Prop("source_relics")),
            CurseAny = NeowFilterText.NormalizeCurseMany(curse.Prop("whitelist_any").StringList()),
            CurseBlacklist = NeowFilterText.NormalizeCurseMany(curse.Prop("blacklist").StringList()),
        };
    }

    private static Dictionary<string, TermList> SourceTermListsFromJson(JsonElement? e)
    {
        var result = new Dictionary<string, TermList>(StringComparer.OrdinalIgnoreCase);
        if (e is null || e.Value.ValueKind != JsonValueKind.Object) return result;
        foreach (var prop in e.Value.EnumerateObject())
        {
            var terms = TermList.FromJson(prop.Value);
            if (terms.HasTerms) result[prop.Name] = terms;
        }
        return result;
    }
}

public sealed class NeowFinalFilter
{
    public TermList Relics { get; init; } = new();
    public TermList Cards { get; init; } = new();
    public TermList Potions { get; init; } = new();
    public TermList NeowRelics { get; init; } = new();
    public List<string> CurseAny { get; init; } = new();
    public List<string> CurseBlacklist { get; init; } = new();
    public List<string> NeowRelicBlacklist { get; init; } = new();

    public bool HasFilter => Relics.HasTerms || Cards.HasTerms || Potions.HasTerms || NeowRelics.HasTerms || CurseAny.Count > 0 || CurseBlacklist.Count > 0 || NeowRelicBlacklist.Count > 0;

    public static NeowFinalFilter FromJson(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind != JsonValueKind.Object) return new NeowFinalFilter();
        var curse = e.Value.Prop("curses");
        return new NeowFinalFilter
        {
            Relics = TermList.FromJson(e.Value.Prop("relics")),
            Cards = TermList.FromJson(e.Value.Prop("cards")),
            Potions = TermList.FromJson(e.Value.Prop("potions")),
            NeowRelics = TermList.FromJson(e.Value.Prop("neow_relics")),
            CurseAny = NeowFilterText.NormalizeCurseMany(curse.Prop("whitelist_any").StringList()),
            CurseBlacklist = NeowFilterText.NormalizeCurseMany(curse.Prop("blacklist").StringList()),
            NeowRelicBlacklist = e.Value.Prop("neow_relic_blacklist").StringList().Select(Term.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}

internal static class NeowFilterText
{
    private static readonly Dictionary<string, string> CurseAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clumsy"] = "Clumsy", ["笨拙"] = "Clumsy",
        ["Debt"] = "Debt", ["债务"] = "Debt",
        ["Decay"] = "Decay", ["腐朽"] = "Decay",
        ["Doubt"] = "Doubt", ["疑虑"] = "Doubt",
        ["Guilty"] = "Guilty", ["愧疚"] = "Guilty",
        ["Greed"] = "Greed", ["贪婪"] = "Greed",
        ["Injury"] = "Injury", ["受伤"] = "Injury",
        ["Normality"] = "Normality", ["凡庸"] = "Normality",
        ["Regret"] = "Regret", ["悔恨"] = "Regret",
        ["Shame"] = "Shame", ["羞耻"] = "Shame",
        ["Writhe"] = "Writhe", ["苦恼"] = "Writhe",
    };

    public static List<string> NormalizeCurseMany(IEnumerable<string> rawTerms)
    {
        var outList = new List<string>();
        foreach (var raw in rawTerms ?? Enumerable.Empty<string>())
        {
            var term = Term.Normalize(raw);
            if (term.Length == 0) continue;
            if (CurseAlias.TryGetValue(term, out var mapped)) term = mapped;
            if (!outList.Contains(term, StringComparer.OrdinalIgnoreCase)) outList.Add(term);
        }
        return outList;
    }
}

public sealed class CardInst
{
    public string Id { get; set; } = "";
    public string Pool { get; set; } = "";
    public string? Source { get; set; }
    public string? SourceDetail { get; set; }
    public int AddedOrder { get; set; }
    public string? OriginalId { get; set; }
}


public sealed record EventQueueFilterTerm(int? ActNumber, int Limit, string EventTerm);

public sealed class SearchPlan
{
    public string Character { get; private set; } = "Silent";
    public ulong NetId { get; private set; } = 1;
    public int PlayersCount { get; private set; } = 1;
    public bool AllowKaleidoscope { get; private set; } = true;
    public long Start { get; private set; } = 0;
    public long End { get; private set; } = 1_000_000;
    public int MaxResults { get; private set; } = 5;
    public int PrintEvery { get; private set; } = 10000;
    public string Mode { get; private set; } = "sequential";
    public int RandomLength { get; private set; } = 10;
    public GameRngVersion RngVersion { get; private set; } = GameRngVersion.Sts2V107Xoshiro;

    public bool RequireBones { get; private set; }
    public TermList NeowTerms { get; private set; } = new();
    public TermList BonesRelicTerms { get; private set; } = new();
    public List<string> BonesCurseAny { get; private set; } = new();
    public List<string> BonesCurseBlacklist { get; private set; } = new();
    public TermList PotionTerms { get; private set; } = new();
    public TermList PredictedRelicTerms { get; private set; } = new();
    public string NeowFilterMode { get; private set; } = "legacy";
    public NeowRouteFilter NeowProcessFilter { get; private set; } = new();
    public NeowFinalFilter NeowFinalFilter { get; private set; } = new();
    public List<string> CardOpportunityCategories { get; private set; } = new();
    public Dictionary<string, CardOpportunityFilter> CardFilters { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> KaleidoscopeCharacterOrder { get; private set; } = new() { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent" };
    public List<int> KaleidoscopeChoiceIndices { get; private set; } = new() { 0, 0 };
    public int HeftyTabletChoice { get; private set; } = 0;
    public int LeadPaperweightChoice { get; private set; } = 0;
    public int LostCofferChoice { get; private set; } = 0;
    public int MassiveScrollChoice { get; private set; } = 0;
    public string NewLeafSelectedCard { get; private set; } = "starter_basic";

    public JsonElement Root { get; private set; }
    public string RootDir { get; private set; } = "";
    public GameData? Data { get; private set; }
    public string TargetNetIdText { get; private set; } = "1";
    public int TargetPlayerSlotIndex { get; private set; } = 0;
    public List<PlayerSpec> PlayersOrder { get; private set; } = new();
    public int ShopLimit { get; private set; } = 5;
    public bool ShopEnabled { get; private set; }
    public bool ShopShow { get; private set; }
    public List<ShopRelicTerm> ShopRequire { get; private set; } = new();
    public List<ShopRelicTerm> ShopBlacklist { get; private set; } = new();
    public List<ShopRelicTerm> ShopExact { get; private set; } = new();
    public bool AncientEnabled { get; private set; }
    public bool AncientShowOptions { get; private set; } = true;
    public bool EventQueueEnabled { get; private set; }
    public bool EventQueueShow { get; private set; }
    public int EventQueueLimit { get; private set; } = 10;
    public int EventQueueFilterLimit { get; private set; } = 10;
    public int EventQueueMaxRequiredAct { get; private set; } = 3;
    public bool EventQueueShowFull { get; private set; }
    public bool RelicQueueShow { get; private set; }
    public int RelicQueueLimit { get; private set; } = 15;
    public TermList EventQueueTerms { get; private set; } = new();
    public List<EventQueueFilterTerm> EventQueueAnyTerms { get; private set; } = new();
    public List<EventQueueFilterTerm> EventQueueAllTerms { get; private set; } = new();
    public List<EventQueueFilterTerm> EventQueueBlacklistTerms { get; private set; } = new();
    public List<AncientTerm> AncientRequire { get; private set; } = new();
    public List<AncientTerm> AncientBlacklist { get; private set; } = new();
    public List<AncientOptionTerm> AncientOptionRequire { get; private set; } = new();
    public List<AncientOptionTerm> AncientOptionBlacklist { get; private set; } = new();
    public AncientOptionConditions AncientConditions { get; private set; } = new();
    public UnlockProfile Unlocks { get; private set; } = UnlockProfile.AllUnlocked();

    public static SearchPlan FromConfig(JsonElement root, string baseDir)
    {
        var p = new SearchPlan { Root = root, RootDir = baseDir };
        var rngConfig = root.Prop("rng");
        p.RngVersion = GameRngVersions.Parse(rngConfig.Prop("version").Str(root.Prop("game_rng_version").Str(GameRngVersions.XoshiroConfig)));
        var player = root.Prop("player");
        p.Character = NormalizeCharacterId(player.Prop("character").Str("Silent") ?? "Silent");

        string runMode = root.Prop("run_mode").Str("").Trim().ToLowerInvariant();
        int playersCount = player.Prop("players_count").Int(1);
        bool multiplayer = runMode == "multiplayer" || (runMode != "singleplayer" && playersCount > 1);
        if (multiplayer)
        {
            p.PlayersCount = playersCount <= 1 ? 2 : playersCount;
            ulong.TryParse(player.Prop("net_id").Str("1") ?? "1", out var nid);
            p.NetId = nid == 0 ? 1 : nid;
        }
        else
        {
            p.PlayersCount = 1;
            p.NetId = 1;
        }
        p.TargetNetIdText = p.NetId.ToString();
        p.Unlocks = UnlockProfile.FromConfigEffective(root, baseDir);
        p.PlayersOrder = BuildPlayersOrder(root, p.Character, p.TargetNetIdText, multiplayer);
        if (multiplayer && p.PlayersOrder.Count > 1) p.PlayersCount = p.PlayersOrder.Count;
        p.TargetPlayerSlotIndex = DetermineTargetPlayerSlotIndex(p.PlayersOrder, p.TargetNetIdText);
        p.AllowKaleidoscope = root.Prop("allow_kaleidoscope").Bool(true) && p.Unlocks.AllowsKaleidoscope();

        var search = root.Prop("search");
        p.Start = search.Prop("start").Long(0);
        p.End = search.Prop("end").Long(1_000_000);
        p.MaxResults = search.Prop("max_results").Int(5);
        p.PrintEvery = search.Prop("print_every").Int(10000);
        var seedGen = root.Prop("seed_generation");
        p.Mode = seedGen.Prop("mode").Str("sequential") ?? "sequential";
        p.RandomLength = seedGen.Prop("length").Int(10);

        var filters = root.Prop("filters");
        p.RequireBones = filters.Prop("require_bones").Bool(false);
        p.NeowTerms = TermList.FromJson(filters.Prop("neow_options"));
        p.BonesRelicTerms = TermList.FromJson(filters.Prop("bones_relics"));
        var bc = filters.Prop("bones_curse");
        var bonesCurseAnyRaw = new List<string>();
        bonesCurseAnyRaw.AddRange(bc.Prop("whitelist_any").StringList());
        // v16.12：骨骰正常固定只给一张诅咒，whitelist_all 与 whitelist_any 等价。
        // 这里兼容旧前端 / 手写 raw_config，避免 Python 与 FastCore 字段理解不一致。
        bonesCurseAnyRaw.AddRange(bc.Prop("whitelist_all").StringList());
        bonesCurseAnyRaw.AddRange(bc.Prop("curse").StringList());
        bonesCurseAnyRaw.AddRange(bc.Prop("curses").StringList());
        p.BonesCurseAny = NormalizeCurseMany(bonesCurseAnyRaw).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.BonesCurseBlacklist = NormalizeCurseMany(bc.Prop("blacklist").StringList()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.PotionTerms = TermList.FromJson(filters.Prop("potions"));
        p.PredictedRelicTerms = TermList.FromJson(filters.Prop("predicted_relics"));
        p.NeowFilterMode = (filters.Prop("neow_mode").Str("legacy") ?? "legacy").Trim().ToLowerInvariant();
        var parsedProcessFilter = NeowRouteFilter.FromJson(filters.Prop("neow_process"));
        var parsedFinalFilter = NeowFinalFilter.FromJson(filters.Prop("neow_final"));
        if (p.NeowFilterMode == "process")
        {
            p.NeowProcessFilter = parsedProcessFilter;
            p.NeowFinalFilter = new NeowFinalFilter();
        }
        else if (p.NeowFilterMode == "final")
        {
            p.NeowProcessFilter = new NeowRouteFilter();
            p.NeowFinalFilter = parsedFinalFilter;
            // 互斥模式下，最终结果导向不应再被旧的过程导向字段限制。
            p.RequireBones = false;
            p.NeowTerms = new TermList();
            p.BonesRelicTerms = new TermList();
            p.BonesCurseAny = new List<string>();
            p.BonesCurseBlacklist = new List<string>();
            p.PredictedRelicTerms = new TermList();
        }
        else if (p.NeowFilterMode == "none")
        {
            p.NeowProcessFilter = new NeowRouteFilter();
            p.NeowFinalFilter = new NeowFinalFilter();
            p.RequireBones = false;
            p.NeowTerms = new TermList();
            p.BonesRelicTerms = new TermList();
            p.BonesCurseAny = new List<string>();
            p.BonesCurseBlacklist = new List<string>();
            p.PredictedRelicTerms = new TermList();
        }
        else
        {
            // 兼容旧 config.json / Web 端：没有 neow_mode 时仍维持原有行为。
            p.NeowProcessFilter = parsedProcessFilter;
            p.NeowFinalFilter = parsedFinalFilter;
        }
        p.CardFilters = CardOpportunityFilter.ParseAll(filters.Prop("card_opportunities"));
        p.CardOpportunityCategories = p.CardFilters.Where(kv => kv.Value.HasFilter).Select(kv => kv.Key).ToList();

        var kaleido = root.Prop("kaleidoscope");
        var kOrder = kaleido.Prop("character_order").StringList();
        if (kOrder.Count > 0) p.KaleidoscopeCharacterOrder = kOrder;
        p.KaleidoscopeCharacterOrder = p.Unlocks.FilterCharacters(p.KaleidoscopeCharacterOrder);
        if (p.KaleidoscopeCharacterOrder.Count == 0) p.KaleidoscopeCharacterOrder = new List<string> { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent" };
        var kChoices = kaleido.Prop("choice_indices");
        if (kChoices is not null && kChoices.Value.ValueKind == JsonValueKind.Array)
        {
            p.KaleidoscopeChoiceIndices = new List<int>();
            foreach (var item in kChoices.Value.EnumerateArray())
            {
                int n;
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out n)) p.KaleidoscopeChoiceIndices.Add(n);
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out n)) p.KaleidoscopeChoiceIndices.Add(n);
            }
            if (p.KaleidoscopeChoiceIndices.Count == 0) p.KaleidoscopeChoiceIndices = new List<int> { 0, 0 };
        }
        var choices = root.Prop("choices");
        p.HeftyTabletChoice = choices.Prop("HeftyTablet").Int(0);
        p.LeadPaperweightChoice = choices.Prop("LeadPaperweight").Int(0);
        p.LostCofferChoice = choices.Prop("LostCoffer").Int(0);
        p.MassiveScrollChoice = choices.Prop("MassiveScroll").Int(0);
        p.NewLeafSelectedCard = choices.Prop("NewLeafSelectedCard").Str("starter_basic");

        var shop = root.Prop("shop_relics");
        p.ShopLimit = Math.Max(0, shop.Prop("limit").Int(5));
        p.ShopEnabled = shop.Prop("enabled").Bool(false);
        p.ShopShow = p.ShopEnabled && shop.Prop("show").Bool(false);
        if (p.ShopEnabled)
        {
            p.ShopRequire = ShopRelicTerm.ParseRequire(shop);
            p.ShopBlacklist = ShopRelicTerm.ParseBlacklist(shop);
            p.ShopExact = ShopRelicTerm.ParseExact(shop);
        }
        var ancient = root.Prop("ancient");
        p.AncientEnabled = ancient is not null && ancient.Prop("enabled").Bool(false);
        p.AncientShowOptions = ancient is null || ancient.Prop("show_options").Bool(true);
        if (p.AncientEnabled)
        {
            p.AncientRequire = AncientTerm.ParseMany(ancient);
            p.AncientBlacklist = AncientTerm.ParseBlacklist(ancient);
            p.AncientOptionRequire = AncientOptionTerm.ParseMany(ancient);
            p.AncientOptionBlacklist = AncientOptionTerm.ParseBlacklist(ancient);
            p.AncientConditions = AncientOptionConditions.FromConfig(ancient);
        }

        var eventQueue = root.Prop("event_queue");
        p.EventQueueEnabled = eventQueue is not null && eventQueue.Prop("enabled").Bool(false);
        p.EventQueueShow = p.EventQueueEnabled && (eventQueue is null || eventQueue.Prop("show").Bool(true));
        p.EventQueueLimit = Math.Max(1, eventQueue.Prop("limit_per_act").Int(10));
        // 批量筛种事件队列筛选只支持“前 N 个”，N 上限固定为 15；单种分析显示数量仍由 EventQueueLimit 控制。
        p.EventQueueFilterLimit = Math.Min(15, Math.Max(1, eventQueue.Prop("filter_limit_per_act").Int(p.EventQueueLimit)));
        p.EventQueueTerms = TermList.FromJson(eventQueue.Prop("events"));
        p.EventQueueAnyTerms = ParseEventQueueFilterTerms(p.EventQueueTerms.Any, p.EventQueueFilterLimit);
        p.EventQueueAllTerms = ParseEventQueueFilterTerms(p.EventQueueTerms.All, p.EventQueueFilterLimit);
        p.EventQueueBlacklistTerms = ParseEventQueueFilterTerms(p.EventQueueTerms.Blacklist, p.EventQueueFilterLimit);
        p.EventQueueMaxRequiredAct = DetermineEventQueueMaxRequiredAct(p.EventQueueAnyTerms, p.EventQueueAllTerms, p.EventQueueBlacklistTerms);
        if (p.EventQueueTerms.HasTerms) p.EventQueueEnabled = true;

        var singleSeed = root.Prop("single_seed");
        p.EventQueueShowFull = singleSeed.Prop("show_full_event_queue").Bool(false) || eventQueue.Prop("show_full").Bool(false);
        p.RelicQueueShow = singleSeed.Prop("show_relic_sequence").Bool(false);
        p.RelicQueueLimit = Math.Max(1, singleSeed.Prop("relic_sequence_limit").Int(15));

        // v16.18：骨骰诅咒-only 快路径不再需要加载完整 GameData。
        // v16.17 把 NeedsBonesCurse 也加入这里，导致只筛一张诅咒时每个 seed 的
        // Neow/骨骰池过滤都会触发数据相关路径，速度明显下降。
        // 卡牌/药水/随机遗物/商店/先古仍按需加载完整数据。
        if (p.NeedsShop || p.RelicQueueShow || p.NeedsAncientIdentity || p.NeedsAncientOptions || p.NeedsPotions || p.NeedsCardOpportunities || p.NeedsPredictedRelics || p.NeedsNeowAdvanced || p.EventQueueEnabled)
        {
            string dataFile = root.Prop("data_file").Str("data/legacy/sts2_runtime_legacy_v2.json");
            string dataPath = Path.IsPathRooted(dataFile) ? dataFile : Path.Combine(baseDir, dataFile);
            p.Data = GameData.Load(dataPath);
        }
        return p;
    }

    public static SearchPlan FromConfigForFullDetails(JsonElement root, string baseDir)
    {
        var p = FromConfig(root, baseDir);
        if (p.Data is null)
        {
            string dataFile = root.Prop("data_file").Str("data/legacy/sts2_runtime_legacy_v2.json");
            string dataPath = Path.IsPathRooted(dataFile) ? dataFile : Path.Combine(baseDir, dataFile);
            p.Data = GameData.Load(dataPath);
        }
        return p;
    }

    private static string NormalizeCharacterId(string? raw)
    {
        string x = (raw ?? "").Trim();
        return x.ToUpperInvariant() switch
        {
            "IRONCLAD" => "Ironclad",
            "SILENT" => "Silent",
            "DEFECT" => "Defect",
            "NECROBINDER" => "Necrobinder",
            "REGENT" => "Regent",
            _ => string.IsNullOrWhiteSpace(x) ? "Silent" : x,
        };
    }

    private static List<PlayerSpec> BuildPlayersOrder(JsonElement root, string targetCharacter, string targetNetId, bool multiplayer)
    {
        if (!multiplayer)
            return new List<PlayerSpec> { new("Singleplayer", targetCharacter, "1") };

        var raw = root.Prop("players");
        var players = new List<PlayerSpec>();
        if (raw is not null && raw.Value.ValueKind == JsonValueKind.Array && raw.Value.GetArrayLength() > 0)
        {
            int i = 0;
            foreach (var item in raw.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                i++;
                players.Add(new PlayerSpec(
                    item.Prop("name").Str("P" + i),
                    NormalizeCharacterId(item.Prop("character").Str(targetCharacter)),
                    item.Prop("net_id").Str(i.ToString())
                ));
            }
        }
        if (players.Count == 0) players.Add(new PlayerSpec("Target", targetCharacter, targetNetId));
        return players;
    }

    private static int DetermineTargetPlayerSlotIndex(IList<PlayerSpec> players, string targetNetId)
    {
        if (players.Count <= 1) return 0;
        int index = players.ToList().FindIndex(p => p.NetId.Equals(targetNetId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 0;
    }

    public bool NeedsBonesRelics => BonesRelicTerms.HasTerms || BonesCurseAny.Count > 0 || BonesCurseBlacklist.Count > 0;
    public bool NeedsBonesCurse => BonesCurseAny.Count > 0 || BonesCurseBlacklist.Count > 0;
    public bool NeedsPotions => PotionTerms.HasTerms;
    public bool NeedsPredictedRelics => PredictedRelicTerms.HasTerms;
    public bool NeedsNeowAdvanced => NeowProcessFilter.HasFilter || NeowFinalFilter.HasFilter;
    public bool NeedsCardOpportunities => CardOpportunityCategories.Count > 0;
    public bool NeedsAdvancedNewLeafRoute => !string.IsNullOrWhiteSpace(NewLeafSelectedCard)
        && !NewLeafSelectedCard.Equals("starter_basic", StringComparison.OrdinalIgnoreCase);
    public bool NeedsShop => ShopEnabled || ShopShow || ShopRequire.Count > 0 || ShopBlacklist.Count > 0 || ShopExact.Count > 0;
    public bool NeedsAncientIdentity => AncientRequire.Count > 0 || AncientBlacklist.Count > 0;
    public bool NeedsAncientOptions => AncientOptionRequire.Count > 0 || AncientOptionBlacklist.Count > 0;
    public bool NeedsEventQueueFilter => EventQueueTerms.HasTerms;
    public bool HasCoarseFilters => false;
    public List<string> CoarseReasons
    {
        get
        {
            var xs = new List<string>();
            if (NeedsPotions) xs.Add("potions:exact-route");
            if (NeedsCardOpportunities) xs.Add("cards:exact-route");
            if (NeedsPredictedRelics) xs.Add("predicted_relics:exact-route");
            if (NeedsNeowAdvanced) xs.Add("neow_advanced:route-aware");
            if (NeedsAdvancedNewLeafRoute) xs.Add("advanced_newleaf:route-aware");
            return xs;
        }
    }

    private static readonly Dictionary<string, string> BonesCurseAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clumsy"] = "Clumsy", ["笨拙"] = "Clumsy",
        ["Debt"] = "Debt", ["债务"] = "Debt",
        ["Decay"] = "Decay", ["腐朽"] = "Decay",
        ["Doubt"] = "Doubt", ["疑虑"] = "Doubt",
        ["Guilty"] = "Guilty", ["愧疚"] = "Guilty",
        ["Greed"] = "Greed", ["贪婪"] = "Greed",
        ["Injury"] = "Injury", ["受伤"] = "Injury",
        ["Normality"] = "Normality", ["凡庸"] = "Normality",
        ["Regret"] = "Regret", ["悔恨"] = "Regret",
        ["Shame"] = "Shame", ["羞耻"] = "Shame",
        ["Writhe"] = "Writhe", ["苦恼"] = "Writhe",
    };

    private static List<string> NormalizeCurseMany(IEnumerable<string> rawTerms)
    {
        var outList = new List<string>();
        foreach (var raw in rawTerms ?? Enumerable.Empty<string>())
        {
            var term = Term.Normalize(raw);
            if (term.Length == 0) continue;
            // 支持纯中文输入，也支持“债务（Debt）”这种显示文本被 Term.Normalize 拆成 Debt。
            if (BonesCurseAlias.TryGetValue(term, out var mapped)) term = mapped;
            if (!outList.Contains(term, StringComparer.OrdinalIgnoreCase)) outList.Add(term);
        }
        return outList;
    }

    public bool IsSupportedByFastCore(out string reason)
    {
        var f = Root.Prop("filters");
        string[] unsupportedFilterKeys =
        {
            "transformed_cards", "kaleidoscope_cards", "effects"
        };
        foreach (var key in unsupportedFilterKeys)
        {
            if (f.Prop(key).HasAnyTerms() == true)
            {
                reason = key + " 暂不走 FastCore v16.9";
                return false;
            }
        }


        var ancient = Root.Prop("ancient");
        if (ancient is not null && ancient.Prop("enabled").Bool(false) == true)
        {
            var scope = ancient.Prop("option_scope").Str("target").Trim().ToLowerInvariant();
            if (ancient.Prop("require_options").StringList().Count > 0 && scope != "" && scope != "target")
            {
                reason = "FastCore v16.9 只支持目标玩家先古选项筛选";
                return false;
            }
            // require_ancients / require_options 已支持。
        }

        reason = "";
        return true;
    }

    private static List<string> ParseCardOpportunityCategories(JsonElement? cardOpp)
    {
        var outList = new List<string>();
        if (cardOpp is null || cardOpp.Value.ValueKind != JsonValueKind.Object) return outList;
        foreach (var cat in new[] { "own", "colorless", "other" })
        {
            var cfg = cardOpp.Value.Prop(cat);
            if (cfg is not null && CardOpportunityEnabled(cfg.Value)) outList.Add(cat);
        }
        return outList;
    }

    private static bool CardOpportunityEnabled(JsonElement cfg)
    {
        if (cfg.ValueKind != JsonValueKind.Object) return false;
        if (!cfg.Prop("enabled").Bool(false)) return false;
        if (cfg.Prop("whitelist_any").StringList().Count > 0) return true;
        if (cfg.Prop("whitelist_all").StringList().Count > 0) return true;
        if (cfg.Prop("blacklist").StringList().Count > 0) return true;
        if (cfg.Prop("rarities").StringList().Count > 0) return true;
        // v16.22: min_count removed; duplicate whitelist_all entries express counts.
        return false;
    }

    private static bool ShopFilterHasTerms(JsonElement? shop)
    {
        if (shop is null || shop.Value.ValueKind != JsonValueKind.Object) return false;
        return NonEmptyArray(shop.Value.Prop("require_relic_filters"))
            || NonEmptyArray(shop.Value.Prop("require_relics"))
            || NonEmptyArray(shop.Value.Prop("blacklist"))
            || NonEmptyArray(shop.Value.Prop("exact_relic_filters"))
            || NonEmptyArray(shop.Value.Prop("exact_relics"));
    }

    private static bool NonEmptyArray(JsonElement? e)
    {
        return e is not null && e.Value.ValueKind == JsonValueKind.Array && e.Value.GetArrayLength() > 0;
    }

    public bool Matches(OpeningResult result)
    {
        if (RequireBones && !result.HasBones) return false;
        if (!NeowTerms.Match(result.NeowOptions)) return false;
        if (BonesRelicTerms.HasTerms)
        {
            if (!result.HasBones) return false;
            if (!BonesRelicTerms.Match(result.BonesRelics)) return false;
        }
        if (NeedsBonesCurse)
        {
            if (!result.HasBones || result.BonesCurses.Count == 0) return false;
            if (BonesCurseAny.Count > 0 && !BonesCurseAny.Any(t => result.BonesCurses.Any(c => Term.ItemMatches(c, t)))) return false;
            if (BonesCurseBlacklist.Count > 0 && result.BonesCurses.Any(c => BonesCurseBlacklist.Any(t => Term.ItemMatches(c, t)))) return false;
        }
        if (NeedsPotions)
        {
            if (!result.OpeningRoutes.Any(route => RouteAllowedByChoiceContext(route, result) && route.Potions.Count > 0 && PotionTerms.Match(route.Potions))) return false;
        }
        if (NeedsCardOpportunities)
        {
            if (!result.OpeningRoutes.Any(route => RouteAllowedByChoiceContext(route, result) && RouteMatchesAllCardFilters(route))) return false;
        }
        if (NeedsPredictedRelics)
        {
            if (!result.OpeningRoutes.Any(route => RouteAllowedByChoiceContext(route, result) && route.PredictedRelics.Count > 0 && PredictedRelicTerms.Match(route.PredictedRelics))) return false;
        }
        if (NeowProcessFilter.HasFilter && !NeowProcessFilterSatisfied(result)) return false;
        if (NeowFinalFilter.HasFilter && !NeowFinalFilterSatisfied(result)) return false;
        if (NeedsShop)
        {
            if (result.ShopRelics.Count == 0) return false;
            foreach (var exact in ShopExact)
                if (!exact.IsSatisfiedBy(result.ShopRelics, ShopLimit)) return false;
            foreach (var req in ShopRequire)
                if (!req.IsSatisfiedBy(result.ShopRelics, ShopLimit)) return false;
            foreach (var ban in ShopBlacklist)
                if (ban.IsSatisfiedBy(result.ShopRelics, ShopLimit)) return false;
        }
        if (NeedsAncientIdentity)
        {
            if (result.Ancients.Count == 0) return false;
            foreach (var req in AncientRequire)
                if (!req.IsSatisfiedBy(result.Ancients)) return false;
            foreach (var ban in AncientBlacklist)
                if (ban.IsSatisfiedBy(result.Ancients)) return false;
        }
        if (NeedsAncientOptions)
        {
            if (result.AncientOptions.Count == 0) return false;
            foreach (var req in AncientOptionRequire)
                if (!req.IsSatisfiedBy(result.AncientOptions)) return false;
            foreach (var ban in AncientOptionBlacklist)
                if (ban.IsSatisfiedBy(result.AncientOptions)) return false;
        }
        if (NeedsEventQueueFilter)
        {
            if (result.EventQueues.Count == 0) return false;
            if (!EventQueueTermsSatisfied(result.EventQueues)) return false;
        }
        return true;
    }


    private bool NeowProcessFilterSatisfied(OpeningResult result)
    {
        return result.OpeningRoutes.Any(route => RouteMatchesNeowProcess(route, result));
    }

    private bool RouteMatchesNeowProcess(OpeningRoute route, OpeningResult result)
    {
        bool isBones = string.Equals(route.Kind, "bones", StringComparison.OrdinalIgnoreCase);
        var mode = NeowProcessFilter.RouteMode;
        if ((mode == "bones" || mode == "bone") && !isBones) return false;
        if ((mode == "direct" || mode == "relic") && isBones) return false;

        var initial = RouteInitialRelics(route, includeNeowsBonesMarker: true);
        if (NeowProcessFilter.InitialRelics.HasTerms && !NeowProcessFilter.InitialRelics.Match(initial)) return false;
        if (NeowProcessFilter.BonesRelics.HasTerms)
        {
            if (!isBones) return false;
            if (!NeowProcessFilter.BonesRelics.Match(route.PickOrder)) return false;
        }
        if (NeowProcessFilter.GeneratedRelics.HasTerms && !NeowProcessFilter.GeneratedRelics.Match(route.PredictedRelics)) return false;
        if (NeowProcessFilter.Potions.HasTerms && !NeowProcessFilter.Potions.Match(route.Potions)) return false;
        if (NeowProcessFilter.Cards.HasTerms && !RouteCardTermsSatisfied(route, NeowProcessFilter.Cards)) return false;
        foreach (var kv in NeowProcessFilter.SourceCardFilters)
            if (!SourceCardFilterSatisfied(route, kv.Key, kv.Value)) return false;
        foreach (var kv in NeowProcessFilter.SourcePotionFilters)
            if (!SourcePotionFilterSatisfied(route, kv.Key, kv.Value)) return false;
        foreach (var kv in NeowProcessFilter.SourceRelicFilters)
            if (!SourceRelicFilterSatisfied(route, kv.Key, kv.Value)) return false;
        if (NeowProcessFilter.CurseAny.Count > 0 || NeowProcessFilter.CurseBlacklist.Count > 0)
        {
            var curses = RouteCurses(route);
            if (NeowProcessFilter.CurseAny.Count > 0 && !NeowProcessFilter.CurseAny.Any(t => curses.Any(c => Term.ItemMatches(c, t)))) return false;
            if (NeowProcessFilter.CurseBlacklist.Count > 0 && curses.Any(c => NeowProcessFilter.CurseBlacklist.Any(t => Term.ItemMatches(c, t)))) return false;
        }
        return true;
    }

    private bool NeowFinalFilterSatisfied(OpeningResult result)
    {
        return result.OpeningRoutes.Any(RouteMatchesNeowFinal);
    }

    private bool RouteMatchesNeowFinal(OpeningRoute route)
    {
        var neowRelics = RouteInitialRelics(route, includeNeowsBonesMarker: true);
        if (NeowFinalFilter.NeowRelics.HasTerms && !NeowFinalFilter.NeowRelics.Match(neowRelics)) return false;
        if (NeowFinalFilter.NeowRelicBlacklist.Count > 0)
        {
            if (neowRelics.Any(r => NeowFinalFilter.NeowRelicBlacklist.Any(t => Term.ItemMatches(r, t)))) return false;
        }
        var relics = RouteFinalOrdinaryRelics(route);
        if (NeowFinalFilter.Relics.HasTerms && !NeowFinalFilter.Relics.Match(relics)) return false;
        if (NeowFinalFilter.Cards.HasTerms && !RouteCardTermsSatisfied(route, NeowFinalFilter.Cards)) return false;
        if (NeowFinalFilter.Potions.HasTerms && !NeowFinalFilter.Potions.Match(route.Potions)) return false;
        if (NeowFinalFilter.CurseAny.Count > 0 || NeowFinalFilter.CurseBlacklist.Count > 0)
        {
            var curses = RouteCurses(route);
            if (NeowFinalFilter.CurseAny.Count > 0 && !NeowFinalFilter.CurseAny.Any(t => curses.Any(c => Term.ItemMatches(c, t)))) return false;
            if (NeowFinalFilter.CurseBlacklist.Count > 0 && curses.Any(c => NeowFinalFilter.CurseBlacklist.Any(t => Term.ItemMatches(c, t)))) return false;
        }
        return true;
    }

    private static List<string> RouteInitialRelics(OpeningRoute route, bool includeNeowsBonesMarker)
    {
        var xs = new List<string>();
        bool isBones = string.Equals(route.Kind, "bones", StringComparison.OrdinalIgnoreCase);
        if (isBones)
        {
            if (includeNeowsBonesMarker) xs.Add("NeowsBones");
            xs.AddRange(route.PickOrder);
        }
        else if (!string.IsNullOrWhiteSpace(route.DirectRelic))
        {
            xs.Add(route.DirectRelic!);
        }
        return xs;
    }

    private static List<string> RouteFinalOrdinaryRelics(OpeningRoute route)
    {
        // 最终结果导向中的“遗物”只指 SmallCapsule / LargeCapsule 等生成的普通随机遗物；
        // Neow 遗物本身由 NeowRelicBlacklist 单独处理，避免把“想要巨大扭蛋”和“想要巨大扭蛋给出的遗物”混在一起。
        return route.PredictedRelics.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> RouteCurses(OpeningRoute route)
    {
        var curses = new List<string>();
        if (!string.IsNullOrWhiteSpace(route.BonesCurse)) curses.Add(route.BonesCurse!);
        var neowRelics = RouteInitialRelics(route, includeNeowsBonesMarker: true);
        if (neowRelics.Any(r => r.Equals("CursedPearl", StringComparison.OrdinalIgnoreCase))) curses.Add("Greed");
        if (neowRelics.Any(r => r.Equals("HeftyTablet", StringComparison.OrdinalIgnoreCase))) curses.Add("Injury");
        return curses.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> RouteAllCards(OpeningRoute route)
    {
        var xs = new List<string>();
        foreach (var ev in route.CardOpportunities)
            xs.AddRange(CardEventAllCards(ev));
        return xs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static bool RouteCardTermsSatisfied(OpeningRoute route, TermList terms)
    {
        if (!terms.HasTerms) return true;
        var events = route.CardOpportunities;
        var allPossibleCards = new List<string>();
        foreach (var ev in events) allPossibleCards.AddRange(CardEventAllCards(ev));
        if (allPossibleCards.Count == 0) return false;
        if (terms.Blacklist.Count > 0 && terms.Blacklist.Any(t => allPossibleCards.Any(c => Term.ItemMatches(c, t)))) return false;

        var optionGroups = new List<List<List<string>>>();
        foreach (var ev in events)
        {
            var opts = CardEventChoiceOptions(ev)
                .Select(opt => opt.Where(c => !string.IsNullOrEmpty(c)).ToList())
                .Where(opt => opt.Count > 0)
                .ToList();
            if (opts.Count > 0) optionGroups.Add(opts);
        }
        if (optionGroups.Count == 0) return false;

        bool SelectedSatisfies(List<string> selected)
        {
            if (terms.Any.Count > 0 && !terms.Any.Any(t => selected.Any(c => Term.ItemMatches(c, t)))) return false;
            foreach (var group in terms.All.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int actual = selected.Count(c => Term.ItemMatches(c, group.Key));
                if (actual < group.Count()) return false;
            }
            if (terms.Any.Count == 0 && terms.All.Count == 0) return selected.Count > 0;
            return true;
        }

        bool Dfs(int i, List<string> selected)
        {
            if (i >= optionGroups.Count) return SelectedSatisfies(selected);
            foreach (var opt in optionGroups[i])
            {
                var next = new List<string>(selected);
                next.AddRange(opt);
                if (Dfs(i + 1, next)) return true;
            }
            return false;
        }

        return Dfs(0, new List<string>());
    }

    private static bool SourceCardFilterSatisfied(OpeningRoute route, string sourceKey, TermList terms)
    {
        var events = CardEventsForSourceKey(route, sourceKey);
        if (!terms.HasTerms) return true;

        // v17.9-preview11: source_cards matching must respect reward choice groups.
        // Kaleidoscope has two choice groups and each group contributes at most one card;
        // ScrollBoxes is a bundle_choice where exactly one bundle can be taken.
        // A plain union would incorrectly match cards that cannot be obtained together.
        var allPossibleCards = new List<string>();
        foreach (var ev in events) allPossibleCards.AddRange(CardEventAllCards(ev));
        if (allPossibleCards.Count == 0) return false;
        if (terms.Blacklist.Count > 0 && terms.Blacklist.Any(t => allPossibleCards.Any(c => Term.ItemMatches(c, t)))) return false;

        var optionGroups = new List<List<List<string>>>();
        foreach (var ev in events)
        {
            var opts = CardEventChoiceOptions(ev)
                .Select(opt => opt.Where(c => !string.IsNullOrEmpty(c)).ToList())
                .Where(opt => opt.Count > 0)
                .ToList();
            if (opts.Count > 0) optionGroups.Add(opts);
        }
        if (optionGroups.Count == 0) return false;

        bool SelectedSatisfies(List<string> selected)
        {
            if (terms.Any.Count > 0 && !terms.Any.Any(t => selected.Any(c => Term.ItemMatches(c, t)))) return false;
            foreach (var group in terms.All.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int actual = selected.Count(c => Term.ItemMatches(c, group.Key));
                if (actual < group.Count()) return false;
            }
            if (terms.Any.Count == 0 && terms.All.Count == 0) return selected.Count > 0;
            return true;
        }

        bool Dfs(int i, List<string> selected)
        {
            if (i >= optionGroups.Count) return SelectedSatisfies(selected);
            foreach (var opt in optionGroups[i])
            {
                var next = new List<string>(selected);
                next.AddRange(opt);
                if (Dfs(i + 1, next)) return true;
            }
            return false;
        }

        return Dfs(0, new List<string>());
    }

    private static bool SourceRelicFilterSatisfied(OpeningRoute route, string sourceKey, TermList terms)
    {
        string source = NormalizeRelicSourceKey(sourceKey);
        var relics = route.RelicOpportunities
            .Where(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Relics)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        // 兼容旧 route：如果没有 source_relics 细分记录，但旧字段已有总随机遗物，
        // generated_relics / PredictedRelics 仍保持原语义。
        if (relics.Count == 0 && string.Equals(source, "generated_relics", StringComparison.OrdinalIgnoreCase))
            relics = route.PredictedRelics.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (terms.HasTerms && relics.Count == 0) return false;
        return terms.Match(relics);
    }

    private static bool SourcePotionFilterSatisfied(OpeningRoute route, string sourceKey, TermList terms)
    {
        string source = NormalizeSourceKey(sourceKey, out _);
        var potions = route.PotionOpportunities
            .Where(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Potions)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        // 兼容旧路线对象：如果没有源记录且只要求 LostCoffer，就退回到路线总药水。
        if (potions.Count == 0 && string.Equals(source, "LostCoffer", StringComparison.OrdinalIgnoreCase))
            potions = route.Potions.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (terms.HasTerms && potions.Count == 0) return false;
        return terms.Match(potions);
    }

    private static List<CardEvent> CardEventsForSourceKey(OpeningRoute route, string sourceKey)
    {
        string source = NormalizeSourceKey(sourceKey, out int? groupIndex);
        var events = route.CardOpportunities
            .Where(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (groupIndex is int idx)
        {
            if (events.Count == 1 && events[0].Type == "bundle_choice")
            {
                var ev = events[0];
                if (idx < 0 || idx >= ev.Options.Count) return new List<CardEvent>();
                return new List<CardEvent>
                {
                    new CardEvent
                    {
                        Source = ev.Source,
                        Category = ev.Category,
                        Type = ev.Type,
                        Method = ev.Method,
                        Options = new List<List<string>> { ev.Options[idx] },
                        Rarities = ev.Rarities,
                    }
                };
            }
            if (idx < 0 || idx >= events.Count) return new List<CardEvent>();
            return new List<CardEvent> { events[idx] };
        }
        return events;
    }

    private static string NormalizeRelicSourceKey(string key)
    {
        key = Term.Normalize(key ?? "");
        if (key.Equals("SmallCapsuleRandomRelic", StringComparison.OrdinalIgnoreCase)) return "SmallCapsule";
        if (key.Equals("LargeCapsuleRandomRelics", StringComparison.OrdinalIgnoreCase)) return "LargeCapsule";
        if (key.Equals("LavaRock", StringComparison.OrdinalIgnoreCase)) return "LavaRockAct1Boss";
        if (key.Equals("LavaRockAct1", StringComparison.OrdinalIgnoreCase)) return "LavaRockAct1Boss";
        if (key.Equals("LavaRockAct1BossRelics", StringComparison.OrdinalIgnoreCase)) return "LavaRockAct1Boss";
        return key;
    }

    private static string NormalizeSourceKey(string key, out int? groupIndex)
    {
        groupIndex = null;
        key = Term.Normalize(key ?? "");
        if (key.Equals("KaleidoscopeGroup1", StringComparison.OrdinalIgnoreCase) || key.Equals("Kaleidoscope1", StringComparison.OrdinalIgnoreCase))
        {
            groupIndex = 0;
            return "Kaleidoscope";
        }
        if (key.Equals("KaleidoscopeGroup2", StringComparison.OrdinalIgnoreCase) || key.Equals("Kaleidoscope2", StringComparison.OrdinalIgnoreCase))
        {
            groupIndex = 1;
            return "Kaleidoscope";
        }
        if (key.Equals("ScrollBoxesBundle1", StringComparison.OrdinalIgnoreCase) || key.Equals("ScrollBoxes1", StringComparison.OrdinalIgnoreCase))
        {
            groupIndex = 0;
            return "ScrollBoxes";
        }
        if (key.Equals("ScrollBoxesBundle2", StringComparison.OrdinalIgnoreCase) || key.Equals("ScrollBoxes2", StringComparison.OrdinalIgnoreCase))
        {
            groupIndex = 1;
            return "ScrollBoxes";
        }
        return key;
    }

    private bool EventQueueTermsSatisfied(IList<EventQueueBlock> queues)
    {
        if (queues.Count == 0) return false;

        // v1.1.0-preview3：批量筛种热路径优化。
        // 旧实现会对每个事件筛选 term 反复解析 Regex、旋转队列、过滤起始偏移/Act 跳过，
        // 并重复做事件别名 Normalize。现在 SearchPlan 在加载 config 时预解析 term，
        // 每个 seed 的 Matches() 里只为每个 Act 构造一次默认可出现队列。
        var readOrderCache = new Dictionary<int, List<string>>();

        List<string> ReadOrderFor(EventQueueBlock q)
        {
            if (readOrderCache.TryGetValue(q.ActNumber, out var cached)) return cached;

            var order = AncientPredictor.EventQueueReadOrder(q);
            List<string> filtered;
            if (EventQueueShowFull)
            {
                filtered = order;
            }
            else
            {
                filtered = new List<string>(order.Count);
                int startSkippedCount = q.StartOffset <= 0 || q.Events.Count == 0 ? 0 : Math.Min(q.StartOffset, q.Events.Count);
                for (int i = 0; i < order.Count; i++)
                {
                    string ev = order[i];
                    // 起始偏移跳过项只可能来自原始队列前 StartOffset 个。
                    bool skippedByStartOffset = false;
                    for (int j = 0; j < startSkippedCount; j++)
                    {
                        if (string.Equals(q.Events[j], ev, StringComparison.OrdinalIgnoreCase))
                        {
                            skippedByStartOffset = true;
                            break;
                        }
                    }
                    if (skippedByStartOffset) continue;
                    if (!string.IsNullOrWhiteSpace(Data?.EventActSkipNote(ev, q.ActNumber))) continue;
                    filtered.Add(ev);
                }
            }

            readOrderCache[q.ActNumber] = filtered;
            return filtered;
        }

        bool EventMatches(string eventId, string term)
        {
            // 下拉框写入的是运行时 ID，绝大多数情况下 exact/base ID 匹配即可；
            // 只有旧配置/手输中文名/别名才落到 GameData.EventMatchesTerm。
            if (Term.ItemMatches(eventId, term)) return true;
            return Data?.EventMatchesTerm(eventId, term) == true;
        }

        bool Contains(EventQueueFilterTerm parsed)
        {
            foreach (var q in queues)
            {
                if (parsed.ActNumber is int act && act != q.ActNumber) continue;
                var readOrder = ReadOrderFor(q);
                int take = Math.Min(parsed.Limit, readOrder.Count);
                for (int i = 0; i < take; i++)
                    if (EventMatches(readOrder[i], parsed.EventTerm)) return true;
            }
            return false;
        }

        if (EventQueueBlacklistTerms.Count > 0 && EventQueueBlacklistTerms.Any(Contains)) return false;
        if (EventQueueAnyTerms.Count > 0 && !EventQueueAnyTerms.Any(Contains)) return false;
        foreach (var t in EventQueueAllTerms)
            if (!Contains(t)) return false;
        return true;
    }

    private IEnumerable<string> EventQueueFilterReadOrder(EventQueueBlock q)
    {
        var order = AncientPredictor.EventQueueReadOrder(q);
        if (EventQueueShowFull) return order;
        return order.Where(ev =>
            string.IsNullOrWhiteSpace(Data?.EventActSkipNote(ev, q.ActNumber))
            && !IsEventSkippedByStartOffset(ev, q));
    }

    private static bool IsEventSkippedByStartOffset(string eventId, EventQueueBlock q)
    {
        if (q.StartOffset <= 0 || q.Events.Count == 0) return false;
        int skippedCount = Math.Min(q.StartOffset, q.Events.Count);
        for (int i = 0; i < skippedCount; i++)
        {
            if (string.Equals(q.Events[i], eventId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static List<EventQueueFilterTerm> ParseEventQueueFilterTerms(IEnumerable<string> terms, int defaultLimit)
    {
        var result = new List<EventQueueFilterTerm>();
        foreach (var raw in terms ?? Enumerable.Empty<string>())
        {
            var parsed = ParseEventQueueTerm(raw);
            string ev = Term.Normalize(parsed.EventTerm ?? "").Trim();
            if (ev.Length == 0) continue;
            int limit = Math.Min(15, Math.Max(1, parsed.Limit ?? defaultLimit));
            result.Add(new EventQueueFilterTerm(parsed.ActNumber, limit, ev));
        }
        return result;
    }

    private static int DetermineEventQueueMaxRequiredAct(params IEnumerable<EventQueueFilterTerm>[] groups)
    {
        int max = 1;
        bool any = false;
        foreach (var g in groups)
        {
            foreach (var t in g)
            {
                any = true;
                if (t.ActNumber is not int act) return 3;
                if (act > max) max = act;
            }
        }
        return any ? Math.Min(3, Math.Max(1, max)) : 3;
    }

    private static (int? ActNumber, int? Limit, string EventTerm) ParseEventQueueTerm(string raw)
    {
        raw = Term.Normalize(raw ?? "").Trim();
        var mActLimit = Regex.Match(raw, @"^\s*(?:act)?\s*([1-3])\s*(?:<=|#|/n|n)\s*(\d{1,2})\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mActLimit.Success && int.TryParse(mActLimit.Groups[1].Value, out int act1) && int.TryParse(mActLimit.Groups[2].Value, out int lim1))
            return (act1, Math.Min(15, Math.Max(1, lim1)), mActLimit.Groups[3].Value.Trim());

        var mAct = Regex.Match(raw, @"^\s*(?:act)?\s*([1-3])\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mAct.Success && int.TryParse(mAct.Groups[1].Value, out int act2))
            return (act2, null, mAct.Groups[2].Value.Trim());

        var mLimit = Regex.Match(raw, @"^\s*n\s*(\d{1,2})\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mLimit.Success && int.TryParse(mLimit.Groups[1].Value, out int lim2))
            return (null, Math.Min(15, Math.Max(1, lim2)), mLimit.Groups[2].Value.Trim());

        return (null, null, raw.Trim());
    }

    private bool RouteAllowedByChoiceContext(OpeningRoute route, OpeningResult result)
    {
        bool isBones = string.Equals(route.Kind, "bones", StringComparison.OrdinalIgnoreCase);

        if (RequireBones && !isBones) return false;

        if (NeowTerms.HasTerms)
        {
            var items = isBones
                ? new List<string> { "NeowsBones" }
                : new List<string> { route.DirectRelic ?? "" };
            if (!NeowTerms.Match(items)) return false;
        }

        if (BonesRelicTerms.HasTerms)
        {
            if (!isBones) return false;
            var items = route.PickOrder.Count > 0 ? route.PickOrder : result.BonesRelics;
            if (!BonesRelicTerms.Match(items)) return false;
        }

        if (NeedsBonesCurse)
        {
            if (!isBones || string.IsNullOrWhiteSpace(route.BonesCurse)) return false;
            var curses = new List<string> { route.BonesCurse! };
            if (BonesCurseAny.Count > 0 && !BonesCurseAny.Any(t => curses.Any(c => Term.ItemMatches(c, t)))) return false;
            if (BonesCurseBlacklist.Count > 0 && curses.Any(c => BonesCurseBlacklist.Any(t => Term.ItemMatches(c, t)))) return false;
        }

        return true;
    }

    private bool RouteMatchesAllCardFilters(OpeningRoute route)
    {
        foreach (var kv in CardFilters)
        {
            if (!kv.Value.HasFilter) continue;
            if (!RouteCardCategorySatisfies(route.CardOpportunities, kv.Key, kv.Value)) return false;
        }
        return true;
    }

    private static bool RouteCardCategorySatisfies(IList<CardEvent> events, string category, CardOpportunityFilter filter)
    {
        if (!filter.HasFilter) return true;
        var relevant = events.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
        var rarityMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in relevant)
            foreach (var kv in ev.Rarities)
                rarityMap[kv.Key] = kv.Value;

        // 黑名单仍按“该路线里出现过这个候选”理解。
        var allPossibleCards = new List<string>();
        foreach (var ev in relevant) allPossibleCards.AddRange(CardEventAllCards(ev));
        if (filter.Terms.Blacklist.Count > 0 && filter.Terms.Blacklist.Any(t => allPossibleCards.Any(c => Term.ItemMatches(c, t)))) return false;

        var optionGroups = new List<List<List<string>>>();
        foreach (var ev in relevant)
        {
            var opts = CardEventChoiceOptions(ev)
                .Select(opt => opt.Where(c => !string.IsNullOrEmpty(c)).ToList())
                .Where(opt => opt.Count > 0)
                .ToList();
            if (opts.Count > 0) optionGroups.Add(opts);
        }
        if (optionGroups.Count == 0) return false;

        bool SelectedSatisfies(List<string> selectedRaw)
        {
            // selectedRaw 表示同一条开局路线下，经过每个选择点后“实际能拿到”的卡。
            // v16.23：重复 whitelist_all 保留计数，但只能由实际可同时获得的牌满足。
            // 例如 LostCoffer 三选一里出现 3 张 Bigbang，也只能选 1 张，不会满足 Bigbang x3。
            var selected = selectedRaw.Where(c => CardOkForRarity(c, filter.Rarities, rarityMap)).ToList();

            if (filter.Terms.Any.Count > 0 && !filter.Terms.Any.Any(t => selected.Any(c => Term.ItemMatches(c, t)))) return false;

            if (filter.Terms.All.Count > 0)
            {
                foreach (var group in filter.Terms.All.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    int actual = selected.Count(c => Term.ItemMatches(c, group.Key));
                    if (actual < group.Count()) return false;
                }
            }

            if (filter.Terms.Any.Count == 0 && filter.Terms.All.Count == 0) return selected.Count > 0;
            return true;
        }

        bool Dfs(int i, List<string> selected)
        {
            if (i >= optionGroups.Count) return SelectedSatisfies(selected);
            foreach (var opt in optionGroups[i])
            {
                var next = new List<string>(selected);
                next.AddRange(opt);
                if (Dfs(i + 1, next)) return true;
            }
            return false;
        }

        return Dfs(0, new List<string>());
    }

    private static bool CardOkForRarity(string card, IList<string> rarities, IDictionary<string, string?> rarityMap)
    {
        if (rarities.Count == 0) return true;
        return rarityMap.TryGetValue(card, out var rarity) && rarity != null && rarities.Contains(rarity, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> CardEventAllCards(CardEvent ev)
    {
        if (ev.Type == "bundle_choice") return ev.Options.SelectMany(x => x).Where(x => !string.IsNullOrEmpty(x)).ToList();
        return ev.Cards.Where(x => !string.IsNullOrEmpty(x)).ToList();
    }

    private static List<List<string>> CardEventChoiceOptions(CardEvent ev)
    {
        if (ev.Type == "fixed") return new List<List<string>> { ev.Cards.Where(x => !string.IsNullOrEmpty(x)).ToList() };
        if (ev.Type == "choice_group") return ev.Cards.Where(x => !string.IsNullOrEmpty(x)).Select(x => new List<string> { x }).ToList();
        if (ev.Type == "bundle_choice") return ev.Options.Select(x => x.Where(c => !string.IsNullOrEmpty(c)).ToList()).Where(x => x.Count > 0).ToList();
        return new List<List<string>>();
    }

}


public sealed class TermList
{
    public List<string> Any { get; private set; } = new();
    public List<string> All { get; private set; } = new();
    public List<string> Blacklist { get; private set; } = new();
    public bool HasTerms => Any.Count > 0 || All.Count > 0 || Blacklist.Count > 0;

    public static TermList FromJson(JsonElement? e)
    {
        return new TermList
        {
            Any = Term.NormalizeMany(e.Prop("whitelist_any").StringList()),
            All = Term.NormalizeMany(e.Prop("whitelist_all").StringList()),
            Blacklist = Term.NormalizeMany(e.Prop("blacklist").StringList()),
        };
    }

    public bool Match(IList<string> items)
    {
        if (Blacklist.Count > 0 && Blacklist.Any(t => items.Any(x => Term.ItemMatches(x, t)))) return false;
        if (Any.Count > 0 && !Any.Any(t => items.Any(x => Term.ItemMatches(x, t)))) return false;
        foreach (var group in All.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            int actual = items.Count(x => Term.ItemMatches(x, group.Key));
            if (actual < group.Count()) return false;
        }
        return true;
    }
}


public static class Term
{
    public static List<string> NormalizeMany(IEnumerable<string> terms) => terms.Select(Normalize).Where(x => x.Length > 0).ToList();

    public static string Normalize(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) return "";
        // 下拉框常见：会员卡（MembershipCard）/ 会员卡(MembershipCard)
        int l = value.LastIndexOf('（');
        int r = value.LastIndexOf('）');
        if (l >= 0 && r > l) value = value[(l + 1)..r].Trim();
        else
        {
            l = value.LastIndexOf('(');
            r = value.LastIndexOf(')');
            if (l >= 0 && r > l) value = value[(l + 1)..r].Trim();
        }
        return value;
    }

    public static bool ItemMatches(string item, string term)
    {
        item = Normalize(item);
        term = Normalize(term);
        if (term.Length == 0) return false;
        if (string.Equals(item, term, StringComparison.OrdinalIgnoreCase)) return true;

        // 先古选项 SeaGlass 有角色变体：SeaGlass:Silent。
        // 用户筛 SeaGlass 时应匹配任意变体；筛 SeaGlass:Silent 时则要求精确变体。
        string itemBase = item.Split(':', 2)[0];
        bool termHasVariant = term.Contains(':');
        if (!termHasVariant && string.Equals(itemBase, term, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}


public sealed record CardSourceRoute(List<string> Categories, List<string> Sources);

public sealed record EventQueueBlock(int ActNumber, string ActId, List<string> Events, int StartOffset = 0, string StartOffsetReason = "");
public sealed record MapBossBlock(int ActNumber, string ActId, string BossId, List<string> NormalEncounters, List<string> EliteEncounters)
{
    public List<string> BossIds { get; init; } = new();
}



public sealed class OpeningResult
{
    public string Seed { get; init; } = "";
    public List<string> NeowOptions { get; init; } = new();
    public List<string> BonesRelics { get; init; } = new();
    public string? BonesCurse { get; init; }
    public List<string> BonesCurses { get; init; } = new();
    public List<string> ShopRelics { get; init; } = new();
    public Dictionary<string, List<string>> RelicQueues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> PotionSources { get; init; } = new();
    public List<CardSourceRoute> CardSourceRoutes { get; init; } = new();
    public List<string> PredictedRelicSources { get; init; } = new();
    public List<OpeningRoute> OpeningRoutes { get; init; } = new();
    public List<string> Ancients { get; init; } = new();
    public List<AncientOptionBlock> AncientOptions { get; init; } = new();
    public List<EventQueueBlock> EventQueues { get; init; } = new();
    public List<MapBossBlock> MapBosses { get; init; } = new();
    public bool ShowShopRelics { get; init; }
    public bool ShowAncients { get; init; }
    public bool ShowAncientOptions { get; init; }
    public bool ShowEventQueues { get; init; }
    public bool HasBones => NeowOptions.Contains("NeowsBones", StringComparer.OrdinalIgnoreCase);
}

