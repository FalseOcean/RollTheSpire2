using System.IO;

namespace RollCore;

/// <summary>
/// Centralized official data-file naming introduced in v17.9-preview4a.
/// Runtime/config filters use CamelCase canonical ids; source/localization files may still carry UPPER_SNAKE_CASE source ids.
/// </summary>
public static class RollDataPaths
{
    public const string OfficialSourceData = "data/sts2_data.json";
    public const string OfficialEventRules = "data/event_rules.json";
    public const string OfficialEventTexts = "data/event_texts.json";
    public const string OfficialEntityIndex = "data/entity_index.json";

    public static string? FindSourceData(string rootDir) => FindFirstExisting(BuildCandidates(rootDir,
        OfficialSourceData,
        "extractor/sts2_extracted_data_v4.json",
        "sts2_extracted_data_v4.json"));

    public static string? FindEventRules(string rootDir) => FindFirstExisting(BuildCandidates(rootDir,
        OfficialEventRules,
        "extractor/event_allowed_rules.json",
        "event_allowed_rules.json"));

    public static string? FindEventTexts(string rootDir) => FindFirstExisting(BuildCandidates(rootDir,
        OfficialEventTexts,
        "extractor/event_texts.json",
        "event_texts.json"));

    public static string? FindEntityIndex(string rootDir) => FindFirstExisting(BuildCandidates(rootDir,
        OfficialEntityIndex,
        "extractor/entity_index.json",
        "entity_index.json"));

    public static IEnumerable<string> BuildCandidates(string rootDir, params string[] relativePaths)
    {
        rootDir = string.IsNullOrWhiteSpace(rootDir) ? Directory.GetCurrentDirectory() : rootDir;
        foreach (var root in SearchRoots(rootDir))
        {
            foreach (var rel in relativePaths)
                yield return Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    public static IEnumerable<string> SearchRoots(string rootDir)
    {
        var roots = new List<string>();
        void Add(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (!roots.Contains(path, StringComparer.OrdinalIgnoreCase)) roots.Add(path);
            }
            catch { }
        }

        Add(rootDir);
        Add(Directory.GetCurrentDirectory());
        Add(AppContext.BaseDirectory);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent) Add(dir.FullName);

        dir = new DirectoryInfo(rootDir);
        for (int i = 0; i < 4 && dir is not null; i++, dir = dir.Parent) Add(dir.FullName);

        return roots;
    }

    public static string? FindFirstExisting(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) return p;
        return null;
    }

    public static string SafeRel(string rootDir, string path)
    {
        try { return Path.GetRelativePath(rootDir, path); }
        catch { return path; }
    }
}
