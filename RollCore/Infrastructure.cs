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

public sealed class Args
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] args)
    {
        var a = new Args();
        for (int i = 0; i < args.Length; i++)
        {
            string k = args[i];
            if (!k.StartsWith("--")) continue;
            k = k[2..];
            string v = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                v = args[++i];
            }
            a._map[k] = v;
        }
        return a;
    }

    public string Get(string key, string defaultValue) => _map.TryGetValue(key, out var v) ? v : defaultValue;
    public int GetInt(string key, int defaultValue) => int.TryParse(Get(key, ""), out var v) ? v : defaultValue;
    public long GetLong(string key, long defaultValue) => long.TryParse(Get(key, ""), out var v) ? v : defaultValue;
}


public static class JsonOut
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}


public static class JsonExt
{
    public static JsonElement? Prop(this JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)) return v;
        return null;
    }

    public static JsonElement? Prop(this JsonElement? e, string name)
    {
        if (e is null) return null;
        return e.Value.Prop(name);
    }

    public static string Str(this JsonElement? e, string defaultValue = "")
    {
        if (e is null) return defaultValue;
        var v = e.Value;
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? defaultValue;
        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        if (v.ValueKind == JsonValueKind.True) return "true";
        if (v.ValueKind == JsonValueKind.False) return "false";
        return defaultValue;
    }

    public static bool Bool(this JsonElement? e, bool defaultValue = false)
    {
        if (e is null) return defaultValue;
        var v = e.Value;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
        return defaultValue;
    }

    public static int Int(this JsonElement? e, int defaultValue = 0)
    {
        if (e is null) return defaultValue;
        var v = e.Value;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
        return defaultValue;
    }

    public static long Long(this JsonElement? e, long defaultValue = 0)
    {
        if (e is null) return defaultValue;
        var v = e.Value;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out i)) return i;
        return defaultValue;
    }

    public static List<string> StringList(this JsonElement e) => ((JsonElement?)e).StringList();

    public static List<string> StringList(this JsonElement? e)
    {
        var outList = new List<string>();
        if (e is null) return outList;
        var v = e.Value;
        if (v.ValueKind == JsonValueKind.String)
        {
            return Regex.Split(v.GetString() ?? "", @"[\n,，]").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
        if (v.ValueKind != JsonValueKind.Array) return outList;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) outList.Add(item.GetString() ?? "");
            else if (item.ValueKind == JsonValueKind.Number) outList.Add(item.GetRawText());
        }
        return outList.SelectMany(x => Regex.Split(x ?? "", @"[\n,，]")).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    public static bool HasAnyTerms(this JsonElement? e)
    {
        if (e is null || e.Value.ValueKind != JsonValueKind.Object) return false;
        return e.Value.Prop("whitelist_any").StringList().Count > 0
            || e.Value.Prop("whitelist_all").StringList().Count > 0
            || e.Value.Prop("blacklist").StringList().Count > 0;
    }
}


public enum GameRngVersion
{
    Legacy,
    Sts2V107Xoshiro
}

public static class GameRngVersions
{
    public const string LegacyConfig = "legacy";
    public const string XoshiroConfig = "sts2_0_107_xoshiro";

    public static GameRngVersion Parse(string? raw)
    {
        string v = (raw ?? "").Trim().ToLowerInvariant();
        if (v is "legacy" or "old" or "v1" or "v1_legacy" or "dotnet" or "system_random") return GameRngVersion.Legacy;
        if (v.Contains("107") || v.Contains("xoshiro") || v.Contains("mega")) return GameRngVersion.Sts2V107Xoshiro;
        // v2.x targets STS2 v0.107.1+ by default.
        return GameRngVersion.Sts2V107Xoshiro;
    }

    public static string ToConfig(GameRngVersion version)
        => version == GameRngVersion.Legacy ? LegacyConfig : XoshiroConfig;

    public static string DisplayName(GameRngVersion version)
        => version == GameRngVersion.Legacy ? "Legacy / v1.x old RNG" : "STS2 v0.107+ / xoshiro256**";
}


public static class Sts2Math
{
    public const int IntMax = 2_147_483_647;
    public const int IntMin = unchecked((int)0x80000000);

    public static uint ToU32(long x) => unchecked((uint)x);

    public static string NormalizeSeed(string seed)
    {
        return (seed ?? "").ToUpperInvariant().Replace('O', '0').Replace('I', '1').Trim();
    }

    public static int DeterministicHash(string s)
    {
        s ??= "";
        unchecked
        {
            int num = 352654597;
            int num2 = num;
            for (int i = 0; i < s.Length; i += 2)
            {
                num = ((num << 5) + num) ^ s[i];
                if (i == s.Length - 1) break;
                num2 = ((num2 << 5) + num2) ^ s[i + 1];
            }
            return num + num2 * 1566083941;
        }
    }

    public static uint MakePlayerSeed(string seedText, ulong netId)
    {
        unchecked { return (uint)((ulong)(uint)DeterministicHash(NormalizeSeed(seedText)) + netId); }
    }

    public static uint MakePlayerSeed(string seedText, ulong legacyNetId, int playerSlotIndex, GameRngVersion version)
    {
        if (version == GameRngVersion.Sts2V107Xoshiro)
        {
            unchecked { return (uint)(DeterministicHash(NormalizeSeed(seedText)) + playerSlotIndex); }
        }
        return MakePlayerSeed(seedText, legacyNetId);
    }

    public static uint MakeRunStreamSeed(string seedText, string streamName)
    {
        unchecked { return (uint)((uint)DeterministicHash(NormalizeSeed(seedText)) + (uint)DeterministicHash(streamName)); }
    }

    public static uint MakeEventSeed(string seedText, string eventId, ulong legacyNetId, int playerSlotIndex, bool isShared, GameRngVersion version)
    {
        string normalizedEventId = (eventId ?? "").Trim().ToUpperInvariant();
        unchecked
        {
            uint runSeed = (uint)DeterministicHash(NormalizeSeed(seedText));
            uint eventHash = (uint)DeterministicHash(normalizedEventId);
            if (version == GameRngVersion.Sts2V107Xoshiro)
            {
                uint basePart = (uint)(DeterministicHash(NormalizeSeed(seedText)) + (isShared ? 0 : playerSlotIndex));
                return (uint)(basePart + eventHash);
            }
            ulong netPart = isShared ? 0UL : legacyNetId;
            return (uint)((ulong)runSeed + netPart + eventHash);
        }
    }
}


public interface IRandomCompat
{
    int Next(int maxValue);
    int Next(int minValue, int maxValue);
    double NextDouble();
}

public sealed class DotNetRandomCompat : IRandomCompat
{
    private readonly int[] _seedArray = new int[56];
    private int _inext;
    private int _inextp;

    public DotNetRandomCompat(int seed)
    {
        unchecked
        {
            int subtraction = seed == Sts2Math.IntMin ? Sts2Math.IntMax : Math.Abs(seed);
            int mj = 161803398 - subtraction;
            _seedArray[55] = mj;
            int mk = 1;
            int ii = 0;

            for (int i = 1; i < 55; i++)
            {
                ii += 21;
                if (ii >= 55) ii -= 55;
                _seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += Sts2Math.IntMax;
                mj = _seedArray[ii];
            }

            for (int k = 1; k < 5; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    int n = i + 30;
                    if (n >= 55) n -= 55;
                    _seedArray[i] -= _seedArray[1 + n];
                    if (_seedArray[i] < 0) _seedArray[i] += Sts2Math.IntMax;
                }
            }
            _inext = 0;
            _inextp = 21;
        }
    }

    private int InternalSample()
    {
        int locInext = _inext + 1;
        if (locInext >= 56) locInext = 1;
        int locInextp = _inextp + 1;
        if (locInextp >= 56) locInextp = 1;
        int retVal = _seedArray[locInext] - _seedArray[locInextp];
        if (retVal == Sts2Math.IntMax) retVal--;
        if (retVal < 0) retVal += Sts2Math.IntMax;
        _seedArray[locInext] = retVal;
        _inext = locInext;
        _inextp = locInextp;
        return retVal;
    }

    public double Sample() => InternalSample() * (1.0 / Sts2Math.IntMax);
    public int Next(int maxValue) => (int)(Sample() * maxValue);
    public int Next(int minValue, int maxValue) => (int)(Sample() * (maxValue - minValue)) + minValue;
    public double NextDouble() => Sample();
}


public sealed class MegaRandomCompat : IRandomCompat
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public MegaRandomCompat(uint seed) : this((ulong)seed) { }

    public MegaRandomCompat(ulong seed)
    {
        Reinitialise(seed);
    }

    public void Reinitialise(ulong seed)
    {
        _s0 = Splitmix64(ref seed);
        _s1 = Splitmix64(ref seed);
        _s2 = Splitmix64(ref seed);
        _s3 = Splitmix64(ref seed);
    }

    public static ulong Splitmix64(ref ulong x)
    {
        unchecked
        {
            ulong num = x += 11400714819323198485UL;
            num = (num ^ (num >> 30)) * 13787848793156543929UL;
            num = (num ^ (num >> 27)) * 10723151780598845931UL;
            return num ^ (num >> 31);
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
        => (value << offset) | (value >> (64 - offset));

    private ulong NextULongInner()
    {
        unchecked
        {
            ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }
    }

    public double NextDouble()
        => (double)(NextULongInner() >> 11) * 1.1102230246251565E-16;

    public int Next(int maxValue)
    {
        if (maxValue < 1) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return (int)(NextDouble() * (double)maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue >= maxValue) throw new ArgumentOutOfRangeException(nameof(maxValue));
        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue) return Next((int)range) + minValue;
        return (int)((long)(NextDouble() * (double)range) + minValue);
    }
}


public sealed class Sts2Rng
{
    private readonly IRandomCompat _random;
    public uint Seed { get; }
    public GameRngVersion Version { get; }
    public int Counter { get; private set; }

    public Sts2Rng(uint seed, string? name = null, GameRngVersion version = GameRngVersion.Legacy)
    {
        unchecked
        {
            if (name != null) seed = (uint)(seed + (uint)Sts2Math.DeterministicHash(name));
            Seed = seed;
            Version = version;
            _random = version == GameRngVersion.Sts2V107Xoshiro
                ? new MegaRandomCompat(seed)
                : new DotNetRandomCompat(unchecked((int)seed));
        }
    }

    public int NextInt(int maxExclusive) { Counter++; return _random.Next(maxExclusive); }
    public int NextInt(int minInclusive, int maxExclusive) { Counter++; return _random.Next(minInclusive, maxExclusive); }
    public bool NextBool() { Counter++; return _random.Next(2) == 0; }
    public double NextDouble() { Counter++; return _random.NextDouble(); }
    public double NextFloat(double minValue = 0.0, double maxValue = 1.0) { Counter++; return _random.NextDouble() * (maxValue - minValue) + minValue; }

    public T? NextItem<T>(IList<T> items)
    {
        if (items.Count == 0) return default;
        int idx = NextInt(0, items.Count);
        return items[idx];
    }

    public void Shuffle<T>(IList<T> items)
    {
        int num = items.Count;
        while (num > 1)
        {
            num--;
            int num2 = NextInt(num + 1);
            (items[num], items[num2]) = (items[num2], items[num]);
        }
    }
}


public static class SeedText
{
    public const string Chars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string ToSeedText(long n)
    {
        if (n == 0) return "0";
        var chars = new List<char>();
        long b = Chars.Length;
        while (n > 0)
        {
            chars.Add(Chars[(int)(n % b)]);
            n /= b;
        }
        chars.Reverse();
        return new string(chars.ToArray());
    }

    public static string ToFixedSeedText(long n, int length)
    {
        length = Math.Max(1, length);
        var text = ToSeedText(n);
        return text.Length >= length ? text : text.PadLeft(length, '0');
    }

    public static string RandomSeed(int length)
    {
        Span<byte> bytes = stackalloc byte[Math.Max(1, length)];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(Chars[bytes[i] % Chars.Length]);
        return sb.ToString();
    }
}



public sealed record PlayerSpec(string Name, string Character, string NetId);


