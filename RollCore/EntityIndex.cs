using System.Text.Json;
using System.Text.RegularExpressions;

namespace RollCore;

public sealed class EntityIndex
{
    private readonly Dictionary<string, EntityIndexEntry> _byCanonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EntityIndexEntry> _byAlias = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byCanonical.Count;
    public string FilePath { get; private set; } = "";
    public bool Loaded => Count > 0;

    public static EntityIndex Load(string rootDir)
    {
        var idx = new EntityIndex();
        string? path = RollDataPaths.FindEntityIndex(rootDir);
        if (path is null) return idx;
        idx.FilePath = path;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var entities = doc.RootElement.Prop("entities");
            if (entities is null || entities.Value.ValueKind != JsonValueKind.Array) return idx;
            foreach (var e in entities.Value.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = e.Prop("display_name");
                var entry = new EntityIndexEntry
                {
                    CanonicalId = e.Prop("canonical_id").Str(""),
                    SourceId = e.Prop("source_id").Str(""),
                    Type = e.Prop("type").Str(""),
                    Zh = name.Prop("zhs").Str(""),
                    Eng = name.Prop("eng").Str(""),
                };
                if (string.IsNullOrWhiteSpace(entry.CanonicalId)) continue;
                idx._byCanonical[entry.CanonicalId] = entry;
                idx.AddAlias(entry.CanonicalId, entry);
                idx.AddAlias(entry.SourceId, entry);
                idx.AddAlias(entry.Zh, entry);
                idx.AddAlias(entry.Eng, entry);
                entry.Aliases.AddRange(e.Prop("aliases").StringList());
                foreach (var a in entry.Aliases) idx.AddAlias(a, entry);
            }
        }
        catch
        {
            // Entity index only affects display/input normalization. Prediction should continue without it.
        }
        return idx;
    }

    public EntityIndexEntry? Resolve(string? input, params string[] types)
    {
        string key = NormalizeLookup(Term.Normalize(input ?? ""));
        if (key.Length == 0) return null;

        bool TypeOk(EntityIndexEntry x)
        {
            if (types.Length == 0) return true;
            foreach (var t in types)
            {
                if (string.Equals(t, "curse", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(x.Type, "curse", StringComparison.OrdinalIgnoreCase)) return true;
                    continue;
                }
                if (string.Equals(x.Type, t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        if (_byAlias.TryGetValue(key, out var exact) && TypeOk(exact)) return exact;
        foreach (var x in _byCanonical.Values)
        {
            if (!TypeOk(x)) continue;
            if (NormalizeLookup(x.CanonicalId).Contains(key, StringComparison.OrdinalIgnoreCase)
                || NormalizeLookup(x.SourceId).Contains(key, StringComparison.OrdinalIgnoreCase)
                || NormalizeLookup(x.Zh).Contains(key, StringComparison.OrdinalIgnoreCase)
                || NormalizeLookup(x.Eng).Contains(key, StringComparison.OrdinalIgnoreCase))
                return x;
        }
        return null;
    }

    public string Canonicalize(string? input, params string[] types)
    {
        return Resolve(input, types)?.CanonicalId ?? Term.Normalize(input ?? "");
    }

    public string DisplayText(string? id, string lang = "zhs")
    {
        var entry = Resolve(id ?? "") ?? (_byCanonical.TryGetValue(Term.Normalize(id ?? ""), out var x) ? x : null);
        if (entry is null) return Term.Normalize(id ?? "");
        return entry.DisplayText(lang);
    }

    public IReadOnlyCollection<EntityIndexEntry> Entries => _byCanonical.Values;

    private void AddAlias(string? alias, EntityIndexEntry entry)
    {
        string key = NormalizeLookup(alias ?? "");
        if (key.Length == 0) return;
        if (!_byAlias.ContainsKey(key)) _byAlias[key] = entry;
    }

    public static string NormalizeLookup(string? text)
    {
        string s = Term.Normalize(text ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return "";
        s = s.Replace("（", "(").Replace("）", ")");
        s = Regex.Replace(s, "[\\s_\\-:()]+", "");
        return s;
    }
}

public sealed record EntityIndexEntry
{
    public string CanonicalId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Type { get; init; } = "";
    public string Zh { get; init; } = "";
    public string Eng { get; init; } = "";
    public List<string> Aliases { get; } = new();

    public string DisplayName(string lang = "zhs")
    {
        if (string.Equals(lang, "eng", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(Eng) ? Eng : (!string.IsNullOrWhiteSpace(Zh) ? Zh : CanonicalId);
        return !string.IsNullOrWhiteSpace(Zh) ? Zh : (!string.IsNullOrWhiteSpace(Eng) ? Eng : CanonicalId);
    }

    public string DisplayText(string lang = "zhs") => DisplayName(lang) + "（" + CanonicalId + "）";
}
