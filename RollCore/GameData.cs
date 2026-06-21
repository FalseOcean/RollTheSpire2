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

public sealed class CardMeta
{
    public string? Rarity { get; init; }
    public string? Type { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? MultiplayerConstraint { get; init; }
}


public sealed class CardPoolData
{
    public List<string> CardsOrder { get; init; } = new();
    public Dictionary<string, List<string>> ByRarity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> BasicStrikes { get; init; } = new();
    public List<string> BasicDefends { get; init; } = new();
}


public sealed class PotionPoolData
{
    public Dictionary<string, List<string>> ByRarity { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}


public sealed class GameData
{
    private readonly Dictionary<string, string?> _relicRarity = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _relicSingleOnly = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _relicMultiOnly = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _relicPools = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, CardMeta> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CardPoolData> _cardPools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cardToPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PotionPoolData> _potionPools = new(StringComparer.OrdinalIgnoreCase);

    // static_model_lists 里包含 Colorless1Epoch.Cards / Relic1Epoch.Relics / Potion1Epoch.Potions 等静态解锁表。
    // 这里按“物品 ID -> 需要 reveal 的 epoch id 列表”存储，运行时用 UnlockProfile.IsEpochRevealed 过滤。
    private readonly Dictionary<string, List<string>> _cardUnlockEpochs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _relicUnlockEpochs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _potionUnlockEpochs = new(StringComparer.OrdinalIgnoreCase);

    // v17.3+：Codex/source extractor sidecar。预测主流程优先使用这里的 Act/事件/遭遇/先古池；
    // 物品 Epoch 也会从 item_epoch_unlocks 合并进上面的三张 unlock map。
    // v17.4/v17.5 的 Neow RNG trace / ancient_options 目前作为 source trace 载入和展示，复杂条件仍走手动覆盖。
    private readonly Dictionary<string, ActSpec> _sourceActs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _sourceEncounterTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sourceSharedEvents = new();
    private readonly List<string> _sourceSharedAncients = new();

    // v17.7 preview3：Codex 提取的普通事件 IsAllowed / ModifyNextEvent 条件百科。
    // 这里只做“显示与人工过滤辅助”，不在 preview3 强行模拟局内金币/血量/牌组等实时状态。
    private readonly Dictionary<string, EventAllowedRuleInfo> _eventAllowedRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _modifyNextEventHooks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _eventSearchAliases = new(StringComparer.OrdinalIgnoreCase);
    // v17.8 preview2b：事件百科名称兜底。event_rules.json 的少数事件 display_name.zhs 为空，
    // 需要从 sts2_data.json/localization/items 的 name 或 candidates.name 回退。
    private readonly Dictionary<string, string> _localizedEventNameZh = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _localizedEventNameEn = new(StringComparer.OrdinalIgnoreCase);
    public bool SourceExtractorLoaded { get; private set; }
    public string SourceExtractorPath { get; private set; } = "";
    public int SourceExtractorWarningsCount { get; private set; }
    public int SourceExtractorManualReviewCount { get; private set; }
    public bool EventAllowedRulesLoaded { get; private set; }
    public string EventAllowedRulesPath { get; private set; } = "";
    public int EventAllowedRulesCount => _eventAllowedRules.Values.Select(v => v.EventId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int EventAllowedRulesManualReviewCount { get; private set; }
    public int ModifyNextEventHooksCount => _modifyNextEventHooks.Count;

    public static GameData Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("data_file not found: " + path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;
        var gd = new GameData();

        var relics = root.Prop("relics");
        if (relics is not null && relics.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in relics.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var rarity = prop.Value.Prop("rarity").Str("");
                gd._relicRarity[prop.Name] = string.IsNullOrWhiteSpace(rarity) ? null : rarity;
                gd._relicSingleOnly[prop.Name] = prop.Value.Prop("is_singleplayer_only_guess").Bool(false)
                    || string.Equals(prop.Value.Prop("multiplayer_constraint").Str(""), "SingleplayerOnly", StringComparison.OrdinalIgnoreCase);
                gd._relicMultiOnly[prop.Name] = prop.Value.Prop("is_multiplayer_only_guess").Bool(false)
                    || string.Equals(prop.Value.Prop("multiplayer_constraint").Str(""), "MultiplayerOnly", StringComparison.OrdinalIgnoreCase);
            }
        }

        var pools = root.Prop("relic_pools");
        if (pools is not null && pools.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in pools.Value.EnumerateObject())
                gd._relicPools[prop.Name] = prop.Value.Prop("relics_order").StringList();
        }

        var cards = root.Prop("cards");
        if (cards is not null && cards.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cards.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                gd._cards[prop.Name] = new CardMeta
                {
                    Rarity = EmptyToNull(prop.Value.Prop("rarity").Str("")),
                    Type = EmptyToNull(prop.Value.Prop("type").Str("")),
                    Tags = prop.Value.Prop("tags").StringList(),
                    MultiplayerConstraint = EmptyToNull(prop.Value.Prop("multiplayer_constraint").Str("")),
                };
            }
        }

        var cardPools = root.Prop("card_pools");
        if (cardPools is not null && cardPools.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cardPools.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var cpd = new CardPoolData
                {
                    CardsOrder = prop.Value.Prop("cards_order").StringList(),
                    BasicStrikes = prop.Value.Prop("basic_strikes").StringList(),
                    BasicDefends = prop.Value.Prop("basic_defends").StringList(),
                };
                var by = prop.Value.Prop("by_rarity");
                if (by is not null && by.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var r in by.Value.EnumerateObject())
                        cpd.ByRarity[r.Name] = r.Value.StringList();
                }
                gd._cardPools[prop.Name] = cpd;

                foreach (var card in cpd.CardsOrder)
                    if (!gd._cardToPool.ContainsKey(card)) gd._cardToPool[card] = prop.Name;
                foreach (var arr in cpd.ByRarity.Values)
                    foreach (var card in arr)
                        if (!gd._cardToPool.ContainsKey(card)) gd._cardToPool[card] = prop.Name;
            }
        }

        var potionPools = root.Prop("potion_pools");
        if (potionPools is not null && potionPools.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in potionPools.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var ppd = new PotionPoolData();
                var by = prop.Value.Prop("by_rarity");
                if (by is not null && by.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var r in by.Value.EnumerateObject())
                        ppd.ByRarity[r.Name] = r.Value.StringList();
                }
                gd._potionPools[prop.Name] = ppd;
            }
        }

        gd.LoadStaticModelUnlocks(root.Prop("static_model_lists"));
        gd.LoadSourceExtractorSidecar(path);
        gd.LoadEventAllowedRulesSidecar(path);
        return gd;
    }

    private void LoadSourceExtractorSidecar(string runtimeDataPath)
    {
        try
        {
            // v17.9-preview4a: official sidecar is data/sts2_data.json.
            // Legacy extractor/sts2_extracted_data_v4.json is kept as a fallback only.
            string runtimeDir = Path.GetDirectoryName(Path.GetFullPath(runtimeDataPath)) ?? Directory.GetCurrentDirectory();
            string? sourcePath = RollDataPaths.FindSourceData(runtimeDir);
            if (sourcePath is null) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(sourcePath, Encoding.UTF8));
            var root = doc.RootElement;
            if (root.Prop("version").Int(0) < 2) return;

            LoadSourceActs(root.Prop("acts"));
            LoadSourceShared(root);
            LoadSourceEncounterTags(root.Prop("encounter_tags"));
            LoadSourceItemEpochUnlocks(root.Prop("item_epoch_unlocks"));
            LoadSourceRelicAllowedRules(root.Prop("relic_allowed_rules"));
            LoadSourceLocalizationAliases(root.Prop("localization"));
            SourceExtractorLoaded = true;
            SourceExtractorPath = sourcePath;
            SourceExtractorWarningsCount = root.Prop("warnings").StringList().Count;
        }
        catch
        {
            // Source sidecar 只是加固数据源；读取失败时保持旧 runtime data + 内置 fallback。
        }
    }

    private void LoadEventAllowedRulesSidecar(string runtimeDataPath)
    {
        try
        {
            // v17.9-preview4a: official event rules file is data/event_rules.json.
            // Legacy extractor/event_allowed_rules.json is kept as a fallback only.
            string runtimeDir = Path.GetDirectoryName(Path.GetFullPath(runtimeDataPath)) ?? Directory.GetCurrentDirectory();
            string? rulesPath = RollDataPaths.FindEventRules(runtimeDir);
            if (rulesPath is null) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(rulesPath, Encoding.UTF8));
            var root = doc.RootElement;
            var rules = root.Prop("event_allowed_rules");
            if (rules is not null && rules.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in rules.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    string id = SourceIdToRuntimeId(prop.Name);
                    var displayName = prop.Value.Prop("display_name");
                    var info = new EventAllowedRuleInfo
                    {
                        EventId = id,
                        SourceId = prop.Name,
                        ClassName = prop.Value.Prop("class_name").Str(""),
                        DisplayNameZh = CleanMaybeGarbledText(displayName.Prop("zhs").Str("")),
                        DisplayNameEn = CleanMaybeGarbledText(displayName.Prop("eng").Str("")),
                        SummaryEn = prop.Value.Prop("allowed_summary_en").Str(""),
                        SummaryZh = CleanMaybeGarbledText(prop.Value.Prop("allowed_summary_zh").Str("")),
                        ManualReview = prop.Value.Prop("manual_review").Bool(false),
                        Confidence = prop.Value.Prop("confidence").Str(""),
                        SourceFile = prop.Value.Prop("source_file").Str(""),
                        SourceLine = prop.Value.Prop("source_line").Int(0),
                    };
                    AddEventSearchAlias(id, prop.Name, info.ClassName, info.DisplayNameZh, info.DisplayNameEn);
                    var conditions = prop.Value.Prop("conditions");
                    if (conditions is not null && conditions.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in conditions.Value.EnumerateArray())
                        {
                            if (c.ValueKind != JsonValueKind.Object) continue;
                            info.Conditions.Add(new EventConditionInfo
                            {
                                Type = c.Prop("type").Str(""),
                                Operator = c.Prop("operator").Str(""),
                                Value = c.Prop("value").Str(""),
                                AppliesTo = c.Prop("applies_to").Str(""),
                                DescriptionZh = CleanMaybeGarbledText(c.Prop("description_zh").Str("")),
                            });
                        }
                    }
                    info.Warnings.AddRange(prop.Value.Prop("warnings").StringList());
                    AddEventAllowedRuleAlias(info, id);
                    AddEventAllowedRuleAlias(info, prop.Name);
                    foreach (var alias in prop.Value.Prop("aliases").StringList())
                    {
                        AddEventAllowedRuleAlias(info, alias);
                        AddEventSearchAlias(id, alias);
                    }
                    if (info.ManualReview) EventAllowedRulesManualReviewCount++;
                }
            }

            var hooks = root.Prop("modify_next_event_hooks");
            if (hooks is not null && hooks.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in hooks.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    string owner = SourceIdToRuntimeId(prop.Name);
                    string replacement = SourceIdToRuntimeId(prop.Value.Prop("replacement_event_id").Str(""));
                    string summary = CleanMaybeGarbledText(prop.Value.Prop("summary_zh").Str(""));
                    if (string.IsNullOrWhiteSpace(summary)) summary = prop.Value.Prop("summary_en").Str("");
                    if (string.IsNullOrWhiteSpace(summary)) summary = $"满足源码 hook 条件时，下一个事件可能被替换为 {replacement}。";
                    _modifyNextEventHooks[owner] = string.IsNullOrWhiteSpace(replacement)
                        ? summary
                        : $"{summary}（替换目标：{replacement}）";
                }
            }

            EventAllowedRulesLoaded = _eventAllowedRules.Count > 0;
            EventAllowedRulesPath = rulesPath;
        }
        catch
        {
            // 条件百科只影响提示，不影响预测。读取失败时回退内置少量提示。
        }
    }

    private void AddEventSearchAlias(string eventId, params string?[] aliases)
    {
        eventId = SourceIdToRuntimeId(eventId ?? "");
        if (string.IsNullOrWhiteSpace(eventId)) return;
        if (!_eventSearchAliases.TryGetValue(eventId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _eventSearchAliases[eventId] = set;
        }
        set.Add(eventId);
        foreach (var alias in aliases ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;
            set.Add(alias.Trim());
            set.Add(SourceIdToRuntimeId(alias));
        }
    }

    private void LoadSourceLocalizationAliases(JsonElement? loc)
    {
        if (loc is null || loc.Value.ValueKind != JsonValueKind.Object) return;
        var items = loc.Value.Prop("items");
        if (items is null || items.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in items.Value.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            string id = SourceIdToRuntimeId(prop.Name);
            string cls = prop.Value.Prop("class").Str("");
            string zhs = CleanMaybeGarbledText(prop.Value.Prop("name").Prop("zhs").Str(""));
            string eng = CleanMaybeGarbledText(prop.Value.Prop("name").Prop("eng").Str(""));

            // 少数事件（例如 BYRDONIS_NEST）官方 name 没直接解析到 name.zhs/name.eng，
            // 但 extractor 会把候选名放到 candidates.name。事件百科可以安全使用第一个候选作显示兜底。
            if (string.IsNullOrWhiteSpace(zhs)) zhs = FirstCleanText(prop.Value.Prop("candidates").Prop("name").Prop("zhs").StringList());
            if (string.IsNullOrWhiteSpace(eng)) eng = FirstCleanText(prop.Value.Prop("candidates").Prop("name").Prop("eng").StringList());

            AddLocalizedEventName(id, zhs, eng);
            AddLocalizedEventName(prop.Name, zhs, eng);
            AddLocalizedEventName(cls, zhs, eng);
            AddEventSearchAlias(id, prop.Name, cls, zhs, eng);
            foreach (var alias in prop.Value.Prop("aliases").StringList())
            {
                AddLocalizedEventName(alias, zhs, eng);
                AddEventSearchAlias(id, alias);
            }
        }
    }

    private static string FirstCleanText(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            string cleaned = CleanMaybeGarbledText(value);
            if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
        }
        return "";
    }

    private void AddLocalizedEventName(string alias, string zhs, string eng)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;
        void AddOne(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!string.IsNullOrWhiteSpace(zhs) && !_localizedEventNameZh.ContainsKey(key)) _localizedEventNameZh[key] = zhs;
            if (!string.IsNullOrWhiteSpace(eng) && !_localizedEventNameEn.ContainsKey(key)) _localizedEventNameEn[key] = eng;
        }
        AddOne(alias.Trim());
        AddOne(SourceIdToRuntimeId(alias.Trim()));
    }

    private void AddEventAllowedRuleAlias(EventAllowedRuleInfo info, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;
        _eventAllowedRules[alias] = info;
        _eventAllowedRules[SourceIdToRuntimeId(alias)] = info;
    }

    private static string CleanMaybeGarbledText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Codex 在某些 Windows 控制台里会把中文摘要写成 ?????，这种文本不显示，改用英文摘要/结构化条件。
        int q = text.Count(ch => ch == '?');
        if (q >= Math.Max(3, text.Length / 3)) return "";
        return text.Trim();
    }

    private string EventRuleDisplayName(EventAllowedRuleInfo info)
    {
        string display = !string.IsNullOrWhiteSpace(info.DisplayNameZh)
            ? info.DisplayNameZh
            : LocalizedEventNameZh(info);
        if (string.IsNullOrWhiteSpace(display))
            display = !string.IsNullOrWhiteSpace(info.DisplayNameEn) ? info.DisplayNameEn : LocalizedEventNameEn(info);
        string id = !string.IsNullOrWhiteSpace(info.SourceId) ? info.SourceId : info.EventId;
        return string.IsNullOrWhiteSpace(display) ? id : $"{display}（{info.EventId}）";
    }

    private string LocalizedEventNameZh(EventAllowedRuleInfo info)
    {
        foreach (var key in EventRuleNameKeys(info))
            if (_localizedEventNameZh.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return "";
    }

    private string LocalizedEventNameEn(EventAllowedRuleInfo info)
    {
        foreach (var key in EventRuleNameKeys(info))
            if (_localizedEventNameEn.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)) return name;
        return "";
    }

    private static IEnumerable<string> EventRuleNameKeys(EventAllowedRuleInfo info)
    {
        foreach (var raw in new[] { info.EventId, info.SourceId, info.ClassName })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            yield return raw;
            yield return SourceIdToRuntimeId(raw);
        }
    }

    public List<object> EventRuleBookItems()
    {
        return _eventAllowedRules.Values
            .GroupBy(x => x.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(EventRuleDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(info => new
            {
                id = info.EventId,
                source_id = info.SourceId,
                name = EventRuleDisplayName(info),
                class_name = info.ClassName,
                summary_zh = string.IsNullOrWhiteSpace(info.SummaryZh) ? info.SummaryEn : info.SummaryZh,
                summary_en = info.SummaryEn,
                confidence = info.Confidence,
                manual_review = info.ManualReview,
                source_file = Path.GetFileName(info.SourceFile ?? ""),
                source_line = info.SourceLine,
                acts = EventRuleActLabels(info.EventId),
                conditions = info.Conditions.Select(c => new
                {
                    type = c.Type,
                    op = c.Operator,
                    value = c.Value,
                    applies_to = c.AppliesTo,
                    description_zh = c.DescriptionZh,
                    display = DescribeCondition(c),
                }).ToList(),
            })
            .Cast<object>()
            .ToList();
    }

    private List<string> EventRuleActLabels(string eventId)
    {
        var labels = new List<string>();
        foreach (var kv in _sourceActs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (kv.Value.Events.Any(e => string.Equals(e, eventId, StringComparison.OrdinalIgnoreCase)))
                labels.Add(kv.Key);
        }
        if (_sourceSharedEvents.Any(e => string.Equals(e, eventId, StringComparison.OrdinalIgnoreCase)))
            labels.Add("SharedEvents");
        return labels;
    }

    public string EventAllowedNote(string eventId)
    {
        if (!_eventAllowedRules.TryGetValue(eventId ?? "", out var info))
            _eventAllowedRules.TryGetValue(SourceIdToRuntimeId(eventId ?? ""), out info);
        if (info is null) return "";

        var parts = new List<string>();
        var conds = info.Conditions.Select(DescribeCondition).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (conds.Count > 0) parts.Add("条件：" + string.Join("；", conds));
        string summary = !string.IsNullOrWhiteSpace(info.SummaryZh) ? info.SummaryZh : info.SummaryEn;
        if (!string.IsNullOrWhiteSpace(summary)) parts.Add("源码摘要：" + summary);
        if (info.ManualReview) parts.Add("复杂条件，建议人工核对");
        if (info.SourceLine > 0) parts.Add($"源码：{Path.GetFileName(info.SourceFile)}:{info.SourceLine}");
        return string.Join("；", parts);
    }

    public bool HasEventAllowedRule(string eventId)
    {
        if (_eventAllowedRules.ContainsKey(eventId ?? "")) return true;
        return _eventAllowedRules.ContainsKey(SourceIdToRuntimeId(eventId ?? ""));
    }

    public string EventActSkipNote(string eventId, int displayActNumber)
    {
        if (displayActNumber <= 0) return "";
        if (!_eventAllowedRules.TryGetValue(eventId ?? "", out var info))
            _eventAllowedRules.TryGetValue(SourceIdToRuntimeId(eventId ?? ""), out info);
        if (info is null) return "";
        foreach (var c in info.Conditions)
        {
            string type = (c.Type ?? "").Trim();
            string op = (c.Operator ?? "").Trim();
            string val = (c.Value ?? "").Trim();
            string desc = (c.DescriptionZh ?? "").Trim();
            if (type.Equals("other", StringComparison.OrdinalIgnoreCase)
                && op.Equals("==", StringComparison.OrdinalIgnoreCase)
                && (val.Equals("false", StringComparison.OrdinalIgnoreCase) || desc.Contains("IsAllowed 直接返回 false", StringComparison.OrdinalIgnoreCase)))
            {
                return "普通队列必跳过";
            }
        }

        string summary = (info.SummaryZh ?? "") + " " + (info.SummaryEn ?? "");
        if (summary.Contains("永远不会自然出现", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("Never allowed through the normal event queue", StringComparison.OrdinalIgnoreCase))
            return "普通队列必跳过";

        int sourceActIndex = displayActNumber - 1;
        foreach (var c in info.Conditions.Where(x => (x.Type ?? "").Equals("act", StringComparison.OrdinalIgnoreCase)))
        {
            if (!int.TryParse((c.Value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) continue;
            string op = (c.Operator ?? "").Trim();
            bool ok = op switch
            {
                "==" => sourceActIndex == v,
                "!=" => sourceActIndex != v,
                ">" => sourceActIndex > v,
                ">=" => sourceActIndex >= v,
                "<" => sourceActIndex < v,
                "<=" => sourceActIndex <= v,
                _ => true,
            };
            if (!ok) return "当前Act必跳过";
        }
        return "";
    }

    public bool EventMatchesTerm(string eventId, string term)
    {
        if (Term.ItemMatches(eventId ?? "", term ?? "")) return true;
        string runtime = SourceIdToRuntimeId(eventId ?? "");
        string query = NormalizeEventSearchText(term);
        if (query.Length == 0) return false;
        if (_eventSearchAliases.TryGetValue(eventId ?? "", out var direct) && direct.Any(a => AliasMatchesQuery(a, query))) return true;
        if (_eventSearchAliases.TryGetValue(runtime, out var set) && set.Any(a => AliasMatchesQuery(a, query))) return true;
        return false;
    }

    private static bool AliasMatchesQuery(string alias, string query)
    {
        string a = NormalizeEventSearchText(alias);
        if (a.Length == 0 || query.Length == 0) return false;
        if (a.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;
        return query.Length >= 2 && a.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEventSearchText(string? text)
    {
        string s = Term.Normalize(text ?? "");
        return Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", "");
    }

    public List<string> ModifyNextEventHookNotes()
    {
        var notes = new List<string>();
        foreach (var kv in _modifyNextEventHooks.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            notes.Add(kv.Key + "：" + kv.Value);
        return notes;
    }

    private static string DescribeCondition(EventConditionInfo c)
    {
        string type = (c.Type ?? "").Trim();
        string op = (c.Operator ?? "").Trim();
        string value = (c.Value ?? "").Trim();
        string applies = (c.AppliesTo ?? "").Trim();

        // 源码里的 CurrentActIndex 是 0-based：0/1/2/3 对应玩家看到的 Act1/2/3/4。
        // 条件百科只做展示，因此这里把 act 条件转换成玩家视角，避免把 Act2 误读成“第 1 层/第 1 幕”。
        if (type.Equals("act", StringComparison.OrdinalIgnoreCase))
        {
            string displayValue = TryFormatDisplayedActValue(value);
            string actText = string.IsNullOrWhiteSpace(displayValue)
                ? $"Act(显示) {op}"
                : $"Act(显示) {op} {displayValue}（源码 CurrentActIndex {op} {value}）";
            return actText.Trim();
        }

        string subject = type.ToLowerInvariant() switch
        {
            "gold" => "金币",
            "hp" => "当前生命",
            "max_hp" => "最大生命",
            "deck" => "牌组",
            "cards" => "卡牌",
            "relic" => "遗物",
            "potion" => "药水",
            "floor" => "楼层",
            "multiplayer" => "多人状态",
            "player_count" => "玩家数",
            "character" => "角色",
            "unlock" => "解锁状态",
            "relic_pool" => "遗物池",
            "other" => "其它条件",
            _ => string.IsNullOrWhiteSpace(type) ? "条件" : type,
        };
        string text = string.IsNullOrWhiteSpace(value) ? $"{subject} {op}" : $"{subject} {op} {value}";
        if (op.Equals("exists", StringComparison.OrdinalIgnoreCase)) text = $"存在 {subject}" + (value.Length > 0 ? $"：{value}" : "");
        else if (op.Equals("not_exists", StringComparison.OrdinalIgnoreCase)) text = $"不存在 {subject}" + (value.Length > 0 ? $"：{value}" : "");
        else if (op.Equals("custom", StringComparison.OrdinalIgnoreCase)) text = value.Length > 0 ? $"{subject}：{value}" : subject;
        if (applies.Equals("all_players", StringComparison.OrdinalIgnoreCase)) text += "（全体玩家）";
        else if (applies.Equals("run", StringComparison.OrdinalIgnoreCase)) text += "（全局）";
        return text.Trim();
    }

    private static string TryFormatDisplayedActValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceIndex))
            return (sourceIndex + 1).ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private sealed class EventAllowedRuleInfo
    {
        public string EventId { get; init; } = "";
        public string SourceId { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string DisplayNameZh { get; init; } = "";
        public string DisplayNameEn { get; init; } = "";
        public string SummaryZh { get; init; } = "";
        public string SummaryEn { get; init; } = "";
        public bool ManualReview { get; init; }
        public string Confidence { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public int SourceLine { get; init; }
        public List<EventConditionInfo> Conditions { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private sealed class EventConditionInfo
    {
        public string Type { get; init; } = "";
        public string Operator { get; init; } = "";
        public string Value { get; init; } = "";
        public string AppliesTo { get; init; } = "";
        public string DescriptionZh { get; init; } = "";
    }

    private void LoadSourceActs(JsonElement? acts)
    {
        if (acts is null || acts.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in acts.Value.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var baseSpec = AncientPredictor.FallbackActSpec(prop.Name);
            int baseRooms = baseSpec?.BaseRooms ?? 15;
            int weakCount = baseSpec?.WeakCount ?? 3;
            var ancients = prop.Value.Prop("ancients").StringList().Select(SourceIdToRuntimeId).Where(x => x.Length > 0).ToArray();
            var events = prop.Value.Prop("events").StringList().Select(SourceIdToRuntimeId).Where(x => x.Length > 0).ToArray();
            foreach (var ev in events) AddEventSearchAlias(ev);
            var encountersNode = prop.Value.Prop("encounters");
            var encounters = new List<string>();
            if (encountersNode is not null && encountersNode.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var bucket in new[] { "weak", "normal", "elite", "boss" })
                    encounters.AddRange(encountersNode.Value.Prop(bucket).StringList().Select(SourceIdToRuntimeId).Where(x => x.Length > 0));
            }
            _sourceActs[prop.Name] = new ActSpec(baseRooms, weakCount, ancients, events, encounters.ToArray());
        }
    }

    private void LoadSourceShared(JsonElement root)
    {
        _sourceSharedEvents.Clear();
        _sourceSharedEvents.AddRange(root.Prop("shared_events").StringList().Select(SourceIdToRuntimeId).Where(x => x.Length > 0));
        foreach (var ev in _sourceSharedEvents) AddEventSearchAlias(ev);
        _sourceSharedAncients.Clear();
        _sourceSharedAncients.AddRange(root.Prop("shared_ancients").StringList().Select(SourceIdToRuntimeId).Where(x => x.Length > 0));
    }

    private void LoadSourceEncounterTags(JsonElement? node)
    {
        if (node is null || node.Value.ValueKind != JsonValueKind.Object) return;
        _sourceEncounterTags.Clear();
        foreach (var prop in node.Value.EnumerateObject())
            _sourceEncounterTags[SourceIdToRuntimeId(prop.Name)] = prop.Value.StringList().ToArray();
    }

    private void LoadSourceItemEpochUnlocks(JsonElement? node)
    {
        if (node is null || node.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var epoch in node.Value.EnumerateObject())
        {
            if (epoch.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var item in epoch.Value.Prop("cards").StringList().Select(SourceIdToRuntimeId)) AddUnlockEpoch(_cardUnlockEpochs, item, epoch.Name);
            foreach (var item in epoch.Value.Prop("relics").StringList().Select(SourceIdToRuntimeId)) AddUnlockEpoch(_relicUnlockEpochs, item, epoch.Name);
            foreach (var item in epoch.Value.Prop("potions").StringList().Select(SourceIdToRuntimeId)) AddUnlockEpoch(_potionUnlockEpochs, item, epoch.Name);
        }
    }

    private void LoadSourceRelicAllowedRules(JsonElement? node)
    {
        SourceExtractorManualReviewCount = 0;
        if (node is null || node.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in node.Value.EnumerateObject())
        {
            if (prop.Value.Prop("manual_review").Bool(false)) SourceExtractorManualReviewCount++;
            string id = SourceIdToRuntimeId(prop.Name);
            var flags = prop.Value.Prop("flags").StringList();
            string text = prop.Value.ToString();
            if (flags.Any(x => x.Contains("single", StringComparison.OrdinalIgnoreCase)) || text.Contains("Players.Count == 1", StringComparison.OrdinalIgnoreCase))
                _relicSingleOnly[id] = true;
            if (flags.Any(x => x.Contains("multi", StringComparison.OrdinalIgnoreCase)) || text.Contains("Players.Count > 1", StringComparison.OrdinalIgnoreCase))
                _relicMultiOnly[id] = true;
        }
    }

    private static string SourceIdToRuntimeId(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return "";
        if (!id.Contains('_') && id.Any(char.IsLower)) return id;
        var sb = new StringBuilder();
        foreach (var part in id.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part[1..].ToLowerInvariant());
        }
        return sb.ToString();
    }

    public ActSpec? SourceActSpec(string actId) => _sourceActs.TryGetValue(actId, out var spec) ? spec : null;
    public List<string> SourceSharedEvents() => _sourceSharedEvents.Count > 0 ? new List<string>(_sourceSharedEvents) : new List<string>();
    public List<string> SourceSharedAncients() => _sourceSharedAncients.Count > 0 ? new List<string>(_sourceSharedAncients) : new List<string>();
    public string[]? SourceEncounterTags(string encounterId) => _sourceEncounterTags.TryGetValue(encounterId, out var xs) ? xs : null;

    private static string? EmptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private void LoadStaticModelUnlocks(JsonElement? node)
    {
        if (node is null || node.Value.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in node.Value.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var parts = prop.Name.Split('.');
            if (parts.Length < 2) continue;
            string epochId = EpochClassToId(parts[0]);
            string kind = prop.Value.Prop("kind").Str(parts[1]);
            var items = prop.Value.Prop("items").StringList();
            Dictionary<string, List<string>>? target = kind switch
            {
                "Card" or "Cards" => _cardUnlockEpochs,
                "Relic" or "Relics" => _relicUnlockEpochs,
                "Potion" or "Potions" => _potionUnlockEpochs,
                _ => null,
            };
            if (target is null) continue;
            foreach (var item in items) AddUnlockEpoch(target, item, epochId);
        }
    }

    private static string EpochClassToId(string epochClass)
    {
        var s = epochClass.EndsWith("Epoch", StringComparison.OrdinalIgnoreCase)
            ? epochClass[..^"Epoch".Length]
            : epochClass;
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", "_");
        return s.ToUpperInvariant() + "_EPOCH";
    }

    private static void AddUnlockEpoch(Dictionary<string, List<string>> map, string item, string epochId)
    {
        if (string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(epochId)) return;
        if (!map.TryGetValue(item, out var xs))
        {
            xs = new List<string>();
            map[item] = xs;
        }
        if (!xs.Contains(epochId, StringComparer.OrdinalIgnoreCase)) xs.Add(epochId);
    }

    private static bool IsUnlockedByEpoch(Dictionary<string, List<string>> map, string id, UnlockProfile? unlocks)
    {
        if (unlocks is null || !unlocks.Enabled) return true;
        if (!map.TryGetValue(id, out var epochs) || epochs.Count == 0) return true;
        return epochs.All(unlocks.IsEpochRevealed);
    }

    public bool IsCardUnlocked(string cardId, UnlockProfile? unlocks) => IsUnlockedByEpoch(_cardUnlockEpochs, cardId, unlocks);
    public bool IsRelicUnlocked(string relicId, UnlockProfile? unlocks) => IsUnlockedByEpoch(_relicUnlockEpochs, relicId, unlocks);
    public bool IsPotionUnlocked(string potionId, UnlockProfile? unlocks) => IsUnlockedByEpoch(_potionUnlockEpochs, potionId, unlocks);

    public List<string> FilterUnlockedCards(IEnumerable<string> cards, UnlockProfile? unlocks) => cards.Where(c => IsCardUnlocked(c, unlocks)).ToList();
    public List<string> FilterUnlockedRelics(IEnumerable<string> relics, UnlockProfile? unlocks) => relics.Where(r => IsRelicUnlocked(r, unlocks)).ToList();
    public List<string> FilterUnlockedPotions(IEnumerable<string> potions, UnlockProfile? unlocks) => potions.Where(p => IsPotionUnlocked(p, unlocks)).ToList();

    public int LockedCardsCount(UnlockProfile? unlocks) => _cardUnlockEpochs.Keys.Count(id => !IsCardUnlocked(id, unlocks));
    public int LockedRelicsCount(UnlockProfile? unlocks) => _relicUnlockEpochs.Keys.Count(id => !IsRelicUnlocked(id, unlocks));
    public int LockedPotionsCount(UnlockProfile? unlocks) => _potionUnlockEpochs.Keys.Count(id => !IsPotionUnlocked(id, unlocks));
    public List<string> LockedCards(UnlockProfile? unlocks, int limit) => _cardUnlockEpochs.Keys.Where(id => !IsCardUnlocked(id, unlocks)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, limit)).ToList();
    public List<string> LockedRelics(UnlockProfile? unlocks, int limit) => _relicUnlockEpochs.Keys.Where(id => !IsRelicUnlocked(id, unlocks)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, limit)).ToList();
    public List<string> LockedPotions(UnlockProfile? unlocks, int limit) => _potionUnlockEpochs.Keys.Where(id => !IsPotionUnlocked(id, unlocks)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, limit)).ToList();

    public string? RelicRarity(string relicId) => _relicRarity.TryGetValue(relicId, out var rarity) ? rarity : null;
    public bool IsRelicSingleplayerOnly(string relicId) => _relicSingleOnly.TryGetValue(relicId, out var v) && v;
    public bool IsRelicMultiplayerOnly(string relicId) => _relicMultiOnly.TryGetValue(relicId, out var v) && v;

    public List<string> RelicPool(string poolName, UnlockProfile? unlocks = null)
        => _relicPools.TryGetValue(poolName, out var xs) ? FilterUnlockedRelics(xs, unlocks) : new List<string>();

    public string CharacterRelicPool(string character) => character switch
    {
        "Ironclad" => "IroncladRelicPool",
        "Silent" => "SilentRelicPool",
        "Defect" => "DefectRelicPool",
        "Necrobinder" => "NecrobinderRelicPool",
        "Regent" => "RegentRelicPool",
        _ => character + "RelicPool",
    };

    public string CharacterCardPool(string character) => character switch
    {
        "Ironclad" => "IroncladCardPool",
        "Silent" => "SilentCardPool",
        "Defect" => "DefectCardPool",
        "Necrobinder" => "NecrobinderCardPool",
        "Regent" => "RegentCardPool",
        _ => character + "CardPool",
    };

    public string CharacterPotionPool(string character) => character switch
    {
        "Ironclad" => "IroncladPotionPool",
        "Silent" => "SilentPotionPool",
        "Defect" => "DefectPotionPool",
        "Necrobinder" => "NecrobinderPotionPool",
        "Regent" => "RegentPotionPool",
        _ => character + "PotionPool",
    };

    public CardPoolData CardPool(string poolName) => _cardPools.TryGetValue(poolName, out var p) ? p : new CardPoolData();
    public PotionPoolData PotionPool(string poolName) => _potionPools.TryGetValue(poolName, out var p) ? p : new PotionPoolData();
    public string? CardPoolOf(string cardId) => _cardToPool.TryGetValue(cardId, out var p) ? p : null;
    public string? CardRarity(string cardId) => _cards.TryGetValue(cardId, out var c) ? c.Rarity : null;
    public string? CardType(string cardId) => _cards.TryGetValue(cardId, out var c) ? c.Type : null;
    public List<string> CardTags(string cardId) => _cards.TryGetValue(cardId, out var c) ? new List<string>(c.Tags) : new List<string>();
    public bool IsCardMultiplayerOnly(string cardId) => _cards.TryGetValue(cardId, out var c) && string.Equals(c.MultiplayerConstraint, "MultiplayerOnly", StringComparison.OrdinalIgnoreCase);
    public bool IsCardSingleplayerOnly(string cardId) => _cards.TryGetValue(cardId, out var c) && string.Equals(c.MultiplayerConstraint, "SingleplayerOnly", StringComparison.OrdinalIgnoreCase);

    public List<string> CardPoolCardsOrder(string poolName, UnlockProfile? unlocks = null)
        => FilterUnlockedCards(CardPool(poolName).CardsOrder, unlocks);

    public List<string> CardsByRarity(string poolName, string rarity, UnlockProfile? unlocks = null)
    {
        var pool = CardPool(poolName);
        return pool.ByRarity.TryGetValue(rarity, out var xs) ? FilterUnlockedCards(xs, unlocks) : new List<string>();
    }

    public List<string> PotionsByRarity(string poolName, string rarity, UnlockProfile? unlocks = null)
    {
        var pool = PotionPool(poolName);
        return pool.ByRarity.TryGetValue(rarity, out var xs) ? FilterUnlockedPotions(xs, unlocks) : new List<string>();
    }

    public string? BasicStrike(string poolName)
    {
        var xs = CardPool(poolName).BasicStrikes;
        return xs.Count > 0 ? xs[0] : null;
    }

    public string? BasicDefend(string poolName)
    {
        var xs = CardPool(poolName).BasicDefends;
        return xs.Count > 0 ? xs[0] : null;
    }
}

