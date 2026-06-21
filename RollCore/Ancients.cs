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

public sealed class AncientTerm
{
    public int? ActNumber { get; init; }
    public string AncientId { get; init; } = "";

    public static List<AncientTerm> ParseMany(JsonElement? ancient)
    {
        var list = ancient.Prop("require_ancients").StringList();
        return list.Select(Parse).Where(t => t.AncientId.Length > 0).ToList();
    }

    public static List<AncientTerm> ParseBlacklist(JsonElement? ancient)
    {
        var list = ancient.Prop("blacklist_ancients").StringList();
        return list.Select(Parse).Where(t => t.AncientId.Length > 0).ToList();
    }

    public static AncientTerm Parse(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return new AncientTerm();
        var parts = raw.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
            return new AncientTerm { AncientId = Term.Normalize(raw) };

        int? actNum = ParseAct(parts[0]);
        string ancientId = Term.Normalize(string.Join(':', parts.Skip(1)));
        return new AncientTerm { ActNumber = actNum, AncientId = ancientId };
    }

    private static int? ParseAct(string value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("act")) value = value[3..];
        return int.TryParse(value, out var n) ? n : null;
    }

    public bool IsSatisfiedBy(IList<string> ancients)
    {
        for (int i = 0; i < ancients.Count; i++)
        {
            int actNo = i + 1;
            if (ActNumber is not null && ActNumber.Value != actNo) continue;
            if (Term.ItemMatches(ancients[i], AncientId)) return true;
        }
        return false;
    }
}


public sealed record ActSpec(int BaseRooms, int WeakCount, string[] Ancients, string[] Events, string[] Encounters);


public static class AncientPredictor
{
    private static readonly string[] DefaultActOrder = new[] { "Overgrowth", "Hive", "Glory" };
    private static readonly string[] UnderdocksActOrder = new[] { "Underdocks", "Hive", "Glory" };
    private static readonly string[] SharedAncients = new[] { "Darv" };
    private static readonly string[] AllSharedEvents = new[]
        {
            "BrainLeech",
            "CrystalSphere",
            "DollRoom",
            "FakeMerchant",
            "PotionCourier",
            "RanwidTheElder",
            "RelicTrader",
            "RoomFullOfCheese",
            "SelfHelpBook",
            "SlipperyBridge",
            "StoneOfAllTime",
            "Symbiote",
            "TeaMaster",
            "TheFutureOfPotions",
            "TheLegendsWereTrue",
            "ThisOrThat",
            "WarHistorianRepy",
            "WelcomeToWongos"
        };

    private static readonly Dictionary<string, ActSpec> Acts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Overgrowth"] = new ActSpec(
            BaseRooms: 15,
            WeakCount: 3,
            Ancients: new[]
        {
            "Neow"
        },
            Events: new[]
        {
            "AromaOfChaos",
            "ByrdonisNest",
            "DenseVegetation",
            "JungleMazeAdventure",
            "LuminousChoir",
            "MorphicGrove",
            "SapphireSeed",
            "SunkenStatue",
            "TabletOfTruth",
            "UnrestSite",
            "Wellspring",
            "WhisperingHollow",
            "WoodCarvings"
        },
            Encounters: new[]
        {
            "BygoneEffigyElite",
            "ByrdonisElite",
            "CeremonialBeastBoss",
            "CubexConstructNormal",
            "FlyconidNormal",
            "FogmogNormal",
            "FuzzyWurmCrawlerWeak",
            "InkletsNormal",
            "MawlerNormal",
            "NibbitsNormal",
            "NibbitsWeak",
            "OvergrowthCrawlers",
            "PhrogParasiteElite",
            "RubyRaidersNormal",
            "ShrinkerBeetleWeak",
            "SlimesNormal",
            "SlimesWeak",
            "SlitheringStranglerNormal",
            "SnappingJaxfruitNormal",
            "TheKinBoss",
            "VantomBoss",
            "VineShamblerNormal"
        }),
        ["Underdocks"] = new ActSpec(
            BaseRooms: 15,
            WeakCount: 3,
            Ancients: new[]
        {
            "Neow"
        },
            Events: new[]
        {
            "AbyssalBaths",
            "DrowningBeacon",
            "EndlessConveyor",
            "PunchOff",
            "SpiralingWhirlpool",
            "SunkenStatue",
            "SunkenTreasury",
            "DoorsOfLightAndDark",
            "TrashHeap",
            "WaterloggedScriptorium"
        },
            Encounters: new[]
        {
            "CorpseSlugsNormal",
            "CorpseSlugsWeak",
            "CultistsNormal",
            "FossilStalkerNormal",
            "GremlinMercNormal",
            "HauntedShipNormal",
            "LagavulinMatriarchBoss",
            "LivingFogNormal",
            "PhantasmalGardenersElite",
            "PunchConstructNormal",
            "SeapunkNormal",
            "SeapunkWeak",
            "SewerClamNormal",
            "SkulkingColonyElite",
            "SludgeSpinnerWeak",
            "SoulFyshBoss",
            "TerrorEelElite",
            "ToadpolesWeak",
            "TwoTailedRatsNormal",
            "WaterfallGiantBoss"
        }),
        ["Hive"] = new ActSpec(
            BaseRooms: 14,
            WeakCount: 2,
            Ancients: new[]
        {
            "Orobas",
            "Pael",
            "Tezcatara"
        },
            Events: new[]
        {
            "Amalgamator",
            "Bugslayer",
            "ColorfulPhilosophers",
            "ColossalFlower",
            "FieldOfManSizedHoles",
            "InfestedAutomaton",
            "LostWisp",
            "SpiritGrafter",
            "TheLanternKey",
            "ZenWeaver"
        },
            Encounters: new[]
        {
            "BowlbugsNormal",
            "BowlbugsWeak",
            "ChompersNormal",
            "DecimillipedeElite",
            "EntomancerElite",
            "ExoskeletonsNormal",
            "ExoskeletonsWeak",
            "HunterKillerNormal",
            "KaiserCrabBoss",
            "InfestedPrismsElite",
            "KnowledgeDemonBoss",
            "LouseProgenitorNormal",
            "MytesNormal",
            "OvicopterNormal",
            "SlumberingBeetleNormal",
            "SpinyToadNormal",
            "TheInsatiableBoss",
            "TheObscuraNormal",
            "ThievingHopperWeak",
            "TunnelerWeak"
        }),
        ["Glory"] = new ActSpec(
            BaseRooms: 13,
            WeakCount: 2,
            Ancients: new[]
        {
            "Nonupeipe",
            "Tanx",
            "Vakuu"
        },
            Events: new[]
        {
            "BattlewornDummy",
            "GraveOfTheForgotten",
            "HungryForMushrooms",
            "Reflections",
            "RoundTeaParty",
            "Trial",
            "TinkerTime"
        },
            Encounters: new[]
        {
            "AxebotsNormal",
            "ConstructMenagerieNormal",
            "DevotedSculptorWeak",
            "AeonglassBoss",
            "FabricatorNormal",
            "FrogKnightNormal",
            "GlobeHeadNormal",
            "KnightsElite",
            "MechaKnightElite",
            "OwlMagistrateNormal",
            "QueenBoss",
            "ScrollsOfBitingNormal",
            "ScrollsOfBitingWeak",
            "SlimedBerserkerNormal",
            "SoulNexusElite",
            "TestSubjectBoss",
            "TheLostAndForgottenNormal",
            "TurretOperatorWeak"
        })
    };

    public static ActSpec? FallbackActSpec(string actId) => Acts.TryGetValue(actId, out var spec) ? spec : null;

    private static readonly Dictionary<string, string[]> EncounterTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BowlbugsNormal"] = new[] { "Workers" },
        ["BowlbugsWeak"] = new[] { "Workers" },
        ["ChompersNormal"] = new[] { "Chomper" },
        ["CorpseSlugsNormal"] = new[] { "Slugs" },
        ["CorpseSlugsWeak"] = new[] { "Slugs" },
        ["ExoskeletonsNormal"] = new[] { "Exoskeletons" },
        ["ExoskeletonsWeak"] = new[] { "Exoskeletons" },
        ["FlyconidNormal"] = new[] { "Mushroom", "Slimes" },
        ["FuzzyWurmCrawlerWeak"] = new[] { "Crawler" },
        ["KnightsElite"] = new[] { "Knights" },
        ["NibbitsWeak"] = new[] { "Nibbit" },
        ["OvergrowthCrawlers"] = new[] { "Shrinker", "Crawler" },
        ["ScrollsOfBitingNormal"] = new[] { "Scrolls" },
        ["ScrollsOfBitingWeak"] = new[] { "Scrolls" },
        ["SeapunkNormal"] = new[] { "Seapunk" },
        ["SeapunkWeak"] = new[] { "Seapunk" },
        ["ShrinkerBeetleWeak"] = new[] { "Shrinker" },
        ["SlimesNormal"] = new[] { "Slimes" },
        ["SlimesWeak"] = new[] { "Slimes" },
        ["SlumberingBeetleNormal"] = new[] { "Workers" },
        ["SnappingJaxfruitNormal"] = new[] { "Mushroom" },
        ["ThievingHopperWeak"] = new[] { "Thieves" },
        ["TunnelerNormal"] = new[] { "Burrower", "Chomper" },
        ["TunnelerWeak"] = new[] { "Burrower" }
    };

    public static List<string> PredictAncients(string seed, SearchPlan plan)
    {
        string normalized = Sts2Math.NormalizeSeed(seed);
        var actOrder = GetActOrder(normalized, plan);
        var upFront = CreateUpFrontAfterInitializeNewRun(normalized, plan);
        var sharedAlloc = AllocateSharedAncients(upFront, actOrder, plan);
        var outList = new List<string>();
        for (int i = 0; i < actOrder.Count; i++)
        {
            string actId = actOrder[i];
            outList.Add(GenerateActAncient(upFront, actId, sharedAlloc.TryGetValue(actId, out var s) ? s : new List<string>(), plan.PlayersCount > 1, plan, i + 1));
        }
        return outList;
    }

    private static readonly Dictionary<string, string> EventConditionNotes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BrainLeech"] = "有 IsAllowed 覆写。具体条件需要继续从源码提取；v17.7 preview3 前可按“可能被跳过”人工核对。",
        ["CrystalSphere"] = "有 IsAllowed 覆写。具体条件需要继续从源码提取；v17.7 preview3 前可按“可能被跳过”人工核对。",
        ["FakeMerchant"] = "有 IsAllowed 覆写。通常与商店/购买/局内状态有关；具体阈值待源码提取。",
        ["MorphicGrove"] = "有 IsAllowed 覆写。可能与当前牌组或玩家状态有关；具体条件待源码提取。",
        ["PotionCourier"] = "有 IsAllowed 覆写。可能与药水栏/药水相关状态有关；具体条件待源码提取。",
        ["RelicTrader"] = "有 IsAllowed 覆写。可能与当前可交易遗物/遗物池状态有关；具体条件待源码提取。",
        ["WarHistorianRepy"] = "有 IsAllowed 覆写，并且可能被 LanternKey 的 ModifyNextEvent 强制改写；具体条件待源码提取。",
        ["SlipperyBridge"] = "条件：楼层 > 6；存在至少一张可移除牌；源码：SlipperyBridge.cs:82。",
        ["SLIPPERY_BRIDGE"] = "条件：楼层 > 6；存在至少一张可移除牌；源码：SlipperyBridge.cs:82。",
        ["TrashHeap"] = "受 EVENT1_EPOCH 控制；未 reveal 时建队列前移除。",
        ["Reflections"] = "受 EVENT2_EPOCH 控制；未 reveal 时建队列前移除。",
        ["ColorfulPhilosophers"] = "受 EVENT3_EPOCH 控制；未 reveal 时建队列前移除。",
    };

    public static string EventConditionNote(string eventId, SearchPlan? plan = null)
    {
        string dynamicNote = plan?.Data?.EventAllowedNote(eventId) ?? "";
        if (dynamicNote.Length > 0) return dynamicNote;
        return EventConditionNotes.TryGetValue(eventId ?? "", out var note) ? note : "";
    }

    public static bool HasEventConditionRule(string eventId, SearchPlan? plan = null)
    {
        if (plan?.Data?.HasEventAllowedRule(eventId) == true) return true;
        return EventConditionNotes.ContainsKey(eventId ?? "");
    }

    public static string EventQueueHint(string eventId, string actId, SearchPlan? plan)
    {
        var parts = new List<string>();
        string source = EventSourceLabel(eventId, actId, plan);
        if (source.Length > 0) parts.Add(source);
        if (HasEventConditionRule(eventId, plan)) parts.Add("条件事件");
        int actNumber = DisplayActNumberForActId(actId, plan);
        string skip = plan?.Data?.EventActSkipNote(eventId, actNumber) ?? "";
        if (skip.Length > 0) parts.Add(skip);
        return parts.Count == 0 ? "" : " [" + string.Join("；", parts) + "]";
    }

    private static string EventSourceLabel(string eventId, string actId, SearchPlan? plan)
    {
        var act = ActSpecForHint(actId, plan);
        bool inAct = act?.Events.Any(e => string.Equals(e, eventId, StringComparison.OrdinalIgnoreCase)) ?? false;
        bool inShared = SharedEventsForHint(plan).Any(e => string.Equals(e, eventId, StringComparison.OrdinalIgnoreCase));
        if (inAct && inShared) return "本Act事件+共享事件";
        if (inAct) return "本Act事件";
        if (inShared) return "SharedEvents共享事件";
        return "来源待核对：不在本Act事件池/SharedEvents";
    }

    private static int DisplayActNumberForActId(string actId, SearchPlan? plan)
    {
        if (string.IsNullOrWhiteSpace(actId)) return 0;
        if (actId.Equals("Overgrowth", StringComparison.OrdinalIgnoreCase) || actId.Equals("Underdocks", StringComparison.OrdinalIgnoreCase)) return 1;
        if (actId.Equals("Hive", StringComparison.OrdinalIgnoreCase)) return 2;
        if (actId.Equals("Glory", StringComparison.OrdinalIgnoreCase)) return 3;
        return 0;
    }

    private static ActSpec? ActSpecForHint(string actId, SearchPlan? plan)
    {
        if (plan?.Data is not null)
            return plan.Data.SourceActSpec(actId) ?? FallbackActSpec(actId);
        return FallbackActSpec(actId);
    }

    private static List<string> SharedEventsForHint(SearchPlan? plan)
    {
        if (plan?.Data is not null)
        {
            var xs = plan.Data.SourceSharedEvents();
            if (xs.Count > 0) return xs;
        }
        return AllSharedEvents.ToList();
    }


    public static List<MapBossBlock> PredictMapAndBoss(string seed, SearchPlan plan)
    {
        string normalized = Sts2Math.NormalizeSeed(seed);
        var actOrder = GetActOrder(normalized, plan);
        var upFront = CreateUpFrontAfterInitializeNewRun(normalized, plan);
        var sharedAlloc = AllocateSharedAncients(upFront, actOrder, plan);
        var outList = new List<MapBossBlock>();
        for (int i = 0; i < actOrder.Count; i++)
        {
            string actId = actOrder[i];
            var rooms = GenerateActRooms(upFront, actId, sharedAlloc.TryGetValue(actId, out var s) ? s : new List<string>(), plan.PlayersCount > 1, plan, i + 1);
            outList.Add(new MapBossBlock(i + 1, actId, rooms.BossId, rooms.NormalEncounters, rooms.EliteEncounters) { BossIds = rooms.BossIds });
        }
        return outList;
    }

    public static List<EventQueueBlock> PredictEventQueues(string seed, SearchPlan plan, int maxActNumber = 3)
    {
        string normalized = Sts2Math.NormalizeSeed(seed);
        var actOrder = GetActOrder(normalized, plan);
        var upFront = CreateUpFrontAfterInitializeNewRun(normalized, plan);
        var sharedAlloc = AllocateSharedAncients(upFront, actOrder, plan);
        var outList = new List<EventQueueBlock>();
        int actLimit = Math.Min(actOrder.Count, Math.Max(1, maxActNumber));
        for (int i = 0; i < actLimit; i++)
        {
            string actId = actOrder[i];
            var rooms = GenerateActRooms(upFront, actId, sharedAlloc.TryGetValue(actId, out var s) ? s : new List<string>(), plan.PlayersCount > 1, plan, i + 1);
            int startOffset = EventQueueStartOffsetForAct(i + 1, rooms.AncientId);
            string offsetReason = EventQueueStartOffsetReason(i + 1, rooms.AncientId, startOffset);
            outList.Add(new EventQueueBlock(i + 1, actId, rooms.EventQueue, startOffset, offsetReason));
        }
        return outList;
    }


    private static int EventQueueStartOffsetForAct(int actNumber, string ancientId)
    {
        // v2.0-preview3：实测确认 Act2 / Act3 也会出现“原始事件队列第一个事件被跳过”。
        // 机制与 Act1 Neow 相同：每一幕开头的先古房/先古事件会以 Event 房间类型计入
        // MarkRoomVisited(RoomType.Event)，因此该 Act 的普通事件读取从原始队列第 2 项开始。
        // 若未来某个特殊模式没有开幕先古，则不加偏移。
        return string.IsNullOrWhiteSpace(ancientId) ? 0 : 1;
    }

    private static string EventQueueStartOffsetReason(int actNumber, string ancientId, int startOffset)
    {
        if (startOffset <= 0) return "";
        string name = string.IsNullOrWhiteSpace(ancientId) ? "先古事件" : ancientId;
        return $"Act{actNumber} 开幕先古 {name} 会作为 Event 房间进入；进入后 MarkRoomVisited(RoomType.Event) 让 eventsVisited 先 +1，因此第一个普通事件从原始队列第 2 项开始。";
    }

    public static List<string> EventQueueReadOrder(EventQueueBlock q)
        => RotateEventQueue(q.Events, q.StartOffset);

    public static List<string> RotateEventQueue(IList<string> events, int startOffset)
    {
        if (events.Count == 0) return new List<string>();
        int offset = startOffset % events.Count;
        if (offset < 0) offset += events.Count;
        if (offset == 0) return events.ToList();
        return events.Skip(offset).Concat(events.Take(offset)).ToList();
    }

    public static string EventQueueOffsetNote(EventQueueBlock q, Func<string, string> eventName)
    {
        if (q.StartOffset <= 0 || q.Events.Count == 0) return "";
        int skippedCount = Math.Min(q.StartOffset, q.Events.Count);
        var skipped = q.Events.Take(skippedCount).Select(eventName).ToList();
        string skippedText = skipped.Count > 0 ? "；原始队首被跳过：" + string.Join(" / ", skipped) : "";
        return "起始偏移 +" + q.StartOffset + "：" + q.StartOffsetReason + skippedText;
    }

    private static List<string> GetActOrder(string seed, SearchPlan plan)
    {
        // v2.0-preview2：复刻源码 StartRunLobby.BeginRunLocally + ActModel.GetRandomList。
        // Act 选择不是 RunRng.UpFront，而是独立 stream：new Rng(hash(seed), "act_selection")。
        // 关键修正：Act1 pool 顺序为 [Overgrowth, Underdocks]，源码使用 NextItem(list2)，
        // 不是 NextBool 后反向解释；候选数为 1 时 NextItem(1) 仍会消耗，但 act_selection
        // 不再被后续房间生成链复用，所以这里只保留语义完整性。
        var rng = plan.RngVersion == GameRngVersion.Sts2V107Xoshiro
            ? new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "act_selection", version: plan.RngVersion)
            : new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), version: plan.RngVersion);

        var buckets = new List<List<string>>
        {
            new() { "Overgrowth", "Underdocks" },
            new() { "Hive" },
            new() { "Glory" }
        };

        bool isMultiplayer = plan.PlayersCount > 1;
        var result = new List<string>();
        foreach (var bucket in buckets)
        {
            var candidates = new List<string>();
            string? forced = null;
            foreach (var act in bucket)
            {
                if (!IsActUnlocked(act, plan)) continue;

                // 源码：未发现的非默认 Act 在单人、非 TestMode 下会强制出现，并且不消耗本 bucket RNG。
                // all_unlocked 模式没有真实 discovered acts，按“已发现”处理，避免错误强制 Underdocks。
                if (!IsDefaultAct(act) && !isMultiplayer && plan.Unlocks.Enabled && !IsActDiscovered(act, plan))
                {
                    forced = act;
                    break;
                }

                candidates.Add(act);
            }

            if (forced is not null)
            {
                result.Add(forced);
            }
            else if (candidates.Count > 0)
            {
                result.Add(rng.NextItem(candidates) ?? candidates[0]);
            }
            else
            {
                // 防御性回退：理论上源码 bucket 至少有一个 default act。
                result.Add(bucket[0]);
            }
        }

        string overrideAct1 = NormalizeActOverride(
            plan.Root.Prop("act_selection").Prop("act1_override").Str(
                plan.Root.Prop("act1_override").Str(plan.Root.Prop("force_act1").Str(""))));
        if (overrideAct1.Length > 0) result[0] = overrideAct1;
        return result;
    }

    private static bool IsDefaultAct(string actId)
        => actId.Equals("Overgrowth", StringComparison.OrdinalIgnoreCase)
        || actId.Equals("Hive", StringComparison.OrdinalIgnoreCase)
        || actId.Equals("Glory", StringComparison.OrdinalIgnoreCase);

    private static bool IsActUnlocked(string actId, SearchPlan plan)
    {
        if (actId.Equals("Underdocks", StringComparison.OrdinalIgnoreCase)) return plan.Unlocks.AllowsUnderdocks();
        return true;
    }

    private static bool IsActDiscovered(string actId, SearchPlan plan)
    {
        if (!plan.Unlocks.Enabled) return true;
        return plan.Unlocks.DiscoveredActs.Any(x => ActTermMatches(x, actId));
    }

    private static bool ActTermMatches(string raw, string actId)
    {
        string n = Term.Normalize(raw);
        if (n.Equals(actId, StringComparison.OrdinalIgnoreCase)) return true;
        if (actId.Equals("Overgrowth", StringComparison.OrdinalIgnoreCase)) return n is "OVERGROWTH" or "ACT.OVERGROWTH" or "ACT_OVERGROWTH" or "密林";
        if (actId.Equals("Underdocks", StringComparison.OrdinalIgnoreCase)) return n is "UNDERDOCKS" or "ACT.UNDERDOCKS" or "ACT_UNDERDOCKS" or "暗港" or "下水道";
        if (actId.Equals("Hive", StringComparison.OrdinalIgnoreCase)) return n is "HIVE" or "ACT.HIVE" or "ACT_HIVE" or "蜂巢";
        if (actId.Equals("Glory", StringComparison.OrdinalIgnoreCase)) return n is "GLORY" or "ACT.GLORY" or "ACT_GLORY" or "辉耀";
        return false;
    }

    private static string NormalizeActOverride(string? raw)
    {
        string n = Term.Normalize(raw ?? "");
        if (n.Length == 0) return "";
        if (n is "OVERGROWTH" or "ACT.OVERGROWTH" or "ACT_OVERGROWTH" or "密林") return "Overgrowth";
        if (n is "UNDERDOCKS" or "ACT.UNDERDOCKS" or "ACT_UNDERDOCKS" or "暗港" or "下水道") return "Underdocks";
        if (n is "HIVE" or "ACT.HIVE" or "ACT_HIVE" or "蜂巢") return "Hive";
        if (n is "GLORY" or "ACT.GLORY" or "ACT_GLORY" or "辉耀") return "Glory";
        return "";
    }

    private static Sts2Rng CreateUpFrontAfterInitializeNewRun(string seed, SearchPlan plan)
    {
        var data = plan.Data ?? throw new InvalidOperationException("Ancient filter needs GameData.");
        var upFront = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "up_front", version: plan.RngVersion);
        var sharedPool = data.RelicPool("SharedRelicPool", plan.Unlocks);
        BuildSharedGroups(sharedPool, data, upFront);
        foreach (var player in plan.PlayersOrder)
        {
            string poolName = data.CharacterRelicPool(player.Character);
            var combined = new List<string>(sharedPool.Count + data.RelicPool(poolName, plan.Unlocks).Count);
            combined.AddRange(sharedPool);
            combined.AddRange(data.RelicPool(poolName, plan.Unlocks));
            BuildPlayerGroups(combined, data, upFront);
        }
        return upFront;
    }

    private static OrderedRelicGroups BuildSharedGroups(IEnumerable<string> relicIds, GameData data, Sts2Rng rng)
    {
        var groups = new OrderedRelicGroups();
        foreach (var relic in relicIds)
        {
            var rarity = data.RelicRarity(relic);
            if (string.IsNullOrEmpty(rarity)) continue;
            groups.Add(rarity!, relic);
        }
        groups.ShuffleAll(rng);
        return groups;
    }

    private static OrderedRelicGroups BuildPlayerGroups(IEnumerable<string> relicIds, GameData data, Sts2Rng rng)
    {
        var groups = new OrderedRelicGroups();
        foreach (var relic in relicIds)
        {
            var rarity = data.RelicRarity(relic);
            if (rarity is not ("Common" or "Uncommon" or "Rare" or "Shop")) continue;
            groups.Add(rarity, relic);
        }
        groups.ShuffleAll(rng);
        return groups;
    }

    private static Dictionary<string, List<string>> AllocateSharedAncients(Sts2Rng rng, IList<string> actOrder, SearchPlan plan)
    {
        // 复刻 UnlockState.SharedAncients：DarvEpoch 未 reveal 时移除 Darv。
        var shared = SharedAncientsFor(plan).Where(plan.Unlocks.AllowsSharedAncient).ToList();
        rng.Shuffle(shared); // 当前只有 Darv，长度 1 不消耗；保留源码结构。
        var allocated = actOrder.ToDictionary(x => x, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var actId in actOrder.Skip(1))
        {
            int count = rng.NextInt(shared.Count + 1); // NextInt(1) 也消耗。
            var subset = shared.Take(count).ToList();
            allocated[actId] = subset;
            if (subset.Count > 0)
            {
                var set = subset.ToHashSet(StringComparer.OrdinalIgnoreCase);
                shared = shared.Where(x => !set.Contains(x)).ToList();
            }
        }
        return allocated;
    }

    private sealed record GeneratedActRooms(List<string> EventQueue, string AncientId, List<string> NormalEncounters, List<string> EliteEncounters, string BossId, List<string> BossIds);

    private static string GenerateActAncient(Sts2Rng rng, string actId, IList<string> sharedAncientSubset, bool isMultiplayer, SearchPlan plan, int actNumber = 0)
        => GenerateActRooms(rng, actId, sharedAncientSubset, isMultiplayer, plan, actNumber).AncientId;

    private static GeneratedActRooms GenerateActRooms(Sts2Rng rng, string actId, IList<string> sharedAncientSubset, bool isMultiplayer, SearchPlan plan, int actNumber = 0)
    {
        var act = ActFor(plan, actId);
        // 事件池会先 shuffle。v17.7 preview3 把这个原始队列保存并输出，并标注来源/条件；
        // 实际进事件房时仍可能因 IsAllowed/VisitedEventIds/Hook 被顺序跳过或改写。
        var events = act.Events.Concat(SharedEventsFor(plan))
            .Where(plan.Unlocks.AllowsEvent)
            .ToList();
        rng.Shuffle(events);

        var normal = new List<string>();
        var weakBag = new List<string>();
        for (int i = 0; i < act.WeakCount; i++)
        {
            if (weakBag.Count == 0) weakBag.AddRange(AllWeakEncounters(actId, plan));
            AddWithoutRepeatingTags(normal, weakBag, rng, plan);
        }

        var regularBag = new List<string>();
        for (int i = act.WeakCount; i < GetNumberOfRooms(actId, isMultiplayer, plan); i++)
        {
            if (regularBag.Count == 0) regularBag.AddRange(AllRegularEncounters(actId, plan));
            AddWithoutRepeatingTags(normal, regularBag, rng, plan);
        }

        var elites = new List<string>();
        var eliteBag = new List<string>();
        for (int i = 0; i < 15; i++)
        {
            if (eliteBag.Count == 0) eliteBag.AddRange(AllEliteEncounters(actId, plan));
            AddWithoutRepeatingTags(elites, eliteBag, rng, plan);
        }

        var bossPool = AllBossEncounters(actId, plan);
        string boss = rng.NextItem(bossPool) ?? "";
        var bosses = new List<string>();
        if (!string.IsNullOrWhiteSpace(boss)) bosses.Add(boss);

        // 先古身份本身也受 progress Epoch 控制：
        // 目前确认 Orobas 需要 OROBAS_EPOCH；Darv 作为 shared ancient 由 AllocateSharedAncients 过滤。
        // 源码顺序为 first boss -> ancient -> A10 final-act second boss。
        // 第二 Boss 不能放在 Ancient 之前，否则会把 Act3 Ancient 的 UpFront RNG 推偏。
        var ancientPool = act.Ancients
            .Where(plan.Unlocks.AllowsAncientEvent)
            .Concat(sharedAncientSubset)
            .ToList();
        string ancient = rng.NextItem(ancientPool) ?? ancientPool.FirstOrDefault() ?? "";

        int ascension = plan.Root.Prop("player").Prop("ascension").Int(0);
        if (ascension >= 10 && actNumber == 3 && bossPool.Count > 1)
        {
            var secondPool = bossPool.Where(x => !x.Equals(boss, StringComparison.OrdinalIgnoreCase)).ToList();
            string secondBoss = rng.NextItem(secondPool.Count > 0 ? secondPool : bossPool) ?? "";
            if (!string.IsNullOrWhiteSpace(secondBoss)) bosses.Add(secondBoss);
        }
        return new GeneratedActRooms(events, ancient, normal, elites, boss, bosses);
    }

    private static int GetNumberOfRooms(string actId, bool isMultiplayer, SearchPlan plan)
    {
        int n = ActFor(plan, actId).BaseRooms;
        if (isMultiplayer) n--;
        return n;
    }

    private static void AddWithoutRepeatingTags(List<string> encounters, List<string> bag, Sts2Rng rng, SearchPlan plan)
    {
        string? last = encounters.Count > 0 ? encounters[^1] : null;
        bool Pred(string e) => !SharesTags(e, last, plan) && !string.Equals(e, last, StringComparison.OrdinalIgnoreCase);
        var encounter = GrabAndRemove(bag, rng, Pred) ?? GrabAndRemove(bag, rng, null);
        if (encounter != null) encounters.Add(encounter);
    }

    private static string? GrabAndRemove(List<string> entries, Sts2Rng rng, Func<string, bool>? pred)
    {
        if (entries.Count == 0) return null;
        if (pred != null && !entries.Any(pred)) return null;
        while (true)
        {
            int index = GrabIndex(entries, rng);
            if (index < 0) return null;
            if (pred == null || pred(entries[index]))
            {
                string item = entries[index];
                entries.RemoveAt(index);
                return item;
            }
        }
    }

    private static int GrabIndex(List<string> entries, Sts2Rng rng)
    {
        if (entries.Count == 0) return -1;
        double num = rng.NextDouble() * entries.Count;
        double total = 0.0;
        for (int i = 0; i < entries.Count; i++)
        {
            total += 1.0;
            if (num < total) return i;
        }
        return -1;
    }

    private static bool SharesTags(string? a, string? b, SearchPlan plan)
    {
        if (a == null || b == null) return false;
        var ta = plan.Data?.SourceEncounterTags(a) ?? (EncounterTags.TryGetValue(a, out var aa) ? aa : Array.Empty<string>());
        var tb = plan.Data?.SourceEncounterTags(b) ?? (EncounterTags.TryGetValue(b, out var bb) ? bb : Array.Empty<string>());
        return ta.Intersect(tb).Any();
    }

    private static string RoomType(string encounter)
    {
        if (encounter.EndsWith("Boss", StringComparison.Ordinal)) return "Boss";
        if (encounter.EndsWith("Elite", StringComparison.Ordinal)) return "Elite";
        return "Monster";
    }

    private static bool IsWeak(string encounter) => encounter.EndsWith("Weak", StringComparison.Ordinal);
    private static ActSpec ActFor(SearchPlan plan, string actId) => plan.Data?.SourceActSpec(actId) ?? Acts[actId];
    private static List<string> SharedEventsFor(SearchPlan plan)
    {
        var xs = plan.Data?.SourceSharedEvents() ?? new List<string>();
        return xs.Count > 0 ? xs : AllSharedEvents.ToList();
    }
    private static List<string> SharedAncientsFor(SearchPlan plan)
    {
        var xs = plan.Data?.SourceSharedAncients() ?? new List<string>();
        return xs.Count > 0 ? xs : SharedAncients.ToList();
    }
    private static List<string> AllWeakEncounters(string actId, SearchPlan plan) => ActFor(plan, actId).Encounters.Where(e => RoomType(e) == "Monster" && IsWeak(e)).ToList();
    private static List<string> AllRegularEncounters(string actId, SearchPlan plan) => ActFor(plan, actId).Encounters.Where(e => RoomType(e) == "Monster" && !IsWeak(e)).ToList();
    private static List<string> AllEliteEncounters(string actId, SearchPlan plan) => ActFor(plan, actId).Encounters.Where(e => RoomType(e) == "Elite").ToList();
    private static List<string> AllBossEncounters(string actId, SearchPlan plan)
    {
        var pool = ActFor(plan, actId).Encounters.Where(e => RoomType(e) == "Boss").ToList();
        return ApplySourceBossPoolOrder(actId, pool);
    }

    private static List<string> ApplySourceBossPoolOrder(string actId, List<string> pool)
    {
        // Source audit v17.10-preview11: boss RNG pool order comes from GenerateAllEncounters(),
        // not BossDiscoveryOrder.  The extractor data may contain discovery-order buckets, so normalize here.
        string[] order = actId.Equals("Overgrowth", StringComparison.OrdinalIgnoreCase)
            ? new[] { "CeremonialBeastBoss", "TheKinBoss", "VantomBoss" }
            : actId.Equals("Hive", StringComparison.OrdinalIgnoreCase)
                ? new[] { "KaiserCrabBoss", "KnowledgeDemonBoss", "TheInsatiableBoss" }
                : actId.Equals("Glory", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "AeonglassBoss", "QueenBoss", "TestSubjectBoss" }
                    : actId.Equals("Underdocks", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "LagavulinMatriarchBoss", "SoulFyshBoss", "WaterfallGiantBoss" }
                        : Array.Empty<string>();
        if (order.Length == 0 || pool.Count == 0) return pool;
        var byId = pool.ToDictionary(x => x, x => x, StringComparer.OrdinalIgnoreCase);
        var sorted = order.Where(byId.ContainsKey).Select(x => byId[x]).ToList();
        sorted.AddRange(pool.Where(x => !sorted.Any(y => y.Equals(x, StringComparison.OrdinalIgnoreCase))));
        return sorted;
    }
}



public sealed class AncientOptionTerm
{
    public int? ActNumber { get; init; }
    public string? AncientId { get; init; }
    public string OptionId { get; init; } = "";

    public static List<AncientOptionTerm> ParseMany(JsonElement? ancient)
    {
        var list = ancient.Prop("require_options").StringList();
        return list.Select(Parse).Where(t => t.OptionId.Length > 0).ToList();
    }

    public static List<AncientOptionTerm> ParseBlacklist(JsonElement? ancient)
    {
        var list = ancient.Prop("blacklist_options").StringList();
        return list.Select(Parse).Where(t => t.OptionId.Length > 0).ToList();
    }

    public static AncientOptionTerm Parse(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return new AncientOptionTerm();
        var parts = raw.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
            return new AncientOptionTerm { OptionId = Term.Normalize(parts[0]) };
        if (parts.Length == 2)
        {
            int? actNum = ParseAct(parts[0]);
            if (actNum is not null)
                return new AncientOptionTerm { ActNumber = actNum, OptionId = Term.Normalize(parts[1]) };
            return new AncientOptionTerm { AncientId = Term.Normalize(parts[0]), OptionId = Term.Normalize(parts[1]) };
        }
        int? act = ParseAct(parts[0]);
        return new AncientOptionTerm { ActNumber = act, AncientId = Term.Normalize(parts[1]), OptionId = Term.Normalize(string.Join(':', parts.Skip(2))) };
    }

    private static int? ParseAct(string value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("act")) value = value[3..];
        return int.TryParse(value, out var n) ? n : null;
    }

    public bool IsSatisfiedBy(IList<AncientOptionBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (ActNumber is not null && block.ActNumber != ActNumber.Value) continue;
            if (!string.IsNullOrWhiteSpace(AncientId) && !Term.ItemMatches(block.AncientId, AncientId!)) continue;
            if (block.Options.Any(o => Term.ItemMatches(o, OptionId))) return true;
        }
        return false;
    }
}


public sealed record AncientOptionBlock(int ActNumber, string AncientId, List<string> Options);


public sealed class AncientOptionConditions
{
    public bool PaelGoopy { get; init; } = true;
    public bool PaelRemovable { get; init; } = true;
    public bool PaelHasEventPet { get; init; } = false;
    public bool OrobasTouch { get; init; } = true;
    public bool OrobasTooth { get; init; } = true;
    public List<string> OrobasUnlockedCharacters { get; init; } = new() { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent" };
    public bool TezcataraHasBasicStrike { get; init; } = true;
    public bool NonupeipeSwift { get; init; } = true;
    public bool TanxInstinct { get; init; } = true;
    public bool DarvPandora { get; init; } = true;

    public static AncientOptionConditions FromConfig(JsonElement? ancient)
    {
        var c = ancient.Prop("conditions");
        if (c is null || c.Value.ValueKind != JsonValueKind.Object) return new AncientOptionConditions();
        var chars = c.Prop("orobas_unlocked_characters").StringList();
        return new AncientOptionConditions
        {
            PaelGoopy = c.Prop("pael_goopy_enchantable_defends_gte_3").Bool(c.Prop("pael_goopy").Bool(true)),
            PaelRemovable = c.Prop("pael_removable_cards_gte_5").Bool(c.Prop("pael_removable").Bool(true)),
            PaelHasEventPet = c.Prop("pael_has_event_pet").Bool(false),
            OrobasTouch = c.Prop("orobas_touch_of_orobas_allowed").Bool(c.Prop("orobas_touch").Bool(true)),
            OrobasTooth = c.Prop("orobas_archaic_tooth_allowed").Bool(c.Prop("orobas_tooth").Bool(true)),
            OrobasUnlockedCharacters = chars.Count > 0 ? chars : new List<string> { "Ironclad", "Silent", "Defect", "Necrobinder", "Regent" },
            TezcataraHasBasicStrike = c.Prop("tezcatara_has_basic_strike").Bool(c.Prop("has_basic_strike").Bool(true)),
            NonupeipeSwift = c.Prop("nonupeipe_swift_enchantable_cards_gte_4").Bool(c.Prop("nonupeipe_swift").Bool(true)),
            TanxInstinct = c.Prop("tanx_instinct_enchantable_cards_gte_3").Bool(c.Prop("tanx_instinct").Bool(true)),
            DarvPandora = c.Prop("darv_pandoras_box_allowed").Bool(c.Prop("darv_pandora").Bool(true)),
        };
    }
}


public static class AncientOptionsPredictor
{
    private static readonly string[] VanillaCharactersInModelDbOrder = { "Ironclad", "Silent", "Regent", "Necrobinder", "Defect" };

    public static List<AncientOptionBlock> PredictForTarget(string seed, IList<string> ancients, SearchPlan plan)
    {
        var blocks = new List<AncientOptionBlock>();
        for (int i = 0; i < ancients.Count; i++)
        {
            int actNo = i + 1;
            var ancient = ancients[i];
            if (string.Equals(ancient, "Neow", StringComparison.OrdinalIgnoreCase)) continue;
            var opts = Predict(seed, ancient, actNo, plan);
            if (opts.Count > 0) blocks.Add(new AncientOptionBlock(actNo, ancient, opts));
        }
        return blocks;
    }

    private static Sts2Rng CreateEventRng(string seed, string eventId, SearchPlan plan, bool isShared = false)
    {
        uint eventSeed = Sts2Math.MakeEventSeed(seed, eventId, plan.NetId, plan.TargetPlayerSlotIndex, isShared, plan.RngVersion);
        return new Sts2Rng(eventSeed, version: plan.RngVersion);
    }

    private static List<string> Predict(string seed, string ancientId, int actNumber, SearchPlan plan)
    {
        var c = plan.AncientConditions;
        var rng = CreateEventRng(seed, ancientId, plan);
        switch (ancientId)
        {
            case "Pael":
                return PredictPael(rng, c, plan);
            case "Orobas":
                return PredictOrobas(rng, c, plan);
            case "Tezcatara":
                return new List<string>
                {
                    NextRelic(rng, plan, c.TezcataraHasBasicStrike ? new List<string>{"VeryHotCocoa", "YummyCookie", "NutritiousSoup"} : new List<string>{"VeryHotCocoa", "YummyCookie"}),
                    NextRelic(rng, plan, new List<string>{"BiiigHug", "Storybook", "ToastyMittens"}),
                    NextRelic(rng, plan, new List<string>{"GoldenCompass", "PumpkinCandle", "ToyBox", "SealOfGold"}),
                }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            case "Nonupeipe":
                return ShuffleTakeRelics(rng, plan, c.NonupeipeSwift
                    ? new List<string>{"BlessedAntler", "BrilliantScarf", "DelicateFrond", "DiamondDiadem", "FurCoat", "Glitter", "JewelryBox", "LoomingFruit", "SignetRing", "BeautifulBracelet"}
                    : new List<string>{"BlessedAntler", "BrilliantScarf", "DelicateFrond", "DiamondDiadem", "FurCoat", "Glitter", "JewelryBox", "LoomingFruit", "SignetRing"}, 3);
            case "Tanx":
                return ShuffleTakeRelics(rng, plan, c.TanxInstinct
                    ? new List<string>{"Claws", "Crossbow", "IronClub", "MeatCleaver", "Sai", "SpikedGauntlets", "TanxsWhistle", "ThrowingAxe", "WarHammer", "TriBoomerang"}
                    : new List<string>{"Claws", "Crossbow", "IronClub", "MeatCleaver", "Sai", "SpikedGauntlets", "TanxsWhistle", "ThrowingAxe", "WarHammer"}, 3);
            case "Vakuu":
                var p1 = new List<string>{"BloodSoakedRose", "WhisperingEarring", "Fiddle"};
                var p2 = new List<string>{"PreservedFog", "SereTalon", "DistinguishedCape"};
                var p3 = new List<string>{"ChoicesParadox", "MusicBox", "LordsParasol", "JeweledMask"};
                rng.Shuffle(p1); rng.Shuffle(p2); rng.Shuffle(p3);
                return new List<string>{p1.FirstOrDefault() ?? "", p2.FirstOrDefault() ?? "", p3.FirstOrDefault() ?? ""}.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            case "Darv":
                return PredictDarv(rng, c, actNumber, plan);
            default:
                return new List<string>();
        }
    }

    private static List<string> PredictPael(Sts2Rng rng, AncientOptionConditions c, SearchPlan plan)
    {
        var chosen1 = NextRelic(rng, plan, new List<string>{"PaelsFlesh", "PaelsHorn", "PaelsTears"});
        var pool2 = new List<string>{"PaelsWing"};
        if (c.PaelGoopy) pool2.Add("PaelsClaw");
        if (c.PaelRemovable) pool2.Add("PaelsTooth");
        pool2 = pool2.Concat(pool2).ToList();
        pool2.Add("PaelsGrowth");
        var chosen2 = NextRelic(rng, plan, pool2);
        var pool3 = new List<string>{"PaelsEye", "PaelsBlood"};
        if (!c.PaelHasEventPet) pool3.Add("PaelsLegion");
        var chosen3 = NextRelic(rng, plan, pool3);
        return new List<string>{ chosen1, chosen2, chosen3 }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> PredictOrobas(Sts2Rng rng, AncientOptionConditions c, SearchPlan plan)
    {
        // 源码 GenerateInitialOptions 使用 Owner.UnlockState.Characters，排除当前角色后 NextItem。
        // 因此这里不能用 character_stats 或 discovered_cards，必须用 progress/save 复刻出的 UnlockState.Characters。
        var characters = plan.Unlocks.FilterCharacters(VanillaCharactersInModelDbOrder).ToList();
        if (characters.Count == 0) characters = VanillaCharactersInModelDbOrder.ToList();
        var others = characters.Where(x => !string.Equals(x, plan.Character, StringComparison.OrdinalIgnoreCase)).ToList();
        var characterModel = rng.NextItem(others) ?? plan.Character;

        var pool1 = new List<string>{"ElectricShrymp", "GlassEye", "SandCastle"};
        if (rng.NextFloat() < 0.3333333) pool1.Add("PrismaticGem");
        else pool1.Add("SeaGlass:" + characterModel);
        var chosen1 = NextRelic(rng, plan, pool1);
        var chosen2 = NextRelic(rng, plan, new List<string>{"AlchemicalCoffer", "Driftwood", "RadiantPearl"});
        var pool3 = new List<string>();
        if (c.OrobasTouch) pool3.Add("TouchOfOrobas");
        if (c.OrobasTooth) pool3.Add("ArchaicTooth");
        var chosen3 = NextRelic(rng, plan, pool3);
        return new List<string>{ chosen1, chosen2, chosen3 }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> PredictDarv(Sts2Rng rng, AncientOptionConditions c, int actNumber, SearchPlan plan)
    {
        var validSets = new List<List<string>>
        {
            new(){"Astrolabe"},
            new(){"BlackStar"},
            new(){"CallingBell"},
            new(){"EmptyCage"},
        };
        if (c.DarvPandora) validSets.Add(new List<string>{"PandorasBox"});
        validSets.Add(new List<string>{"RunicPyramid"});
        validSets.Add(new List<string>{"SneckoEye"});
        if (actNumber == 2) validSets.Add(new List<string>{"Ectoplasm", "Sozu"});
        else if (actNumber == 3) validSets.Add(new List<string>{"PhilosophersStone", "VelvetChoker"});

        var source = new List<string>();
        foreach (var rs in validSets)
        {
            // 先古事件选项的遗物池按源码写死；不再走普通遗物 Epoch 过滤。
            // 这些条件与局内状态有关的部分继续交给 ancient.conditions 手动开关控制。
            if (rs.Count == 0) continue;
            source.Add(rng.NextItem(rs) ?? rs[0]);
        }
        rng.Shuffle(source);
        if (rng.NextBool()) return source.Take(2).Concat(new[]{"DustyTome"}).ToList();
        return source.Take(3).ToList();
    }

    private static string NextRelic(Sts2Rng rng, SearchPlan plan, List<string> pool)
    {
        // 先古事件选项池本身由事件源码固定，不走普通遗物池的 Epoch 解锁过滤。
        // plan 参数保留是为了减少调用处改动。
        var xs = pool.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return rng.NextItem(xs) ?? "";
    }

    private static List<string> ShuffleTakeRelics(Sts2Rng rng, SearchPlan plan, List<string> pool, int count)
    {
        var xs = pool.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        rng.Shuffle(xs);
        return xs.Take(count).ToList();
    }
}

