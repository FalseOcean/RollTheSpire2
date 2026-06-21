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

public sealed class ExtractedSourceDataStatus
{
    public bool Loaded { get; init; }
    public string FilePath { get; init; } = "";
    public int Version { get; init; }
    public int ActsCount { get; init; }
    public int SharedEventsCount { get; init; }
    public int SharedAncientsCount { get; init; }
    public int EncounterTagsCount { get; init; }
    public int EpochUnlocksCount { get; init; }
    public int CardsCount { get; init; }
    public int RelicsCount { get; init; }
    public int PotionsCount { get; init; }
    public int CursesCount { get; init; }
    public int ItemEpochUnlocksCount { get; init; }
    public int RelicAllowedRulesCount { get; init; }
    public int NeowRewardPoolCount { get; init; }
    public int NeowSpecialRoutesCount { get; init; }
    public int NeowRngConsumersCount { get; init; }
    public int AncientOptionsCount { get; init; }
    public int AncientOptionPoolEntriesCount { get; init; }
    public int AncientConditionalOptionsCount { get; init; }
    public int AncientRngTraceCount { get; init; }
    public int ManualReviewRulesCount { get; init; }
    public int LocalizationItemsCount { get; init; }
    public int LocalizationZhsNamesCount { get; init; }
    public int LocalizationManualReviewCount { get; init; }
    public string LocalizationLanguages { get; init; } = "";
    public int WarningsCount { get; init; }
    public List<string> WarningItems { get; init; } = new();
    public string Note { get; init; } = "";

    public static ExtractedSourceDataStatus Load(string rootDir)
    {
        rootDir = string.IsNullOrWhiteSpace(rootDir) ? Directory.GetCurrentDirectory() : rootDir;
        string? path = RollDataPaths.FindSourceData(rootDir);
        if (path is null)
        {
            return new ExtractedSourceDataStatus
            {
                Loaded = false,
                FilePath = RollDataPaths.OfficialSourceData,
                Note = "未找到 data/sts2_data.json；运行预测不受影响，会继续使用内置 runtime data_file。"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            var warnings = root.Prop("warnings").StringList();
            int manualReview = 0;
            var rules = root.Prop("relic_allowed_rules");
            if (rules is not null && rules.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in rules.Value.EnumerateObject())
                    if (prop.Value.Prop("manual_review").Bool(false)) manualReview++;
            }
            var neow = root.Prop("neow");
            var ancientOptions = root.Prop("ancient_options");
            int neowRewardPool = CountArray(neow.Prop("reward_pool"));
            int neowSpecialRoutes = CountObject(neow.Prop("special_routes"));
            int neowRngConsumers = CountObject(neow.Prop("rng_consumers"));
            int ancientOptionsCount = CountObject(ancientOptions);
            int ancientOptionPoolEntries = 0;
            int ancientConditionalOptions = 0;
            int ancientRngTrace = 0;
            if (neow is not null && neow.Value.ValueKind == JsonValueKind.Object)
            {
                var routes = neow.Value.Prop("special_routes");
                if (routes is not null && routes.Value.ValueKind == JsonValueKind.Object)
                    foreach (var prop in routes.Value.EnumerateObject())
                        if (prop.Value.Prop("manual_review").Bool(false)) manualReview++;
            }
            if (ancientOptions is not null && ancientOptions.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ancientOptions.Value.EnumerateObject())
                {
                    if (prop.Value.Prop("manual_review").Bool(false)) manualReview++;
                    ancientOptionPoolEntries += CountArray(prop.Value.Prop("option_pool"));
                    ancientConditionalOptions += CountArray(prop.Value.Prop("conditional_options"));
                    ancientRngTrace += CountArray(prop.Value.Prop("rng_trace"));
                }
            }

            var localization = root.Prop("localization");
            var locItemsNode = localization.Prop("items");
            int locItems = CountObject(locItemsNode);
            int locZhsNames = 0;
            if (locItemsNode is not null && locItemsNode.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in locItemsNode.Value.EnumerateObject())
                    if (!string.IsNullOrWhiteSpace(prop.Value.Prop("name").Prop("zhs").Str(""))) locZhsNames++;
            }
            int locManual = CountArray(localization.Prop("manual_review"));
            string locLangs = string.Join(",", localization.Prop("languages_detected").StringList());

            return new ExtractedSourceDataStatus
            {
                Loaded = true,
                FilePath = SafeRel(rootDir, path),
                Version = root.Prop("version").Int(0),
                ActsCount = CountObject(root.Prop("acts")),
                SharedEventsCount = CountArray(root.Prop("shared_events")),
                SharedAncientsCount = CountArray(root.Prop("shared_ancients")),
                EncounterTagsCount = CountObject(root.Prop("encounter_tags")),
                EpochUnlocksCount = CountObject(root.Prop("epoch_unlocks")),
                CardsCount = CountObject(root.Prop("cards")),
                RelicsCount = CountObject(root.Prop("relics")),
                PotionsCount = CountObject(root.Prop("potions")),
                CursesCount = CountArray(root.Prop("curses")),
                ItemEpochUnlocksCount = CountObject(root.Prop("item_epoch_unlocks")),
                RelicAllowedRulesCount = CountObject(root.Prop("relic_allowed_rules")),
                NeowRewardPoolCount = neowRewardPool,
                NeowSpecialRoutesCount = neowSpecialRoutes,
                NeowRngConsumersCount = neowRngConsumers,
                AncientOptionsCount = ancientOptionsCount,
                AncientOptionPoolEntriesCount = ancientOptionPoolEntries,
                AncientConditionalOptionsCount = ancientConditionalOptions,
                AncientRngTraceCount = ancientRngTrace,
                ManualReviewRulesCount = manualReview,
                LocalizationItemsCount = locItems,
                LocalizationZhsNamesCount = locZhsNames,
                LocalizationManualReviewCount = locManual,
                LocalizationLanguages = locLangs,
                WarningsCount = warnings.Count,
                WarningItems = warnings.Take(20).ToList(),
                Note = "v17.6：源码抽取数据已接入主流程。Act/事件/遭遇/共享先古/EncounterTags 优先使用 source JSON；卡牌/遗物/药水 Epoch 从 item_epoch_unlocks 合并；Neow 特殊路线/RNG 消耗与先古选项池作为 source trace 载入；本地化优先使用 localization/zhs，找不到时回退英文/旧内置名称。"
            };
        }
        catch (Exception ex)
        {
            return new ExtractedSourceDataStatus
            {
                Loaded = false,
                FilePath = SafeRel(rootDir, path),
                Note = "源码抽取数据读取失败：" + ex.Message
            };
        }
    }

    private static int CountObject(JsonElement? e) => e is not null && e.Value.ValueKind == JsonValueKind.Object ? e.Value.EnumerateObject().Count() : 0;
    private static int CountArray(JsonElement? e) => e is not null && e.Value.ValueKind == JsonValueKind.Array ? e.Value.GetArrayLength() : 0;

    private static string SafeRel(string rootDir, string path)
    {
        try { return System.IO.Path.GetRelativePath(rootDir, path).Replace('\\', '/'); }
        catch { return path; }
    }
}


