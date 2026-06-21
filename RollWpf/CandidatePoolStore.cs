using System.IO;
using System.Text;
using System.Text.Json;

namespace RollWpf;

public sealed class CandidatePoolStore
{
    public const int CurrentSchemaVersion = 1;
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CandidatePoolStore(string rootDir)
    {
        _path = System.IO.Path.Combine(rootDir, "profiles", "database", "candidate_pools.json");
    }

    public string Path => _path;
    public string DirectoryPath => System.IO.Path.GetDirectoryName(_path) ?? ".";

    public List<CandidateSeedPool> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<CandidateSeedPool>();
            string json = File.ReadAllText(_path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new List<CandidateSeedPool>();
            var pools = JsonSerializer.Deserialize<List<CandidateSeedPool>>(json, JsonOptions) ?? new List<CandidateSeedPool>();
            foreach (var p in pools) NormalizePool(p);
            return pools;
        }
        catch
        {
            return new List<CandidateSeedPool>();
        }
    }

    public void SaveAll(IEnumerable<CandidateSeedPool> pools)
    {
        Directory.CreateDirectory(DirectoryPath);
        var ordered = pools.Select(p => { NormalizePool(p); return p; })
            .Where(p => p.Seeds.Count > 0)
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ordered, JsonOptions), Encoding.UTF8);
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    public CandidateSeedPool AddPool(CandidateSeedPool pool)
    {
        NormalizePool(pool);
        var pools = Load();
        if (string.IsNullOrWhiteSpace(pool.Id)) pool.Id = Guid.NewGuid().ToString("N");
        var existing = pools.FirstOrDefault(p => string.Equals(p.Id, pool.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            pool.CreatedAt = pool.CreatedAt == default ? DateTime.Now : pool.CreatedAt;
            pool.UpdatedAt = DateTime.Now;
            pools.Add(pool);
        }
        else
        {
            existing.Name = string.IsNullOrWhiteSpace(pool.Name) ? existing.Name : pool.Name;
            existing.Note = pool.Note ?? existing.Note;
            existing.SourceFilterSummary = pool.SourceFilterSummary ?? existing.SourceFilterSummary;
            existing.Character = string.IsNullOrWhiteSpace(pool.Character) ? existing.Character : pool.Character;
            existing.Ascension = pool.Ascension;
            existing.RngVersion = string.IsNullOrWhiteSpace(pool.RngVersion) ? existing.RngVersion : pool.RngVersion;
            existing.AppVersion = string.IsNullOrWhiteSpace(pool.AppVersion) ? existing.AppVersion : pool.AppVersion;
            existing.GameVersion = string.IsNullOrWhiteSpace(pool.GameVersion) ? existing.GameVersion : pool.GameVersion;
            existing.Seeds = MergeSeeds(existing.Seeds, pool.Seeds);
            existing.UpdatedAt = DateTime.Now;
        }
        SaveAll(pools);
        return existing ?? pool;
    }

    public void Delete(string id)
    {
        var pools = Load();
        pools.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveAll(pools);
    }

    public CandidateSeedPool? GetById(string id)
        => Load().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public List<CandidateSeedPool> Query(string query)
    {
        string q = (query ?? "").Trim();
        var pools = Load();
        if (q.Length == 0) return pools;
        return pools.Where(p => p.Matches(q)).ToList();
    }

    public string ExportPoolSeeds(CandidateSeedPool pool)
    {
        Directory.CreateDirectory(DirectoryPath);
        string safeName = string.Concat((pool.Name ?? "candidate_pool").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (safeName.Length > 40) safeName = safeName[..40];
        string file = System.IO.Path.Combine(DirectoryPath, $"candidate_pool_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllLines(file, pool.Seeds.Select(s => s.Seed).Where(s => !string.IsNullOrWhiteSpace(s)), Encoding.UTF8);
        return file;
    }

    private static List<CandidateSeedEntry> MergeSeeds(IEnumerable<CandidateSeedEntry> a, IEnumerable<CandidateSeedEntry> b)
    {
        var dict = new Dictionary<string, CandidateSeedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in a.Concat(b))
        {
            NormalizeSeed(s);
            if (string.IsNullOrWhiteSpace(s.Seed)) continue;
            if (!dict.ContainsKey(s.NormalizedSeed)) dict[s.NormalizedSeed] = s;
        }
        return dict.Values.OrderBy(s => s.Index).ThenBy(s => s.Seed, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void NormalizePool(CandidateSeedPool p)
    {
        p.SchemaVersion = p.SchemaVersion <= 0 ? CurrentSchemaVersion : Math.Max(p.SchemaVersion, CurrentSchemaVersion);
        p.Id = (p.Id ?? "").Trim();
        p.Name = string.IsNullOrWhiteSpace(p.Name) ? "粗筛候选池" : p.Name.Trim();
        p.Character = string.IsNullOrWhiteSpace(p.Character) ? "IRONCLAD" : p.Character.Trim().ToUpperInvariant();
        p.RngVersion = string.IsNullOrWhiteSpace(p.RngVersion) ? "sts2_0_107_xoshiro" : p.RngVersion.Trim();
        p.GameVersion = string.IsNullOrWhiteSpace(p.GameVersion) ? "v0.107.1+" : p.GameVersion.Trim();
        p.AppVersion ??= "";
        p.SourceFilterSummary ??= "";
        p.Note ??= "";
        p.Tags ??= new List<string>();
        p.Tags = SeedHistoryStore.NormalizeTags(p.Tags);
        p.Seeds ??= new List<CandidateSeedEntry>();
        p.Seeds = MergeSeeds(Array.Empty<CandidateSeedEntry>(), p.Seeds);
        if (p.CreatedAt == default) p.CreatedAt = DateTime.Now;
        if (p.UpdatedAt == default) p.UpdatedAt = p.CreatedAt;
    }

    private static void NormalizeSeed(CandidateSeedEntry s)
    {
        s.Seed = (s.Seed ?? "").Trim();
        s.NormalizedSeed = string.IsNullOrWhiteSpace(s.NormalizedSeed) ? s.Seed.ToUpperInvariant() : s.NormalizedSeed.Trim().ToUpperInvariant();
        s.Summary ??= "";
        if (s.CreatedAt == default) s.CreatedAt = DateTime.Now;
    }
}

public sealed class CandidateSeedPool
{
    public int SchemaVersion { get; set; } = CandidatePoolStore.CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "粗筛候选池";
    public string Character { get; set; } = "IRONCLAD";
    public int Ascension { get; set; }
    public string RngVersion { get; set; } = "sts2_0_107_xoshiro";
    public string GameVersion { get; set; } = "v0.107.1+";
    public string AppVersion { get; set; } = "";
    public string SourceFilterSummary { get; set; } = "";
    public string Note { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<CandidateSeedEntry> Seeds { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public bool Matches(string query)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0) return true;
        return Contains(Name, q)
            || Contains(Character, q)
            || Contains(SeedHistoryStore.DisplayCharacter(Character), q)
            || Contains(RngVersion, q)
            || Contains(SourceFilterSummary, q)
            || Contains(Note, q)
            || Tags.Any(t => Contains(t, q))
            || Seeds.Any(s => Contains(s.Seed, q) || Contains(s.Summary, q));
    }

    private static bool Contains(string? text, string q)
        => !string.IsNullOrWhiteSpace(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
}

public sealed class CandidateSeedEntry
{
    public int Index { get; set; }
    public string Seed { get; set; } = "";
    public string NormalizedSeed { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CandidateSeedPoolView
{
    public CandidateSeedPoolView(CandidateSeedPool pool) => Pool = pool;
    public CandidateSeedPool Pool { get; }
    public string Id => Pool.Id;
    public string Name => Pool.Name;
    public string Character => SeedHistoryStore.DisplayCharacter(Pool.Character);
    public string AscensionText => "A" + Pool.Ascension;
    public int SeedCount => Pool.Seeds.Count;
    public string RngVersion => Pool.RngVersion;
    public string UpdatedAtText => Pool.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string Summary => string.IsNullOrWhiteSpace(Pool.SourceFilterSummary) ? Pool.Note : Pool.SourceFilterSummary;
    public override string ToString() => $"{Name}（{SeedCount}）";
}

public sealed class CandidateSeedEntryView
{
    public CandidateSeedEntryView(CandidateSeedEntry entry) => Entry = entry;
    public CandidateSeedEntry Entry { get; }
    public int Index => Entry.Index;
    public string Seed => Entry.Seed;
    public string Summary => Entry.Summary;
}
