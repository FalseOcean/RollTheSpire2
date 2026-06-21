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

public sealed class OrderedRelicGroups
{
    private readonly Dictionary<string, List<string>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();

    public void Add(string rarity, string relic)
    {
        if (!_groups.TryGetValue(rarity, out var list))
        {
            list = new List<string>();
            _groups[rarity] = list;
            _order.Add(rarity);
        }
        list.Add(relic);
    }

    public void ShuffleAll(Sts2Rng rng)
    {
        foreach (var rarity in _order)
            rng.Shuffle(_groups[rarity]);
    }

    public List<string> Get(string rarity)
    {
        return _groups.TryGetValue(rarity, out var xs) ? xs : new List<string>();
    }
}


public sealed class ShopRelicTerm
{
    public string RelicId { get; init; } = "";
    public int? MaxPos { get; init; }
    public int? ExactPos { get; init; }

    public bool IsSatisfiedBy(IList<string> shopOrder, int defaultLimit)
    {
        if (ExactPos is int exact)
        {
            if (exact <= 0) return false;
            int idx = exact - 1;
            return idx >= 0 && idx < shopOrder.Count && Term.ItemMatches(shopOrder[idx], RelicId);
        }

        int n = MaxPos ?? defaultLimit;
        if (n <= 0) n = shopOrder.Count;
        n = Math.Min(n, shopOrder.Count);
        for (int i = 0; i < n; i++)
            if (Term.ItemMatches(shopOrder[i], RelicId)) return true;
        return false;
    }

    public static List<ShopRelicTerm> ParseRequire(JsonElement? shop)
    {
        var outList = new List<string>();
        var direct = shop.Prop("require_relics");
        if (direct is not null)
        {
            if (direct.Value.ValueKind == JsonValueKind.String)
                outList.AddRange(SplitTextTerms(direct.Value.GetString() ?? ""));
            else
                outList.AddRange(direct.StringList());
        }

        var structured = shop.Prop("require_relic_filters");
        if (structured is not null && structured.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in structured.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string relic = item.Prop("relic").Str("").Trim();
                if (relic.Length == 0) continue;
                int maxPos = item.Prop("max_pos").Int(0);
                outList.Add(maxPos > 0 ? $"{relic} <= {maxPos}" : relic);
            }
        }
        return outList.Select(Parse).Where(x => x.RelicId.Length > 0).ToList();
    }


    public static List<ShopRelicTerm> ParseExact(JsonElement? shop)
    {
        var outList = new List<ShopRelicTerm>();
        var structured = shop.Prop("exact_relic_filters");
        if (structured is not null && structured.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in structured.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string relic = item.Prop("relic").Str("").Trim();
                int position = item.Prop("position").Int(item.Prop("pos").Int(0));
                if (relic.Length == 0 || position <= 0) continue;
                outList.Add(new ShopRelicTerm { RelicId = Term.Normalize(relic), ExactPos = position });
            }
        }

        var direct = shop.Prop("exact_relics");
        if (direct is not null)
        {
            var rawTerms = direct.Value.ValueKind == JsonValueKind.String
                ? SplitTextTerms(direct.Value.GetString() ?? "")
                : direct.StringList();
            foreach (var raw in rawTerms)
            {
                var term = ParseExactText(raw);
                if (term.RelicId.Length > 0) outList.Add(term);
            }
        }
        return outList;
    }

    private static ShopRelicTerm ParseExactText(string raw)
    {
        raw = (raw ?? "").Trim().Replace("＝", "=");
        if (raw.Length == 0) return new ShopRelicTerm();
        var patterns = new[]
        {
            @"^第\s*(\d+)\s*个?\s*[:：,，\s]+(.+)$",
            @"^(\d+)\s*[:：,，\s]+(.+)$",
            @"^(.+?)\s*(?:==|=|@=)\s*(\d+)\s*$",
        };
        for (int i = 0; i < patterns.Length; i++)
        {
            var m = Regex.Match(raw, patterns[i]);
            if (!m.Success) continue;
            if (i <= 1) return new ShopRelicTerm { RelicId = Term.Normalize(m.Groups[2].Value), ExactPos = Math.Max(1, int.Parse(m.Groups[1].Value)) };
            return new ShopRelicTerm { RelicId = Term.Normalize(m.Groups[1].Value), ExactPos = Math.Max(1, int.Parse(m.Groups[2].Value)) };
        }
        return new ShopRelicTerm();
    }

    public static List<ShopRelicTerm> ParseBlacklist(JsonElement? shop)
    {
        var direct = shop.Prop("blacklist");
        var outList = new List<string>();
        if (direct is not null)
        {
            if (direct.Value.ValueKind == JsonValueKind.String)
                outList.AddRange(SplitTextTerms(direct.Value.GetString() ?? ""));
            else
                outList.AddRange(direct.StringList());
        }
        return outList.Select(Parse).Where(x => x.RelicId.Length > 0).ToList();
    }

    private static IEnumerable<string> SplitTextTerms(string text)
    {
        return (text ?? "").Replace('，', '\n').Replace(',', '\n').Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);
    }

    public static ShopRelicTerm Parse(string raw)
    {
        raw = (raw ?? "").Trim().Replace("≤", "<=").Replace("＝", "=");
        if (raw.Length == 0) return new ShopRelicTerm();

        var patterns = new[]
        {
            @"^前\s*(\d+)\s*[:：,，\s]+(.+)$",
            @"^(.+?)\s*<=\s*(\d+)\s*$",
            @"^(.+?)\s*<\s*=\s*(\d+)\s*$",
            @"^(.+?)\s*前\s*(\d+)\s*$",
            @"^(.+?)\s*(?:@|#|:|：)\s*(\d+)\s*$",
        };
        for (int i = 0; i < patterns.Length; i++)
        {
            var m = Regex.Match(raw, patterns[i]);
            if (!m.Success) continue;
            if (i == 0)
            {
                return new ShopRelicTerm { RelicId = Term.Normalize(m.Groups[2].Value), MaxPos = Math.Max(1, int.Parse(m.Groups[1].Value)) };
            }
            return new ShopRelicTerm { RelicId = Term.Normalize(m.Groups[1].Value), MaxPos = Math.Max(1, int.Parse(m.Groups[2].Value)) };
        }
        return new ShopRelicTerm { RelicId = Term.Normalize(raw), MaxPos = null };
    }
}


public sealed class RelicQueuePrediction
{
    public List<string> Common { get; init; } = new();
    public List<string> Uncommon { get; init; } = new();
    public List<string> Rare { get; init; } = new();
    public List<string> Shop { get; init; } = new();

    public Dictionary<string, List<string>> ToDictionary() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = Common,
        ["Uncommon"] = Uncommon,
        ["Rare"] = Rare,
        ["Shop"] = Shop,
    };
}

public static class ShopRelicPredictor
{
    public static List<string> PredictTargetShopOrder(string seed, SearchPlan plan)
        => PredictTargetRelicQueues(seed, plan).Shop;

    public static RelicQueuePrediction PredictTargetRelicQueues(string seed, SearchPlan plan)
    {
        var target = BuildTargetGroups(seed, plan);
        return new RelicQueuePrediction
        {
            Common = DrawOrder(target.Get("Common")),
            Uncommon = DrawOrder(target.Get("Uncommon")),
            Rare = DrawOrder(target.Get("Rare")),
            Shop = DrawOrder(target.Get("Shop")),
        };
    }

    private static List<string> DrawOrder(List<string> shuffledGroup)
    {
        var outList = new List<string>(shuffledGroup);
        outList.Reverse();
        return outList;
    }

    private static OrderedRelicGroups BuildTargetGroups(string seed, SearchPlan plan)
    {
        var data = plan.Data ?? throw new InvalidOperationException("Relic queue prediction needs GameData.");
        var upFront = new Sts2Rng((uint)Sts2Math.DeterministicHash(seed), name: "up_front", version: plan.RngVersion);
        var sharedPool = data.RelicPool("SharedRelicPool", plan.Unlocks);

        // SharedRelicGrabBag.Populate consumes UpFront once for shared pool groups.
        BuildSharedGroups(sharedPool, data, upFront);

        OrderedRelicGroups? target = null;
        foreach (var player in plan.PlayersOrder)
        {
            string poolName = data.CharacterRelicPool(player.Character);
            var charPool = data.RelicPool(poolName, plan.Unlocks);
            var combined = new List<string>(sharedPool.Count + charPool.Count);
            combined.AddRange(sharedPool);
            combined.AddRange(charPool);
            var groups = BuildPlayerGroups(combined, data, upFront);
            if (player.NetId == plan.TargetNetIdText)
                target = groups;
        }

        if (target is null)
        {
            // 兜底：如果多人列表里没有目标，就用目标角色单独 Populate。
            string poolName = data.CharacterRelicPool(plan.Character);
            var combined = new List<string>(sharedPool);
            combined.AddRange(data.RelicPool(poolName, plan.Unlocks));
            target = BuildPlayerGroups(combined, data, upFront);
        }

        return target;
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
}


