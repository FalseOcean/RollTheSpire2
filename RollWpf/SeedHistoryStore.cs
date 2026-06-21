using System.IO;
using System.Text;
using System.Text.Json;

namespace RollWpf;

public sealed class SeedHistoryStore
{
    public const int CurrentSchemaVersion = 2;

    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SeedHistoryStore(string rootDir)
    {
        _path = System.IO.Path.Combine(rootDir, "profiles", "database", "search_history.json");
    }

    public string Path => _path;
    public string DirectoryPath => System.IO.Path.GetDirectoryName(_path) ?? ".";

    public List<SeedHistoryRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<SeedHistoryRecord>();
            var json = File.ReadAllText(_path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new List<SeedHistoryRecord>();
            var records = JsonSerializer.Deserialize<List<SeedHistoryRecord>>(json, JsonOptions) ?? new List<SeedHistoryRecord>();
            foreach (var r in records) NormalizeLoadedRecord(r);
            return records;
        }
        catch
        {
            return new List<SeedHistoryRecord>();
        }
    }

    public void SaveAll(IEnumerable<SeedHistoryRecord> records)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        var ordered = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Seed))
            .Select(r => { NormalizeLoadedRecord(r); return r; })
            .OrderByDescending(r => r.Favorite)
            .ThenByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ordered, JsonOptions), Encoding.UTF8);
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    public SeedHistoryRecord AddOrUpdate(SeedHistoryRecord record)
    {
        NormalizeLoadedRecord(record);
        var records = Load();
        string key = record.Key;
        var existing = records.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        var now = DateTime.Now;
        if (existing is null)
        {
            record.Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id;
            record.CreatedAt = record.CreatedAt == default ? now : record.CreatedAt;
            record.UpdatedAt = now;
            records.Add(record);
            SaveAll(records);
            return record;
        }

        existing.Source = MergeSource(existing.Source, record.Source);
        existing.Summary = Prefer(record.Summary, existing.Summary);
        existing.HitExplanation = Prefer(record.HitExplanation, existing.HitExplanation);
        existing.Detail = Prefer(record.Detail, existing.Detail);
        existing.Note = Prefer(existing.Note, record.Note);
        existing.Tags = MergeTags(existing.Tags, record.Tags);
        existing.AppVersion = Prefer(record.AppVersion, existing.AppVersion);
        existing.ConfigHash = Prefer(record.ConfigHash, existing.ConfigHash);
        existing.RngVersion = Prefer(record.RngVersion, existing.RngVersion);
        existing.GameVersion = Prefer(record.GameVersion, existing.GameVersion);
        existing.UnlockMode = Prefer(record.UnlockMode, existing.UnlockMode);
        existing.Favorite = existing.Favorite || record.Favorite;
        existing.Rating = Math.Max(existing.Rating, record.Rating);
        existing.LastAnalyzedAt = Later(existing.LastAnalyzedAt, record.LastAnalyzedAt);
        existing.VerifiedAt = Later(existing.VerifiedAt, record.VerifiedAt);
        existing.SchemaVersion = Math.Max(existing.SchemaVersion, record.SchemaVersion);
        existing.UpdatedAt = now;
        SaveAll(records);
        return existing;
    }

    public void Delete(string id)
    {
        var records = Load();
        records.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveAll(records);
    }

    public void UpdateUserFields(string id, IEnumerable<string> tags, string note, bool favorite, int rating)
    {
        var records = Load();
        var rec = records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (rec is null) return;
        rec.Tags = NormalizeTags(tags);
        rec.Note = note ?? "";
        rec.Favorite = favorite;
        rec.Rating = ClampRating(rating);
        rec.UpdatedAt = DateTime.Now;
        SaveAll(records);
    }

    public List<SeedHistoryRecord> Query(string query)
    {
        var records = Load();
        string q = (query ?? "").Trim();
        if (q.Length == 0) return records.OrderByDescending(r => r.Favorite).ThenByDescending(r => r.UpdatedAt).ThenByDescending(r => r.CreatedAt).ToList();
        return records
            .Where(r => r.Matches(q))
            .OrderByDescending(r => r.Favorite)
            .ThenByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
    }

    public string ExportVisibleRecords(IEnumerable<SeedHistoryRecordView> views)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        string file = System.IO.Path.Combine(DirectoryPath, "search_history_export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".tsv");
        var sb = new StringBuilder();
        sb.AppendLine("seed\tnormalized_seed\tcharacter\tcharacter_display\tascension\trng_version\tfavorite\ttags\tnote\tsource\tupdated_at\tsummary");
        foreach (var v in views)
        {
            var r = v.Record;
            sb.Append(EscapeTsv(r.Seed)).Append('\t')
              .Append(EscapeTsv(r.NormalizedSeed)).Append('\t')
              .Append(EscapeTsv(r.Character)).Append('\t')
              .Append(EscapeTsv(DisplayCharacter(r.Character))).Append('\t')
              .Append(r.Ascension).Append('\t')
              .Append(EscapeTsv(r.RngVersion)).Append('\t')
              .Append(r.Favorite ? "1" : "0").Append('\t')
              .Append(EscapeTsv(string.Join(", ", r.Tags))).Append('\t')
              .Append(EscapeTsv(r.Note)).Append('\t')
              .Append(EscapeTsv(r.Source)).Append('\t')
              .Append(r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")).Append('\t')
              .Append(EscapeTsv(r.Summary)).AppendLine();
        }
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        return file;
    }

    public static List<string> NormalizeTags(IEnumerable<string> tags)
        => tags.SelectMany(t => (t ?? "").Split(new[] { ',', '，', ';', '；', '|', '/', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void NormalizeLoadedRecord(SeedHistoryRecord r)
    {
        r.Seed = (r.Seed ?? "").Trim();
        r.NormalizedSeed = string.IsNullOrWhiteSpace(r.NormalizedSeed) ? r.Seed.ToUpperInvariant() : r.NormalizedSeed.Trim().ToUpperInvariant();
        r.Character = string.IsNullOrWhiteSpace(r.Character) ? "IRONCLAD" : r.Character.Trim();
        r.RunMode = string.IsNullOrWhiteSpace(r.RunMode) ? "singleplayer" : r.RunMode.Trim();
        r.TargetNetId = string.IsNullOrWhiteSpace(r.TargetNetId) ? "1" : r.TargetNetId.Trim();
        r.RngVersion = string.IsNullOrWhiteSpace(r.RngVersion) ? "sts2_0_107_xoshiro" : r.RngVersion.Trim();
        r.UnlockMode = string.IsNullOrWhiteSpace(r.UnlockMode) ? "all_unlocked" : r.UnlockMode.Trim();
        r.Source = string.IsNullOrWhiteSpace(r.Source) ? "manual" : r.Source.Trim();
        r.Tags = NormalizeTags(r.Tags ?? Enumerable.Empty<string>());
        r.Note ??= "";
        r.Summary ??= "";
        r.HitExplanation ??= "";
        r.Detail ??= "";
        r.AppVersion ??= "";
        r.ConfigHash ??= "";
        r.GameVersion ??= "";
        r.Rating = ClampRating(r.Rating);
        r.SchemaVersion = r.SchemaVersion <= 0 ? CurrentSchemaVersion : Math.Max(r.SchemaVersion, CurrentSchemaVersion);
        if (r.CreatedAt == default) r.CreatedAt = DateTime.Now;
        if (r.UpdatedAt == default) r.UpdatedAt = r.CreatedAt;
    }

    private static string EscapeTsv(string? text)
        => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static List<string> MergeTags(IEnumerable<string> a, IEnumerable<string> b)
        => NormalizeTags(a.Concat(b));

    private static string Prefer(string? primary, string? fallback)
        => string.IsNullOrWhiteSpace(primary) ? (fallback ?? "") : primary.Trim();

    private static DateTime? Later(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value >= b.Value ? a : b;
    }

    public static int ClampRating(int value) => Math.Max(0, Math.Min(5, value));

    public static string DisplayCharacter(string? character)
    {
        string key = (character ?? "").Trim();
        return key.ToUpperInvariant() switch
        {
            "IRONCLAD" => "铁甲战士（Ironclad）",
            "SILENT" => "静默猎手（Silent）",
            "DEFECT" => "故障机器人（Defect）",
            "NECROBINDER" => "亡灵契约师（Necrobinder）",
            "REGENT" => "储君（Regent）",
            _ => string.IsNullOrWhiteSpace(key) ? "" : key,
        };
    }

    private static string MergeSource(string a, string b)
    {
        var parts = NormalizeTags(new[] { a, b });
        return parts.Count == 0 ? "manual" : string.Join(",", parts);
    }
}

public sealed class SeedHistoryRecord
{
    public int SchemaVersion { get; set; } = SeedHistoryStore.CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string Seed { get; set; } = "";
    public string NormalizedSeed { get; set; } = "";
    public string Character { get; set; } = "IRONCLAD";
    public int Ascension { get; set; }
    public string RunMode { get; set; } = "singleplayer";
    public string TargetNetId { get; set; } = "1";
    public string RngVersion { get; set; } = "sts2_0_107_xoshiro";
    public string GameVersion { get; set; } = "v0.107.1+";
    public string UnlockMode { get; set; } = "all_unlocked";
    public string Source { get; set; } = "manual";
    public bool Favorite { get; set; }
    public int Rating { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Note { get; set; } = "";
    public string Summary { get; set; } = "";
    public string HitExplanation { get; set; } = "";
    public string Detail { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public DateTime? LastAnalyzedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string Key => string.Join("|",
        Normalize(Seed),
        Character.Trim().ToUpperInvariant(),
        Ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
        RunMode.Trim().ToLowerInvariant(),
        TargetNetId.Trim(),
        RngVersion.Trim().ToLowerInvariant());

    public bool Matches(string query)
    {
        string q = query.Trim();
        return Contains(Seed, q)
            || Contains(NormalizedSeed, q)
            || Contains(Character, q)
            || Contains(SeedHistoryStore.DisplayCharacter(Character), q)
            || Contains(Source, q)
            || Contains(RngVersion, q)
            || Contains(GameVersion, q)
            || Contains(Summary, q)
            || Contains(HitExplanation, q)
            || Contains(Note, q)
            || (Favorite && (Contains("favorite", q) || Contains("星标", q) || Contains("收藏", q)))
            || (Rating > 0 && Contains("rating:" + Rating, q))
            || Tags.Any(t => Contains(t, q));
    }

    private static bool Contains(string? text, string q)
        => !string.IsNullOrWhiteSpace(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Normalize(string text)
        => (text ?? "").Trim().ToUpperInvariant();
}

public sealed class SeedHistoryRecordView
{
    public SeedHistoryRecordView(SeedHistoryRecord record) => Record = record;
    public SeedHistoryRecord Record { get; }
    public string Id => Record.Id;
    public string FavoriteLabel => Record.Favorite ? "是" : "";
    public string Seed => Record.Seed;
    public string Character => SeedHistoryStore.DisplayCharacter(Record.Character);
    public string AscensionText => "A" + Record.Ascension;
    public string TagsText => Record.Tags.Count == 0 ? "" : string.Join(", ", Record.Tags);
    public string Summary => Record.Summary;
    public string Source => string.IsNullOrWhiteSpace(Record.RngVersion) ? Record.Source : Record.Source + " · " + Record.RngVersion;
    public string CreatedAtText => Record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string UpdatedAtText => Record.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}
