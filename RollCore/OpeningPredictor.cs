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

public sealed class OpeningPredictor
{
    private readonly SearchPlan _plan;
    private static readonly string[] CurseOptions =
    {
        "CursedPearl", "HeftyTablet", "LargeCapsule", "LeafyPoultice", "NeowsBones", "PrecariousShears", "SilkenTress", "SilverCrucible"
    };

    private static readonly string[] PositiveOptions =
    {
        "ArcaneScroll", "BoomingConch", "FishingRod", "GoldenPearl", "Kaleidoscope", "LeadPaperweight", "LostCoffer", "MassiveScroll", "NeowsTorment", "NewLeaf", "PhialHolster", "PreciseScissors", "ScrollBoxes", "WingedBoots"
    };

    private static readonly string[] ExtraOptions =
    {
        "LavaRock", "NeowsTalisman", "NutritiousOyster", "Pomander", "SmallCapsule", "StoneHumidifier"
    };

    private static readonly string[] BonesCursePool =
    {
        "Clumsy", "Debt", "Decay", "Doubt", "Guilty", "Injury", "Normality", "Regret", "Shame", "Writhe"
    };

    // Cards with Eternal cannot be selected from the deck as the original card for NewLeaf/Transform.
    // They may still appear as transform results if the target pool allows them.
    private static readonly HashSet<string> DeckEternalUntransformableCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "AscendersBane", "BadLuck", "CurseOfTheBell", "Enthralled", "Folly", "Greed"
    };

    public OpeningPredictor(SearchPlan plan) { _plan = plan; }

    public OpeningResult Check(string rawSeed)
    {
        string seed = Sts2Math.NormalizeSeed(rawSeed);
        var neow = GenerateNeowOptions(seed);
        var bonesRelics = new List<string>();
        var routes = new List<OpeningRoute>();
        var bonesCurses = new List<string>();

        bool needsRouteEffects = _plan.NeedsPotions || _plan.NeedsCardOpportunities || _plan.NeedsPredictedRelics || _plan.NeedsNeowAdvanced || _plan.NeedsAdvancedNewLeafRoute;
        bool needsRoutes = needsRouteEffects || _plan.NeedsBonesCurse || _plan.NeedsNeowAdvanced;
        bool hasBones = neow.Contains("NeowsBones", StringComparer.OrdinalIgnoreCase);
        if (hasBones && (_plan.NeedsBonesRelics || needsRoutes))
            bonesRelics = GenerateBonesRelics(seed);

        if (needsRoutes)
        {
            // 骨骰诅咒单独筛选时，不需要展开所有卡牌/药水/随机遗物收益。
            // v16.15 里这里会走完整 SimulateKnownRelicEffect，等于为了抽一张诅咒，
            // 顺带模拟 LargeCapsule / ScrollBoxes / Kaleidoscope 等重逻辑；在小范围 random
            // 搜索时表现成“查了几十个就不动”。这里改成 curse-only 快路径：只模拟会消耗
            // RunRng.Niche 的遗物影响，然后直接抽骨骰诅咒。
            if (needsRouteEffects)
                routes.AddRange(GenerateDirectRoutes(seed, neow));

            if (hasBones && bonesRelics.Count > 0)
            {
                var boneRoutes = needsRouteEffects
                    ? GenerateBonesRoutes(seed, bonesRelics)
                    : GenerateBonesCurseOnlyRoutes(seed, bonesRelics);
                foreach (var route in boneRoutes)
                {
                    routes.Add(route);
                    if (!string.IsNullOrEmpty(route.BonesCurse)) bonesCurses.Add(route.BonesCurse!);
                }
            }
        }

        var potionSources = (_plan.NeedsPotions || _plan.NeedsNeowAdvanced) ? GeneratePotionSourcesFromRoutes(routes) : new List<string>();
        var cardSourceRoutes = (_plan.NeedsCardOpportunities || _plan.NeedsNeowAdvanced) ? GenerateCardSourceRoutesFromRoutes(routes) : new List<CardSourceRoute>();
        var predictedRelicSources = (_plan.NeedsPredictedRelics || _plan.NeedsNeowAdvanced) ? GeneratePredictedRelicSourcesFromRoutes(routes) : new List<string>();
        var shopRelics = _plan.NeedsShop ? ShopRelicPredictor.PredictTargetShopOrder(seed, _plan) : new List<string>();
        var relicQueues = _plan.RelicQueueShow ? ShopRelicPredictor.PredictTargetRelicQueues(seed, _plan).ToDictionary() : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var ancients = (_plan.NeedsAncientIdentity || _plan.NeedsAncientOptions) ? AncientPredictor.PredictAncients(seed, _plan) : new List<string>();
        var ancientOptions = _plan.NeedsAncientOptions ? AncientOptionsPredictor.PredictForTarget(seed, ancients, _plan) : new List<AncientOptionBlock>();
        var eventQueues = (_plan.EventQueueEnabled && (_plan.EventQueueShow || _plan.NeedsEventQueueFilter)) ? AncientPredictor.PredictEventQueues(seed, _plan, _plan.EventQueueShow ? 3 : _plan.EventQueueMaxRequiredAct) : new List<EventQueueBlock>();
        var mapBosses = new List<MapBossBlock>();
        return new OpeningResult
        {
            Seed = seed,
            NeowOptions = neow,
            BonesRelics = bonesRelics,
            BonesCurse = bonesCurses.FirstOrDefault(),
            BonesCurses = bonesCurses.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ShopRelics = shopRelics,
            RelicQueues = relicQueues,
            PotionSources = potionSources,
            CardSourceRoutes = cardSourceRoutes,
            PredictedRelicSources = predictedRelicSources,
            OpeningRoutes = routes,
            Ancients = ancients,
            AncientOptions = ancientOptions,
            EventQueues = eventQueues,
            MapBosses = mapBosses,
            ShowShopRelics = _plan.ShopShow,
            ShowAncients = _plan.AncientEnabled,
            ShowAncientOptions = _plan.AncientEnabled && _plan.AncientShowOptions,
            ShowEventQueues = _plan.EventQueueEnabled && _plan.EventQueueShow,
        };
    }

    public OpeningResult CheckFullDetails(string rawSeed)
    {
        string seed = Sts2Math.NormalizeSeed(rawSeed);
        var neow = GenerateNeowOptions(seed);
        var routes = new List<OpeningRoute>();
        var bonesRelics = new List<string>();
        var bonesCurses = new List<string>();

        routes.AddRange(GenerateAllDirectRoutes(seed, neow));

        bool hasBones = neow.Contains("NeowsBones", StringComparer.OrdinalIgnoreCase);
        if (hasBones)
        {
            bonesRelics = GenerateBonesRelics(seed);
            foreach (var route in GenerateBonesRoutes(seed, bonesRelics))
            {
                routes.Add(route);
                if (!string.IsNullOrWhiteSpace(route.BonesCurse)) bonesCurses.Add(route.BonesCurse!);
            }
        }

        // v17.2 stable+ui2：FullDetails 仍然展开开局路线，但商店/先古遵守前端开关。
        // 启用筛选但不显示时，仍会计算用于 Matches；未启用时完全不算，避免“开关没用”。
        bool computeShop = _plan.ShopEnabled && (_plan.ShopShow || _plan.NeedsShop);
        bool computeAncients = _plan.AncientEnabled;
        bool computeAncientOptions = _plan.AncientEnabled && (_plan.AncientShowOptions || _plan.NeedsAncientOptions);
        var shopRelics = computeShop ? ShopRelicPredictor.PredictTargetShopOrder(seed, _plan) : new List<string>();
        var relicQueues = _plan.RelicQueueShow ? ShopRelicPredictor.PredictTargetRelicQueues(seed, _plan).ToDictionary() : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var ancients = computeAncients ? AncientPredictor.PredictAncients(seed, _plan) : new List<string>();
        var ancientOptions = computeAncientOptions ? AncientOptionsPredictor.PredictForTarget(seed, ancients, _plan) : new List<AncientOptionBlock>();
        var eventQueues = (_plan.EventQueueEnabled && (_plan.EventQueueShow || _plan.NeedsEventQueueFilter)) ? AncientPredictor.PredictEventQueues(seed, _plan, _plan.EventQueueShow ? 3 : _plan.EventQueueMaxRequiredAct) : new List<EventQueueBlock>();
        var mapBosses = AncientPredictor.PredictMapAndBoss(seed, _plan);

        return new OpeningResult
        {
            Seed = seed,
            NeowOptions = neow,
            BonesRelics = bonesRelics,
            BonesCurse = bonesCurses.FirstOrDefault(),
            BonesCurses = bonesCurses.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ShopRelics = shopRelics,
            RelicQueues = relicQueues,
            PotionSources = GeneratePotionSourcesFromRoutes(routes),
            CardSourceRoutes = GenerateCardSourceRoutesFromRoutes(routes),
            PredictedRelicSources = GeneratePredictedRelicSourcesFromRoutes(routes),
            OpeningRoutes = routes,
            Ancients = ancients,
            AncientOptions = ancientOptions,
            EventQueues = eventQueues,
            MapBosses = mapBosses,
            ShowShopRelics = _plan.ShopShow,
            ShowAncients = _plan.AncientEnabled,
            ShowAncientOptions = _plan.AncientEnabled && _plan.AncientShowOptions,
            ShowEventQueues = _plan.EventQueueEnabled && _plan.EventQueueShow,
        };
    }

    private List<OpeningRoute> GenerateAllDirectRoutes(string seed, IList<string> neowOptions)
    {
        var routes = new List<OpeningRoute>();
        foreach (var relic in neowOptions)
        {
            if (string.Equals(relic, "NeowsBones", StringComparison.OrdinalIgnoreCase)) continue;
            var st = new SimState(seed, _plan, allowAdvancedNewLeaf: false);
            SimulateKnownRelicEffect(st, relic);
            routes.Add(st.ToRoute("direct", relic, null, null));
        }
        return routes;
    }

    private List<OpeningRoute> GenerateDirectRoutes(string seed, IList<string> neowOptions)
    {
        var routes = new List<OpeningRoute>();
        var relevant = RelevantDirectRelics();
        if (relevant.Count == 0 && !_plan.NeedsNeowAdvanced) return routes;
        foreach (var relic in neowOptions)
        {
            if (relic == "NeowsBones") continue;
            if (!_plan.NeedsNeowAdvanced && !relevant.Contains(relic, StringComparer.OrdinalIgnoreCase)) continue;
            var st = new SimState(seed, _plan, allowAdvancedNewLeaf: false);
            SimulateKnownRelicEffect(st, relic);
            routes.Add(st.ToRoute("direct", relic, null, null));
        }
        return routes;
    }

    private List<OpeningRoute> GenerateBonesCurseOnlyRoutes(string seed, IList<string> bonesRelics)
    {
        var routes = new List<OpeningRoute>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in MakePickOrdersBasic(bonesRelics))
        {
            // v16.18：真正的 curse-only 快路径。
            // 骨骰诅咒只使用 RunRng.Niche；骨骰遗物本身用 PlayerRewards 抽取，
            // 但这不会影响 Niche，所以不需要创建 SimState，也不需要 GameData。
            var niche = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "niche", version: _plan.RngVersion);
            SimulateRunNicheOnlyForBonesCurse(niche, order);
            var curse = niche.NextItem(BonesCursePool.ToList()) ?? BonesCursePool[0];
            if (!seen.Add(curse)) continue;
            routes.Add(new OpeningRoute
            {
                Kind = "bones",
                PickOrder = order.ToList(),
                BonesCurse = curse,
            });
        }
        return routes;
    }

    private void SimulateRunNicheOnlyForBonesCurse(Sts2Rng niche, IList<string> order)
    {
        bool kaleidoscopeAddedCards = false;
        foreach (var relic in order)
        {
            if (string.Equals(relic, "Kaleidoscope", StringComparison.OrdinalIgnoreCase))
            {
                ConsumeKaleidoscopeNicheOnly(niche);
                kaleidoscopeAddedCards = true;
            }
            else if (string.Equals(relic, "NewLeaf", StringComparison.OrdinalIgnoreCase))
            {
                ConsumeNewLeafNicheOnly(niche, kaleidoscopeAddedCards);
            }
        }
    }

    private void ConsumeKaleidoscopeNicheOnly(Sts2Rng niche)
    {
        // Python simulate_kaleidoscope 对 RunRng.Niche 的唯一消耗是：
        // 每个奖励组把“其他角色卡池列表” StableShuffle 一次；后续具体出牌走 PlayerRewards。
        // 对后续 Niche 状态来说，只需要保证 shuffle 的列表长度一致，元素内容无关。
        var pools = new List<int>();
        foreach (var ch in _plan.KaleidoscopeCharacterOrder)
        {
            if (string.Equals(ch, _plan.Character, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(ch)) continue;
            pools.Add(pools.Count);
        }
        for (int gi = 0; gi < 2; gi++)
        {
            var tmp = pools.ToList();
            niche.Shuffle(tmp);
        }
    }

    private void ConsumeNewLeafNicheOnly(Sts2Rng niche, bool kaleidoscopeAddedCards)
    {
        var policy = _plan.NewLeafSelectedCard;
        bool hasTarget;
        if (string.Equals(policy, "first_kaleidoscope_card", StringComparison.OrdinalIgnoreCase))
            hasTarget = kaleidoscopeAddedCards;
        else if (string.IsNullOrWhiteSpace(policy) || string.Equals(policy, "starter_basic", StringComparison.OrdinalIgnoreCase))
            hasTarget = true;
        else
            // 非默认手动指定卡牌时，为了 curse-only 不加载 GameData，采用“有目标则消耗一次”的保守路径。
            // 常规配置不会走到这里；需要完全精确的卡牌收益筛选会进入完整 OpeningRoute。
            hasTarget = true;

        // transform_card 对 RunRng.Niche 的后续状态只体现为一次 NextItem(options)；
        // NextInt(1) 与 NextInt(options.Count) 都消耗一次 System.Random。
        if (hasTarget) niche.NextInt(1);
    }

    private List<OpeningRoute> GenerateBonesRoutes(string seed, IList<string> bonesRelics)
    {
        var routes = new List<OpeningRoute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in MakePickOrdersForPlan(bonesRelics))
        {
            var st = new SimState(seed, _plan, allowAdvancedNewLeaf: true);
            AdvancePastBonesRelicRoll(st);
            foreach (var relic in order) SimulateKnownRelicEffect(st, relic);
            var curse = st.RunNiche.NextItem(BonesCursePool.ToList()) ?? BonesCursePool[0];
            var route = st.ToRoute("bones", null, order, curse);
            string key = JsonSerializer.Serialize(new
            {
                route.BonesCurse,
                cards = route.CardOpportunities,
                potions = route.Potions,
                relics = route.PredictedRelics,
            }, JsonOut.Options);
            if (seen.Add(key)) routes.Add(route);
        }
        return routes;
    }

    private void AdvancePastBonesRelicRoll(SimState st)
    {
        // Python simulate_bones_paths() receives the same SimState that was already used by
        // generate_bones_relics(state). NeowsBones.GetValidRelics consumes PlayerRng.Rewards
        // by shuffling the whole valid Neow relic pool before each pick-order route is cloned.
        // If we start route simulation from a fresh PlayerRewards RNG, LargeCapsule/SmallCapsule
        // relic rarity rolls and card rewards drift and create false positive FastCore hits.
        var pool = AllPossibleNeowRelics()
            .Where(r => r != "NeowsBones" && IsAllowedNeowRelic(r))
            .ToList();
        st.PlayerRewards.Shuffle(pool);
    }

    private List<string> RelevantDirectRelics()
    {
        var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_plan.NeedsPotions)
        {
            rels.Add("LostCoffer");
            rels.Add("PhialHolster");
        }
        if (_plan.NeedsPredictedRelics)
        {
            rels.Add("SmallCapsule");
            rels.Add("LargeCapsule");
        }
        if (_plan.NeedsCardOpportunities)
        {
            if (_plan.CardFilters.TryGetValue("own", out var own) && own.HasFilter)
            {
                foreach (var r in new[] { "ArcaneScroll", "HeftyTablet", "LostCoffer", "ScrollBoxes", "LeafyPoultice", "NewLeaf", "MassiveScroll" }) rels.Add(r);
            }
            if (_plan.CardFilters.TryGetValue("colorless", out var colorless) && colorless.HasFilter) rels.Add("LeadPaperweight");
            if (_plan.CardFilters.TryGetValue("other", out var other) && other.HasFilter) rels.Add("Kaleidoscope");
        }
        return rels.ToList();
    }


    private static List<List<string>> MakePickOrdersBasic(IList<string> bonesRelics)
    {
        var xs = bonesRelics.ToList();
        if (xs.Count == 2 && !string.Equals(xs[0], xs[1], StringComparison.OrdinalIgnoreCase))
            return new List<List<string>> { xs, xs.AsEnumerable().Reverse().ToList() };
        return new List<List<string>> { xs };
    }

    private List<List<string>> MakePickOrdersForPlan(IList<string> bonesRelics)
    {
        var xs = bonesRelics.ToList();

        // 高级新叶是路线语义：骨骰先拾取另一个 Neow 遗物，把它产生的牌放进临时牌组，
        // 然后第二个拾取 NewLeaf，NewLeaf 才能变化该来源牌。
        // 因此当 NewLeafSelectedCard 为非默认高级选择器时，不再同时模拟 NewLeaf -> other 的反向顺序。
        // 反向顺序下 NewLeaf 执行时目标来源牌尚未进入牌组，只能退化为普通初始牌变化，
        // 不应作为“高级新叶”命中路径。
        if (_plan.NeedsAdvancedNewLeafRoute && xs.Count == 2 && xs.Any(r => string.Equals(r, "NewLeaf", StringComparison.OrdinalIgnoreCase)))
        {
            var other = xs.FirstOrDefault(r => !string.Equals(r, "NewLeaf", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(other))
                return new List<List<string>> { new List<string> { other!, "NewLeaf" } };
        }

        if (xs.Count == 2 && !string.Equals(xs[0], xs[1], StringComparison.OrdinalIgnoreCase))
            return new List<List<string>> { xs, xs.AsEnumerable().Reverse().ToList() };
        return new List<List<string>> { xs };
    }

    private static List<string> GeneratePotionSourcesFromRoutes(IList<OpeningRoute> routes)
    {
        var outList = new List<string>();
        foreach (var r in routes)
            if (r.Potions.Count > 0)
                outList.Add((r.Kind == "bones" ? "bones:" : "direct:") + (r.DirectRelic ?? string.Join("+", r.PickOrder)));
        return outList;
    }

    private static List<string> GeneratePredictedRelicSourcesFromRoutes(IList<OpeningRoute> routes)
    {
        var outList = new List<string>();
        foreach (var r in routes)
            if (r.PredictedRelics.Count > 0)
                outList.AddRange(r.PredictedRelics);
        return outList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<CardSourceRoute> GenerateCardSourceRoutesFromRoutes(IList<OpeningRoute> routes)
    {
        var outList = new List<CardSourceRoute>();
        foreach (var r in routes)
        {
            var cats = r.CardOpportunities.Select(e => e.Category).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cats.Count == 0) continue;
            var sources = r.CardOpportunities.Select(e => (r.Kind == "bones" ? "bones:" : "direct:") + e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            outList.Add(new CardSourceRoute(cats, sources));
        }
        return outList;
    }

    private bool IsAllowedNeowRelic(string relic)
    {
        if (_plan.PlayersCount == 1 && relic == "MassiveScroll") return false;
        if (_plan.PlayersCount > 1 && (relic == "WingedBoots" || relic == "SilverCrucible")) return false;
        if (relic == "Kaleidoscope") return _plan.AllowKaleidoscope;
        if (relic == "ScrollBoxes" && _plan.Data is not null)
        {
            var cardPool = _plan.Data.CharacterCardPool(_plan.Character);
            return _plan.Data.CardsByRarity(cardPool, "Common", _plan.Unlocks).Count >= 4 && _plan.Data.CardsByRarity(cardPool, "Uncommon", _plan.Unlocks).Count >= 2;
        }
        return true;
    }

    private List<string> AllPossibleNeowRelics()
    {
        var pool = new List<string>();
        pool.AddRange(CurseOptions);
        pool.AddRange(PositiveOptions);
        pool.AddRange(ExtraOptions);
        return pool;
    }

    private Sts2Rng CreateEventRng(string seed, string eventId = "NEOW", bool isShared = false)
    {
        uint eventSeed = Sts2Math.MakeEventSeed(seed, eventId, _plan.NetId, _plan.TargetPlayerSlotIndex, isShared, _plan.RngVersion);
        return new Sts2Rng(eventSeed, version: _plan.RngVersion);
    }

    private List<string> GenerateNeowOptions(string seed)
    {
        var rng = CreateEventRng(seed);
        var cursePool = CurseOptions.Where(IsAllowedNeowRelic).ToList();
        string curse = rng.NextItem(cursePool) ?? cursePool[0];

        var positive = PositiveOptions.ToList();
        if (curse == "CursedPearl") positive.Remove("GoldenPearl");
        if (curse == "HeftyTablet") positive.Remove("ArcaneScroll");
        if (curse == "LeafyPoultice") positive.Remove("NewLeaf");
        if (curse == "PrecariousShears") positive.Remove("PreciseScissors");

        if (curse != "LargeCapsule") positive.Add(rng.NextBool() ? "LavaRock" : "SmallCapsule");
        positive.Add(rng.NextBool() ? "NutritiousOyster" : "StoneHumidifier");
        positive.Add(rng.NextBool() ? "NeowsTalisman" : "Pomander");

        positive = positive.Where(IsAllowedNeowRelic).ToList();
        rng.Shuffle(positive);
        return new List<string> { positive[0], positive[1], curse };
    }

    private List<string> GenerateBonesRelics(string seed)
    {
        var pool = AllPossibleNeowRelics().Where(r => r != "NeowsBones" && IsAllowedNeowRelic(r)).ToList();
        var rng = new Sts2Rng(Sts2Math.MakePlayerSeed(seed, _plan.NetId, _plan.TargetPlayerSlotIndex, _plan.RngVersion), name: "rewards", version: _plan.RngVersion);
        rng.Shuffle(pool);
        return pool.Take(2).ToList();
    }

    private void SimulateKnownRelicEffect(SimState state, string relic)
    {
        switch (relic)
        {
            case "ArcaneScroll": SimulateArcaneScroll(state); break;
            case "HeftyTablet": SimulateHeftyTablet(state); break;
            case "LeadPaperweight": SimulateLeadPaperweight(state); break;
            case "LostCoffer": SimulateLostCoffer(state); break;
            case "NeowsTorment": SimulateNeowsTorment(state); break;
            case "ScrollBoxes": SimulateScrollBoxes(state); break;
            case "LeafyPoultice": SimulateLeafyPoultice(state); break;
            case "NewLeaf": SimulateNewLeaf(state); break;
            case "SmallCapsule": SimulateSmallCapsule(state); break;
            case "LargeCapsule": SimulateLargeCapsule(state); break;
            case "Kaleidoscope": SimulateKaleidoscope(state); break;
            case "PhialHolster": SimulatePhialHolster(state); break;
            case "MassiveScroll": SimulateMassiveScroll(state); break;
            case "CursedPearl": SimulateCursedPearl(state); break;
        }
    }

    private void SimulateArcaneScroll(SimState st)
    {
        var cards = st.CreateCardForReward(st.CardPoolName, 1, "Uniform", "Rare", true);
        st.RecordCardFixed("ArcaneScroll", cards, st.CardPoolName, "own", "card_reward");
        st.AddCardsToDeck(cards, st.CardPoolName, "ArcaneScroll");
    }

    private void SimulateHeftyTablet(SimState st)
    {
        var cards = st.CreateCardForReward(st.CardPoolName, 3, "Uniform", "Rare", true);
        st.RecordCardChoiceGroup("HeftyTablet", cards, st.CardPoolName, "own", "card_reward");
        if (_plan.HeftyTabletChoice >= 0 && _plan.HeftyTabletChoice < cards.Count) st.AddCardsToDeck(new[] { cards[_plan.HeftyTabletChoice] }, st.CardPoolName, "HeftyTablet");
        st.AddCardsToDeck(new[] { "Injury" }, "CurseCardPool", "HeftyTablet");
    }

    private void SimulateLeadPaperweight(SimState st)
    {
        var cards = st.CreateCardForRewardNoRare(st.ColorlessCardPoolName, 2, false);
        st.RecordCardChoiceGroup("LeadPaperweight", cards, st.ColorlessCardPoolName, "colorless", "card_reward");
        if (_plan.LeadPaperweightChoice >= 0 && _plan.LeadPaperweightChoice < cards.Count) st.AddCardsToDeck(new[] { cards[_plan.LeadPaperweightChoice] }, st.ColorlessCardPoolName, "LeadPaperweight");
    }

    private void SimulateLostCoffer(SimState st)
    {
        var cards = st.CreateCardForReward(st.CardPoolName, 3, "RegularEncounter", null, false);
        st.RecordCardChoiceGroup("LostCoffer", cards, st.CardPoolName, "own", "card_reward");
        var potions = st.CreatePotionReward(1, st.PlayerRewards);
        if (_plan.LostCofferChoice >= 0 && _plan.LostCofferChoice < cards.Count) st.AddCardsToDeck(new[] { cards[_plan.LostCofferChoice] }, st.CardPoolName, "LostCoffer");
        st.RecordPotionFixed("LostCoffer", potions.Where(p => !string.IsNullOrEmpty(p.Potion)).Select(p => p.Potion!));
    }

    private void SimulateScrollBoxes(SimState st)
    {
        var common = st.Data.CardsByRarity(st.CardPoolName, "Common", _plan.Unlocks);
        var uncommon = st.Data.CardsByRarity(st.CardPoolName, "Uncommon", _plan.Unlocks);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new List<List<string>>();
        for (int i = 0; i < 2; i++)
        {
            if (st.Character.Equals("Defect", StringComparison.OrdinalIgnoreCase) && st.PlayerRewards.NextInt(100) < 1)
            {
                bundles.Add(new List<string> { "Claw", "Claw", "Claw" });
                continue;
            }
            var b = new List<string>();
            var ca = common.Where(c => !used.Contains(c)).ToList();
            for (int j = 0; j < 2; j++)
            {
                var c = st.PlayerRewards.NextItem(ca);
                if (c != null)
                {
                    b.Add(c); used.Add(c); ca.Remove(c);
                }
            }
            var ua = uncommon.Where(c => !used.Contains(c)).ToList();
            var u = st.PlayerRewards.NextItem(ua);
            if (u != null) { b.Add(u); used.Add(u); }
            bundles.Add(b);
        }
        st.RecordCardBundleChoice("ScrollBoxes", bundles, st.CardPoolName, "own", "card_bundle");
    }

    private void SimulateLeafyPoultice(SimState st)
    {
        var transformed = new List<string>();
        var strike = st.FindFirstBasicWithTag("Strike");
        var defend = st.FindFirstBasicWithTag("Defend");
        if (strike != null)
        {
            var n = st.TransformCard(strike, st.PlayerTransformations);
            if (n != null) transformed.Add(n);
        }
        if (defend != null)
        {
            var n = st.TransformCard(defend, st.PlayerTransformations);
            if (n != null) transformed.Add(n);
        }
        st.RecordCardFixed("LeafyPoultice", transformed, st.CardPoolName, "own", "transform");
    }

    private void SimulateNewLeaf(SimState st)
    {
        var target = st.SelectNewLeafCard(st.NewLeafSelectedCard);
        if (target == null) return;
        var sourceId = target.Id;
        var sourcePool = target.Pool;
        var source = target.Source ?? "Deck";
        var n = st.TransformCard(target, st.RunNiche);
        if (n != null) st.RecordNewLeafTransform(n, target.Pool, st.CardPoolScope(n, target.Pool), sourceId, sourcePool, source, st.NewLeafSelectedCard);
    }

    private void SimulateCursedPearl(SimState st)
    {
        // Fixed curse side effect. Greed is added to the temporary deck for route completeness,
        // but Greed has Eternal and cannot be selected as a deck transform original.
        st.AddCardsToDeck(new[] { "Greed" }, "CurseCardPool", "CursedPearl");
    }

    private void SimulateNeowsTorment(SimState st)
    {
        // Fixed Neow card; no RNG, but expose a source_cards trace so WPF process tags can match it.
        st.RecordCardFixed("NeowsTorment", new[] { "NeowsFury" }, "EventCardPool", "event", "fixed_card");
        st.AddCardsToDeck(new[] { "NeowsFury" }, "EventCardPool", "NeowsTorment");
    }

    private void SimulateSmallCapsule(SimState st)
    {
        st.RecordRelics("SmallCapsule", st.SimulateRandomRelics(1).Select(item => item.Relic), "random_relic");
    }

    private void SimulateLargeCapsule(SimState st)
    {
        st.RecordRelics("LargeCapsule", st.SimulateRandomRelics(2).Select(item => item.Relic), "random_relic");
        var strike = st.Data.BasicStrike(st.CardPoolName);
        var defend = st.Data.BasicDefend(st.CardPoolName);
        // Fixed side effect: keep it as deck state for later route-aware transforms, but do not treat it as a random card opportunity.
        if (strike != null) st.AddCardsToDeck(new[] { strike }, st.CardPoolName, "LargeCapsule");
        if (defend != null) st.AddCardsToDeck(new[] { defend }, st.CardPoolName, "LargeCapsule");
    }

    private void SimulateKaleidoscope(SimState st)
    {
        for (int gi = 0; gi < 2; gi++)
        {
            var pools = new List<(string Character, string Pool)>();
            foreach (var ch in _plan.KaleidoscopeCharacterOrder)
            {
                if (string.Equals(ch, st.Character, StringComparison.OrdinalIgnoreCase)) continue;
                pools.Add((ch, st.Data.CharacterCardPool(ch)));
            }
            pools.Sort((a, b) => string.CompareOrdinal(a.Pool, b.Pool));
            st.RunNiche.Shuffle(pools);
            var group = new List<string>();
            var selected = pools.Take(3).ToList();
            foreach (var item in selected)
            {
                var cards = st.CreateCardForReward(item.Pool, 1, "RegularEncounter", null, false);
                if (cards.Count > 0) group.Add(cards[0]);
            }
            st.RecordCardChoiceGroup("Kaleidoscope", group, null, "other", "kaleidoscope");
            int idx = gi < _plan.KaleidoscopeChoiceIndices.Count ? _plan.KaleidoscopeChoiceIndices[gi] : 0;
            if (idx >= 0 && idx < selected.Count && idx < group.Count)
                st.AddCardsToDeck(new[] { group[idx] }, selected[idx].Pool, "Kaleidoscope");
        }
    }

    private void SimulatePhialHolster(SimState st)
    {
        var potions = st.CreatePotionReward(2, st.RunCombatPotionGeneration);
        st.RecordPotionFixed("PhialHolster", potions.Where(p => !string.IsNullOrEmpty(p.Potion)).Select(p => p.Potion!));
    }

    private void SimulateMassiveScroll(SimState st)
    {
        if (_plan.PlayersCount <= 1) return;
        var items = st.CreateMassiveScrollCards(3);
        var cards = items.Select(x => x.Card).Where(x => !string.IsNullOrEmpty(x)).ToList();
        st.RecordCardChoiceGroup("MassiveScroll", cards!, st.CardPoolName, "own", "card_reward");
        if (_plan.MassiveScrollChoice >= 0 && _plan.MassiveScrollChoice < cards.Count)
            st.AddCardsToDeck(new[] { cards[_plan.MassiveScrollChoice] }, st.CardPoolName, "MassiveScroll");
    }

    private sealed class SimState
    {
        public string Seed { get; }
        public SearchPlan Plan { get; }
        public GameData Data { get; }
        public string Character { get; }
        public string CardPoolName { get; }
        public string PotionPoolName { get; }
        public string ColorlessCardPoolName { get; }
        public string SharedRelicPoolName { get; }
        public string SharedPotionPoolName { get; }
        public bool AscensionScarcity { get; }
        public string NewLeafSelectedCard { get; }
        public Sts2Rng PlayerRewards { get; }
        public Sts2Rng PlayerTransformations { get; }
        public Sts2Rng RunNiche { get; }
        public Sts2Rng RunCombatPotionGeneration { get; }
        public Sts2Rng RunUpFront { get; }
        public List<CardEvent> CardOpportunities { get; } = new();
        public List<PotionEvent> PotionOpportunities { get; } = new();
        public List<RelicEvent> RelicOpportunities { get; } = new();
        public List<string> Potions { get; } = new();
        public List<string> PredictedRelics { get; } = new();
        private List<CardInst>? _deck;
        private Dictionary<string, List<string>>? _relicDeques;
        private Dictionary<string, List<string>>? _sharedRelicDeques;

        public SimState(string seed, SearchPlan plan, bool allowAdvancedNewLeaf = false)
        {
            Seed = seed;
            Plan = plan;
            Data = plan.Data ?? throw new InvalidOperationException("Opening reward simulation needs GameData.");
            Character = plan.Character;
            CardPoolName = Data.CharacterCardPool(Character);
            PotionPoolName = Data.CharacterPotionPool(Character);
            ColorlessCardPoolName = plan.Root.Prop("shared").Prop("colorless_card_pool").Str("ColorlessCardPool");
            SharedRelicPoolName = plan.Root.Prop("shared").Prop("shared_relic_pool").Str("SharedRelicPool");
            SharedPotionPoolName = plan.Root.Prop("shared").Prop("shared_potion_pool").Str("SharedPotionPool");
            AscensionScarcity = plan.Root.Prop("player").Prop("ascension_scarcity").Bool(false);
            NewLeafSelectedCard = allowAdvancedNewLeaf ? plan.NewLeafSelectedCard : "starter_basic";
            PlayerRewards = new Sts2Rng(Sts2Math.MakePlayerSeed(seed, plan.NetId, plan.TargetPlayerSlotIndex, plan.RngVersion), name: "rewards", version: plan.RngVersion);
            PlayerTransformations = new Sts2Rng(Sts2Math.MakePlayerSeed(seed, plan.NetId, plan.TargetPlayerSlotIndex, plan.RngVersion), name: "transformations", version: plan.RngVersion);
            RunNiche = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "niche", version: plan.RngVersion);
            RunCombatPotionGeneration = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "combat_potion_generation", version: plan.RngVersion);
            RunUpFront = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "up_front", version: plan.RngVersion);
        }

        public OpeningRoute ToRoute(string kind, string? directRelic, IList<string>? pickOrder, string? bonesCurse)
        {
            return new OpeningRoute
            {
                Kind = kind,
                DirectRelic = directRelic,
                PickOrder = pickOrder?.ToList() ?? new List<string>(),
                BonesCurse = bonesCurse,
                CardOpportunities = CardOpportunities.Select(CloneCardEvent).ToList(),
                PotionOpportunities = PotionOpportunities.Select(ClonePotionEvent).ToList(),
                RelicOpportunities = RelicOpportunities.Select(CloneRelicEvent).ToList(),
                Potions = Potions.ToList(),
                PredictedRelics = PredictedRelics.ToList(),
            };
        }

        private static PotionEvent ClonePotionEvent(PotionEvent e) => new()
        {
            Source = e.Source,
            Potions = e.Potions.ToList(),
        };

        private static RelicEvent CloneRelicEvent(RelicEvent e) => new()
        {
            Source = e.Source,
            Method = e.Method,
            Relics = e.Relics.ToList(),
        };

        private static CardEvent CloneCardEvent(CardEvent e) => new()
        {
            Source = e.Source,
            Category = e.Category,
            Type = e.Type,
            Method = e.Method,
            Cards = e.Cards.ToList(),
            Options = e.Options.Select(x => x.ToList()).ToList(),
            Rarities = new Dictionary<string, string?>(e.Rarities, StringComparer.OrdinalIgnoreCase),
        };

        public List<CardInst> EnsureDeck()
        {
            if (_deck != null) return _deck;
            _deck = new List<CardInst>();
            var strike = Data.BasicStrike(CardPoolName);
            var defend = Data.BasicDefend(CardPoolName);
            if (strike != null) for (int i = 0; i < 5; i++) AddCardInst(_deck, strike, CardPoolName, "Starter", "Strike");
            if (defend != null) for (int i = 0; i < 4; i++) AddCardInst(_deck, defend, CardPoolName, "Starter", "Defend");
            foreach (var c in Data.CardsByRarity(CardPoolName, "Basic", Plan.Unlocks))
            {
                if (c == strike || c == defend) continue;
                AddCardInst(_deck, c, CardPoolName, "Starter", "Basic");
            }
            return _deck;
        }

        public void AddCardsToDeck(IEnumerable<string> cards, string pool, string source, string? sourceDetail = null)
        {
            var deck = EnsureDeck();
            foreach (var c in cards.Where(x => !string.IsNullOrEmpty(x)))
                AddCardInst(deck, c, string.IsNullOrWhiteSpace(pool) ? (Data.CardPoolOf(c) ?? CardPoolName) : pool, source, sourceDetail);
        }

        private void AddCardInst(List<CardInst> deck, string id, string pool, string? source, string? sourceDetail = null)
        {
            deck.Add(new CardInst
            {
                Id = id,
                OriginalId = id,
                Pool = string.IsNullOrWhiteSpace(pool) ? (Data.CardPoolOf(id) ?? CardPoolName) : pool,
                Source = source,
                SourceDetail = sourceDetail,
                AddedOrder = deck.Count,
            });
        }

        public CardInst? FindFirstBasicWithTag(string tag)
        {
            foreach (var inst in EnsureDeck())
            {
                if (Data.CardRarity(inst.Id) == "Basic" && Data.CardTags(inst.Id).Contains(tag)) return inst;
            }
            return null;
        }

        public CardInst? SelectNewLeafCard(string policy)
        {
            policy = string.IsNullOrWhiteSpace(policy) ? "starter_basic" : policy.Trim();
            var deck = EnsureDeck();

            bool IsTransformable(CardInst x) => IsTransformableInDeck(x.Id);

            if (policy.Equals("starter_basic", StringComparison.OrdinalIgnoreCase))
                return (FindFirstBasicWithTag("Strike") ?? FindFirstBasicWithTag("Defend"));
            if (policy.Equals("first_kaleidoscope_card", StringComparison.OrdinalIgnoreCase))
                return deck.FirstOrDefault(x => IsTransformable(x) && string.Equals(x.Source, "Kaleidoscope", StringComparison.OrdinalIgnoreCase));

            if (policy.StartsWith("starter:", StringComparison.OrdinalIgnoreCase))
            {
                var tag = policy["starter:".Length..].Trim();
                if (tag.Equals("strike", StringComparison.OrdinalIgnoreCase)) return FindFirstBasicWithTag("Strike");
                if (tag.Equals("defend", StringComparison.OrdinalIgnoreCase) || tag.Equals("defense", StringComparison.OrdinalIgnoreCase)) return FindFirstBasicWithTag("Defend");
            }

            if (policy.StartsWith("exact:", StringComparison.OrdinalIgnoreCase))
            {
                var id = Term.Normalize(policy["exact:".Length..]);
                return deck.FirstOrDefault(x => IsTransformable(x) && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            }

            if (policy.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = policy["source:".Length..];
                var parts = rest.Split(':', 2, StringSplitOptions.TrimEntries);
                var source = parts.Length > 0 ? Term.Normalize(parts[0]) : "";
                var wanted = parts.Length > 1 ? Term.Normalize(parts[1]) : "any";
                return deck.FirstOrDefault(x =>
                    IsTransformable(x)
                    && string.Equals(x.Source ?? "", source, StringComparison.OrdinalIgnoreCase)
                    && (wanted.Length == 0 || wanted.Equals("any", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Id, wanted, StringComparison.OrdinalIgnoreCase)));
            }

            var legacy = Term.Normalize(policy);
            return deck.FirstOrDefault(x => IsTransformable(x) && string.Equals(x.Id, legacy, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsTransformableInDeck(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return false;
            if (string.Equals(Data.CardType(cardId), "Quest", StringComparison.OrdinalIgnoreCase)) return false;
            // In Deck, Eternal cards are not removable and therefore not transformable.
            // This matters for CursedPearl -> Greed: Greed can be a Curse transform result,
            // but it cannot be selected by NewLeaf as the original card.
            if (DeckEternalUntransformableCards.Contains(cardId)) return false;
            return true;
        }

        public string? TransformCard(CardInst inst, Sts2Rng rng)
        {
            var originalId = inst.Id;
            var poolName = TransformBasePool(inst);
            var options = GetDefaultTransformOptions(inst, false).ToList();
            var result = rng.NextItem(options);
            if (result != null)
            {
                inst.Id = result;
                inst.Pool = poolName;
                inst.Source = "NewLeafResult";
                inst.SourceDetail = originalId;
            }
            return result;
        }

        private IEnumerable<string> GetDefaultTransformOptions(CardInst original, bool isInCombat)
        {
            var poolName = TransformBasePool(original);
            return Data.CardPoolCardsOrder(poolName, Plan.Unlocks)
                .Where(c => IsCardAllowedForTransform(c, original, isInCombat));
        }

        private string TransformBasePool(CardInst original)
        {
            var originalId = original.Id;
            var originalType = Data.CardType(originalId);
            var originalRarity = Data.CardRarity(originalId);
            var originalPool = string.IsNullOrWhiteSpace(original.Pool) ? (Data.CardPoolOf(originalId) ?? CardPoolName) : original.Pool;

            // Source behavior from CardFactory.GetDefaultTransformationOptions:
            // Quest/Event/Ancient/Token originals fallback to ColorlessCardPool.
            // Status and Curse originals keep original.Pool and skip the C/U/R target rarity filter.
            if (string.Equals(originalType, "Quest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(originalRarity, "Event", StringComparison.OrdinalIgnoreCase)
                || string.Equals(originalRarity, "Ancient", StringComparison.OrdinalIgnoreCase)
                || string.Equals(originalRarity, "Token", StringComparison.OrdinalIgnoreCase))
            {
                return ColorlessCardPoolName;
            }

            return originalPool;
        }

        private bool IsCardAllowedForTransform(string cardId, CardInst original, bool isInCombat)
        {
            if (string.Equals(cardId, original.Id, StringComparison.OrdinalIgnoreCase)) return false;

            var originalRarity = Data.CardRarity(original.Id);
            if (!string.Equals(originalRarity, "Status", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(originalRarity, "Curse", StringComparison.OrdinalIgnoreCase))
            {
                var rarity = Data.CardRarity(cardId);
                if (rarity is not ("Common" or "Uncommon" or "Rare")) return false;
            }

            // Opening/Neow transforms are out of combat, so CanBeGeneratedInCombat does not apply here.
            // Player-count and unlock filters still apply through GameData and FilterForPlayerCount semantics.
            if (!IsCardAllowedForPlayerCount(cardId)) return false;
            return true;
        }

        public void RecordNewLeafTransform(string result, string? resultPool, string? category, string sourceCard, string sourcePool, string source, string selector)
        {
            var ev = new CardEvent
            {
                Source = "NewLeaf",
                Category = category ?? CardPoolScope(result, resultPool),
                Type = "fixed",
                Method = "transform",
                Cards = new List<string> { result },
                Rarities = RarityMap(new[] { result }),
            };
            ev.Rarities["__source_card"] = sourceCard;
            ev.Rarities["__source_pool"] = sourcePool;
            ev.Rarities["__source"] = source;
            ev.Rarities["__selector"] = selector;
            CardOpportunities.Add(ev);
        }

        private bool IsCardAllowedForPlayerCount(string cardId)
        {
            if (Plan.PlayersCount == 1 && (Data.IsCardMultiplayerOnly(cardId) || cardId == "Tank")) return false;
            if (Plan.PlayersCount > 1 && Data.IsCardSingleplayerOnly(cardId)) return false;
            return true;
        }

        private IEnumerable<string> FilterCardsForPlayerCount(IEnumerable<string> cards) => cards.Where(IsCardAllowedForPlayerCount);

        private string? NextAllowedRarity(string? rarity, HashSet<string> available)
        {
            var current = rarity;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(current) && seen.Add(current))
            {
                if (available.Contains(current)) return current;
                current = current switch
                {
                    "Common" => "Uncommon",
                    "Uncommon" => "Rare",
                    "Rare" => "Common",
                    _ => null,
                };
            }
            return null;
        }

        private string RollRegularCardRarity()
        {
            // CardRarityOdds.RollWithBaseOdds(RegularEncounter):
            // x < RegularRareOdds => Rare; x < regularUncommonOdds => Uncommon; otherwise Common.
            // The uncommon threshold is not cumulative with the rare threshold.
            double x = PlayerRewards.NextFloat();
            double rare = AscensionScarcity ? 0.0149 : 0.03;
            if (x < rare) return "Rare";
            if (x < 0.37) return "Uncommon";
            return "Common";
        }

        public List<string> CreateCardForReward(string poolName, int count = 1, string rarityOdds = "RegularEncounter", string? forceRarity = null, bool noUpgradeRoll = false)
        {
            var results = new List<string>();
            var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> Candidates(string rarity)
            {
                var raw = Data.CardsByRarity(poolName, rarity, Plan.Unlocks);
                return FilterCardsForPlayerCount(raw.Where(c => !blacklist.Contains(c))).ToList();
            }

            for (int i = 0; i < count; i++)
            {
                string? selectedRarity;
                if (!string.IsNullOrEmpty(forceRarity)) selectedRarity = forceRarity;
                else if (rarityOdds == "Uniform")
                {
                    var availableItems = new List<string>();
                    foreach (var r in new[] { "Common", "Uncommon", "Rare" }) availableItems.AddRange(Candidates(r));
                    var card = PlayerRewards.NextItem(availableItems);
                    if (card == null) break;
                    results.Add(card); blacklist.Add(card);
                    if (!noUpgradeRoll) PlayerRewards.NextFloat();
                    continue;
                }
                else
                {
                    var rolled = RollRegularCardRarity();
                    var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in new[] { "Common", "Uncommon", "Rare" }) if (Candidates(r).Count > 0) available.Add(r);
                    selectedRarity = NextAllowedRarity(rolled, available);
                }
                if (selectedRarity == null) break;
                var items = Candidates(selectedRarity);
                var selected = PlayerRewards.NextItem(items);
                if (selected == null) break;
                results.Add(selected); blacklist.Add(selected);
                if (!noUpgradeRoll) PlayerRewards.NextFloat();
            }
            return results;
        }

        public List<string> CreateCardForRewardNoRare(string poolName, int count = 1, bool noUpgradeRoll = false)
        {
            var results = new List<string>();
            var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> Candidates(string rarity)
            {
                if (rarity is not ("Common" or "Uncommon")) return new List<string>();
                var raw = Data.CardsByRarity(poolName, rarity, Plan.Unlocks);
                return FilterCardsForPlayerCount(raw.Where(c => !blacklist.Contains(c))).ToList();
            }
            for (int i = 0; i < count; i++)
            {
                var rolled = RollRegularCardRarity();
                var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in new[] { "Common", "Uncommon" }) if (Candidates(r).Count > 0) available.Add(r);
                var selectedRarity = NextAllowedRarity(rolled, available);
                if (selectedRarity == null) break;
                var card = PlayerRewards.NextItem(Candidates(selectedRarity));
                if (card == null) break;
                results.Add(card); blacklist.Add(card);
                if (!noUpgradeRoll) PlayerRewards.NextFloat();
            }
            return results;
        }

        public string CardPoolScope(string cardId, string? poolName = null, string? forced = null)
        {
            if (!string.IsNullOrWhiteSpace(forced)) return forced!;
            poolName ??= Data.CardPoolOf(cardId);
            if (poolName == ColorlessCardPoolName) return "colorless";
            if (poolName == CardPoolName) return "own";
            return "other";
        }

        private Dictionary<string, string?> RarityMap(IEnumerable<string> cards)
        {
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cards.Where(x => !string.IsNullOrEmpty(x))) map[c] = Data.CardRarity(c);
            return map;
        }

        public void RecordCardFixed(string source, IEnumerable<string> cards, string? poolName, string? category, string method)
        {
            var xs = cards.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (xs.Count == 0) return;
            var cat = category ?? CardPoolScope(xs[0], poolName);
            CardOpportunities.Add(new CardEvent { Source = source, Category = cat, Type = "fixed", Method = method, Cards = xs, Rarities = RarityMap(xs) });
        }

        public void RecordCardChoiceGroup(string source, IEnumerable<string> cards, string? poolName, string? category, string method)
        {
            var xs = cards.Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (xs.Count == 0) return;
            var cat = category ?? CardPoolScope(xs[0], poolName);
            CardOpportunities.Add(new CardEvent { Source = source, Category = cat, Type = "choice_group", Method = method, Cards = xs, Rarities = RarityMap(xs) });
        }

        public void RecordCardBundleChoice(string source, IEnumerable<List<string>> bundles, string? poolName, string? category, string method)
        {
            var clean = bundles.Select(b => b.Where(c => !string.IsNullOrEmpty(c)).ToList()).Where(b => b.Count > 0).ToList();
            if (clean.Count == 0) return;
            var cat = category ?? CardPoolScope(clean[0][0], poolName);
            CardOpportunities.Add(new CardEvent
            {
                Source = source,
                Category = cat,
                Type = "bundle_choice",
                Method = method,
                Options = clean,
                Rarities = RarityMap(clean.SelectMany(x => x)),
            });
        }

        public void RecordRelics(string source, IEnumerable<string> relics, string method)
        {
            var xs = relics.Where(r => !string.IsNullOrEmpty(r)).ToList();
            if (xs.Count == 0) return;
            PredictedRelics.AddRange(xs);
            RelicOpportunities.Add(new RelicEvent { Source = source, Method = method, Relics = xs });
        }

        public void RecordPotionFixed(string source, IEnumerable<string> potions)
        {
            var xs = potions.Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (xs.Count == 0) return;
            Potions.AddRange(xs);
            PotionOpportunities.Add(new PotionEvent { Source = source, Potions = xs });
        }

        public List<(string Rarity, string? Potion)> CreatePotionReward(int count, Sts2Rng rng)
        {
            var outList = new List<(string Rarity, string? Potion)>();
            for (int i = 0; i < count; i++)
            {
                double x = rng.NextFloat();
                string rarity = x <= 0.1 ? "Rare" : (x <= 0.35 ? "Uncommon" : "Common");
                var options = Data.PotionsByRarity(PotionPoolName, rarity, Plan.Unlocks).Concat(Data.PotionsByRarity(SharedPotionPoolName, rarity, Plan.Unlocks)).ToList();
                var potion = options.Count > 0 ? rng.NextItem(options) : null;
                outList.Add((rarity, potion));
            }
            return outList;
        }

        private string RollRelicRarity()
        {
            double x = PlayerRewards.NextFloat();
            if (x < 0.5) return "Common";
            if (x < 0.83) return "Uncommon";
            return "Rare";
        }

        public List<(string RolledRarity, string ActualRarity, string Relic)> SimulateRandomRelics(int count)
        {
            var outList = new List<(string, string, string)>();
            for (int i = 0; i < count; i++)
            {
                var rolled = RollRelicRarity();
                var pulled = PullRelicFromFront(rolled);
                outList.Add((rolled, pulled.ActualRarity, pulled.Relic));
            }
            return outList;
        }

        private (string ActualRarity, string Relic) PullRelicFromFront(string rarity)
        {
            EnsureRelicGrabBag();
            RemoveDisallowedRelicsFromDeques(_relicDeques!);
            string? current = rarity;
            while (current != null)
            {
                var q = _relicDeques!.TryGetValue(current, out var xs) ? xs : new List<string>();
                if (q.Count > 0)
                {
                    var relic = q[0]; q.RemoveAt(0);
                    if (_sharedRelicDeques != null) RemoveRelicFromDeques(_sharedRelicDeques, relic);
                    return (current, relic);
                }
                current = current switch
                {
                    "Shop" => "Common",
                    "Common" => "Uncommon",
                    "Uncommon" => "Rare",
                    _ => null,
                };
            }
            return (rarity, "FallbackRelic");
        }

        private void EnsureRelicGrabBag()
        {
            if (_relicDeques != null) return;
            var sharedPool = Data.RelicPool(SharedRelicPoolName, Plan.Unlocks);
            _sharedRelicDeques = BuildSharedRelicDeques(sharedPool);
            if (Plan.PlayersCount <= 1 || Plan.PlayersOrder.Count <= 1)
            {
                var charPool = Data.RelicPool(Data.CharacterRelicPool(Character), Plan.Unlocks);
                _relicDeques = BuildPlayerRelicDeques(sharedPool.Concat(charPool));
                return;
            }
            Dictionary<string, List<string>>? target = null;
            foreach (var player in Plan.PlayersOrder)
            {
                var charPool = Data.RelicPool(Data.CharacterRelicPool(player.Character), Plan.Unlocks);
                var deques = BuildPlayerRelicDeques(sharedPool.Concat(charPool));
                if (player.NetId == Plan.TargetNetIdText) target = deques;
            }
            _relicDeques = target ?? BuildPlayerRelicDeques(sharedPool.Concat(Data.RelicPool(Data.CharacterRelicPool(Character), Plan.Unlocks)));
        }

        private Dictionary<string, List<string>> BuildSharedRelicDeques(IEnumerable<string> relicIds)
        {
            var deques = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var relic in relicIds)
            {
                var rarity = Data.RelicRarity(relic);
                if (string.IsNullOrEmpty(rarity)) continue;
                if (!deques.TryGetValue(rarity!, out var q)) { q = new List<string>(); deques[rarity!] = q; }
                q.Add(relic);
            }
            foreach (var q in deques.Values) RunUpFront.Shuffle(q);
            return deques;
        }

        private Dictionary<string, List<string>> BuildPlayerRelicDeques(IEnumerable<string> relicIds)
        {
            var deques = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var relic in relicIds)
            {
                var rarity = Data.RelicRarity(relic);
                if (rarity is not ("Common" or "Uncommon" or "Rare" or "Shop")) continue;
                if (!deques.TryGetValue(rarity, out var q)) { q = new List<string>(); deques[rarity] = q; }
                q.Add(relic);
            }
            foreach (var q in deques.Values) RunUpFront.Shuffle(q);
            return deques;
        }

        private void RemoveDisallowedRelicsFromDeques(Dictionary<string, List<string>> deques)
        {
            foreach (var key in deques.Keys.ToList()) deques[key] = deques[key].Where(IsRelicAllowedNow).ToList();
        }

        private void RemoveRelicFromDeques(Dictionary<string, List<string>> deques, string relic)
        {
            foreach (var key in deques.Keys.ToList()) deques[key] = deques[key].Where(x => !string.Equals(x, relic, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private bool IsRelicAllowedNow(string relic)
        {
            if (relic == "MassiveScroll" && Plan.PlayersCount <= 1) return false;
            if (Plan.PlayersCount == 1 && Data.IsRelicMultiplayerOnly(relic)) return false;
            if (Plan.PlayersCount > 1 && Data.IsRelicSingleplayerOnly(relic)) return false;
            return true;
        }

        private string RollMassiveScrollCardRarity()
        {
            double x = PlayerRewards.NextFloat();
            return x < 0.37 ? "Uncommon" : "Common";
        }

        private string? NextMassiveScrollRarity(string rarity) => rarity switch
        {
            "Common" => "Uncommon",
            "Uncommon" => "Common",
            _ => null,
        };

        private (string ActualRarity, string? Card) PullMassiveScrollCardByRarity(List<string> candidates, string rarity, HashSet<string> used)
        {
            string? current = rarity;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current != null && seen.Add(current))
            {
                var available = candidates.Where(c => !used.Contains(c) && Data.CardRarity(c) == current).ToList();
                if (available.Count > 0) return (current, PlayerRewards.NextItem(available));
                current = NextMassiveScrollRarity(current);
            }
            return (rarity, null);
        }

        public List<(string Card, string RolledRarity, string ActualRarity)> CreateMassiveScrollCards(int count)
        {
            var colorless = Data.CardPoolCardsOrder(ColorlessCardPoolName, Plan.Unlocks);
            var current = Data.CardPoolCardsOrder(CardPoolName, Plan.Unlocks);
            var candidates = new List<string>();
            foreach (var c in colorless.Concat(current))
            {
                if (!(Data.IsCardMultiplayerOnly(c) || c == "Tank")) continue;
                var rarity = Data.CardRarity(c);
                if (rarity is not ("Common" or "Uncommon")) continue;
                if (!candidates.Contains(c, StringComparer.OrdinalIgnoreCase)) candidates.Add(c);
            }
            var result = new List<(string, string, string)>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                var rolled = RollMassiveScrollCardRarity();
                var pulled = PullMassiveScrollCardByRarity(candidates, rolled, used);
                if (pulled.Card == null) continue;
                result.Add((pulled.Card, rolled, pulled.ActualRarity));
                used.Add(pulled.Card);
                PlayerRewards.NextFloat();
            }
            return result;
        }
    }
}

