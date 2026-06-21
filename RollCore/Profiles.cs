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

public static class ProgressSaveImporter
{
    public static JsonNode ImportToFile(string inputPath, string outputPath, string dataPath)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);
        dataPath = Path.GetFullPath(dataPath);

        if (!File.Exists(inputPath)) throw new FileNotFoundException("progress.save 不存在：" + inputPath);
        var progress = JsonNode.Parse(File.ReadAllText(inputPath, Encoding.UTF8)) as JsonObject
            ?? throw new InvalidOperationException("progress.save 不是 JSON object");

        var mapper = ModelIdMapper.Load(dataPath);
        var profile = BuildProfile(progress, inputPath, mapper);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(outputPath, profile.ToJsonString(new JsonSerializerOptions(JsonOut.Options) { WriteIndented = true }), Encoding.UTF8);
        return profile;
    }

    private static JsonObject BuildProfile(JsonObject progress, string inputPath, ModelIdMapper mapper)
    {
        var epochs = progress["epochs"] as JsonArray ?? new JsonArray();
        var revealedEpochs = new JsonArray();
        var allEpochs = new JsonArray();
        var stateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in epochs)
        {
            if (item is not JsonObject e) continue;
            string id = e["id"]?.GetValue<string>() ?? "";
            string state = e["state"]?.GetValue<string>() ?? "";
            if (!string.IsNullOrWhiteSpace(state)) stateCounts[state] = stateCounts.GetValueOrDefault(state) + 1;
            if (string.Equals(state, "revealed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id))
                revealedEpochs.Add(id);
            allEpochs.Add(JsonNode.Parse(e.ToJsonString())!);
        }

        var uniqueId = progress["unique_id"]?.GetValue<string>() ?? "";
        var characterStats = progress["character_stats"] as JsonArray ?? new JsonArray();
        var ancientStats = progress["ancient_stats"] as JsonArray ?? new JsonArray();
        var charsFromStats = ExtractCharactersFromStats(characterStats, mapper).OrderBy(x => x).ToList();
        var charsFromAncients = ExtractCharactersFromAncientStats(ancientStats, mapper).OrderBy(x => x).ToList();
        var probablyPlayable = charsFromStats
            .Where(c => !c.Equals("RandomCharacter", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var discovered = new JsonObject
        {
            ["acts_raw"] = CopyStringArray(progress["discovered_acts"]),
            ["cards_raw"] = CopyStringArray(progress["discovered_cards"]),
            ["events_raw"] = CopyStringArray(progress["discovered_events"]),
            ["potions_raw"] = CopyStringArray(progress["discovered_potions"]),
            ["relics_raw"] = CopyStringArray(progress["discovered_relics"]),
            ["acts"] = NormalizeStringArray(progress["discovered_acts"], mapper),
            ["cards"] = NormalizeStringArray(progress["discovered_cards"], mapper),
            ["events"] = NormalizeStringArray(progress["discovered_events"], mapper),
            ["potions"] = NormalizeStringArray(progress["discovered_potions"], mapper),
            ["relics"] = NormalizeStringArray(progress["discovered_relics"], mapper),
        };

        var counts = new JsonObject
        {
            ["ancient_stats"] = CountArray(progress["ancient_stats"]),
            ["card_stats"] = CountArray(progress["card_stats"]),
            ["character_stats"] = CountArray(progress["character_stats"]),
            ["encounter_stats"] = CountArray(progress["encounter_stats"]),
            ["enemy_stats"] = CountArray(progress["enemy_stats"]),
            ["epochs"] = CountArray(progress["epochs"]),
            ["discovered_acts"] = CountArray(progress["discovered_acts"]),
            ["discovered_cards"] = CountArray(progress["discovered_cards"]),
            ["discovered_events"] = CountArray(progress["discovered_events"]),
            ["discovered_potions"] = CountArray(progress["discovered_potions"]),
            ["discovered_relics"] = CountArray(progress["discovered_relics"]),
            ["ftue_completed"] = CountArray(progress["ftue_completed"]),
            ["unlocked_achievements"] = CountArray(progress["unlocked_achievements"]),
        };

        var profile = new JsonObject
        {
            ["format"] = "sts2_progress_unlock_profile_v2",
            ["generated_from"] = new JsonObject
            {
                ["filename"] = Path.GetFileName(inputPath),
                ["source_is_plain_json"] = true,
                ["progress_schema_version"] = GetInt(progress, "schema_version"),
                ["generated_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
            ["identifiers"] = new JsonObject
            {
                ["unique_id"] = uniqueId,
                ["note"] = "保留 progress.save 里的 unique_id 供显示/关联 profile 使用；不要直接当作 STS2 Player.NetId。单人 NetId 仍是 1，多人 NetId 由联机/大厅玩家对象决定。"
            },
            ["unlocks"] = new JsonObject
            {
                ["schema_version"] = GetInt(progress, "schema_version"),
                ["total_unlocks"] = GetInt(progress, "total_unlocks"),
                ["pending_character_unlock"] = progress["pending_character_unlock"]?.GetValue<string>() ?? "",
                ["epochs_state_counts"] = ToJsonObject(stateCounts),
                ["revealed_epoch_ids"] = revealedEpochs,
                ["all_epochs"] = allEpochs,
            },
            ["characters"] = new JsonObject
            {
                ["from_character_stats"] = ToJsonArray(charsFromStats),
                ["from_ancient_stats"] = ToJsonArray(charsFromAncients),
                ["probably_playable_for_unlock_state"] = ToJsonArray(probablyPlayable),
            },
            ["discovered"] = discovered,
            ["counts"] = counts,
            ["raw_profile_numbers"] = new JsonObject
            {
                ["current_score"] = GetInt(progress, "current_score"),
                ["floors_climbed"] = GetInt(progress, "floors_climbed"),
                ["total_playtime"] = GetInt(progress, "total_playtime"),
                ["max_multiplayer_ascension"] = GetInt(progress, "max_multiplayer_ascension"),
                ["preferred_multiplayer_ascension"] = GetInt(progress, "preferred_multiplayer_ascension"),
                ["wongo_points"] = GetInt(progress, "wongo_points"),
                ["test_subject_kills"] = GetInt(progress, "test_subject_kills"),
            },
            ["prediction_hints"] = new JsonObject
            {
                ["unlock_state"] = new JsonObject
                {
                    ["known_source"] = "UnlockState.Characters is derived from revealed epochs. CardPoolModel.FilterThroughEpochs removes epoch-locked cards. static_model_lists contains Epoch.Cards / Epoch.Relics / Epoch.Potions arrays.",
                    ["backend_action"] = "后端按 revealed_epoch_ids 复刻 Characters/CharacterCardPools、Kaleidoscope、Underdocks、Darv，并用 static_model_lists 过滤卡牌/遗物/药水的 Epoch 解锁项。"
                },
                ["kaleidoscope_check"] = new JsonObject
                {
                    ["known_relevant_condition_from_code"] = "Kaleidoscope.IsAllowedAtNeow requires player.UnlockState.CharacterCardPools.Count == ModelDb.AllCharacters.Count.",
                    ["backend_action"] = "用 revealed epochs 计算 UnlockState.Characters，而不是用 discovered_cards 或统计记录直接判断。"
                },
                ["net_id"] = new JsonObject
                {
                    ["note"] = "progress.save 的 unique_id 不是 Player.NetId。单人 net_id 固定为 1；多人 net_id 应来自实际联机玩家对象或游戏源码中的 Owner.NetId。"
                }
            }
        };
        return profile;
    }

    private static IEnumerable<string> ExtractCharactersFromStats(JsonArray stats, ModelIdMapper mapper)
    {
        foreach (var item in stats)
        {
            string raw = item?["character"]?.GetValue<string>() ?? "";
            if (!string.IsNullOrWhiteSpace(raw)) yield return mapper.Normalize(raw);
        }
    }

    private static IEnumerable<string> ExtractCharactersFromAncientStats(JsonArray ancientStats, ModelIdMapper mapper)
    {
        foreach (var a in ancientStats)
        {
            if (a?["character_stats"] is not JsonArray cs) continue;
            foreach (var item in cs)
            {
                string raw = item?["character"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(raw)) yield return mapper.Normalize(raw);
            }
        }
    }

    private static int CountArray(JsonNode? node) => node is JsonArray arr ? arr.Count : 0;


    private static int GetInt(JsonObject obj, string key)
    {
        if (obj[key] is JsonValue v && v.TryGetValue<int>(out var n)) return n;
        if (obj[key] is JsonValue vs && vs.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return 0;
    }

    private static JsonArray CopyStringArray(JsonNode? node)
    {
        var arr = new JsonArray();
        if (node is JsonArray xs)
            foreach (var x in xs) if (x is not null) arr.Add(x.GetValue<string>());
        return arr;
    }

    private static JsonArray NormalizeStringArray(JsonNode? node, ModelIdMapper mapper)
    {
        var arr = new JsonArray();
        if (node is JsonArray xs)
            foreach (var x in xs)
                if (x is not null) arr.Add(mapper.Normalize(x.GetValue<string>()));
        return arr;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var x in items.Distinct(StringComparer.OrdinalIgnoreCase)) arr.Add(x);
        return arr;
    }

    private static JsonObject ToJsonObject(Dictionary<string, int> dict)
    {
        var obj = new JsonObject();
        foreach (var kv in dict.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)) obj[kv.Key] = kv.Value;
        return obj;
    }

    private sealed class ModelIdMapper
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        public static ModelIdMapper Load(string dataPath)
        {
            var mapper = new ModelIdMapper();
            mapper.AddKnownCharacters();
            if (File.Exists(dataPath))
            {
                try
                {
                    var data = JsonNode.Parse(File.ReadAllText(dataPath, Encoding.UTF8));
                    mapper.AddCategory(data, "cards", "CARD");
                    mapper.AddCategory(data, "relics", "RELIC");
                    mapper.AddCategory(data, "potions", "POTION");
                    mapper.AddCharacterNames(data);
                }
                catch { }
            }
            return mapper;
        }

        public string Normalize(string raw)
        {
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return raw;
            if (_map.TryGetValue(raw, out var id)) return id;
            int dot = raw.IndexOf('.');
            if (dot >= 0 && dot + 1 < raw.Length)
                return SnakeToPascal(raw[(dot + 1)..]);
            return raw;
        }

        private void AddCategory(JsonNode? data, string category, string prefix)
        {
            if (data?[category] is not JsonObject obj) return;
            foreach (var kv in obj)
            {
                string internalId = kv.Key;
                string external = prefix + "." + PascalToSnake(internalId);
                _map[external] = internalId;
                _map[internalId] = internalId;
            }
        }

        private void AddCharacterNames(JsonNode? data)
        {
            if (data?["character_names"] is not JsonObject obj) return;
            foreach (var kv in obj)
            {
                string internalId = kv.Key;
                _map["CHARACTER." + PascalToSnake(internalId)] = internalId;
                _map[internalId] = internalId;
            }
        }

        private void AddKnownCharacters()
        {
            foreach (var id in new[] { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent", "Watcher", "WatcherV2", "RandomCharacter" })
            {
                _map["CHARACTER." + PascalToSnake(id)] = id;
                _map[id] = id;
            }
        }

        private static string PascalToSnake(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            var sb = new StringBuilder();
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(id[i - 1]) || (i + 1 < id.Length && char.IsLower(id[i + 1])))) sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        private static string SnakeToPascal(string snake)
        {
            var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p[1..].ToLowerInvariant());
            }
            return sb.ToString();
        }
    }
}



public sealed class UnlockProfile
{
    public bool Enabled { get; private set; }
    public string Mode { get; private set; } = "all_unlocked";
    public string PathText { get; private set; } = "";
    public string UniqueId { get; private set; } = "";
    public int SchemaVersion { get; private set; }
    public int TotalUnlocks { get; private set; }
    public List<string> RevealedEpochIds { get; private set; } = new();
    public List<string> PlayableCharacters { get; private set; } = new();
    public List<string> CharactersFromStats { get; private set; } = new();
    public List<string> IgnoredModCharacters { get; private set; } = new();
    public bool HasRandomCharacterStat { get; private set; }
    public List<string> DiscoveredCards { get; private set; } = new();
    public List<string> DiscoveredRelics { get; private set; } = new();
    public List<string> DiscoveredPotions { get; private set; } = new();
    public List<string> DiscoveredEvents { get; private set; } = new();
    public List<string> DiscoveredActs { get; private set; } = new();

    // 与当前工具数据文件一致的角色卡池集合。Ironclad 在 UnlockState.Characters 中默认存在；
    // Silent/Regent/Necrobinder/Defect 由对应 1 号 Epoch 控制。
    private static readonly string[] ToolCharacterPools = { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent" };
    private static readonly HashSet<string> ToolCharacterPoolSet = ToolCharacterPools.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static UnlockProfile AllUnlocked() => new UnlockProfile { Enabled = false, Mode = "all_unlocked" };

    public static bool IsMultiplayerConfig(JsonElement root)
    {
        var player = root.Prop("player");
        string runMode = root.Prop("run_mode").Str("").Trim().ToLowerInvariant();
        int playersCount = player.Prop("players_count").Int(1);
        return runMode == "multiplayer" || (runMode != "singleplayer" && playersCount > 1);
    }

    public static string MultiplayerUnlockMode(JsonElement root)
        => (root.Prop("multiplayer_unlock_mode").Str("all_unlocked") ?? "all_unlocked").Trim().ToLowerInvariant();

    public static UnlockProfile FromConfigEffective(JsonElement root, string baseDir)
    {
        // v17.2 stable-plus：单人优先使用 progress.save；找不到/不导入则自动全解锁。
        // 多人默认 all_unlocked，因为源码显示 Act 列表使用全队 UnlockState 并集，
        // 而每个 Player 又使用各自 unlockState；工具通常拿不到其他玩家 progress.save。
        // 若用户明确设置 multiplayer_unlock_mode=profile，则允许多人使用当前 profile 做个人奖励近似。
        if (IsMultiplayerConfig(root) && MultiplayerUnlockMode(root) != "profile")
            return AllUnlocked();
        return FromConfig(root, baseDir);
    }

    public static UnlockProfile FromConfig(JsonElement root, string baseDir)
    {
        var up = root.Prop("unlock_profile");
        string mode = up.Prop("mode").Str("all_unlocked") ?? "all_unlocked";
        if (!string.Equals(mode, "profile", StringComparison.OrdinalIgnoreCase))
            return AllUnlocked();

        string rel = up.Prop("path").Str("profiles/unlock_profile.json") ?? "profiles/unlock_profile.json";
        string full = System.IO.Path.IsPathRooted(rel) ? rel : System.IO.Path.Combine(baseDir, rel);
        if (!File.Exists(full))
            return new UnlockProfile { Enabled = false, Mode = "profile", PathText = rel };

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(full, Encoding.UTF8));
            var r = doc.RootElement;
            var p = new UnlockProfile
            {
                Enabled = true,
                Mode = "profile",
                PathText = rel,
                UniqueId = r.Prop("identifiers").Prop("unique_id").Str(""),
                SchemaVersion = r.Prop("unlocks").Prop("schema_version").Int(0),
                TotalUnlocks = r.Prop("unlocks").Prop("total_unlocks").Int(0),
                RevealedEpochIds = r.Prop("unlocks").Prop("revealed_epoch_ids").StringList(),
                CharactersFromStats = r.Prop("characters").Prop("probably_playable_for_unlock_state").StringList(),
                DiscoveredCards = r.Prop("discovered").Prop("cards").StringList(),
                DiscoveredRelics = r.Prop("discovered").Prop("relics").StringList(),
                DiscoveredPotions = r.Prop("discovered").Prop("potions").StringList(),
                DiscoveredEvents = r.Prop("discovered").Prop("events").StringList(),
                DiscoveredActs = r.Prop("discovered").Prop("acts").StringList(),
            };
            p.RevealedEpochIds = p.RevealedEpochIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            p.CharactersFromStats = p.CharactersFromStats
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            p.HasRandomCharacterStat = p.CharactersFromStats.Any(x => x.Equals("RandomCharacter", StringComparison.OrdinalIgnoreCase));
            p.IgnoredModCharacters = p.CharactersFromStats
                .Where(x => !x.Equals("RandomCharacter", StringComparison.OrdinalIgnoreCase))
                .Where(x => !ToolCharacterPoolSet.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            p.PlayableCharacters = p.BuildUnlockStateCharacters();
            return p;
        }
        catch
        {
            return new UnlockProfile { Enabled = false, Mode = "profile", PathText = rel };
        }
    }

    public bool IsEpochRevealed(string epochId)
    {
        if (!Enabled) return true;
        return RevealedEpochIds.Any(x => string.Equals(x, epochId, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> BuildUnlockStateCharacters()
    {
        // 复刻源码 UnlockState.Characters：
        // list = ModelDb.AllCharacters;
        // 缺少 Silent1/Regent1/Necrobinder1/Defect1 Epoch 时，从列表移除对应角色。
        var xs = ToolCharacterPools.ToList();
        if (!IsEpochRevealed("SILENT1_EPOCH")) xs.RemoveAll(x => x.Equals("Silent", StringComparison.OrdinalIgnoreCase));
        if (!IsEpochRevealed("REGENT1_EPOCH")) xs.RemoveAll(x => x.Equals("Regent", StringComparison.OrdinalIgnoreCase));
        if (!IsEpochRevealed("NECROBINDER1_EPOCH")) xs.RemoveAll(x => x.Equals("Necrobinder", StringComparison.OrdinalIgnoreCase));
        if (!IsEpochRevealed("DEFECT1_EPOCH")) xs.RemoveAll(x => x.Equals("Defect", StringComparison.OrdinalIgnoreCase));
        return xs;
    }

    public bool HasCharacter(string character)
    {
        if (!Enabled) return true;
        if (PlayableCharacters.Count == 0) return true;
        return PlayableCharacters.Any(c => string.Equals(c, character, StringComparison.OrdinalIgnoreCase));
    }

    public List<string> FilterCharacters(IEnumerable<string> candidates)
    {
        var xs = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!Enabled || PlayableCharacters.Count == 0) return xs;
        return xs.Where(HasCharacter).ToList();
    }

    public bool HasDiscoveredRelic(string relicId)
    {
        if (!Enabled) return true;
        return DiscoveredRelics.Any(x => string.Equals(x, relicId, StringComparison.OrdinalIgnoreCase));
    }

    public bool AllowsKaleidoscope()
    {
        // 复刻源码 Kaleidoscope.IsAllowedAtNeow：
        // player.UnlockState.CharacterCardPools.Count == ModelDb.AllCharacters.Count。
        // 之前新档误判是前端/后端复用了旧 unlock_profile.json，不是万花筒额外需要 total_unlocks。
        if (!Enabled) return true;
        return ToolCharacterPools.All(HasCharacter);
    }

    public bool AllowsSharedAncient(string ancientId)
    {
        if (!Enabled) return true;
        if (ancientId.Equals("Darv", StringComparison.OrdinalIgnoreCase))
            return IsEpochRevealed("DARV_EPOCH");
        return true;
    }

    public bool AllowsAncientEvent(string ancientId)
    {
        if (!Enabled) return true;
        if (ancientId.Equals("Orobas", StringComparison.OrdinalIgnoreCase))
            return IsEpochRevealed("OROBAS_EPOCH");
        // 目前源码/进度里只确认 Orobas、Darv 有独立 Ancient Epoch。
        // Darv 属于 SharedAncients，走 AllowsSharedAncient；其它先古暂按默认可用处理。
        return true;
    }

    public bool AllowsEvent(string eventId)
    {
        if (!Enabled) return true;
        // Event Epoch 源码确认：
        // EVENT1_EPOCH -> TrashHeap
        // EVENT2_EPOCH -> Reflections
        // EVENT3_EPOCH -> ColorfulPhilosophers
        if (eventId.Equals("TrashHeap", StringComparison.OrdinalIgnoreCase))
            return IsEpochRevealed("EVENT1_EPOCH");
        if (eventId.Equals("Reflections", StringComparison.OrdinalIgnoreCase))
            return IsEpochRevealed("EVENT2_EPOCH");
        if (eventId.Equals("ColorfulPhilosophers", StringComparison.OrdinalIgnoreCase))
            return IsEpochRevealed("EVENT3_EPOCH");
        return true;
    }

    public bool AllowsUnderdocks()
    {
        if (!Enabled) return true;
        return IsEpochRevealed("UNDERDOCKS_EPOCH");
    }

    public bool StartsWithNeow()
    {
        // 源码 SetStartedWithNeowFlag(): StartedWithNeow = UnlockState.IsEpochRevealed<NeowEpoch>()。
        // all_unlocked 时视为所有 epoch reveal，因此 Act1 开局会先进入 Neow。
        if (!Enabled) return true;
        return IsEpochRevealed("NEOW_EPOCH");
    }

    public object ToSummary(string rootDir)
    {
        return new
        {
            enabled = Enabled,
            mode = Mode,
            path = PathText,
            unique_id = UniqueId,
            schema_version = SchemaVersion,
            total_unlocks = TotalUnlocks,
            revealed_epoch_count = RevealedEpochIds.Count,
            supported_character_mode = "vanilla_5_characters",
            supported_characters = ToolCharacterPools,
            playable_characters = PlayableCharacters,
            characters_from_stats = CharactersFromStats,
            ignored_mod_characters = IgnoredModCharacters,
            random_character_stat_present = HasRandomCharacterStat,
            mod_warning = IgnoredModCharacters.Count > 0
                ? "检测到模组/非原版角色：" + string.Join(" / ", IgnoredModCharacters) + "。当前版本只按原版 5 角色预测，这些角色会被忽略；测试准确性建议使用无 mod 存档。"
                : "",
            unlockstate = new
            {
                silent1 = IsEpochRevealed("SILENT1_EPOCH"),
                defect1 = IsEpochRevealed("DEFECT1_EPOCH"),
                regent1 = IsEpochRevealed("REGENT1_EPOCH"),
                necrobinder1 = IsEpochRevealed("NECROBINDER1_EPOCH"),
                underdocks = AllowsUnderdocks(),
                orobas = AllowsAncientEvent("Orobas"),
                darv = AllowsSharedAncient("Darv"),
                event1 = IsEpochRevealed("EVENT1_EPOCH"),
                event2 = IsEpochRevealed("EVENT2_EPOCH"),
                event3 = IsEpochRevealed("EVENT3_EPOCH"),
                neow = StartsWithNeow(),
                static_model_lists_epoch_filter_enabled = true,
                cards_filter_through_epochs = true,
                relics_filter_static_epoch_lists = true,
                potions_filter_static_epoch_lists = true,
                kaleidoscope_uses_source_condition = "CharacterCardPools.Count == ModelDb.AllCharacters.Count",
                kaleidoscope_discovered = HasDiscoveredRelic("Kaleidoscope"),
            },
            discovered = new
            {
                cards = DiscoveredCards.Count,
                relics = DiscoveredRelics.Count,
                potions = DiscoveredPotions.Count,
                events = DiscoveredEvents.Count,
                acts = DiscoveredActs.Count,
            },
            kaleidoscope_allowed_by_profile = AllowsKaleidoscope(),
            note = Enabled
                ? "v17.1 preview12 使用原版 5 角色模式；万花筒按源码只要求原版 5 角色卡池全部可用。导入 progress.save 前会清除旧 unlock_profile/imported_progress，避免复用旧全解锁档。static_model_lists 已接入 Epoch.Cards / Epoch.Relics / Epoch.Potions 过滤；revealed epochs 同时用于 Characters/CharacterCardPools、Underdocks、Orobas、Darv、Event1~3 事件池过滤。discovered_* 仍只展示，不当作通用解锁池。"
                : "当前使用默认全解锁模式：未导入 progress.save、找不到 unlock_profile.json，或多人模式默认采用全解锁近似。"
        };
    }
}


public static class ProgressSaveLocator
{
    public static List<ProgressSaveCandidate> FindCandidates()
    {
        var outList = new List<ProgressSaveCandidate>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData)) return outList;
        string steamRoot = System.IO.Path.Combine(appData, "SlayTheSpire2", "steam");
        if (!Directory.Exists(steamRoot)) return outList;

        foreach (var steamDir in Directory.EnumerateDirectories(steamRoot))
        {
            string steamId = System.IO.Path.GetFileName(steamDir);
            AddProfileCandidates(outList, steamDir, steamId, "vanilla");

            string moddedDir = System.IO.Path.Combine(steamDir, "modded");
            if (Directory.Exists(moddedDir)) AddProfileCandidates(outList, moddedDir, steamId, "modded");

        }

        return outList
            // v17.1 preview10：恢复按修改时间优先。
            // 用户可能频繁在 vanilla / modded / 多个 profile 间切换测试，自动导入应选择最近写入的 progress.save。
            .OrderByDescending(x => x.ModifiedUtc ?? DateTime.MinValue)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddProfileCandidates(List<ProgressSaveCandidate> xs, string parentDir, string steamId, string mode)
    {
        foreach (var profileDir in Directory.EnumerateDirectories(parentDir, "profile*"))
        {
            string progressPath = System.IO.Path.Combine(profileDir, "saves", "progress.save");
            if (!File.Exists(progressPath)) continue;
            FileInfo fi = new FileInfo(progressPath);
            xs.Add(new ProgressSaveCandidate(
                progressPath,
                steamId,
                mode,
                System.IO.Path.GetFileName(profileDir),
                fi.Length,
                fi.LastWriteTimeUtc
            ));
        }
    }
}


public sealed record ProgressSaveCandidate(string Path, string SteamId, string Mode, string Profile, long SizeBytes, DateTime? ModifiedUtc)
{
    public object ToDto() => new
    {
        path = Path,
        steam_id = SteamId,
        mode = Mode,
        profile = Profile,
        size_bytes = SizeBytes,
        modified_utc = ModifiedUtc?.ToString("O"),
        display = $"{Mode}/{Profile}  {Path}"
    };
}


public static class ProfileImportFiles
{
    public static void ClearBeforeImport(string profilesDir)
    {
        Directory.CreateDirectory(profilesDir);
        foreach (var name in new[]
        {
            "unlock_profile.json",
            "imported_progress.save",
            "unlock_profile_from_progress.json"
        })
        {
            TryDelete(Path.Combine(profilesDir, name));
        }
    }

    public static void SaveRawCopy(string sourcePath, string profilesDir)
    {
        try
        {
            if (!File.Exists(sourcePath)) return;
            string dst = Path.Combine(profilesDir, "imported_progress.save");
            string srcFull = Path.GetFullPath(sourcePath);
            string dstFull = Path.GetFullPath(dst);
            if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase)) return;
            File.Copy(srcFull, dstFull, overwrite: true);
        }
        catch
        {
            // 原始 progress.save 备份失败不应影响 unlock_profile 生成。
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}


