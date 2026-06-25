using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RollCore;

namespace RollWpf;

public partial class MainWindow : Window
{
    private readonly string _rootDir;
    private readonly string _configPath;
    private CancellationTokenSource? _searchCts;
    private readonly ObservableCollection<SearchHitView> _searchHits = new();
    private readonly ObservableCollection<ResultCardView> _analyzeCards = new();
    private readonly ObservableCollection<ResultCardView> _searchDetailCards = new();
    private readonly ObservableCollection<SearchRequirementTagView> _searchRequirementTags = new();
    private readonly ObservableCollection<EventFilterConditionView> _eventFilterConditions = new();
    private readonly ObservableCollection<SeedHistoryRecordView> _historyRecords = new();
    private readonly ObservableCollection<CandidateSeedPoolView> _candidatePools = new();
    private readonly ObservableCollection<CandidateSeedEntryView> _candidateSeeds = new();
    private readonly ObservableCollection<MultiplayerPlayerView> _multiplayerPlayers = new();
    private readonly ObservableCollection<ItemAliasView> _neowRelicItems = new();
    private readonly Dictionary<string, ItemAliasView> _itemAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemAliasView> _itemsByRuntimeId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NeowRelicEffectView> _neowRelicEffects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NeowRelicTemplateView> _neowTemplatesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _dynamicTemplateTerms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _dynamicTemplateInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemAliasView> _cardItems = new();
    private readonly List<ItemAliasView> _potionItems = new();
    private readonly List<ItemAliasView> _relicItems = new();
    private readonly List<ItemAliasView> _shopRelicItems = new();
    private readonly List<ItemAliasView> _neowRelicBlacklistItems = new();
    private readonly List<ItemAliasView> _ancientItems = new();
    private readonly List<ItemAliasView> _ancientOptionItems = new();
    private readonly List<ItemAliasView> _eventItems = new();
    private readonly List<ItemAliasView> _curseItems = new();
    private readonly List<ItemAliasView> _finalCardItems = new();
    private readonly List<ItemAliasView> _finalPotionItems = new();
    private readonly List<ItemAliasView> _finalCurseItems = new();
    private readonly List<ItemAliasView> _finalRelicItems = new();
    private readonly Dictionary<string, CardPoolMetaView> _cardPoolMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PotionPoolMetaView> _potionPoolMeta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RelicPoolMetaView> _relicPoolMeta = new(StringComparer.OrdinalIgnoreCase);
    private EntityIndex _entityIndex = new();
    private List<EventEncyclopediaItem> _allEvents = new();
    private Dictionary<string, EventEncyclopediaItem> _eventTooltipIndex = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedSearchSeed = "";
    private SeedHistoryStore? _historyStore;
    private CandidatePoolStore? _candidatePoolStore;
    private string _lastAnalyzeSeed = "";
    private string _lastAnalyzeCharacter = "IRONCLAD";
    private int _lastAnalyzeAscension;
    private string _lastAnalyzeSummary = "";

    private const string AppVersionText = "v2.1.1";

    // 与 RollCore.OpeningPredictor.BonesCursePool 保持一致；这里只用于 WPF 下拉候选，不改预测算法。
    private static readonly string[] BonesCursePoolRuntimeIds =
    {
        "Clumsy", "Debt", "Decay", "Doubt", "Guilty", "Injury", "Normality", "Regret", "Shame", "Writhe"
    };

    public MainWindow()
    {
        InitializeComponent();
        _rootDir = FindRootDir();
        _configPath = Path.Combine(_rootDir, "config.json");
        RootDirTextBox.Text = _rootDir;
        ConfigPathTextBox.Text = _configPath;
        _historyStore = new SeedHistoryStore(_rootDir);
        _candidatePoolStore = new CandidatePoolStore(_rootDir);
        SearchHitsListBox.ItemsSource = _searchHits;
        AnalyzeCardsListBox.ItemsSource = _analyzeCards;
        SearchDetailCardsListBox.ItemsSource = _searchDetailCards;
        SearchRequirementTagsListBox.ItemsSource = _searchRequirementTags;
        EventFilterConditionsListBox.ItemsSource = _eventFilterConditions;
        HistoryDataGrid.ItemsSource = _historyRecords;
        CandidatePoolsDataGrid.ItemsSource = _candidatePools;
        CandidateSeedsDataGrid.ItemsSource = _candidateSeeds;
        MultiplayerPlayersItemsControl.ItemsSource = _multiplayerPlayers;
        LoadItemAliases();
        LoadNeowRelicEffects();
        PopulateNeowRelicDropdowns();
        PopulateFinalResultDropdowns();
        RefreshUnlockProfileStatus();
        AttachSearchTagRefreshHandlers();
        InitializeDefaultMultiplayerPlayers();
        AppendLog("RollTheSpire2 v2.1.1 started. Root=" + _rootDir);
        RefreshStatus();
        RefreshCandidatePoolComboBox();
        EnsureEventDropdownItems();
        RefreshRunModeStatus();
        UpdateNeowFilterModeUi();
        UpdateTagBuilderUi(syncLegacy: true);
        UpdateAnalyzeAdvancedNewLeafUi();
        RefreshSearchRequirementTags();
        ShowPage(AnalyzePage, "单种分析", "单种分析已加入卡片摘要；原始文本仍保留用于核对。");
    }

    private static string FindRootDir()
    {
        var candidates = new List<string>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(dir.FullName);

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(c, "config.json")) &&
                (File.Exists(Path.Combine(c, "data", "sts2_data.json")) || File.Exists(Path.Combine(c, "extractor", "sts2_extracted_data_v4.json"))))
                return c;
        }
        return Directory.GetCurrentDirectory();
    }

    private void ShowPage(UIElement page, string title, string subtitle)
    {
        foreach (UIElement p in new UIElement[] { ConfigPage, AnalyzePage, SearchPage, SearchResultsPage, HistoryPage, CandidatePage, EventsPage, LogPage, StatusPage })
            p.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageTitleText.Text = title;
        PageSubtitleText.Text = subtitle;
    }

    private void NavConfig_Click(object sender, RoutedEventArgs e) => ShowPage(ConfigPage, "配置", "查看当前 WPF 使用的项目根目录与配置文件。");
    private void NavAnalyze_Click(object sender, RoutedEventArgs e) => ShowPage(AnalyzePage, "单种分析", "直接调用 RollCore 预测单个 seed。");
    private void NavSearch_Click(object sender, RoutedEventArgs e) => ShowPage(SearchPage, "批量筛种", "设置筛选条件并启动/停止筛种；命中详情已移到“筛种结果”页。");
    private void NavSearchResults_Click(object sender, RoutedEventArgs e) => ShowPage(SearchResultsPage, "筛种结果", "浏览批量筛种命中，点击左侧 seed 查看右侧详情。");
    private void NavHistory_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(HistoryPage, "种子收藏", "收藏库 v2：保存爽种、标签、备注、命中解释与 RNG 版本；评分星数已隐藏。 ");
        RefreshHistory();
    }

    private void NavCandidate_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(CandidatePage, "粗筛候选库", "保存放宽条件后的候选 seed 池，后续可从候选池继续精筛。候选池只保存 seed 与极简摘要，不保存完整分析详情。 ");
        RefreshCandidatePools();
    }
    private void NavEvents_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(EventsPage, "事件百科", "搜索全部 57 个普通事件，查看区域、条件、游戏内描述和选项文本。");
        if (_allEvents.Count == 0) LoadEvents();
    }
    private void NavLog_Click(object sender, RoutedEventArgs e) => ShowPage(LogPage, "运行日志", "WPF 运行日志与错误信息。");
    private void NavStatus_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(StatusPage, "数据状态", "RollCore 数据与配置加载状态。");
        RefreshStatus();
    }

    private void NeowFilterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateNeowFilterModeUi();
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
    }

    private void TagBuilderChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
    }

    private void AncientDarvActComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        AncientSearchEnabledCheckBox.IsChecked = true;
        RefreshSearchRequirementTags();
    }

    private void AddBonesRelicTag_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedAliasFromCombo(BonesRelicBuilderComboBox, "relic");
        if (item is null)
        {
            BottomStatusText.Text = "请选择或输入要指定的骨骰遗物。";
            return;
        }
        string runtimeId = item.RuntimeId;
        if (runtimeId.Equals("NeowsBones", StringComparison.OrdinalIgnoreCase))
        {
            BottomStatusText.Text = "骨骰路线的遗物结果不能再次选择涅奥骨骰本身；请选择骨骰实际给出的其它遗物。";
            return;
        }
        SetCombo(BonesRequirementComboBox, "yes");
        SetCombo(BonesRelicModeComboBox, "specified");
        AppendTerm(BonesRelicAllTextBox, runtimeId);
        BonesRelicBuilderComboBox.Text = "";
        UpdateTagBuilderUi(syncLegacy: true);
        UpdateSpecialRelicTemplateUi();
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加骨骰遗物 Tag：" + DisplayTerm(runtimeId);
    }

    private void ClearBonesRelicTags_Click(object sender, RoutedEventArgs e)
    {
        BonesRelicAnyTextBox.Clear();
        BonesRelicAllTextBox.Clear();
        BonesRelicBlacklistTextBox.Clear();
        NewLeafCardAllTextBox.Clear();
        KaleidoscopeGroup1CardAllTextBox.Clear();
        KaleidoscopeGroup2CardAllTextBox.Clear();
        LostCofferCardAllTextBox.Clear();
        LostCofferPotionAllTextBox.Clear();
        BonesRelicBuilderComboBox.Text = "";
        NewLeafCardBuilderTextBox.Clear();
        KaleidoscopeGroup1CardTextBox.Clear();
        KaleidoscopeGroup2CardTextBox.Clear();
        LostCofferCardTextBox.Clear();
        LostCofferPotionTextBox.Clear();
        SetCombo(BonesRelicModeComboBox, "undirected");
        UpdateTagBuilderUi(syncLegacy: true);
        UpdateSpecialRelicTemplateUi();
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空骨骰遗物和特殊遗物子条件 Tag。";
    }

    private void AddDirectRelicTag_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedAliasFromCombo(DirectRelicBuilderComboBox, "relic");
        if (item is null)
        {
            BottomStatusText.Text = "请选择或输入直接 Neow 遗物。";
            return;
        }
        if (item.RuntimeId.Equals("NeowsBones", StringComparison.OrdinalIgnoreCase))
        {
            BottomStatusText.Text = "无骨骰路线不能选择涅奥骨骰本身；请改选其它直接 Neow 遗物。";
            return;
        }
        SetCombo(BonesRequirementComboBox, "no");
        AppendTerm(NeowAllTextBox, item.RuntimeId);
        DirectRelicBuilderComboBox.Text = "";
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加 Neow 遗物/效果 Tag：" + DisplayTerm(item.RuntimeId);
    }

    private void ClearDirectRelicTags_Click(object sender, RoutedEventArgs e)
    {
        NeowAllTextBox.Clear();
        DirectRelicBuilderComboBox.Text = "";
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空直接 Neow 遗物 Tag。";
    }


    private void AddShopExactTag_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedAliasFromCombo(ShopRelicBuilderComboBox, "relic");
        if (item is null || !IsShopRelicRuntimeId(item.RuntimeId))
        {
            BottomStatusText.Text = "请选择一个 Shop 稀有度的商店专属遗物。";
            return;
        }
        int pos = SelectedShopLimit(1);
        ShopFilterEnabledCheckBox.IsChecked = true;
        AppendTerm(ShopExactTextBox, pos + ":" + item.RuntimeId);
        ShopRelicBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = $"已添加商店 Tag：第 {pos} 个商店专属遗物是 {DisplayTerm(item.RuntimeId)}";
    }

    private void AddShopPrefixTag_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedAliasFromCombo(ShopRelicBuilderComboBox, "relic");
        if (item is null || !IsShopRelicRuntimeId(item.RuntimeId))
        {
            BottomStatusText.Text = "请选择一个 Shop 稀有度的商店专属遗物。";
            return;
        }
        int n = SelectedShopLimit(5);
        ShopFilterEnabledCheckBox.IsChecked = true;
        AppendTerm(ShopRequireTextBox, item.RuntimeId + " <= " + n);
        ShopRelicBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = $"已添加商店 Tag：前 {n} 个商店专属遗物包含 {DisplayTerm(item.RuntimeId)}";
    }

    private void ClearShopTags_Click(object sender, RoutedEventArgs e)
    {
        ShopExactTextBox.Clear();
        ShopRequireTextBox.Clear();
        ShopBlacklistTextBox.Clear();
        ShopRelicBuilderComboBox.Text = "";
        ShopFilterEnabledCheckBox.IsChecked = false;
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空商店专属遗物 Tag。";
    }

    // preview1e: 事件筛选 UI 暂不显示“包含任一”。保留方法不再由 XAML 绑定，避免旧预设/旧代码引用时出错。
    private void AddEventAnyTag_Click(object sender, RoutedEventArgs e) => AddEventTag(EventAnyTextBox, "任一包含");

    private void AddEventAllTag_Click(object sender, RoutedEventArgs e) => AddEventTag(EventAllTextBox, "全部包含");

    private void AddEventBlacklistTag_Click(object sender, RoutedEventArgs e) => AddEventTag(EventBlacklistTextBox, "禁止出现");

    private void AddEventTag(TextBox targetBox, string label)
    {
        EnsureEventDropdownItems();
        string term = SelectedEventTerm();
        if (string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请从下拉框选择一个事件。";
            return;
        }
        string filterTerm = BuildEventFilterTerm(term);
        AppendTerm(targetBox, filterTerm);
        EventBuilderComboBox.SelectedItem = null;
        EventBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = $"已添加事件条件：{EventFilterKindDisplay(targetBox)} · {DisplayEventTerm(filterTerm)}";
    }

    private void ClearEventTags_Click(object sender, RoutedEventArgs e)
    {
        EventAnyTextBox.Clear();
        EventAllTextBox.Clear();
        EventBlacklistTextBox.Clear();
        EventBuilderComboBox.SelectedItem = null;
        EventBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空事件队列筛选条件。";
    }

    private void RemoveEventCondition_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EventFilterConditionView row) return;
        if (row.Kind.Equals("event_all", StringComparison.OrdinalIgnoreCase)) RemoveTerm(EventAllTextBox, row.RawTerm);
        else if (row.Kind.Equals("event_blacklist", StringComparison.OrdinalIgnoreCase)) RemoveTerm(EventBlacklistTextBox, row.RawTerm);
        else if (row.Kind.Equals("event_any", StringComparison.OrdinalIgnoreCase)) RemoveTerm(EventAnyTextBox, row.RawTerm);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已删除事件条件：" + row.ConditionText + " " + row.EventText;
    }

    private string BuildEventFilterTerm(string eventRuntimeId)
    {
        string ev = Term.Normalize(eventRuntimeId ?? "").Trim();
        int limit = SelectedSearchEventFilterLimit();
        string scope = SelectedComboText(EventActScopeComboBox, "any").Trim().ToLowerInvariant();
        if (int.TryParse(scope, out int act) && act >= 1 && act <= 3)
            return $"act{act}<={limit}:{ev}";
        return $"n{limit}:{ev}";
    }

    private string EventFilterKindDisplay(TextBox targetBox)
    {
        if (ReferenceEquals(targetBox, EventBlacklistTextBox)) return "排除";
        if (ReferenceEquals(targetBox, EventAllTextBox)) return "必须包含";
        return "包含任一";
    }

    private string SelectedEventTerm()
    {
        if (EventBuilderComboBox.SelectedItem is ItemAliasView item) return item.RuntimeId;
        string text = EventBuilderComboBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return "";
        var resolved = ResolveItemAlias(text, "event");
        return resolved?.RuntimeId ?? "";
    }

    private void AddAncientRequireTag_Click(object sender, RoutedEventArgs e)
    {
        string term = SelectedAncientTerm();
        if (string.IsNullOrWhiteSpace(term)) { BottomStatusText.Text = "请选择或输入先古之民。"; return; }
        AncientSearchEnabledCheckBox.IsChecked = true;
        AppendTerm(AncientRequireTextBox, term);
        AncientBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加先古 Tag：需要 " + term;
    }

    private void AddAncientBlacklistTag_Click(object sender, RoutedEventArgs e)
    {
        string term = SelectedAncientTerm();
        if (string.IsNullOrWhiteSpace(term)) { BottomStatusText.Text = "请选择或输入先古之民。"; return; }
        AncientSearchEnabledCheckBox.IsChecked = true;
        AppendTerm(AncientBlacklistTextBox, term);
        AncientBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加先古 Tag：不要 " + term;
    }

    private void AddAncientOptionRequireTag_Click(object sender, RoutedEventArgs e)
    {
        string term = SelectedAncientOptionTerm();
        if (string.IsNullOrWhiteSpace(term)) { BottomStatusText.Text = "请选择或输入先古定向选项。"; return; }
        AncientSearchEnabledCheckBox.IsChecked = true;
        AppendTerm(AncientOptionRequireTextBox, term);
        AncientOptionBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加先古选项 Tag：需要 " + term;
    }

    private void AddAncientOptionBlacklistTag_Click(object sender, RoutedEventArgs e)
    {
        string term = SelectedAncientOptionTerm();
        if (string.IsNullOrWhiteSpace(term)) { BottomStatusText.Text = "请选择或输入先古定向选项。"; return; }
        AncientSearchEnabledCheckBox.IsChecked = true;
        AppendTerm(AncientOptionBlacklistTextBox, term);
        AncientOptionBuilderComboBox.Text = "";
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加先古选项 Tag：不想要 " + term;
    }

    private void ClearAncientTags_Click(object sender, RoutedEventArgs e)
    {
        AncientRequireTextBox.Clear();
        AncientBlacklistTextBox.Clear();
        AncientOptionRequireTextBox.Clear();
        AncientOptionBlacklistTextBox.Clear();
        AncientBuilderComboBox.Text = "";
        AncientOptionBuilderComboBox.Text = "";
        AncientSearchEnabledCheckBox.IsChecked = false;
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空先古之民 Tag。";
    }

    private void ImportProgressSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择 STS2 progress.save",
                Filter = "progress.save|progress.save|JSON / save files|*.save;*.json|All files|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            ImportProgressSaveFromFile(dlg.FileName, "手动导入");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导入 progress.save 失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            AppendLog("Import progress.save failed: " + ex.Message);
        }
    }

    private void AutoImportProgressSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = FindDefaultProgressSavePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string defaultDir = Path.Combine(appData, "SlayTheSpire2", "steam");
                BottomStatusText.Text = "默认路径未找到 progress.save，可手动导入。";
                string expectedModded = Path.Combine(defaultDir, "<SteamID>", "modded", "profileN", "saves", "progress.save");
                string expectedVanilla = Path.Combine(defaultDir, "<SteamID>", "profileN", "saves", "progress.save");
                MessageBox.Show("没有在默认路径找到 progress.save。\n\n会自动扫描：\n"
                    + expectedModded + "\n"
                    + expectedVanilla + "\n\n可点击“手动导入 progress.save”选择文件。",
                    "自动导入 progress.save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ImportProgressSaveFromFile(path, "自动导入默认路径");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "自动导入 progress.save 失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            AppendLog("Auto import progress.save failed: " + ex.Message);
        }
    }

    private string? FindDefaultProgressSavePath()
    {
        // STS2 progress.save usually lives under:
        // %APPDATA%\SlayTheSpire2\steam\<SteamID>\modded\profileN\saves\progress.save
        // or, without mods:
        // %APPDATA%\SlayTheSpire2\steam\<SteamID>\profileN\saves\progress.save
        // Use RollCore's locator first so auto import follows the real SteamID/profile layout.
        try
        {
            var candidates = ProgressSaveLocator.FindCandidates();
            if (candidates.Count > 0) return candidates[0].Path;
        }
        catch
        {
            // Fall through to the legacy broad scan below.
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string baseDir = Path.Combine(appData, "SlayTheSpire2", "steam");
        if (!Directory.Exists(baseDir)) return null;

        try
        {
            return Directory.EnumerateFiles(baseDir, "progress.save", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void ImportProgressSaveFromFile(string fileName, string sourceLabel)
    {
        string profilesDir = Path.Combine(_rootDir, "profiles");
        string outputPath = Path.Combine(profilesDir, "unlock_profile.json");
        string dataPath = Path.Combine(_rootDir, "data", "sts2_data.json");
        ProfileImportFiles.ClearBeforeImport(profilesDir);
        var profile = ProgressSaveImporter.ImportToFile(fileName, outputPath, dataPath);
        ProfileImportFiles.SaveRawCopy(fileName, profilesDir);
        UseProgressProfileCheckBox.IsChecked = true;
        UnlockProfilePathTextBox.Text = "profiles/unlock_profile.json";
        RefreshUnlockProfileStatus(profile);
        BottomStatusText.Text = "已导入 progress.save 并启用解锁档案。";
        AppendLog($"Imported progress.save ({sourceLabel}): " + fileName);
    }

    private void RefreshUnlockProfileStatus(JsonNode? importedProfile = null)
    {
        try
        {
            string rel = "profiles/unlock_profile.json";
            string full = Path.Combine(_rootDir, rel);
            UnlockProfilePathTextBox.Text = rel;
            if (!File.Exists(full))
            {
                UnlockProfileStatusText.Text = "当前模式：全解锁模式（尚未导入 progress.save）。";
                SearchProfileHintText.Text = "解锁档案：默认全解锁";
                RefreshRunModeStatus();
                return;
            }

            JsonNode? node = importedProfile ?? JsonNode.Parse(File.ReadAllText(full, Encoding.UTF8));
            string unique = node?["identifiers"]?["unique_id"]?.GetValue<string>() ?? "";
            int schema = node?["unlocks"]?["schema_version"]?.GetValue<int>() ?? 0;
            int total = node?["unlocks"]?["total_unlocks"]?.GetValue<int>() ?? 0;
            int epochs = node?["unlocks"]?["revealed_epoch_ids"] is JsonArray arr ? arr.Count : 0;
            int cards = ProfileCount(node, "discovered_cards");
            int relics = ProfileCount(node, "discovered_relics");
            int potions = ProfileCount(node, "discovered_potions");
            int events = ProfileCount(node, "discovered_events");
            var playable = ProfilePlayableCharacters(node);
            string status = $"已找到 unlock_profile.json：schema={schema}，total_unlocks={total}，revealed_epochs={epochs}";
            status += $"，发现：卡牌 {cards} / 遗物 {relics} / 药水 {potions} / 事件 {events}";
            if (playable.Count > 0) status += "，角色=" + string.Join("/", playable.Take(6).Select(D));
            if (!string.IsNullOrWhiteSpace(unique)) status += "，unique_id=" + unique;
            bool enabled = UseProgressProfileCheckBox.IsChecked == true;
            UnlockProfileStatusText.Text = enabled ? "当前模式：使用 progress.save 解锁档案。" + status : "当前模式：全解锁模式（已找到 unlock_profile.json，但未使用）。" + status;
            SearchProfileHintText.Text = enabled ? $"解锁档案：按 progress.save 限制（epochs={epochs}, cards={cards}, relics={relics}, potions={potions}）" : "解锁档案：全解锁模式（有导入档案但未使用）";
            RefreshRunModeStatus();
        }
        catch (Exception ex)
        {
            UnlockProfileStatusText.Text = "解锁档案状态读取失败：" + ex.Message;
            SearchProfileHintText.Text = "解锁档案：状态读取失败";
        }
    }


    private static int ProfileCount(JsonNode? node, string key)
    {
        if (node?["counts"]?[key] is JsonValue v && v.TryGetValue<int>(out var n)) return n;
        if (node?["discovered"]?[key.Replace("discovered_", "")] is JsonArray arr) return arr.Count;
        return 0;
    }

    private static List<string> ProfilePlayableCharacters(JsonNode? node)
    {
        var arr = node?["characters"]?["probably_playable_for_unlock_state"] as JsonArray;
        if (arr is null) return new List<string>();
        return arr.Select(x => x?.GetValue<string>() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RunModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EnsureMultiplayerRosterForCurrentTarget();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void MultiplayerPlayersCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EnsureMultiplayerRosterForCurrentTarget();
        RefreshRunModeStatus();
    }

    private void TargetNetIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingMultiplayerRoster) return;
        EnsureMultiplayerRosterForCurrentTarget();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void MultiplayerPlayersTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingMultiplayerRoster) return;
        LoadMultiplayerPlayersFromText(MultiplayerPlayersTextBox.Text ?? "");
        RefreshRunModeStatus();
    }

    private bool IsMultiplayerUiSelected()
        => SelectedComboText(RunModeComboBox, "singleplayer").Equals("multiplayer", StringComparison.OrdinalIgnoreCase);

    private int SelectedPlayersCount()
    {
        if (int.TryParse(SelectedComboText(MultiplayerPlayersCountComboBox, "2"), out int n)) return Math.Clamp(n, 2, 8);
        return 2;
    }

    private string SelectedTargetNetId()
    {
        var target = _multiplayerPlayers.FirstOrDefault(p => p.IsTarget) ?? _multiplayerPlayers.FirstOrDefault();
        string text = (target?.NetId ?? TargetNetIdTextBox?.Text ?? "").Trim();
        return string.IsNullOrWhiteSpace(text) ? "1" : text;
    }

    private bool _updatingMultiplayerRoster;

    private void InitializeDefaultMultiplayerPlayers()
    {
        if (_multiplayerPlayers.Count == 0)
        {
            _multiplayerPlayers.Add(new MultiplayerPlayerView { Order = 1, Name = "P1", NetId = "1", Character = "IRONCLAD", Enabled = true, IsTarget = true });
            _multiplayerPlayers.Add(new MultiplayerPlayerView { Order = 2, Name = "P2", NetId = "2", Character = "SILENT", Enabled = true, IsTarget = false });
        }
        RenumberMultiplayerPlayers();
        SyncHiddenMultiplayerFields();
    }

    private string CurrentCharacterForPage()
    {
        if (SearchPage.Visibility == Visibility.Visible) return SelectedComboText(SearchCharacterComboBox, "IRONCLAD");
        if (AnalyzePage.Visibility == Visibility.Visible) return SelectedCharacter();
        return SelectedComboText(SearchCharacterComboBox, SelectedCharacter());
    }

    private void SyncTargetPlayerCharacterFromCurrent()
    {
        SyncTargetPlayerCharacter(CurrentCharacterForPage());
    }

    private void SyncTargetPlayerCharacter(string character)
    {
        if (!IsInitialized || _updatingMultiplayerRoster) return;
        var target = EnsureTargetMultiplayerPlayer();
        target.Character = string.IsNullOrWhiteSpace(character) ? "IRONCLAD" : character.ToUpperInvariant();
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private MultiplayerPlayerView EnsureTargetMultiplayerPlayer()
    {
        InitializeDefaultMultiplayerPlayers();
        var target = _multiplayerPlayers.FirstOrDefault(p => p.IsTarget);
        if (target is not null) return target;
        target = _multiplayerPlayers.First();
        target.IsTarget = true;
        return target;
    }

    private void EnsureMultiplayerRosterForCurrentTarget()
    {
        if (!IsInitialized || _updatingMultiplayerRoster) return;
        _updatingMultiplayerRoster = true;
        try
        {
            InitializeDefaultMultiplayerPlayers();
            int count = SelectedPlayersCount();

            while (_multiplayerPlayers.Count < count)
            {
                int next = _multiplayerPlayers.Count + 1;
                _multiplayerPlayers.Add(new MultiplayerPlayerView
                {
                    Order = next,
                    Name = "P" + next,
                    NetId = next.ToString(),
                    Character = next == 1 ? "IRONCLAD" : "SILENT",
                    Enabled = true,
                    IsTarget = false,
                });
            }

            while (_multiplayerPlayers.Count > count && _multiplayerPlayers.Count > 2)
            {
                var last = _multiplayerPlayers.Last();
                if (last.IsTarget) break;
                _multiplayerPlayers.Remove(last);
            }

            if (!_multiplayerPlayers.Any(p => p.IsTarget))
                _multiplayerPlayers.First().IsTarget = true;

            RenumberMultiplayerPlayers();
            if (_multiplayerPlayers.Count != count)
                SetCombo(MultiplayerPlayersCountComboBox, Math.Clamp(_multiplayerPlayers.Count, 2, 8).ToString());
            SyncHiddenMultiplayerFields();
        }
        finally
        {
            _updatingMultiplayerRoster = false;
        }
    }

    private void RenumberMultiplayerPlayers()
    {
        for (int i = 0; i < _multiplayerPlayers.Count; i++)
            _multiplayerPlayers[i].Order = i + 1;
    }

    private void SyncHiddenMultiplayerFields()
    {
        if (!IsInitialized) return;
        var target = _multiplayerPlayers.FirstOrDefault(p => p.IsTarget) ?? _multiplayerPlayers.FirstOrDefault();
        string targetNet = target?.NetId ?? "1";
        _updatingMultiplayerRoster = true;
        try
        {
            TargetNetIdTextBox.Text = targetNet;
            MultiplayerPlayersTextBox.Text = SerializeMultiplayerRosterText();
        }
        finally
        {
            _updatingMultiplayerRoster = false;
        }
    }

    private string SerializeMultiplayerRosterText()
        => string.Join(", ", _multiplayerPlayers.Select(p => $"{p.NetId}:{p.Character}"));

    private string TargetPlayerSummary()
    {
        var target = _multiplayerPlayers.FirstOrDefault(p => p.IsTarget) ?? _multiplayerPlayers.FirstOrDefault();
        if (target is null) return "目标玩家：未设置";
        return $"你是：slot index={Math.Max(0, target.Order - 1)}（顺序第 {target.Order} 位），{target.Name}，角色={D(target.Character)}，net_id={target.NetId}";
    }

    private void RefreshRunModeStatus()
    {
        if (!IsInitialized) return;
        bool multi = IsMultiplayerUiSelected();
        int count = SelectedPlayersCount();
        string status;
        if (!multi)
        {
            status = "当前：单人模式，net_id=1。";
            SearchRunModeHintText.Text = "运行模式：单人 net_id=1";
        }
        else
        {
            EnsureMultiplayerRosterForCurrentTarget();
            string unlockMode = MultiplayerUseProfileCheckBox.IsChecked == true ? "profile 近似" : "全解锁";
            status = $"当前：多人模式，players_count={count}，{TargetPlayerSummary()}，多人解锁={unlockMode}。";
            SearchRunModeHintText.Text = "运行模式：多人 · " + TargetPlayerSummary();
        }
        MultiplayerStatusText.Text = status;
    }

    private static Dictionary<string, string> ParseRosterEntries(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in Regex.Split(text ?? "", "[,，;；\r\n]+"))
        {
            var raw = part.Trim();
            if (raw.Length == 0) continue;
            var m = Regex.Match(raw, @"^\s*(\d+)\s*[:：=\s]+([A-Za-z_]+)\s*$");
            if (!m.Success) continue;
            string net = m.Groups[1].Value.Trim();
            string ch = m.Groups[2].Value.Trim().ToUpperInvariant();
            if (net.Length > 0 && ch.Length > 0) dict[net] = ch;
        }
        return dict;
    }

    private void LoadMultiplayerPlayersFromText(string text)
    {
        if (_updatingMultiplayerRoster) return;
        var entries = Regex.Split(text ?? "", "[,，;；\r\n]+")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (entries.Count == 0) return;

        string preferredTarget = (TargetNetIdTextBox?.Text ?? "").Trim();
        _updatingMultiplayerRoster = true;
        try
        {
            _multiplayerPlayers.Clear();
            int order = 1;
            foreach (var raw in entries.Take(8))
            {
                var m = Regex.Match(raw, @"^\s*(\d+)\s*[:：=\s]+([A-Za-z_]+)\s*$");
                if (!m.Success) continue;
                string net = m.Groups[1].Value.Trim();
                _multiplayerPlayers.Add(new MultiplayerPlayerView
                {
                    Order = order,
                    Name = "P" + order,
                    NetId = net,
                    Character = m.Groups[2].Value.Trim().ToUpperInvariant(),
                    Enabled = true,
                    IsTarget = !string.IsNullOrWhiteSpace(preferredTarget)
                        ? net.Equals(preferredTarget, StringComparison.OrdinalIgnoreCase)
                        : order == 1,
                });
                order++;
            }
            if (_multiplayerPlayers.Count < 2) InitializeDefaultMultiplayerPlayers();
            if (!_multiplayerPlayers.Any(p => p.IsTarget) && _multiplayerPlayers.Count > 0) _multiplayerPlayers[0].IsTarget = true;
            RenumberMultiplayerPlayers();
        }
        finally
        {
            _updatingMultiplayerRoster = false;
        }
        SyncHiddenMultiplayerFields();
    }

    private JsonArray BuildPlayersArray(string targetCharacter, int playersCount, string targetNetId)
    {
        var arr = new JsonArray();
        EnsureMultiplayerRosterForCurrentTarget();
        playersCount = Math.Clamp(playersCount, 2, 8);

        var target = EnsureTargetMultiplayerPlayer();
        target.Character = targetCharacter;
        targetNetId = string.IsNullOrWhiteSpace(target.NetId) ? targetNetId : target.NetId;

        int index = 1;
        foreach (var p in _multiplayerPlayers.Where(p => p.Enabled).Take(playersCount))
        {
            string character = p.IsTarget ? targetCharacter : p.Character;
            arr.Add(new JsonObject
            {
                ["order"] = index,
                ["name"] = string.IsNullOrWhiteSpace(p.Name) ? "P" + index : p.Name,
                ["character"] = character,
                ["net_id"] = string.IsNullOrWhiteSpace(p.NetId) ? index.ToString() : p.NetId,
                ["enabled"] = p.Enabled,
                ["is_target"] = p.IsTarget,
            });
            index++;
        }
        return arr;
    }

    private MultiplayerPlayerView? PlayerFromSender(object sender)
        => (sender as FrameworkElement)?.DataContext as MultiplayerPlayerView;

    private void MultiplayerRosterFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingMultiplayerRoster) return;
        RenumberMultiplayerPlayers();
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void MultiplayerPlayerCharacter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMultiplayerRoster) return;
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void MultiplayerTargetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingMultiplayerRoster) return;
        var player = PlayerFromSender(sender);
        if (player is null) return;
        _updatingMultiplayerRoster = true;
        try
        {
            foreach (var p in _multiplayerPlayers) p.IsTarget = ReferenceEquals(p, player);
        }
        finally
        {
            _updatingMultiplayerRoster = false;
        }
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void AddMultiplayerPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_multiplayerPlayers.Count >= 8) return;
        int next = _multiplayerPlayers.Count + 1;
        _multiplayerPlayers.Add(new MultiplayerPlayerView { Order = next, Name = "P" + next, NetId = next.ToString(), Character = "SILENT", Enabled = true, IsTarget = false });
        SetCombo(MultiplayerPlayersCountComboBox, Math.Clamp(_multiplayerPlayers.Count, 2, 8).ToString());
        RenumberMultiplayerPlayers();
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
    }

    private void ClearMultiplayerPlayers_Click(object sender, RoutedEventArgs e)
    {
        _multiplayerPlayers.Clear();
        _multiplayerPlayers.Add(new MultiplayerPlayerView { Order = 1, Name = "P1", NetId = "1", Character = "IRONCLAD", Enabled = true, IsTarget = true });
        _multiplayerPlayers.Add(new MultiplayerPlayerView { Order = 2, Name = "P2", NetId = "2", Character = "SILENT", Enabled = true, IsTarget = false });
        SetCombo(MultiplayerPlayersCountComboBox, "2");
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void UpdateTargetPlayerFromCurrent_Click(object sender, RoutedEventArgs e)
    {
        SyncTargetPlayerCharacterFromCurrent();
    }

    private void FillCurrentFromTargetPlayer_Click(object sender, RoutedEventArgs e)
    {
        var target = EnsureTargetMultiplayerPlayer();
        SetCombo(CharacterComboBox, target.Character);
        SetCombo(SearchCharacterComboBox, target.Character);
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void ActivateMultiplayerPlayerForPage(MultiplayerPlayerView player, bool analyzePage)
    {
        SetCombo(RunModeComboBox, "multiplayer");
        if (_multiplayerPlayers.Count >= 2) SetCombo(MultiplayerPlayersCountComboBox, Math.Clamp(_multiplayerPlayers.Count, 2, 8).ToString());

        _updatingMultiplayerRoster = true;
        try
        {
            foreach (var p in _multiplayerPlayers) p.IsTarget = ReferenceEquals(p, player);
        }
        finally
        {
            _updatingMultiplayerRoster = false;
        }

        string character = string.IsNullOrWhiteSpace(player.Character) ? "IRONCLAD" : player.Character.ToUpperInvariant();
        SetCombo(CharacterComboBox, character);
        SetCombo(SearchCharacterComboBox, character);
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();

        if (analyzePage)
        {
            ShowPage(AnalyzePage, "单种分析", $"已将多人 Lobby 第 {player.Order} 位设为当前目标，角色={D(character)}，net_id={player.NetId}。");
            BottomStatusText.Text = $"多人目标 -> 单种分析：{player.Name} / {D(character)} / net_id={player.NetId}";
        }
        else
        {
            ShowPage(SearchPage, "批量筛种", $"已将多人 Lobby 第 {player.Order} 位设为当前目标，角色={D(character)}，net_id={player.NetId}。");
            BottomStatusText.Text = $"多人目标 -> 批量筛种：{player.Name} / {D(character)} / net_id={player.NetId}";
        }
    }

    private void AnalyzeAsMultiplayerPlayer_Click(object sender, RoutedEventArgs e)
    {
        var player = PlayerFromSender(sender);
        if (player is null) return;
        ActivateMultiplayerPlayerForPage(player, analyzePage: true);
    }

    private void SearchAsMultiplayerPlayer_Click(object sender, RoutedEventArgs e)
    {
        var player = PlayerFromSender(sender);
        if (player is null) return;
        ActivateMultiplayerPlayerForPage(player, analyzePage: false);
    }

    private void MoveMultiplayerPlayerUp_Click(object sender, RoutedEventArgs e)
    {
        var player = PlayerFromSender(sender);
        if (player is null) return;
        int idx = _multiplayerPlayers.IndexOf(player);
        if (idx <= 0) return;
        _multiplayerPlayers.Move(idx, idx - 1);
        RenumberMultiplayerPlayers();
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
    }

    private void MoveMultiplayerPlayerDown_Click(object sender, RoutedEventArgs e)
    {
        var player = PlayerFromSender(sender);
        if (player is null) return;
        int idx = _multiplayerPlayers.IndexOf(player);
        if (idx < 0 || idx >= _multiplayerPlayers.Count - 1) return;
        _multiplayerPlayers.Move(idx, idx + 1);
        RenumberMultiplayerPlayers();
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
    }

    private void DeleteMultiplayerPlayer_Click(object sender, RoutedEventArgs e)
    {
        var player = PlayerFromSender(sender);
        if (player is null || _multiplayerPlayers.Count <= 2) return;
        bool wasTarget = player.IsTarget;
        _multiplayerPlayers.Remove(player);
        if (wasTarget && _multiplayerPlayers.Count > 0) _multiplayerPlayers[0].IsTarget = true;
        RenumberMultiplayerPlayers();
        SetCombo(MultiplayerPlayersCountComboBox, Math.Clamp(_multiplayerPlayers.Count, 2, 8).ToString());
        SyncHiddenMultiplayerFields();
        RefreshRunModeStatus();
        RefreshSearchRequirementTags();
    }

    private void AddFinalCardAnyTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalCardBuilderComboBox, FinalCardAnyTextBox, "card", "最终卡牌：任一想要");
    private void AddFinalCardAllTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalCardBuilderComboBox, FinalCardAllTextBox, "card", "最终卡牌：全部必须");
    private void AddFinalCardBlacklistTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalCardBuilderComboBox, FinalCardBlacklistTextBox, "card", "最终卡牌：不想要");
    private void AddFinalPotionAnyTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalPotionBuilderComboBox, FinalPotionAnyTextBox, "potion", "最终药水：任一想要");
    private void AddFinalPotionAllTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalPotionBuilderComboBox, FinalPotionAllTextBox, "potion", "最终药水：全部必须");
    private void AddFinalPotionBlacklistTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalPotionBuilderComboBox, FinalPotionBlacklistTextBox, "potion", "最终药水：不想要");
    private void AddFinalRelicAnyTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalRelicBuilderComboBox, FinalRelicAnyTextBox, "relic", "最终普通遗物：任一想要", IsOpeningRandomRelicRuntimeId);
    private void AddFinalRelicAllTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalRelicBuilderComboBox, FinalRelicAllTextBox, "relic", "最终普通遗物：全部必须", IsOpeningRandomRelicRuntimeId);
    private void AddFinalRelicBlacklistTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalRelicBuilderComboBox, FinalRelicBlacklistTextBox, "relic", "最终普通遗物：不想要", IsOpeningRandomRelicRuntimeId);
    private void AddFinalCurseAnyTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalCurseBuilderComboBox, FinalCurseAnyTextBox, "curse", "最终诅咒：想要", IsFinalCurseRuntimeId);
    private void AddFinalCurseBlacklistTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalCurseBuilderComboBox, FinalCurseBlacklistTextBox, "curse", "最终诅咒：不想要", IsFinalCurseRuntimeId);
    private void AddFinalNeowRelicAnyTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalNeowRelicComboBox, FinalNeowRelicAnyTextBox, "relic", "必须包含 Neow 遗物", IsNeowRelicRuntimeId);
    private void AddFinalNeowRelicBlacklistTag_Click(object sender, RoutedEventArgs e) => AddFinalResultTag(FinalNeowRelicComboBox, FinalNeowRelicBlacklistTextBox, "relic", "不想要 Neow 遗物", IsNeowRelicRuntimeId);

    private void AddFinalResultTag(ComboBox combo, TextBox target, string category, string label, Func<string, bool>? validator = null)
    {
        var item = SelectedAliasFromCombo(combo, category);
        string term = combo.Text?.Trim() ?? "";
        if (item is null && string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请选择或输入" + label + "。";
            return;
        }
        if (item is null) item = ResolveItemAlias(term, category);
        if (item is null && category.Equals("curse", StringComparison.OrdinalIgnoreCase)) item = ResolveItemAlias(term, "card");
        string runtimeId = item?.RuntimeId ?? Term.Normalize(term);
        if (validator is not null && !validator(runtimeId))
        {
            BottomStatusText.Text = "该项目不属于" + label + "的允许池，已拒绝：" + DisplayTerm(runtimeId);
            return;
        }
        SetCombo(NeowFilterModeComboBox, "final");
        AppendTerm(target, runtimeId, allowDuplicates: AllowsDuplicateCountTerms(target));
        combo.SelectedItem = null;
        combo.Text = "";
        UpdateNeowFilterModeUi();
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加 " + label + " Tag：" + DisplayTerm(runtimeId);
    }

    private static int ParsePositiveInt(string? text, int fallback)
        => int.TryParse((text ?? "").Trim(), out int n) && n > 0 ? n : fallback;

    private int SelectedShopLimit(int fallback)
    {
        string text = SelectedComboText(SearchShopLimitComboBox, fallback.ToString());
        return ParsePositiveInt(text, fallback);
    }

    private string SelectedAncientTerm()
    {
        if (AncientBuilderComboBox.SelectedItem is ItemAliasView item) return item.RuntimeId;
        return Term.Normalize(AncientBuilderComboBox.Text ?? "");
    }

    private string SelectedAncientOptionTerm()
    {
        if (AncientOptionBuilderComboBox.SelectedItem is ItemAliasView item) return item.RuntimeId;
        return Term.Normalize(AncientOptionBuilderComboBox.Text ?? "");
    }

    private void BonesRelicBuilderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSpecialRelicTemplateUi();

    private void NewLeafSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateSpecialRelicTemplateUi();
        RefreshSearchRequirementTags();
    }

    private bool AdvancedNewLeafEnabled() => EnableAdvancedNewLeafCheckBox?.IsChecked == true;

    private string SelectedNewLeafSourceSelector()
        => AdvancedNewLeafEnabled() ? SelectedComboText(NewLeafSourceComboBox, "starter_basic") : "starter_basic";

    private string SelectedAnalyzeNewLeafSourceSelector()
        => AdvancedNewLeafEnabled() && AnalyzeUseAdvancedNewLeafCheckBox?.IsChecked == true
            ? SelectedComboText(AnalyzeNewLeafSourceComboBox, "starter_basic")
            : "starter_basic";

    private void UpdateAnalyzeAdvancedNewLeafUi()
    {
        if (!IsInitialized) return;
        bool globalEnabled = AdvancedNewLeafEnabled();
        bool useForAnalyze = globalEnabled && AnalyzeUseAdvancedNewLeafCheckBox?.IsChecked == true;
        if (AnalyzeAdvancedNewLeafExpander != null)
        {
            AnalyzeAdvancedNewLeafExpander.IsEnabled = globalEnabled;
            AnalyzeAdvancedNewLeafExpander.Opacity = globalEnabled ? 1.0 : 0.62;
        }
        if (AnalyzeNewLeafSourceComboBox != null) AnalyzeNewLeafSourceComboBox.IsEnabled = useForAnalyze;
        if (AnalyzeAdvancedNewLeafHintText != null)
        {
            AnalyzeAdvancedNewLeafHintText.Text = !globalEnabled
                ? "当前：配置页高级新叶关闭，单种分析按普通新叶处理。"
                : (useForAnalyze
                    ? "当前：单种分析高级新叶已启用，仅影响骨骰路线中的 NewLeaf。直接 Neow 新叶仍按普通初始牌变化。"
                    : "当前：单种分析高级新叶未启用；需要时勾选上方选项。");
        }
        if (!globalEnabled)
        {
            if (AnalyzeUseAdvancedNewLeafCheckBox != null) AnalyzeUseAdvancedNewLeafCheckBox.IsChecked = false;
            if (AnalyzeNewLeafSourceComboBox != null) SetCombo(AnalyzeNewLeafSourceComboBox, "starter_basic");
        }
    }

    private void AddNewLeafCardTag_Click(object sender, RoutedEventArgs e)
    {
        AddSpecialCardTag(NewLeafCardBuilderTextBox, NewLeafCardAllTextBox, "NewLeaf", "新叶变化结果");
    }

    private void AddKaleidoscopeGroup1Tag_Click(object sender, RoutedEventArgs e)
    {
        AddSpecialCardTag(KaleidoscopeGroup1CardTextBox, KaleidoscopeGroup1CardAllTextBox, "Kaleidoscope", "万花筒给牌结果");
    }

    private void AddKaleidoscopeGroup2Tag_Click(object sender, RoutedEventArgs e)
    {
        AddSpecialCardTag(KaleidoscopeGroup2CardTextBox, KaleidoscopeGroup2CardAllTextBox, "Kaleidoscope", "万花筒给牌结果");
    }

    private void AddLostCofferCardTag_Click(object sender, RoutedEventArgs e)
    {
        AddSpecialCardTag(LostCofferCardTextBox, LostCofferCardAllTextBox, "LostCoffer", "失物盒卡牌");
    }

    private void AddLostCofferPotionTag_Click(object sender, RoutedEventArgs e)
    {
        string term = LostCofferPotionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请输入失物盒药水。";
            return;
        }
        var item = ResolveItemAlias(term, "potion");
        string runtimeId = item?.RuntimeId ?? Term.Normalize(term);
        EnsureBonesRelicSelected("LostCoffer");
        AppendTerm(LostCofferPotionAllTextBox, runtimeId);
        LostCofferPotionTextBox.Clear();
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加失物盒药水 Tag：" + DisplayTerm(runtimeId);
    }

    private void AddSpecialCardTag(TextBox input, TextBox target, string requiredRelic, string label)
    {
        string term = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请输入" + label + "要指定的卡牌。";
            return;
        }
        var item = ResolveItemAlias(term, "card");
        string runtimeId = item?.RuntimeId ?? Term.Normalize(term);
        EnsureBonesRelicSelected(requiredRelic);
        AppendTerm(target, runtimeId, allowDuplicates: true);
        input.Clear();
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加" + label + " Tag：" + DisplayTerm(runtimeId);
    }

    private void EnsureBonesRelicSelected(string runtimeId)
    {
        SetCombo(BonesRequirementComboBox, "yes");
        SetCombo(BonesRelicModeComboBox, "specified");
        AppendTerm(BonesRelicAllTextBox, runtimeId);
    }

    private void AddBonesCurseTag_Click(object sender, RoutedEventArgs e)
    {
        string term = BonesCurseBuilderComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请输入要指定的骨骰诅咒。";
            return;
        }
        var item = ResolveItemAlias(term, "curse") ?? ResolveItemAlias(term, "card");
        string runtimeId = item?.RuntimeId ?? Term.Normalize(term);
        if (!IsKnownCurseRuntimeId(runtimeId))
        {
            BottomStatusText.Text = "骨骰诅咒只能从当前 RollCore 骨骰诅咒池中选择，已拒绝：" + DisplayTerm(runtimeId);
            return;
        }
        SetCombo(BonesRequirementComboBox, "yes");
        string mode = SelectedComboText(BonesCurseModeComboBox, "none").ToLowerInvariant();
        if (mode == "require")
        {
            AppendTerm(BonesCurseAnyTextBox, runtimeId);
        }
        else if (mode == "ban")
        {
            AppendTerm(BonesCurseBlacklistTextBox, runtimeId);
        }
        else
        {
            BottomStatusText.Text = "请先选择骨骰诅咒要求：必须 或 禁止。";
            return;
        }
        BonesCurseBuilderComboBox.SelectedItem = null;
        BonesCurseBuilderComboBox.Text = "";
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加骨骰诅咒 Tag：" + DisplayTerm(runtimeId);
    }

    private void ClearBonesCurseTags_Click(object sender, RoutedEventArgs e)
    {
        BonesCurseAnyTextBox.Clear();
        BonesCurseBlacklistTextBox.Clear();
        BonesCurseBuilderComboBox.SelectedItem = null;
        BonesCurseBuilderComboBox.Text = "";
        SetCombo(BonesCurseModeComboBox, "none");
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已清空骨骰诅咒 Tag。";
    }

    private void UpdateTagBuilderUi(bool syncLegacy)
    {
        if (!IsInitialized) return;
        string neowMode = SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant();
        bool processMode = neowMode == "process";
        string bonesReq = SelectedComboText(BonesRequirementComboBox, "yes").ToLowerInvariant();
        bool requireBones = processMode && bonesReq == "yes";
        bool noBonesRequirement = processMode && bonesReq == "no";
        bool showBonesDetail = requireBones;

        DirectRelicSpecifyPanel.Visibility = processMode && noBonesRequirement ? Visibility.Visible : Visibility.Collapsed;
        BonesRelicModePanel.Visibility = showBonesDetail ? Visibility.Visible : Visibility.Collapsed;
        BonesCurseModePanel.Visibility = showBonesDetail ? Visibility.Visible : Visibility.Collapsed;
        BonesDetailPanel.Visibility = showBonesDetail ? Visibility.Visible : Visibility.Collapsed;

        string relicMode = SelectedComboText(BonesRelicModeComboBox, "undirected").ToLowerInvariant();
        BonesRelicSpecifyPanel.Visibility = showBonesDetail && relicMode == "specified" ? Visibility.Visible : Visibility.Collapsed;

        string curseMode = SelectedComboText(BonesCurseModeComboBox, "none").ToLowerInvariant();
        BonesCurseSpecifyPanel.Visibility = showBonesDetail && curseMode != "none" ? Visibility.Visible : Visibility.Collapsed;

        if (!syncLegacy) return;
        RequireBonesCheckBox.IsChecked = requireBones;
        if (requireBones) SetCombo(NeowRouteModeComboBox, "bones");
        else if (noBonesRequirement) SetCombo(NeowRouteModeComboBox, "direct");
        else SetCombo(NeowRouteModeComboBox, "any");

        if (!noBonesRequirement)
        {
            NeowAnyTextBox.Clear();
            NeowAllTextBox.Clear();
            NeowBlacklistTextBox.Clear();
        }

        if (!showBonesDetail)
        {
            BonesRelicAnyTextBox.Clear();
            BonesRelicAllTextBox.Clear();
            BonesRelicBlacklistTextBox.Clear();
            BonesCurseAnyTextBox.Clear();
            BonesCurseBlacklistTextBox.Clear();
            ClearSpecialRelicTerms(clearDynamic: bonesReq != "no");
            UpdateSpecialRelicTemplateUi();
            return;
        }

        if (relicMode == "undirected")
        {
            BonesRelicAnyTextBox.Clear();
            BonesRelicAllTextBox.Clear();
            BonesRelicBlacklistTextBox.Clear();
            ClearSpecialRelicTerms();
        }
        if (curseMode == "none")
        {
            BonesCurseAnyTextBox.Clear();
            BonesCurseBlacklistTextBox.Clear();
        }
        UpdateSpecialRelicTemplateUi();
    }

    private void ClearSpecialRelicTerms(bool clearDynamic = true)
    {
        NewLeafCardAllTextBox.Clear();
        KaleidoscopeGroup1CardAllTextBox.Clear();
        KaleidoscopeGroup2CardAllTextBox.Clear();
        LostCofferCardAllTextBox.Clear();
        LostCofferPotionAllTextBox.Clear();
        if (clearDynamic) _dynamicTemplateTerms.Clear();
    }

    private void UpdateSpecialRelicTemplateUi()
    {
        if (!IsInitialized) return;
        string bonesRelic = SelectedAliasFromCombo(BonesRelicBuilderComboBox, "relic")?.RuntimeId ?? "";
        string directRelic = SelectedAliasFromCombo(DirectRelicBuilderComboBox, "relic")?.RuntimeId ?? "";
        RebuildEffectTemplatePanel(bonesRelic, SpecialRelicTemplatePanel, BonesGenericEffectTemplatesPanel,
            SpecialRelicTemplateTitleText, SpecialRelicTemplateHintText, "bones");
        RebuildEffectTemplatePanel(directRelic, DirectSpecialRelicTemplatePanel, DirectGenericEffectTemplatesPanel,
            DirectSpecialRelicTemplateTitleText, DirectSpecialRelicTemplateHintText, "direct");

        // Preview6 起子条件由 data/neow_relic_effects.json 驱动，旧固定面板保留但默认隐藏，避免重复显示。
        NewLeafTemplatePanel.Visibility = Visibility.Collapsed;
        KaleidoscopeTemplatePanel.Visibility = Visibility.Collapsed;
        LostCofferTemplatePanel.Visibility = Visibility.Collapsed;
        UpdateAdvancedNewLeafUi();
    }

    private void UpdateAdvancedNewLeafUi()
    {
        if (!IsInitialized) return;
        bool enabled = AdvancedNewLeafEnabled();
        if (AdvancedNewLeafConfigHintText != null)
        {
            AdvancedNewLeafConfigHintText.Text = enabled
                ? "当前：高级新叶已开启。筛种器在骨骰路线选择新叶时，可以指定新叶要变化的原始牌来源。"
                : "当前：高级新叶关闭。筛种器里的新叶按普通模式处理。";
        }

        string selectedRelic = SelectedAliasFromCombo(BonesRelicBuilderComboBox, "relic")?.RuntimeId ?? "";
        bool bonesRoute = SelectedComboText(BonesRequirementComboBox, "yes").Equals("yes", StringComparison.OrdinalIgnoreCase);
        bool showSource = enabled && bonesRoute && selectedRelic.Equals("NewLeaf", StringComparison.OrdinalIgnoreCase);
        AdvancedNewLeafSourcePanel.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled)
            SetCombo(NewLeafSourceComboBox, "starter_basic");
        UpdateAnalyzeAdvancedNewLeafUi();
    }

    private void RebuildEffectTemplatePanel(string relicId, Border host, StackPanel panel, TextBlock title, TextBlock hint, string routeKind)
    {
        panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(relicId) || !_neowRelicEffects.TryGetValue(relicId, out var effect))
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }

        var templates = effect.Templates.Where(IsTemplateUsableInTagBuilder).ToList();
        if (templates.Count == 0)
        {
            host.Visibility = effect.EffectKind.Equals("plain_relic", StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
            title.Text = DisplayTerm(relicId) + "：没有可筛选子条件";
            hint.Text = string.IsNullOrWhiteSpace(effect.Notes) ? "该遗物没有当前可筛选的随机结果；只要求遗物本身即可。" : effect.Notes;
            return;
        }

        host.Visibility = Visibility.Visible;
        title.Text = DisplayTerm(relicId) + "：可选子条件";
        hint.Text = "子条件来自 data/neow_relic_effects.json；这里只显示当前 RollCore 能按来源匹配的卡牌、药水、遗物或诅咒结果。";
        foreach (var template in templates)
        {
            panel.Children.Add(BuildTemplateRow(effect, template, routeKind));
        }
    }

    private UIElement BuildTemplateRow(NeowRelicEffectView effect, NeowRelicTemplateView template, string routeKind)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            DisplayMemberPath = "DisplayText",
            ToolTip = TemplateInputToolTip(template),
            Margin = new Thickness(0, 0, 0, 0),
            ItemsSource = DropdownItemsForTemplate(template),
        };
        string inputKey = routeKind + ":" + template.TemplateId;
        _dynamicTemplateInputs[inputKey] = combo;

        var button = new Button
        {
            Content = "添加 Tag",
            Tag = new DynamicTemplateButtonTag(routeKind, effect.CanonicalId, template.TemplateId),
            Background = (System.Windows.Media.Brush)FindResource("PrimaryBrush"),
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += AddDynamicTemplateTag_Click;

        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(button);
        dock.Children.Add(combo);

        var wrapper = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        wrapper.Children.Add(new TextBlock
        {
            Text = TemplateDisplayLabel(effect, template),
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.Black,
        });
        wrapper.Children.Add(new TextBlock
        {
            Text = TemplateHint(template),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        wrapper.Children.Add(dock);
        return wrapper;
    }

    private void AddDynamicTemplateTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DynamicTemplateButtonTag tag) return;
        if (!_neowTemplatesById.TryGetValue(tag.TemplateId, out var template)) return;
        string inputKey = tag.RouteKind + ":" + tag.TemplateId;
        if (!_dynamicTemplateInputs.TryGetValue(inputKey, out var input)) return;

        string category = TemplateCategoryForAlias(template);
        var item = SelectedAliasFromCombo(input, category);
        string term = input.Text.Trim();
        if (item is null && string.IsNullOrWhiteSpace(term))
        {
            BottomStatusText.Text = "请选择或输入要添加的子条件。";
            return;
        }
        if (item is null) item = ResolveItemAlias(term, category) ?? ResolveItemAlias(term, "card") ?? ResolveItemAlias(term, "relic") ?? ResolveItemAlias(term, "potion") ?? ResolveItemAlias(term, "curse");
        string runtimeId = item?.RuntimeId ?? Term.Normalize(term);

        if (category == "relic" && IsOpeningRandomRelicTemplate(template) && !IsOpeningRandomRelicRuntimeId(runtimeId))
        {
            string rarity = RelicRarity(runtimeId);
            BottomStatusText.Text = "这个来源只会出当前角色可用的普通 / 罕见 / 稀有随机遗物，已拒绝：" + DisplayTerm(runtimeId)
                + (string.IsNullOrWhiteSpace(rarity) ? "" : "（" + rarity + "）");
            return;
        }

        if (category == "card" && item is not null && !IsCardAllowedForTemplate(template, runtimeId))
        {
            BottomStatusText.Text = "这张卡不属于当前来源的可选卡池，已拒绝：" + DisplayTerm(runtimeId)
                + CardRejectReason(template, runtimeId);
            return;
        }

        if (tag.RouteKind.Equals("direct", StringComparison.OrdinalIgnoreCase)) EnsureDirectRelicSelected(tag.RelicId);
        else EnsureBonesRelicSelected(tag.RelicId);

        if (!_dynamicTemplateTerms.TryGetValue(tag.TemplateId, out var list))
        {
            list = new List<string>();
            _dynamicTemplateTerms[tag.TemplateId] = list;
        }
        // 动态来源 Tag 允许重复：重复的 whitelist_all 条目表示数量要求，
        // 例如 ScrollBoxes 的 3 张 Claw，或 LeafyPoultice 两次变化成同一张牌。
        list.Add(runtimeId);
        input.SelectedItem = null;
        input.Text = "";
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已添加" + TemplateDisplayLabel(_neowRelicEffects[tag.RelicId], template) + " Tag：" + DisplayTerm(runtimeId);
    }

    private void EnsureDirectRelicSelected(string runtimeId)
    {
        if (runtimeId.Equals("NeowsBones", StringComparison.OrdinalIgnoreCase)) return;
        SetCombo(BonesRequirementComboBox, "no");
        AppendTerm(NeowAllTextBox, runtimeId);
    }

    private static bool IsTemplateUsableInTagBuilder(NeowRelicTemplateView template)
    {
        if (!template.MatchModes.Any(m => m.Equals("contains", StringComparison.OrdinalIgnoreCase))) return false;
        if (string.IsNullOrWhiteSpace(template.SourceKey)) return false;
        if (template.SourceKey.StartsWith("side_effect", StringComparison.OrdinalIgnoreCase)) return false;
        return template.OutputType is "card" or "transform_card" or "potion" or "relic" or "curse";
    }

    private string TemplateDisplayLabel(NeowRelicEffectView effect, NeowRelicTemplateView template)
    {
        string zhs = template.LabelZhs;
        if (!string.IsNullOrWhiteSpace(zhs) && !zhs.Contains("??", StringComparison.Ordinal)) return zhs;
        string name = DisplayTerm(effect.CanonicalId);
        string id = template.TemplateId.ToLowerInvariant();
        if (id.Contains("bundle_1")) return name + " 第 1 组卡牌包";
        if (id.Contains("bundle_2")) return name + " 第 2 组卡牌包";
        if (id.Contains("group_1")) return name + " 第 1 组给牌";
        if (id.Contains("group_2")) return name + " 第 2 组给牌";
        if (id.Contains("strike_transform")) return name + " 打击变化结果";
        if (id.Contains("defend_transform")) return name + " 防御变化结果";
        if (id.Contains("transform")) return name + " 变化结果";
        if (id.Contains("potion")) return name + " 药水结果";
        if (id.Contains("relic")) return name + " 遗物结果";
        if (id.Contains("curse") || template.OutputType.Equals("curse", StringComparison.OrdinalIgnoreCase)) return name + " 诅咒结果";
        if (template.OutputType.Equals("card", StringComparison.OrdinalIgnoreCase)) return name + " 卡牌结果";
        return !string.IsNullOrWhiteSpace(template.LabelEng) ? template.LabelEng : name + " 子条件";
    }

    private string TemplateHint(NeowRelicTemplateView template)
    {
        string type = template.OutputType switch
        {
            "card" => "卡牌",
            "transform_card" => "变化后的卡牌",
            "potion" => "药水",
            "relic" => "遗物",
            "curse" => "诅咒",
            _ => template.OutputType,
        };
        string pool = TemplatePoolHint(template);
        return "要求该来源结果必须包含指定" + type + "。" + pool + " 当前版本先支持“必须包含”，blacklist 后续再做按钮化。";
    }

    private string TemplateInputToolTip(NeowRelicTemplateView template)
    {
        string category = TemplateCategoryForAlias(template);
        string kind = category switch
        {
            "card" => "卡牌",
            "potion" => "药水",
            "relic" => "遗物",
            "curse" => "诅咒",
            _ => "实体",
        };
        return "从下拉框选择" + kind + "，也可以输入中文名 / 英文名 / source_id / CamelCase ID。";
    }

    private string TemplatePoolHint(NeowRelicTemplateView template)
    {
        string sourceKey = template.SourceKey ?? "";
        string character = CurrentSearchOwnerName();
        if (template.OutputType is "card" or "transform_card")
        {
            if (sourceKey.Contains("LeadPaperweight", StringComparison.OrdinalIgnoreCase)) return "下拉框显示无色正常奖励卡（普通 / 罕见 / 稀有）；稀有概率由当前进阶的 RegularEncounter 规则决定。";
            if (sourceKey.Contains("Kaleidoscope", StringComparison.OrdinalIgnoreCase)) return "下拉框显示当前角色以外的职业卡（普通 / 罕见 / 稀有）。preview11 起按两组“每组只能选一张”的可选组语义匹配。";
            if (sourceKey.Contains("ArcaneScroll", StringComparison.OrdinalIgnoreCase) || sourceKey.Contains("HeftyTablet", StringComparison.OrdinalIgnoreCase))
                return "下拉框只显示当前角色稀有卡（" + character + "）。";
            if (sourceKey.Contains("LostCoffer", StringComparison.OrdinalIgnoreCase))
                return "下拉框显示当前角色正常奖励卡（普通 / 罕见 / 稀有），稀有概率由 RollCore 按进阶难度处理。";
            if (sourceKey.Contains("ScrollBoxes", StringComparison.OrdinalIgnoreCase))
                return "下拉框显示当前角色普通 / 罕见卡。preview11 起按“两组卡牌包只能选一组”的可选组语义匹配。";
            if (sourceKey.Contains("MassiveScroll", StringComparison.OrdinalIgnoreCase)) return "下拉框优先显示多人游戏卡；多人筛种会后续继续适配。";
            if (sourceKey.Contains("LeafyPoultice", StringComparison.OrdinalIgnoreCase)) return "树叶药膏变化的是本职业基础打击/防御；下拉框只显示当前角色普通 / 罕见 / 稀有卡。";
            if (sourceKey.Contains("NewLeaf", StringComparison.OrdinalIgnoreCase))
                return AdvancedNewLeafEnabled()
                    ? "高级新叶已开启；下拉框会根据“原始牌来源”切换：默认本职业、铅制镇纸为无色、万花筒为其它职业、沉重石板为诅咒池、涅奥之怒为无色池。"
                    : "普通新叶模式：下拉框只显示当前角色普通 / 罕见 / 稀有变化结果；如需跨来源整活筛选，请先到配置页开启高级新叶。";
            if (template.OutputType.Equals("transform_card", StringComparison.OrdinalIgnoreCase)) return "下拉框显示当前角色普通 / 罕见 / 稀有变化结果。";
            return "下拉框显示卡牌。";
        }
        if (template.OutputType.Equals("relic", StringComparison.OrdinalIgnoreCase))
        {
            if (IsOpeningRandomRelicTemplate(template)) return "下拉框只显示当前角色可用的开局随机遗物池：普通 / 罕见 / 稀有；不显示商店 / Ancient / 事件 / 初始 / 其它职业专属遗物。";
            return "下拉框显示遗物。";
        }
        if (template.OutputType.Equals("potion", StringComparison.OrdinalIgnoreCase)) return "下拉框显示药水。";
        if (template.OutputType.Equals("curse", StringComparison.OrdinalIgnoreCase)) return "下拉框显示诅咒。";
        return "";
    }

    private IReadOnlyList<ItemAliasView> DropdownItemsForTemplate(NeowRelicTemplateView template)
    {
        string category = TemplateCategoryForAlias(template);
        if (category == "potion") return _potionItems;
        if (category == "relic") return RelicDropdownItemsForTemplate(template);
        if (category == "curse") return _curseItems;
        if (category == "card") return CardDropdownItemsForTemplate(template);
        return Array.Empty<ItemAliasView>();
    }

    private IReadOnlyList<ItemAliasView> RelicDropdownItemsForTemplate(NeowRelicTemplateView template)
    {
        if (!IsOpeningRandomRelicTemplate(template)) return _relicItems;

        var list = _relicItems
            .Where(IsOpeningRandomRelicItem)
            .OrderBy(x => RelicRaritySortKey(x.RuntimeId))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count > 0 ? list : _relicItems;
    }

    private IReadOnlyList<ItemAliasView> CardDropdownItemsForTemplate(NeowRelicTemplateView template)
    {
        var list = _cardItems
            .Where(x => IsCardAllowedForTemplate(template, x.RuntimeId))
            .OrderBy(x => CardRaritySortKey(x.RuntimeId))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count > 0 ? list : _cardItems;
    }

    private bool IsCardAllowedForTemplate(NeowRelicTemplateView template, string runtimeId)
    {
        string sourceKey = template.SourceKey ?? "";
        string owner = CurrentSearchOwnerName();

        if (sourceKey.Contains("LeadPaperweight", StringComparison.OrdinalIgnoreCase))
            return IsColorlessCard(runtimeId) && IsNormalRewardCardRarity(runtimeId);

        if (sourceKey.Contains("Kaleidoscope", StringComparison.OrdinalIgnoreCase))
            return IsCharacterCard(runtimeId) && !CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);

        if (sourceKey.Contains("ArcaneScroll", StringComparison.OrdinalIgnoreCase)
            || sourceKey.Contains("HeftyTablet", StringComparison.OrdinalIgnoreCase))
            return CardOwnedBy(runtimeId, owner) && CardRarityEquals(runtimeId, "Rare");

        if (sourceKey.Contains("LostCoffer", StringComparison.OrdinalIgnoreCase))
            return CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);

        if (sourceKey.Contains("ScrollBoxes", StringComparison.OrdinalIgnoreCase))
            return CardOwnedBy(runtimeId, owner)
                && (CardRarityEquals(runtimeId, "Common") || CardRarityEquals(runtimeId, "Uncommon"));

        if (sourceKey.Contains("MassiveScroll", StringComparison.OrdinalIgnoreCase))
            return IsMultiplayerCard(runtimeId);

        if (sourceKey.Contains("LeafyPoultice", StringComparison.OrdinalIgnoreCase))
            return CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);

        if (sourceKey.Contains("NewLeaf", StringComparison.OrdinalIgnoreCase))
            return IsCardAllowedForNewLeafOutput(runtimeId, owner);

        if (template.OutputType.Equals("transform_card", StringComparison.OrdinalIgnoreCase))
        {
            // 未专项适配的 transform 模板暂按当前角色正常奖励卡处理。
            // 新叶的跨来源高级变化会放到后续 route-aware 计划里。
            return CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);
        }

        return true;
    }

    private bool IsCardAllowedForNewLeafOutput(string runtimeId, string owner)
    {
        string selector = SelectedNewLeafSourceSelector();
        if (selector.Contains("LeadPaperweight", StringComparison.OrdinalIgnoreCase))
            return IsColorlessCard(runtimeId) && IsNormalRewardCardRarity(runtimeId);
        if (selector.Contains("Kaleidoscope", StringComparison.OrdinalIgnoreCase))
            return IsCharacterCard(runtimeId) && !CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);
        if (selector.Contains("HeftyTablet", StringComparison.OrdinalIgnoreCase))
            return CardRarityEquals(runtimeId, "Curse") && !runtimeId.Equals("Injury", StringComparison.OrdinalIgnoreCase);
        if (selector.Contains("NeowsTorment", StringComparison.OrdinalIgnoreCase))
            return IsColorlessCard(runtimeId) && IsNormalRewardCardRarity(runtimeId);
        if (selector.Contains("LostCoffer", StringComparison.OrdinalIgnoreCase))
            return CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);
        return CardOwnedBy(runtimeId, owner) && IsNormalRewardCardRarity(runtimeId);
    }

    private string CardRejectReason(NeowRelicTemplateView template, string runtimeId)
    {
        string rarity = CardRarity(runtimeId);
        string suffix = string.IsNullOrWhiteSpace(rarity) ? "" : "（" + rarity + "）";
        string sourceKey = template.SourceKey ?? "";
        if (sourceKey.Contains("LeadPaperweight", StringComparison.OrdinalIgnoreCase)) return suffix + "；铅制镇纸只筛无色普通 / 罕见 / 稀有奖励卡。";
        if (sourceKey.Contains("Kaleidoscope", StringComparison.OrdinalIgnoreCase)) return suffix + "；万花筒只筛当前角色以外的职业普通 / 罕见 / 稀有卡。";
        if (sourceKey.Contains("ArcaneScroll", StringComparison.OrdinalIgnoreCase) || sourceKey.Contains("HeftyTablet", StringComparison.OrdinalIgnoreCase)) return suffix + "；该来源只筛当前角色稀有卡。";
        if (sourceKey.Contains("LostCoffer", StringComparison.OrdinalIgnoreCase)) return suffix + "；失物盒只筛当前角色普通 / 罕见 / 稀有奖励卡。";
        if (sourceKey.Contains("ScrollBoxes", StringComparison.OrdinalIgnoreCase)) return suffix + "；卷轴箱只筛当前角色普通 / 罕见卡。";
        if (sourceKey.Contains("LeafyPoultice", StringComparison.OrdinalIgnoreCase)) return suffix + "；树叶药膏只变化本职业基础打击/防御，因此结果下拉只显示当前角色普通 / 罕见 / 稀有卡。";
        if (sourceKey.Contains("NewLeaf", StringComparison.OrdinalIgnoreCase))
            return AdvancedNewLeafEnabled()
                ? suffix + "；高级新叶结果会根据上方原始牌来源筛选：默认本职业；铅制镇纸为无色；万花筒为其它职业；沉重石板 Injury 为其它诅咒；涅奥之怒为无色池。"
                : suffix + "；普通新叶只筛当前角色普通 / 罕见 / 稀有变化结果。要筛铅制镇纸/万花筒等来源，请先在配置页开启高级新叶。";
        return suffix;
    }

    private string CurrentSearchOwnerName()
    {
        string character = SelectedComboText(SearchCharacterComboBox, "IRONCLAD");
        return SourceIdToRuntimeIdLocal(character);
    }

    private bool CardOwnedBy(string cardId, string owner)
        => _cardPoolMeta.TryGetValue(cardId, out var meta) && meta.Owners.Any(o => o.Equals(owner, StringComparison.OrdinalIgnoreCase));

    private bool IsColorlessCard(string cardId)
        => _cardPoolMeta.TryGetValue(cardId, out var meta) && meta.PoolKinds.Any(k => k.Equals("colorless", StringComparison.OrdinalIgnoreCase));

    private bool IsCharacterCard(string cardId)
        => _cardPoolMeta.TryGetValue(cardId, out var meta) && meta.Owners.Count > 0;

    private bool IsMultiplayerCard(string cardId)
        => _cardPoolMeta.TryGetValue(cardId, out var meta) && !string.IsNullOrWhiteSpace(meta.MultiplayerConstraint) && !meta.MultiplayerConstraint.Equals("None", StringComparison.OrdinalIgnoreCase);

    private string CardRarity(string runtimeId)
        => _cardPoolMeta.TryGetValue(runtimeId, out var meta) ? meta.Rarity : "";

    private bool CardRarityEquals(string runtimeId, string rarity)
        => CardRarity(runtimeId).Equals(rarity, StringComparison.OrdinalIgnoreCase);

    private bool IsNormalRewardCardRarity(string runtimeId)
        => CardRarityEquals(runtimeId, "Common") || CardRarityEquals(runtimeId, "Uncommon") || CardRarityEquals(runtimeId, "Rare");

    private int CardRaritySortKey(string runtimeId)
    {
        string rarity = CardRarity(runtimeId);
        if (rarity.Equals("Common", StringComparison.OrdinalIgnoreCase)) return 0;
        if (rarity.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)) return 1;
        if (rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase)) return 2;
        return 9;
    }

    private static bool IsOpeningRandomRelicTemplate(NeowRelicTemplateView template)
    {
        string sourceKey = template.SourceKey ?? "";
        return template.OutputType.Equals("relic", StringComparison.OrdinalIgnoreCase)
            && (sourceKey.StartsWith("source_relics.", StringComparison.OrdinalIgnoreCase)
                || sourceKey.Equals("generated_relics", StringComparison.OrdinalIgnoreCase)
                || sourceKey.Equals("predicted_relics", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsOpeningRandomRelicItem(ItemAliasView item) => IsOpeningRandomRelicRuntimeId(item.RuntimeId);

    private bool IsOpeningRandomRelicRuntimeId(string runtimeId)
    {
        if (!_relicPoolMeta.TryGetValue(runtimeId, out var meta)) return false;
        string rarity = meta.Rarity;
        bool rarityOk = rarity.Equals("Common", StringComparison.OrdinalIgnoreCase)
            || rarity.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)
            || rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase);
        if (!rarityOk) return false;

        // 开局随机遗物应来自通用池或当前角色专属池；排除其它职业专属，以及事件/商店/先古/初始/废弃等特殊池。
        if (meta.PoolKinds.Any(k => k.Equals("event", StringComparison.OrdinalIgnoreCase)
            || k.Equals("shop", StringComparison.OrdinalIgnoreCase)
            || k.Equals("ancient", StringComparison.OrdinalIgnoreCase)
            || k.Equals("starter", StringComparison.OrdinalIgnoreCase)
            || k.Equals("deprecated", StringComparison.OrdinalIgnoreCase)
            || k.Equals("fallback", StringComparison.OrdinalIgnoreCase))) return false;

        string owner = CurrentSearchOwnerName();
        if (meta.Owners.Count > 0 && !meta.Owners.Any(o => o.Equals(owner, StringComparison.OrdinalIgnoreCase))) return false;
        return meta.PoolKinds.Count == 0
            || meta.PoolKinds.Any(k => k.Equals("shared", StringComparison.OrdinalIgnoreCase) || k.Equals("character", StringComparison.OrdinalIgnoreCase));
    }

    private string RelicRarity(string runtimeId)
        => _relicPoolMeta.TryGetValue(runtimeId, out var meta) ? meta.Rarity : "";

    private int RelicRaritySortKey(string runtimeId)
    {
        string rarity = RelicRarity(runtimeId);
        if (rarity.Equals("Common", StringComparison.OrdinalIgnoreCase)) return 0;
        if (rarity.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)) return 1;
        if (rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase)) return 2;
        return 9;
    }

    private static string TemplateCategoryForAlias(NeowRelicTemplateView template) => template.OutputType switch
    {
        "potion" => "potion",
        "relic" => "relic",
        "curse" => "curse",
        "card" => "card",
        "transform_card" => "card",
        _ => "",
    };

    private static void AppendTerm(TextBox box, string term, bool allowDuplicates = false)
    {
        var terms = allowDuplicates ? SplitTermsAllowDuplicates(box.Text) : SplitTerms(box.Text);
        if (allowDuplicates || !terms.Any(x => x.Equals(term, StringComparison.OrdinalIgnoreCase)))
            terms.Add(term);
        box.Text = string.Join(Environment.NewLine, terms);
    }

    private bool AllowsDuplicateCountTerms(TextBox box)
        => ReferenceEquals(box, FinalCardAllTextBox)
           || ReferenceEquals(box, NewLeafCardAllTextBox)
           || ReferenceEquals(box, KaleidoscopeGroup1CardAllTextBox)
           || ReferenceEquals(box, KaleidoscopeGroup2CardAllTextBox)
           || ReferenceEquals(box, LostCofferCardAllTextBox);

    private void UpdateNeowFilterModeUi()
    {
        if (!IsInitialized) return;
        string mode = SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant();
        bool final = mode == "final";
        bool process = mode == "process";
        NeowProcessPanel.Visibility = process ? Visibility.Visible : Visibility.Collapsed;
        NeowFinalPanel.Visibility = final ? Visibility.Visible : Visibility.Collapsed;
        NeowProcessPanel.IsEnabled = process;
        NeowFinalPanel.IsEnabled = final;
        NeowModeHintText.Text = final
            ? "最终结果导向：只筛最终遗物、最终卡牌、最终诅咒、最终药水；过程来源条件会隐藏并忽略。"
            : process
                ? "过程导向：按来源、路线、初始选项、骨骰、后续随机遗物和过程卡牌筛选；最终结果条件会隐藏并忽略。"
                : "不筛选：不启用 Neow 高级筛选；过程导向和最终结果导向条件都会隐藏并忽略。";
    }

    private void AttachSearchTagRefreshHandlers()
    {
        TextChangedEventHandler textChanged = (_, _) => RefreshSearchRequirementTags();
        RoutedEventHandler routed = (_, _) => RefreshSearchRequirementTags();
        SelectionChangedEventHandler comboChanged = (_, _) => RefreshSearchRequirementTags();

        foreach (var box in new TextBox[]
        {
            NeowAnyTextBox, NeowAllTextBox, NeowBlacklistTextBox,
            BonesRelicAnyTextBox, BonesRelicAllTextBox, BonesRelicBlacklistTextBox,
            BonesCurseAnyTextBox, BonesCurseBlacklistTextBox,
            PredictedRelicAnyTextBox, PredictedRelicAllTextBox, PredictedRelicBlacklistTextBox,
            ProcessCardAnyTextBox, ProcessCardBlacklistTextBox,
            FinalRelicAnyTextBox, FinalRelicAllTextBox, FinalRelicBlacklistTextBox,
            FinalCardAnyTextBox, FinalCardAllTextBox, FinalCardBlacklistTextBox,
            FinalCurseAnyTextBox, FinalCurseBlacklistTextBox,
            FinalPotionAnyTextBox, FinalPotionAllTextBox, FinalPotionBlacklistTextBox, FinalNeowRelicAnyTextBox, FinalNeowRelicBlacklistTextBox,
            EventAnyTextBox, EventAllTextBox, EventBlacklistTextBox, SearchEventLimitTextBox,
            ShopRequireTextBox, ShopBlacklistTextBox, ShopExactTextBox,
            AncientRequireTextBox, AncientBlacklistTextBox, AncientOptionRequireTextBox, AncientOptionBlacklistTextBox,
            NewLeafCardBuilderTextBox, KaleidoscopeGroup1CardTextBox, KaleidoscopeGroup2CardTextBox,
            LostCofferCardTextBox, LostCofferPotionTextBox,
            NewLeafCardAllTextBox, KaleidoscopeGroup1CardAllTextBox, KaleidoscopeGroup2CardAllTextBox,
            LostCofferCardAllTextBox, LostCofferPotionAllTextBox,
        })
        {
            box.TextChanged += textChanged;
        }

        foreach (var check in new CheckBox[]
        {
            RequireBonesCheckBox, SearchAscensionScarcityCheckBox, ShopFilterEnabledCheckBox, AncientSearchEnabledCheckBox,
            AncientPaelGoopyCheckBox, AncientPaelRemovableCheckBox, AncientPaelHasEventPetCheckBox,
            AncientOrobasTouchCheckBox, AncientOrobasToothCheckBox, AncientTezcataraBasicStrikeCheckBox,
            AncientNonupeipeSwiftCheckBox, AncientTanxInstinctCheckBox, AncientDarvPandoraCheckBox
        })
        {
            check.Checked += routed;
            check.Unchecked += routed;
        }

        NeowRouteModeComboBox.SelectionChanged += comboChanged;
        BonesRequirementComboBox.SelectionChanged += comboChanged;
        BonesRelicModeComboBox.SelectionChanged += comboChanged;
        BonesCurseModeComboBox.SelectionChanged += comboChanged;
        BonesCurseBuilderComboBox.SelectionChanged += comboChanged;
        NewLeafSourceComboBox.SelectionChanged += comboChanged;
        CharacterComboBox.SelectionChanged += (_, _) => { SyncTargetPlayerCharacter(SelectedCharacter()); RefreshRunModeStatus(); };
        SearchCharacterComboBox.SelectionChanged += (_, _) => { UpdateSpecialRelicTemplateUi(); PopulateFinalResultDropdowns(); SyncTargetPlayerCharacter(SelectedComboText(SearchCharacterComboBox, "IRONCLAD")); RefreshSearchRequirementTags(); RefreshRunModeStatus(); };
        BonesRelicBuilderComboBox.SelectionChanged += (_, _) => { UpdateSpecialRelicTemplateUi(); RefreshSearchRequirementTags(); };
        DirectRelicBuilderComboBox.SelectionChanged += (_, _) => { UpdateSpecialRelicTemplateUi(); RefreshSearchRequirementTags(); };
        SearchShopLimitComboBox.SelectionChanged += comboChanged;
        EventActScopeComboBox.SelectionChanged += comboChanged;
        EventBuilderComboBox.SelectionChanged += comboChanged;
        SearchAscensionComboBox.SelectionChanged += comboChanged;
        AnalyzeAscensionComboBox.SelectionChanged += (_, _) => { };
        ShopRelicBuilderComboBox.SelectionChanged += comboChanged;
        UseProgressProfileCheckBox.Checked += (_, _) => { RefreshUnlockProfileStatus(); RefreshSearchRequirementTags(); };
        UseProgressProfileCheckBox.Unchecked += (_, _) => { RefreshUnlockProfileStatus(); RefreshSearchRequirementTags(); };
        EnableAdvancedNewLeafCheckBox.Checked += (_, _) => { UpdateSpecialRelicTemplateUi(); UpdateAnalyzeAdvancedNewLeafUi(); RefreshSearchRequirementTags(); };
        EnableAdvancedNewLeafCheckBox.Unchecked += (_, _) => { UpdateSpecialRelicTemplateUi(); UpdateAnalyzeAdvancedNewLeafUi(); RefreshSearchRequirementTags(); };
        AnalyzeUseAdvancedNewLeafCheckBox.Checked += (_, _) => UpdateAnalyzeAdvancedNewLeafUi();
        AnalyzeUseAdvancedNewLeafCheckBox.Unchecked += (_, _) => UpdateAnalyzeAdvancedNewLeafUi();
        AnalyzeNewLeafSourceComboBox.SelectionChanged += (_, _) => UpdateAnalyzeAdvancedNewLeafUi();
        MultiplayerUseProfileCheckBox.Checked += (_, _) => { RefreshRunModeStatus(); RefreshSearchRequirementTags(); };
        MultiplayerUseProfileCheckBox.Unchecked += (_, _) => { RefreshRunModeStatus(); RefreshSearchRequirementTags(); };
        FinalCardBuilderComboBox.SelectionChanged += comboChanged;
        FinalPotionBuilderComboBox.SelectionChanged += comboChanged;
        FinalCurseBuilderComboBox.SelectionChanged += comboChanged;
        FinalRelicBuilderComboBox.SelectionChanged += comboChanged;
        FinalNeowRelicComboBox.SelectionChanged += comboChanged;
        AncientBuilderComboBox.SelectionChanged += comboChanged;
        AncientOptionBuilderComboBox.SelectionChanged += comboChanged;
        AncientDarvActComboBox.SelectionChanged += comboChanged;
    }

    private void RefreshSearchTags_Click(object sender, RoutedEventArgs e) => RefreshSearchRequirementTags();

    private void RefreshSearchRequirementTags()
    {
        if (!IsInitialized) return;
        _searchRequirementTags.Clear();
        foreach (var tag in BuildSearchRequirementTags())
            _searchRequirementTags.Add(tag);
        int removable = _searchRequirementTags.Count(t => t.CanRemove);
        int groups = _searchRequirementTags.Count(t => t.Kind.Equals("header", StringComparison.OrdinalIgnoreCase));
        SearchTagCountText.Text = $"{removable} 个筛选 tag" + (groups > 0 ? $" · {groups} 组" : "");
        RefreshEventFilterConditionTable();
    }

    private void RefreshEventFilterConditionTable()
    {
        if (!IsInitialized) return;
        _eventFilterConditions.Clear();
        AddEventConditionRows(EventAllTextBox, "event_all", "必须包含");
        AddEventConditionRows(EventBlacklistTextBox, "event_blacklist", "排除");
        EventFilterConditionsEmptyText.Visibility = _eventFilterConditions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddEventConditionRows(TextBox box, string kind, string conditionText)
    {
        foreach (var term in SplitTerms(box.Text))
        {
            var parsed = ParseEventQueueTermForDisplay(term);
            int limit = parsed.Limit ?? SelectedSearchEventFilterLimit();
            string actText = parsed.ActNumber is int act ? $"Act{act}" : "任意 Act";
            _eventFilterConditions.Add(new EventFilterConditionView(
                actText,
                "前 " + Math.Min(15, Math.Max(1, limit)),
                conditionText,
                D(parsed.EventTerm),
                kind,
                term));
        }
    }

    private void DeleteSearchTag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SearchRequirementTagView tag || !tag.CanRemove) return;
        RemoveSearchRequirementTag(tag);
        UpdateTagBuilderUi(syncLegacy: true);
        RefreshSearchRequirementTags();
        BottomStatusText.Text = "已删除 Tag：" + tag.Text;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        string seed = SeedTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(seed))
        {
            MessageBox.Show("请输入 seed。", "RollTheSpire2", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AnalyzeButton.IsEnabled = false;
        AnalyzeButton.Content = "分析中...";
        BottomStatusText.Text = "正在分析 " + seed;
        try
        {
            // WPF 控件只能在 UI 线程读取；先在 UI 线程构建配置快照，再把纯数据交给后台线程。
            // 单种分析不再套用批量筛种 Tag；它专注完整预测与阅读。
            using var doc = BuildRuntimeConfigDocument(includeSearchFields: false, activeCharacterOverride: SelectedCharacter(), analyzeMode: true);
            var config = doc.RootElement.Clone();

            bool includeDebug = false;

            var payload = await Task.Run(() => AnalyzeSeedPayload(seed, config, includeDebug));
            AnalyzeOutputBox.Text = payload.Text;
            ReplaceCards(_analyzeCards, payload.Cards);
            _lastAnalyzeSeed = seed.Trim();
            _lastAnalyzeCharacter = SelectedCharacter();
            _lastAnalyzeAscension = SelectedAnalyzeAscensionLevel();
            _lastAnalyzeSummary = BuildAnalyzeHistorySummary(payload.Cards);
            AppendLog("Analyze done: " + seed);
            BottomStatusText.Text = "分析完成";
        }
        catch (Exception ex)
        {
            AnalyzeOutputBox.Text = ex.ToString();
            AppendLog("Analyze failed: " + ex.Message);
            BottomStatusText.Text = "分析失败";
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            AnalyzeButton.Content = "开始分析";
        }
    }

    private AnalyzePayload AnalyzeSeedPayload(string seed, JsonElement config, bool includeDebug)
    {
        var plan = SearchPlan.FromConfigForFullDetails(config, _rootDir);
        var result = new OpeningPredictor(plan).CheckFullDetails(seed);
        bool matched = plan.Matches(result);
        return new AnalyzePayload
        {
            Text = FormatResult(result, plan, matched, includeDebug),
            Cards = BuildResultCards(result, plan, matched).ToList(),
        };
    }

    private JsonDocument BuildRuntimeConfigDocument(bool includeSearchFields, string? activeCharacterOverride = null, bool analyzeMode = false)
    {
        if (!File.Exists(_configPath)) throw new FileNotFoundException("config.json not found", _configPath);
        var node = JsonNode.Parse(File.ReadAllText(_configPath, Encoding.UTF8))?.AsObject()
            ?? throw new InvalidOperationException("config.json is not a JSON object");

        var rng = EnsureObject(node, "rng");
        string rngVersion = SelectedGameRngVersion();
        rng["version"] = rngVersion;
        node["game_rng_version"] = rngVersion;

        var player = EnsureObject(node, "player");
        string activeCharacter = activeCharacterOverride ?? (includeSearchFields ? SelectedComboText(SearchCharacterComboBox, "IRONCLAD") : SelectedCharacter());
        player["character"] = activeCharacter;
        bool multiplayer = IsMultiplayerUiSelected();
        node["run_mode"] = multiplayer ? "multiplayer" : "singleplayer";
        if (multiplayer)
        {
            int playersCount = SelectedPlayersCount();
            string targetNetId = SelectedTargetNetId();
            player["players_count"] = playersCount;
            player["net_id"] = targetNetId;
            node["players"] = BuildPlayersArray(activeCharacter, playersCount, targetNetId);
            node["multiplayer_unlock_mode"] = MultiplayerUseProfileCheckBox.IsChecked == true ? "profile" : "all_unlocked";
        }
        else
        {
            player["players_count"] = 1;
            player["net_id"] = "1";
            node["players"] = new JsonArray();
            node["multiplayer_unlock_mode"] = "all_unlocked";
        }
        if (analyzeMode)
        {
            int ascension = SelectedAnalyzeAscensionLevel();
            player["ascension"] = ascension;
            player["ascension_scarcity"] = ascension >= 7;
            var singleSeed = EnsureObject(node, "single_seed");
            singleSeed["show_map_boss"] = AnalyzeMapBossCheckBox.IsChecked == true;
            singleSeed["map_boss_detail"] = AnalyzeDetailedMapBossCheckBox.IsChecked == true;
            singleSeed["show_full_event_queue"] = AnalyzeShowFullEventQueueCheckBox.IsChecked == true;
            singleSeed["show_relic_sequence"] = AnalyzeRelicSequenceCheckBox.IsChecked == true;
            singleSeed["relic_sequence_limit"] = SelectedAnalyzeRelicSequenceLimit();
        }
        else if (includeSearchFields)
        {
            int ascension = SelectedSearchAscensionLevel();
            player["ascension"] = ascension;
            player["ascension_scarcity"] = ascension >= 7;
        }

        var unlockProfile = EnsureObject(node, "unlock_profile");
        bool useProfile = UseProgressProfileCheckBox.IsChecked == true && File.Exists(Path.Combine(_rootDir, "profiles", "unlock_profile.json"));
        unlockProfile["mode"] = useProfile ? "profile" : "all_unlocked";
        unlockProfile["path"] = "profiles/unlock_profile.json";

        var filters = EnsureObject(node, "filters");
        if (includeSearchFields)
        {
            string neowMode = SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant();
            filters["neow_mode"] = neowMode;
            ApplyNeowModeFilters(filters, neowMode);

            // 卡牌/药水结果筛选已并入 Neow 最终结果导向；批量筛种页不再单独启用旧 card_opportunities / potions 筛选。
            ClearTermList(EnsureObject(filters, "potions"));
            ClearCardOpportunityFilters(filters);
        }

        var choices = EnsureObject(node, "choices");
        string currentNeowModeForChoices = includeSearchFields ? SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant() : "none";
        string currentBonesReqForChoices = includeSearchFields ? SelectedComboText(BonesRequirementComboBox, "yes").ToLowerInvariant() : "yes";
        // Advanced NewLeaf source selection is a bones-route-only feature.
        // RollCore additionally guards direct Neow routes so direct NewLeaf still uses starter_basic.
        if (analyzeMode)
        {
            choices["NewLeafSelectedCard"] = SelectedAnalyzeNewLeafSourceSelector();
        }
        else
        {
            choices["NewLeafSelectedCard"] = currentNeowModeForChoices == "process" && currentBonesReqForChoices == "yes" && AdvancedNewLeafEnabled()
                ? SelectedNewLeafSourceSelector()
                : "starter_basic";
        }

        var eventQueue = EnsureObject(node, "event_queue");
        bool eventEnabled = analyzeMode
            ? EnableEventCheckBox.IsChecked == true || HasAnyEventFilterText()
            : (includeSearchFields ? HasAnyEventFilterText() : EnableEventCheckBox.IsChecked == true || HasAnyEventFilterText());
        eventQueue["enabled"] = eventEnabled;
        eventQueue["show"] = eventEnabled;
        if (includeSearchFields)
        {
            int filterLimit = MaxSelectedEventFilterLimitUsed();
            eventQueue["limit_per_act"] = filterLimit;
            eventQueue["filter_limit_per_act"] = filterLimit;
            if (eventEnabled) ApplyEventFilterText(eventQueue);
            else ClearEventFilterText(eventQueue);
        }
        else
        {
            var limitBox = analyzeMode ? EventLimitTextBox : EventLimitTextBox;
            if (int.TryParse(limitBox.Text.Trim(), out int limit) && limit > 0)
            {
                eventQueue["limit_per_act"] = limit;
                eventQueue["filter_limit_per_act"] = Math.Min(15, limit);
            }
            ClearEventFilterText(eventQueue);
        }

        var shop = EnsureObject(node, "shop_relics");
        if (includeSearchFields)
        {
            bool shopEnabled = ShopFilterEnabledCheckBox.IsChecked == true || HasAnyTerms(ShopRequireTextBox, ShopBlacklistTextBox, ShopExactTextBox);
            if (analyzeMode) shopEnabled = shopEnabled || EnableShopCheckBox.IsChecked == true;
            shop["enabled"] = shopEnabled;
            shop["show"] = shopEnabled;
            if (int.TryParse(SelectedComboText(SearchShopLimitComboBox, "5").Trim(), out int shopLimit) && shopLimit > 0) shop["limit"] = shopLimit;
            shop["require_relics"] = ToJsonArray(SplitTerms(ShopRequireTextBox.Text));
            shop["blacklist"] = ToJsonArray(SplitTerms(ShopBlacklistTextBox.Text));
            shop["exact_relic_filters"] = BuildShopExactFilters();
        }
        else
        {
            bool showShop = EnableShopCheckBox.IsChecked == true;
            shop["enabled"] = showShop;
            shop["show"] = showShop;
            if (showShop) shop["limit"] = 8;
        }

        var ancient = EnsureObject(node, "ancient");
        if (includeSearchFields)
        {
            bool ancientEnabled = AncientSearchEnabledCheckBox.IsChecked == true
                || HasAnyTerms(AncientRequireTextBox, AncientBlacklistTextBox, AncientOptionRequireTextBox, AncientOptionBlacklistTextBox)
                || SelectedDarvActFilter().Length > 0;
            if (analyzeMode) ancientEnabled = ancientEnabled || EnableAncientCheckBox.IsChecked == true;
            ancient["enabled"] = ancientEnabled;
            ancient["show_options"] = true;
            ancient["ascension"] = analyzeMode ? SelectedAnalyzeAscensionLevel() : SelectedSearchAscensionLevel();
            ancient["require_ancients"] = ToJsonArray(BuildAncientRequireTerms());
            ancient["blacklist_ancients"] = ToJsonArray(SplitTerms(AncientBlacklistTextBox.Text));
            ancient["require_options"] = ToJsonArray(SplitTerms(AncientOptionRequireTextBox.Text));
            ancient["blacklist_options"] = ToJsonArray(SplitTerms(AncientOptionBlacklistTextBox.Text));
            WriteAncientConditionPresets(ancient);
        }
        else
        {
            ancient["enabled"] = EnableAncientCheckBox.IsChecked == true;
            ancient["show_options"] = true;
            if (analyzeMode)
            {
                ancient["ascension"] = SelectedAnalyzeAscensionLevel();
                WriteAnalyzeAncientConditionPresets(ancient);
            }
        }

        if (includeSearchFields)
        {
            var search = EnsureObject(node, "search");
            if (long.TryParse(SearchStartTextBox.Text.Trim(), out long start)) search["start"] = start;
            if (long.TryParse(SearchEndTextBox.Text.Trim(), out long end)) search["end"] = end;
            if (int.TryParse(SearchMaxResultsTextBox.Text.Trim(), out int maxResults)) search["max_results"] = Math.Max(1, maxResults);
            search["print_every"] = 1000;

            var seedGen = EnsureObject(node, "seed_generation");
            seedGen["mode"] = SelectedComboText(SearchModeComboBox, "random");
            seedGen["length"] = 10;
            string candidatePoolId = SelectedCandidatePoolId();
            if (!string.IsNullOrWhiteSpace(candidatePoolId)) seedGen["candidate_pool_id"] = candidatePoolId;
        }

        return JsonDocument.Parse(node.ToJsonString());
    }


    private JsonArray BuildShopExactFilters()
    {
        var arr = new JsonArray();
        foreach (var raw in SplitTerms(ShopExactTextBox.Text))
        {
            var text = raw.Trim();
            if (text.Length == 0) continue;
            var m = Regex.Match(text, @"^\s*(?:第)?\s*(\d+)\s*(?:个)?\s*[:：,，\s]+(.+)$");
            if (!m.Success) continue;
            int pos = Math.Max(1, int.Parse(m.Groups[1].Value));
            string relic = Term.Normalize(m.Groups[2].Value);
            if (relic.Length == 0) continue;
            arr.Add(new JsonObject { ["position"] = pos, ["relic"] = relic });
        }
        return arr;
    }

    private static JsonObject EnsureObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject obj) return obj;
        obj = new JsonObject();
        root[name] = obj;
        return obj;
    }

    private static void ApplyTermList(JsonObject obj, TextBox anyBox, TextBox allBox, TextBox blacklistBox)
    {
        obj["whitelist_any"] = ToJsonArray(SplitTerms(anyBox.Text));
        obj["whitelist_all"] = ToJsonArray(SplitTermsAllowDuplicates(allBox.Text));
        obj["blacklist"] = ToJsonArray(SplitTerms(blacklistBox.Text));
    }

    private void ApplyCardOpportunityFilters(JsonObject filters)
    {
        var cardOpp = EnsureObject(filters, "card_opportunities");
        ApplyCardCategory(cardOpp, "own", OwnCardsEnabledCheckBox, OwnCardsAnyTextBox);
        ApplyCardCategory(cardOpp, "colorless", ColorlessCardsEnabledCheckBox, ColorlessCardsAnyTextBox);
        ApplyCardCategory(cardOpp, "other", OtherCardsEnabledCheckBox, OtherCardsAnyTextBox);
    }

    private static void ApplyCardCategory(JsonObject cardOpp, string name, CheckBox enabledBox, TextBox anyBox)
    {
        var obj = EnsureObject(cardOpp, name);
        var terms = SplitTerms(anyBox.Text);
        obj["enabled"] = enabledBox.IsChecked == true || terms.Count > 0;
        obj["whitelist_any"] = ToJsonArray(terms);
        obj["whitelist_all"] = new JsonArray();
        obj["blacklist"] = new JsonArray();
        obj["rarities"] = new JsonArray();
        obj["min_count"] = terms.Count > 0 ? 1 : 0;
    }

    private static void ClearCardOpportunityFilters(JsonObject filters)
    {
        var cardOpp = EnsureObject(filters, "card_opportunities");
        ClearCardCategory(cardOpp, "own");
        ClearCardCategory(cardOpp, "colorless");
        ClearCardCategory(cardOpp, "other");
    }

    private static void ClearCardCategory(JsonObject cardOpp, string name)
    {
        var obj = EnsureObject(cardOpp, name);
        obj["enabled"] = false;
        obj["whitelist_any"] = new JsonArray();
        obj["whitelist_all"] = new JsonArray();
        obj["blacklist"] = new JsonArray();
        obj["rarities"] = new JsonArray();
        obj["min_count"] = 0;
    }


    private void ApplyNeowModeFilters(JsonObject filters, string neowMode)
    {
        if (neowMode == "process")
        {
            UpdateTagBuilderUi(syncLegacy: true);
            string bonesReq = SelectedComboText(BonesRequirementComboBox, "yes").ToLowerInvariant();
            filters["require_bones"] = bonesReq == "yes";

            // Preview3：过程导向先由 Tag 构建器控制骨骰路线、骨骰遗物、骨骰诅咒。
            ClearTermList(EnsureObject(filters, "neow_options"));
            ApplyTermList(EnsureObject(filters, "bones_relics"), BonesRelicAnyTextBox, BonesRelicAllTextBox, BonesRelicBlacklistTextBox);

            var bonesCurse = EnsureObject(filters, "bones_curse");
            bonesCurse["whitelist_any"] = ToJsonArray(SplitTerms(BonesCurseAnyTextBox.Text));
            bonesCurse["blacklist"] = ToJsonArray(SplitTerms(BonesCurseBlacklistTextBox.Text));

            ClearTermList(EnsureObject(filters, "predicted_relics"));
            ApplyNeowProcessFilter(filters);
            ClearNeowFinalFilter(filters);
            return;
        }

        ClearNeowProcessFilter(filters);
        if (neowMode == "final")
        {
            ApplyNeowFinalFilter(filters);
            return;
        }

        ClearNeowFinalFilter(filters);
    }

    private void ApplyNeowProcessFilter(JsonObject filters)
    {
        var process = EnsureObject(filters, "neow_process");
        string bonesReq = SelectedComboText(BonesRequirementComboBox, "yes").ToLowerInvariant();
        string routeMode = bonesReq == "yes" ? "bones" : "direct";
        process["route"] = routeMode;

        ApplyTermList(EnsureObject(process, "initial_relics"), NeowAnyTextBox, NeowAllTextBox, NeowBlacklistTextBox);
        ApplyTermList(EnsureObject(process, "bones_relics"), BonesRelicAnyTextBox, BonesRelicAllTextBox, BonesRelicBlacklistTextBox);
        ClearTermList(EnsureObject(process, "generated_relics"));
        process["source_relics"] = new JsonObject();
        ClearTermList(EnsureObject(process, "cards"));
        ClearTermList(EnsureObject(process, "potions"));

        var sourceCards = new JsonObject();
        process["source_cards"] = sourceCards;
        ApplySourceTermList(sourceCards, "NewLeaf", NewLeafCardAllTextBox);
        ApplySourceTermList(sourceCards, "Kaleidoscope", KaleidoscopeGroup1CardAllTextBox, KaleidoscopeGroup2CardAllTextBox);
        ApplySourceTermList(sourceCards, "LostCoffer", LostCofferCardAllTextBox);

        var sourcePotions = new JsonObject();
        process["source_potions"] = sourcePotions;
        ApplySourceTermList(sourcePotions, "LostCoffer", LostCofferPotionAllTextBox);

        var processCurse = EnsureObject(process, "curse");
        processCurse["whitelist_any"] = ToJsonArray(SplitTerms(BonesCurseAnyTextBox.Text));
        processCurse["blacklist"] = ToJsonArray(SplitTerms(BonesCurseBlacklistTextBox.Text));
        ApplyDynamicTemplateTerms(process);
    }

    private static void ApplySourceTermList(JsonObject parent, string key, params TextBox[] allBoxes)
    {
        var terms = allBoxes
            .SelectMany(box => SplitTermsAllowDuplicates(box.Text))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var obj = EnsureObject(parent, key);
        obj["whitelist_any"] = new JsonArray();
        obj["whitelist_all"] = ToJsonArray(terms);
        obj["blacklist"] = new JsonArray();
    }

    private void ApplyDynamicTemplateTerms(JsonObject process)
    {
        foreach (var kv in _dynamicTemplateTerms)
        {
            if (!_neowTemplatesById.TryGetValue(kv.Key, out var template)) continue;
            var terms = kv.Value.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (terms.Count == 0) continue;
            string sourceKey = template.SourceKey ?? "";
            if (sourceKey.StartsWith("source_cards.", StringComparison.OrdinalIgnoreCase))
            {
                var sourceCards = EnsureObject(process, "source_cards");
                AddWhitelistAll(EnsureObject(sourceCards, sourceKey["source_cards.".Length..]), terms);
            }
            else if (sourceKey.StartsWith("source_potions.", StringComparison.OrdinalIgnoreCase))
            {
                var sourcePotions = EnsureObject(process, "source_potions");
                AddWhitelistAll(EnsureObject(sourcePotions, sourceKey["source_potions.".Length..]), terms);
            }
            else if (sourceKey.StartsWith("source_relics.", StringComparison.OrdinalIgnoreCase))
            {
                var sourceRelics = EnsureObject(process, "source_relics");
                AddWhitelistAll(EnsureObject(sourceRelics, sourceKey["source_relics.".Length..]), terms);
            }
            else if (sourceKey.Equals("generated_relics", StringComparison.OrdinalIgnoreCase))
            {
                AddWhitelistAll(EnsureObject(process, "generated_relics"), terms);
            }
            else if (sourceKey.Equals("bones_relics", StringComparison.OrdinalIgnoreCase))
            {
                AddWhitelistAll(EnsureObject(process, "bones_relics"), terms);
            }
            else if (sourceKey.Equals("curse", StringComparison.OrdinalIgnoreCase))
            {
                var curse = EnsureObject(process, "curse");
                AddStringArray(curse, "whitelist_any", terms);
            }
        }
    }

    private static void AddWhitelistAll(JsonObject termList, IEnumerable<string> terms)
    {
        // 动态模板的 whitelist_all 需要保留重复项来表达数量要求。
        AddStringArray(termList, "whitelist_all", terms, allowDuplicates: true);
        if (termList["whitelist_any"] is null) termList["whitelist_any"] = new JsonArray();
        if (termList["blacklist"] is null) termList["blacklist"] = new JsonArray();
    }

    private static void AddStringArray(JsonObject obj, string name, IEnumerable<string> terms, bool allowDuplicates = false)
    {
        JsonArray arr;
        if (obj[name] is JsonArray existing) arr = existing;
        else
        {
            arr = new JsonArray();
            obj[name] = arr;
        }
        var seen = allowDuplicates ? null : new HashSet<string>(arr.Select(x => x?.GetValue<string>() ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            if (allowDuplicates) arr.Add(term);
            else if (seen!.Add(term)) arr.Add(term);
        }
    }

    private void ApplyNeowFinalFilter(JsonObject filters)
    {
        var final = EnsureObject(filters, "neow_final");
        ApplyTermList(EnsureObject(final, "relics"), FinalRelicAnyTextBox, FinalRelicAllTextBox, FinalRelicBlacklistTextBox);
        ApplyTermList(EnsureObject(final, "cards"), FinalCardAnyTextBox, FinalCardAllTextBox, FinalCardBlacklistTextBox);
        ApplyTermList(EnsureObject(final, "potions"), FinalPotionAnyTextBox, FinalPotionAllTextBox, FinalPotionBlacklistTextBox);
        var neowRelics = EnsureObject(final, "neow_relics");
        neowRelics["whitelist_any"] = new JsonArray();
        neowRelics["whitelist_all"] = ToJsonArray(SplitTermsAllowDuplicates(FinalNeowRelicAnyTextBox.Text));
        neowRelics["blacklist"] = new JsonArray();
        var finalCurses = EnsureObject(final, "curses");
        finalCurses["whitelist_any"] = ToJsonArray(SplitTerms(FinalCurseAnyTextBox.Text));
        finalCurses["blacklist"] = ToJsonArray(SplitTerms(FinalCurseBlacklistTextBox.Text));
        final["neow_relic_blacklist"] = ToJsonArray(SplitTerms(FinalNeowRelicBlacklistTextBox.Text));
    }

    private static void ClearTermList(JsonObject obj)
    {
        obj["whitelist_any"] = new JsonArray();
        obj["whitelist_all"] = new JsonArray();
        obj["blacklist"] = new JsonArray();
    }

    private static void ClearNeowProcessFilter(JsonObject filters)
    {
        filters["require_bones"] = false;
        ClearTermList(EnsureObject(filters, "neow_options"));
        ClearTermList(EnsureObject(filters, "bones_relics"));
        var bonesCurse = EnsureObject(filters, "bones_curse");
        bonesCurse["whitelist_any"] = new JsonArray();
        bonesCurse["blacklist"] = new JsonArray();
        ClearTermList(EnsureObject(filters, "predicted_relics"));

        var process = EnsureObject(filters, "neow_process");
        process["route"] = "any";
        ClearTermList(EnsureObject(process, "initial_relics"));
        ClearTermList(EnsureObject(process, "bones_relics"));
        ClearTermList(EnsureObject(process, "generated_relics"));
        process["source_cards"] = new JsonObject();
        process["source_potions"] = new JsonObject();
        process["source_relics"] = new JsonObject();
        ClearTermList(EnsureObject(process, "cards"));
        ClearTermList(EnsureObject(process, "potions"));
        var processCurse = EnsureObject(process, "curse");
        processCurse["whitelist_any"] = new JsonArray();
        processCurse["blacklist"] = new JsonArray();
    }

    private static void ClearNeowFinalFilter(JsonObject filters)
    {
        var final = EnsureObject(filters, "neow_final");
        ClearTermList(EnsureObject(final, "relics"));
        ClearTermList(EnsureObject(final, "cards"));
        ClearTermList(EnsureObject(final, "potions"));
        var finalCurses = EnsureObject(final, "curses");
        finalCurses["whitelist_any"] = new JsonArray();
        finalCurses["blacklist"] = new JsonArray();
        ClearTermList(EnsureObject(final, "neow_relics"));
        final["neow_relic_blacklist"] = new JsonArray();
    }

    private static bool HasAnyTerms(params TextBox[] boxes)
    {
        foreach (var box in boxes)
            if (SplitTerms(box.Text).Count > 0) return true;
        return false;
    }

    private void RemoveSearchRequirementTag(SearchRequirementTagView tag)
    {
        switch (tag.Kind)
        {
            case "bones_route":
                SetCombo(NeowFilterModeComboBox, "none");
                break;
            case "bones_relic_mode":
                SetCombo(BonesRelicModeComboBox, "specified");
                break;
            case "bones_relic_all":
                RemoveTerm(BonesRelicAllTextBox, tag.Value);
                break;
            case "bones_relic_any":
                RemoveTerm(BonesRelicAnyTextBox, tag.Value);
                break;
            case "bones_relic_blacklist":
                RemoveTerm(BonesRelicBlacklistTextBox, tag.Value);
                break;
            case "direct_relic_all":
                RemoveTerm(NeowAllTextBox, tag.Value);
                break;
            case "newleaf_card_all":
                RemoveTerm(NewLeafCardAllTextBox, tag.Value);
                break;
            case "kaleidoscope_card_all":
                RemoveTerm(KaleidoscopeGroup1CardAllTextBox, tag.Value);
                RemoveTerm(KaleidoscopeGroup2CardAllTextBox, tag.Value);
                break;
            case "kaleidoscope_group1_card_all":
                RemoveTerm(KaleidoscopeGroup1CardAllTextBox, tag.Value);
                break;
            case "kaleidoscope_group2_card_all":
                RemoveTerm(KaleidoscopeGroup2CardAllTextBox, tag.Value);
                break;
            case "lostcoffer_card_all":
                RemoveTerm(LostCofferCardAllTextBox, tag.Value);
                break;
            case "lostcoffer_potion_all":
                RemoveTerm(LostCofferPotionAllTextBox, tag.Value);
                break;
            case "event_any": RemoveTerm(EventAnyTextBox, tag.Value); break;
            case "event_all": RemoveTerm(EventAllTextBox, tag.Value); break;
            case "event_blacklist": RemoveTerm(EventBlacklistTextBox, tag.Value); break;
            case "shop_exact": RemoveTerm(ShopExactTextBox, tag.Value); break;
            case "shop_require": RemoveTerm(ShopRequireTextBox, tag.Value); break;
            case "shop_blacklist": RemoveTerm(ShopBlacklistTextBox, tag.Value); break;
            case "ancient_require": RemoveTerm(AncientRequireTextBox, tag.Value); break;
            case "ancient_blacklist": RemoveTerm(AncientBlacklistTextBox, tag.Value); break;
            case "ancient_option_require": RemoveTerm(AncientOptionRequireTextBox, tag.Value); break;
            case "ancient_option_blacklist": RemoveTerm(AncientOptionBlacklistTextBox, tag.Value); break;
            default:
                if (tag.Kind.StartsWith("dynamic_template:", StringComparison.OrdinalIgnoreCase))
                {
                    string templateId = tag.Kind["dynamic_template:".Length..];
                    if (_dynamicTemplateTerms.TryGetValue(templateId, out var values))
                    {
                        values.RemoveAll(x => x.Equals(tag.Value, StringComparison.OrdinalIgnoreCase));
                        if (values.Count == 0) _dynamicTemplateTerms.Remove(templateId);
                    }
                }
                break;
            case "bones_curse_any":
                RemoveTerm(BonesCurseAnyTextBox, tag.Value);
                break;
            case "bones_curse_blacklist":
                RemoveTerm(BonesCurseBlacklistTextBox, tag.Value);
                break;
            case "final_relic_any": RemoveTerm(FinalRelicAnyTextBox, tag.Value); break;
            case "final_relic_all": RemoveTerm(FinalRelicAllTextBox, tag.Value); break;
            case "final_relic_blacklist": RemoveTerm(FinalRelicBlacklistTextBox, tag.Value); break;
            case "final_card_any": RemoveTerm(FinalCardAnyTextBox, tag.Value); break;
            case "final_card_all": RemoveTerm(FinalCardAllTextBox, tag.Value); break;
            case "final_card_blacklist": RemoveTerm(FinalCardBlacklistTextBox, tag.Value); break;
            case "final_curse_any": RemoveTerm(FinalCurseAnyTextBox, tag.Value); break;
            case "final_curse_blacklist": RemoveTerm(FinalCurseBlacklistTextBox, tag.Value); break;
            case "final_potion_any": RemoveTerm(FinalPotionAnyTextBox, tag.Value); break;
            case "final_potion_all": RemoveTerm(FinalPotionAllTextBox, tag.Value); break;
            case "final_potion_blacklist": RemoveTerm(FinalPotionBlacklistTextBox, tag.Value); break;
            case "final_neow_any": RemoveTerm(FinalNeowRelicAnyTextBox, tag.Value); break;
            case "final_neow_blacklist": RemoveTerm(FinalNeowRelicBlacklistTextBox, tag.Value); break;
        }
    }

    private static void RemoveTerm(TextBox box, string term)
    {
        var terms = SplitTermsAllowDuplicates(box.Text)
            .Where(x => !x.Equals(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        box.Text = string.Join(Environment.NewLine, terms);
    }

    private void LoadItemAliases()
    {
        _itemAliases.Clear();
        _itemsByRuntimeId.Clear();
        try
        {
            _entityIndex = EntityIndex.Load(_rootDir);
            if (_entityIndex.Loaded)
            {
                foreach (var entry in _entityIndex.Entries)
                {
                    var item = new ItemAliasView(entry.SourceId, entry.CanonicalId, entry.Type, entry.Zh, entry.Eng);
                    AddItemAlias(item, entry.SourceId);
                    AddItemAlias(item, entry.CanonicalId);
                    AddItemAlias(item, entry.Zh);
                    AddItemAlias(item, entry.Eng);
                    foreach (var a in entry.Aliases) AddItemAlias(item, a);
                    if (!_itemsByRuntimeId.ContainsKey(entry.CanonicalId)) _itemsByRuntimeId[entry.CanonicalId] = item;
                }
                RebuildTypedItemLists();
                AppendLog($"Entity index loaded: {_entityIndex.Count} entities from {RollDataPaths.SafeRel(_rootDir, _entityIndex.FilePath)}");
                return;
            }

            // Legacy fallback: older packages only had extractor/sts2_extracted_data_v4.json.
            string? path = RollDataPaths.FindSourceData(_rootDir);
            if (path is null) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var items = doc.RootElement.Prop("localization").Prop("items");
            if (items is null || items.Value.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in items.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string sourceId = prop.Value.Prop("id").Str(prop.Name);
                string runtimeId = prop.Value.Prop("class").Str("");
                if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = SourceIdToRuntimeIdLocal(sourceId);
                string category = prop.Value.Prop("category").Str("");
                string zh = prop.Value.Prop("name").Prop("zhs").Str("");
                string en = prop.Value.Prop("name").Prop("eng").Str("");
                if (string.IsNullOrWhiteSpace(en)) en = prop.Value.Prop("fallback_name").Str("");
                var alias = new ItemAliasView(sourceId, runtimeId, category, zh, en);
                AddItemAlias(alias, sourceId);
                AddItemAlias(alias, runtimeId);
                AddItemAlias(alias, zh);
                AddItemAlias(alias, en);
                var aliases = prop.Value.Prop("aliases").StringList();
                foreach (var a in aliases) AddItemAlias(alias, a);
                if (!_itemsByRuntimeId.ContainsKey(runtimeId)) _itemsByRuntimeId[runtimeId] = alias;
            }
            AddItemAliasByRuntime("NewLeaf", "新叶");
            AddItemAliasByRuntime("Kaleidoscope", "万花");
            AddItemAliasByRuntime("LostCoffer", "盒子");
            RebuildTypedItemLists();
        }
        catch (Exception ex)
        {
            AppendLog("加载物品别名失败：" + ex.Message);
        }
    }

    private void RebuildTypedItemLists()
    {
        _cardItems.Clear();
        _potionItems.Clear();
        _relicItems.Clear();
        _curseItems.Clear();

        var items = _itemsByRuntimeId.Values
            .GroupBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cardItems.AddRange(items.Where(x => x.Category.Equals("card", StringComparison.OrdinalIgnoreCase)));
        _potionItems.AddRange(items.Where(x => x.Category.Equals("potion", StringComparison.OrdinalIgnoreCase)));
        _relicItems.AddRange(items.Where(x => x.Category.Equals("relic", StringComparison.OrdinalIgnoreCase)));
        _curseItems.AddRange(items.Where(x => x.Category.Equals("curse", StringComparison.OrdinalIgnoreCase) || IsKnownCurseRuntimeId(x.RuntimeId)));

        // 骨骰诅咒下拉只显示 RollCore 当前骨骰诅咒池，不混入其它固定/事件诅咒。
        var bonesCurseDropdown = BonesCursePoolRuntimeIds
            .Select(id => _itemsByRuntimeId.TryGetValue(id, out var item) ? item : new ItemAliasView(id, id, "curse", id, id))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        BonesCurseBuilderComboBox.ItemsSource = bonesCurseDropdown;

        LoadCardPoolMeta();
        LoadPotionPoolMeta();
        LoadRelicPoolMeta();
        LoadShopAndAncientDropdownItems();
        PopulateFinalResultDropdowns();
    }

    private void LoadCardPoolMeta()
    {
        _cardPoolMeta.Clear();
        try
        {
            string path = Path.Combine(_rootDir, "data", "sts2_data.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in cards.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string runtimeId = JsonStr(prop.Value, "class", "");
                if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = SourceIdToRuntimeIdLocal(prop.Name);
                var meta = new CardPoolMetaView();
                if (prop.Value.TryGetProperty("owners", out var owners) && owners.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in owners.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String) meta.Owners.Add(o.GetString() ?? "");
                }
                if (prop.Value.TryGetProperty("pool_kinds", out var poolKinds) && poolKinds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in poolKinds.EnumerateArray())
                        if (k.ValueKind == JsonValueKind.String) meta.PoolKinds.Add(k.GetString() ?? "");
                }
                meta.Rarity = JsonStr(prop.Value, "rarity", "");
                meta.MultiplayerConstraint = JsonStr(prop.Value, "multiplayer_constraint", "");
                _cardPoolMeta[runtimeId] = meta;
            }
        }
        catch (Exception ex)
        {
            AppendLog("加载卡牌池元数据失败：" + ex.Message);
        }
    }

    private void LoadPotionPoolMeta()
    {
        _potionPoolMeta.Clear();
        try
        {
            string path = Path.Combine(_rootDir, "data", "sts2_data.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("potions", out var potions) || potions.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in potions.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string runtimeId = JsonStr(prop.Value, "class", "");
                if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = SourceIdToRuntimeIdLocal(prop.Name);
                var meta = new PotionPoolMetaView { Rarity = JsonStr(prop.Value, "rarity", "") };
                if (prop.Value.TryGetProperty("owners", out var owners) && owners.ValueKind == JsonValueKind.Array)
                    foreach (var owner in owners.EnumerateArray()) if (owner.ValueKind == JsonValueKind.String) meta.Owners.Add(owner.GetString() ?? "");
                if (prop.Value.TryGetProperty("pool_kinds", out var poolKinds) && poolKinds.ValueKind == JsonValueKind.Array)
                    foreach (var k in poolKinds.EnumerateArray()) if (k.ValueKind == JsonValueKind.String) meta.PoolKinds.Add(k.GetString() ?? "");
                _potionPoolMeta[runtimeId] = meta;
            }
        }
        catch (Exception ex)
        {
            AppendLog("加载药水池元数据失败：" + ex.Message);
        }
    }

    private void LoadRelicPoolMeta()
    {
        _relicPoolMeta.Clear();
        try
        {
            string path = Path.Combine(_rootDir, "data", "sts2_data.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("relics", out var relics) || relics.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in relics.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                string runtimeId = JsonStr(prop.Value, "class", "");
                if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = SourceIdToRuntimeIdLocal(prop.Name);
                var meta = new RelicPoolMetaView
                {
                    Rarity = JsonStr(prop.Value, "rarity", ""),
                };
                if (prop.Value.TryGetProperty("owners", out var owners) && owners.ValueKind == JsonValueKind.Array)
                {
                    foreach (var owner in owners.EnumerateArray())
                        if (owner.ValueKind == JsonValueKind.String) meta.Owners.Add(owner.GetString() ?? "");
                }
                if (prop.Value.TryGetProperty("pools", out var pools) && pools.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pool in pools.EnumerateArray())
                        if (pool.ValueKind == JsonValueKind.String) meta.Pools.Add(pool.GetString() ?? "");
                }
                if (prop.Value.TryGetProperty("pool_kinds", out var poolKinds) && poolKinds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in poolKinds.EnumerateArray())
                        if (k.ValueKind == JsonValueKind.String) meta.PoolKinds.Add(k.GetString() ?? "");
                }
                _relicPoolMeta[runtimeId] = meta;
            }
        }
        catch (Exception ex)
        {
            AppendLog("加载遗物池元数据失败：" + ex.Message);
        }
    }


    private void LoadShopAndAncientDropdownItems()
    {
        _shopRelicItems.Clear();
        _ancientItems.Clear();
        _ancientOptionItems.Clear();

        _shopRelicItems.AddRange(_relicItems
            .Where(x => IsShopRelicRuntimeId(x.RuntimeId))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase));
        ShopRelicBuilderComboBox.ItemsSource = _shopRelicItems;

        try
        {
            string path = Path.Combine(_rootDir, "data", "sts2_data.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var ancientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("acts", out var acts) && acts.ValueKind == JsonValueKind.Object)
            {
                foreach (var act in acts.EnumerateObject())
                    if (act.Value.TryGetProperty("ancients", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var a in arr.EnumerateArray())
                        {
                            if (a.ValueKind != JsonValueKind.String) continue;
                            string id = SourceIdToRuntimeIdLocal(a.GetString() ?? "");
                            if (!IsNeowAncientId(id)) ancientIds.Add(id);
                        }
            }
            if (doc.RootElement.TryGetProperty("shared_ancients", out var shared) && shared.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in shared.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.String) continue;
                    string id = SourceIdToRuntimeIdLocal(a.GetString() ?? "");
                    if (!IsNeowAncientId(id)) ancientIds.Add(id);
                }
            }

            foreach (var item in ancientIds
                .Select(AncientDisplayItem)
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase))
            {
                _ancientItems.Add(item);
                AddItemAlias(item, item.RuntimeId);
                AddItemAlias(item, item.SourceId);
                AddItemAlias(item, item.Zh);
                AddItemAlias(item, item.En);
            }
            AncientBuilderComboBox.ItemsSource = _ancientItems;

            if (doc.RootElement.TryGetProperty("ancient_options", out var opts) && opts.ValueKind == JsonValueKind.Object)
            {
                var optionItems = new Dictionary<string, ItemAliasView>(StringComparer.OrdinalIgnoreCase);
                foreach (var ancient in opts.EnumerateObject())
                {
                    string ancientId = SourceIdToRuntimeIdLocal(ancient.Name);
                    if (IsNeowAncientId(ancientId)) continue; // Neow 是开局选项，不放到先古之民选项筛选里。
                    CollectAncientOptions(optionItems, ancientId, ancient.Value, "option_pool");
                    CollectAncientOptions(optionItems, ancientId, ancient.Value, "conditional_options");
                }
                foreach (var item in optionItems.Values
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase))
                {
                    _ancientOptionItems.Add(item);
                    AddItemAlias(item, item.RuntimeId);
                    AddItemAlias(item, item.Zh);
                    AddItemAlias(item, item.En);
                }
                AncientOptionBuilderComboBox.ItemsSource = _ancientOptionItems;
            }
        }
        catch (Exception ex)
        {
            AppendLog("加载先古下拉候选失败：" + ex.Message);
        }
    }

    private static bool IsNeowAncientId(string id)
        => id.Equals("Neow", StringComparison.OrdinalIgnoreCase)
            || id.Equals("NEOW", StringComparison.OrdinalIgnoreCase);

    private ItemAliasView AncientDisplayItem(string ancientId)
    {
        if (_itemsByRuntimeId.TryGetValue(ancientId, out var item))
            return new ItemAliasView(item.SourceId, item.RuntimeId, "ancient", item.Zh, item.En);
        return new ItemAliasView(ancientId, ancientId, "ancient", ancientId, ancientId);
    }

    private ItemAliasView AncientOptionDisplayItem(string ancientId, string optionId)
    {
        string term = ancientId + ":" + optionId;
        var ancient = AncientDisplayItem(ancientId);
        string ancientZh = ancient.DisplayName;
        string ancientEn = string.IsNullOrWhiteSpace(ancient.En) ? ancientId : ancient.En;

        string optionZh = optionId;
        string optionEn = optionId;
        if (_itemsByRuntimeId.TryGetValue(optionId, out var optionItem))
        {
            optionZh = optionItem.DisplayName;
            optionEn = string.IsNullOrWhiteSpace(optionItem.En) ? optionItem.RuntimeId : optionItem.En;
        }

        return new ItemAliasView(term, term, "ancient_option", ancientZh + "：" + optionZh, ancientEn + ": " + optionEn);
    }

    private void CollectAncientOptions(Dictionary<string, ItemAliasView> optionItems, string ancientId, JsonElement ancientObj, string arrayName)
    {
        if (!ancientObj.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string optionId = JsonStr(item, "option_id", JsonStr(item, "id", JsonStr(item, "class", "")));
            if (string.IsNullOrWhiteSpace(optionId)) optionId = JsonStr(item, "option", "");
            optionId = SourceIdToRuntimeIdLocal(optionId);
            if (string.IsNullOrWhiteSpace(optionId)) continue;
            string term = ancientId + ":" + optionId;
            if (!optionItems.ContainsKey(term)) optionItems[term] = AncientOptionDisplayItem(ancientId, optionId);
        }
    }

    private bool IsShopRelicRuntimeId(string runtimeId)
    {
        if (!_relicPoolMeta.TryGetValue(runtimeId, out var meta)) return false;
        if (meta.Rarity.Equals("Shop", StringComparison.OrdinalIgnoreCase)) return true;
        return meta.PoolKinds.Any(k => k.Equals("shop", StringComparison.OrdinalIgnoreCase))
            || meta.Pools.Any(p => p.Contains("Shop", StringComparison.OrdinalIgnoreCase));
    }

    private void LoadNeowRelicEffects()
    {
        _neowRelicEffects.Clear();
        _neowTemplatesById.Clear();
        try
        {
            string path = Path.Combine(_rootDir, "data", "neow_relic_effects.json");
            if (!File.Exists(path))
            {
                AppendLog("Neow relic effects not found: " + RollDataPaths.SafeRel(_rootDir, path));
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("relics", out var relics) || relics.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in relics.EnumerateObject())
            {
                var obj = prop.Value;
                string id = JsonStr(obj, "canonical_id", prop.Name);
                var effect = new NeowRelicEffectView
                {
                    CanonicalId = id,
                    SourceId = JsonStr(obj, "source_id", ""),
                    EffectKind = JsonStr(obj, "effect_kind", "plain_relic"),
                    Confidence = JsonStr(obj, "confidence", ""),
                    Notes = JsonStr(obj, "notes", ""),
                    SupportedInWpf = JsonBool(obj, "supported_in_wpf_tag_builder"),
                };
                if (obj.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.Object)
                {
                    effect.Zh = JsonStr(dn, "zhs", "");
                    effect.En = JsonStr(dn, "eng", "");
                }
                if (obj.TryGetProperty("process_templates", out var templates) && templates.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in templates.EnumerateArray())
                    {
                        var tv = new NeowRelicTemplateView
                        {
                            RelicId = id,
                            TemplateId = JsonStr(t, "template_id", ""),
                            OutputType = JsonStr(t, "output_type", ""),
                            SourceKey = JsonStr(t, "source_key", ""),
                        };
                        if (t.TryGetProperty("label", out var lab) && lab.ValueKind == JsonValueKind.Object)
                        {
                            tv.LabelZhs = JsonStr(lab, "zhs", "");
                            tv.LabelEng = JsonStr(lab, "eng", "");
                        }
                        if (t.TryGetProperty("match_modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in modes.EnumerateArray())
                                if (m.ValueKind == JsonValueKind.String) tv.MatchModes.Add(m.GetString() ?? "");
                        }
                        if (!string.IsNullOrWhiteSpace(tv.TemplateId))
                        {
                            effect.Templates.Add(tv);
                            _neowTemplatesById[tv.TemplateId] = tv;
                        }
                    }
                }
                _neowRelicEffects[id] = effect;
            }
            AppendLog($"Neow relic effects loaded: {_neowRelicEffects.Count} relics, {_neowTemplatesById.Count} templates.");
        }
        catch (Exception ex)
        {
            AppendLog("加载 Neow 遗物效果审计表失败：" + ex.Message);
        }
    }

    private static string JsonStr(JsonElement obj, string name, string fallback = "")
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? fallback)
            : fallback;
    }

    private static bool JsonBool(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    private void PopulateNeowRelicDropdowns()
    {
        _neowRelicItems.Clear();
        var ids = _neowRelicEffects.Count > 0
            ? _neowRelicEffects.Keys.ToList()
            : new List<string> { "ArcaneScroll", "BoomingConch", "FishingRod", "GoldenPearl", "Kaleidoscope", "LeadPaperweight", "LostCoffer", "MassiveScroll", "NeowsTorment", "NewLeaf", "PhialHolster", "PreciseScissors", "ScrollBoxes", "WingedBoots", "HeftyTablet", "LeafyPoultice", "SmallCapsule", "LargeCapsule" };
        foreach (var id in ids)
        {
            if (_itemsByRuntimeId.TryGetValue(id, out var item))
            {
                if (!_neowRelicItems.Any(x => x.RuntimeId.Equals(item.RuntimeId, StringComparison.OrdinalIgnoreCase)))
                    _neowRelicItems.Add(item);
            }
            else if (_neowRelicEffects.TryGetValue(id, out var e))
            {
                var fallback = new ItemAliasView(e.SourceId, e.CanonicalId, "relic", e.Zh, e.En);
                _neowRelicItems.Add(fallback);
                _itemsByRuntimeId[e.CanonicalId] = fallback;
                AddItemAlias(fallback, e.CanonicalId);
                AddItemAlias(fallback, e.SourceId);
                AddItemAlias(fallback, e.Zh);
                AddItemAlias(fallback, e.En);
            }
        }
        var sorted = _neowRelicItems.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _neowRelicBlacklistItems.Clear();
        foreach (var item in sorted) _neowRelicBlacklistItems.Add(item);
        var selectableRelics = sorted
            .Where(x => !x.RuntimeId.Equals("NeowsBones", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _neowRelicItems.Clear();
        foreach (var item in selectableRelics) _neowRelicItems.Add(item);

        // NeowsBones 是进入骨骰路线的起点，不应该作为骨骰结果再次出现。
        BonesRelicBuilderComboBox.ItemsSource = _neowRelicItems;

        // 直接 Neow 遗物路线代表“不走骨骰”，也不能把 NeowsBones 本身作为可选目标。
        DirectRelicBuilderComboBox.ItemsSource = selectableRelics;
        if (FinalNeowRelicComboBox is not null) FinalNeowRelicComboBox.ItemsSource = _neowRelicBlacklistItems;
    }

    private void PopulateFinalResultDropdowns()
    {
        if (!IsInitialized) return;
        _finalCardItems.Clear();
        _finalPotionItems.Clear();
        _finalCurseItems.Clear();
        _finalRelicItems.Clear();

        _finalCardItems.AddRange(_cardItems
            .Where(x => IsFinalCardRuntimeId(x.RuntimeId))
            .OrderBy(x => CardRaritySortKey(x.RuntimeId))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase));
        _finalPotionItems.AddRange(_potionItems
            .Where(x => IsFinalPotionRuntimeId(x.RuntimeId))
            .OrderBy(x => PotionRaritySortKey(x.RuntimeId))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase));
        _finalCurseItems.AddRange(FinalCurseRuntimeIds()
            .Select(id => _itemsByRuntimeId.TryGetValue(id, out var item) ? item : new ItemAliasView(id, id, "curse", id, id))
            .GroupBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));
        _finalRelicItems.AddRange(_relicItems
            .Where(x => IsOpeningRandomRelicRuntimeId(x.RuntimeId))
            .OrderBy(x => RelicRaritySortKey(x.RuntimeId))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RuntimeId, StringComparer.OrdinalIgnoreCase));

        FinalCardBuilderComboBox.ItemsSource = _finalCardItems;
        FinalPotionBuilderComboBox.ItemsSource = _finalPotionItems;
        FinalCurseBuilderComboBox.ItemsSource = _finalCurseItems;
        FinalRelicBuilderComboBox.ItemsSource = _finalRelicItems;
        if (_neowRelicBlacklistItems.Count > 0) FinalNeowRelicComboBox.ItemsSource = _neowRelicBlacklistItems;
    }

    private ItemAliasView? SelectedAliasFromCombo(ComboBox combo, params string[] categories)
    {
        if (combo.SelectedItem is ItemAliasView item) return item;
        string text = combo.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ResolveItemAlias(text, categories);
    }

    private void AddItemAliasByRuntime(string runtimeId, string aliasText)
    {
        if (_itemsByRuntimeId.TryGetValue(runtimeId, out var item)) AddItemAlias(item, aliasText);
    }

    private void AddItemAlias(ItemAliasView item, string alias)
    {
        string key = NormalizeLookup(alias);
        if (key.Length == 0) return;
        if (!_itemAliases.ContainsKey(key)) _itemAliases[key] = item;
    }

    private ItemAliasView? ResolveItemAlias(string input, params string[] categories)
    {
        string key = NormalizeLookup(Term.Normalize(input));
        if (key.Length == 0) return null;
        bool CategoryOk(ItemAliasView x)
        {
            if (categories.Length == 0) return true;
            foreach (var c in categories)
            {
                if (c.Equals("curse", StringComparison.OrdinalIgnoreCase))
                {
                    if (x.Category.Equals("curse", StringComparison.OrdinalIgnoreCase)) return true;
                    if (x.Category.Equals("card", StringComparison.OrdinalIgnoreCase) && IsKnownCurseRuntimeId(x.RuntimeId)) return true;
                    continue;
                }
                if (x.Category.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        if (_itemAliases.TryGetValue(key, out var exact) && CategoryOk(exact)) return exact;
        var matches = _itemsByRuntimeId.Values
            .Where(CategoryOk)
            .Where(x => NormalizeLookup(x.SourceId).Contains(key, StringComparison.OrdinalIgnoreCase)
                     || NormalizeLookup(x.RuntimeId).Contains(key, StringComparison.OrdinalIgnoreCase)
                     || NormalizeLookup(x.Zh).Contains(key, StringComparison.OrdinalIgnoreCase)
                     || NormalizeLookup(x.En).Contains(key, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool IsKnownCurseRuntimeId(string id)
        => BonesCursePoolRuntimeIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> FinalCurseRuntimeIds()
        => BonesCursePoolRuntimeIds.Concat(new[] { "Greed", "Injury" }).Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsFinalCurseRuntimeId(string id)
        => FinalCurseRuntimeIds().Contains(id, StringComparer.OrdinalIgnoreCase);

    private bool IsNeowRelicRuntimeId(string id)
        => _neowRelicEffects.ContainsKey(id) || _neowRelicBlacklistItems.Any(x => x.RuntimeId.Equals(id, StringComparison.OrdinalIgnoreCase));

    private bool IsFinalCardRuntimeId(string id)
    {
        if (id.Equals("NeowsFury", StringComparison.OrdinalIgnoreCase)) return true;
        if (!_cardPoolMeta.TryGetValue(id, out var meta)) return false;
        bool rarityOk = CardRarityEquals(id, "Common") || CardRarityEquals(id, "Uncommon") || CardRarityEquals(id, "Rare");
        if (!rarityOk) return false;
        return IsColorlessCard(id) || IsCharacterCard(id);
    }

    private bool IsFinalPotionRuntimeId(string id)
    {
        if (!_potionPoolMeta.TryGetValue(id, out var meta)) return true;
        string owner = CurrentSearchOwnerName();
        if (meta.PoolKinds.Any(k => k.Equals("shared", StringComparison.OrdinalIgnoreCase))) return true;
        if (meta.Owners.Count == 0) return true;
        return meta.Owners.Any(o => o.Equals(owner, StringComparison.OrdinalIgnoreCase));
    }

    private string PotionRarity(string runtimeId)
        => _potionPoolMeta.TryGetValue(runtimeId, out var meta) ? meta.Rarity : "";

    private int PotionRaritySortKey(string runtimeId)
    {
        string rarity = PotionRarity(runtimeId);
        if (rarity.Equals("Common", StringComparison.OrdinalIgnoreCase)) return 0;
        if (rarity.Equals("Uncommon", StringComparison.OrdinalIgnoreCase)) return 1;
        if (rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase)) return 2;
        return 9;
    }

    private string DisplayTerm(string term)
    {
        string normalized = Term.Normalize(term);
        if (_entityIndex.Loaded) return _entityIndex.DisplayText(normalized, "zhs");
        if (_itemsByRuntimeId.TryGetValue(normalized, out var item)) return item.DisplayText;
        var alias = ResolveItemAlias(normalized);
        return alias?.DisplayText ?? normalized;
    }

    private static string NormalizeLookup(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return "";
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch == '_' || ch == '-' || ch == '（' || ch == '）' || ch == '(' || ch == ')') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string SourceIdToRuntimeIdLocal(string sourceId)
    {
        var parts = Regex.Split(sourceId ?? "", "[_\\s.-]+").Where(p => p.Length > 0);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private string SearchFilterSummary()
    {
        var tags = BuildSearchRequirementTags().Where(t => t.CanRemove).ToList();
        return tags.Count == 0 ? "未设置筛选条件，将按范围枚举 seed。" : "已启用筛选：" + string.Join(" / ", tags.Select(t => t.Text));
    }

    private List<SearchRequirementTagView> BuildSearchRequirementTags()
    {
        var tags = new List<SearchRequirementTagView>();
        if (IsMultiplayerUiSelected())
        {
            AddTagHeader(tags, "运行模式");
            tags.Add(new SearchRequirementTagView($"多人：players={SelectedPlayersCount()}，{TargetPlayerSummary()}", "run_mode", "multiplayer", false));
        }
        string mode = SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant();
        if (mode == "final")
        {
            AddTagHeader(tags, "Neow 最终结果导向");
            tags.Add(new SearchRequirementTagView("模式：最终结果导向", "neow_mode", "final", false));
            AddTermTag(tags, "最终遗物", FinalRelicAnyTextBox, FinalRelicAllTextBox, FinalRelicBlacklistTextBox, "final_relic");
            AddTermTag(tags, "最终卡牌", FinalCardAnyTextBox, FinalCardAllTextBox, FinalCardBlacklistTextBox, "final_card");
            AddTermValues(tags, "最终诅咒：必须", FinalCurseAnyTextBox, "final_curse_any");
            AddTermValues(tags, "最终诅咒：禁止", FinalCurseBlacklistTextBox, "final_curse_blacklist");
            AddTermTag(tags, "最终药水", FinalPotionAnyTextBox, FinalPotionAllTextBox, FinalPotionBlacklistTextBox, "final_potion");
            AddTermValues(tags, "Neow 遗物：必须包含", FinalNeowRelicAnyTextBox, "final_neow_any");
            AddTermValues(tags, "Neow 遗物：不想要", FinalNeowRelicBlacklistTextBox, "final_neow_blacklist");
        }
        else if (mode == "process")
        {
            AddTagHeader(tags, "Neow 过程导向");
            tags.Add(new SearchRequirementTagView("模式：过程导向", "neow_mode", "process", false));
            string bonesReq = SelectedComboText(BonesRequirementComboBox, "yes").ToLowerInvariant();
            if (bonesReq == "yes")
            {
                tags.Add(new SearchRequirementTagView("骨骰路线：必须", "bones_route", "yes"));
                string relicMode = SelectedComboText(BonesRelicModeComboBox, "undirected").ToLowerInvariant();
                if (relicMode == "undirected" && !HasAnyTerms(BonesRelicAnyTextBox, BonesRelicAllTextBox, BonesRelicBlacklistTextBox))
                {
                    tags.Add(new SearchRequirementTagView("骨骰遗物：不定向", "bones_relic_mode", "undirected"));
                }
                else
                {
                    AddTermValues(tags, "骨骰遗物：必须包含", BonesRelicAllTextBox, "bones_relic_all");
                    AddTermValues(tags, "骨骰遗物：任意包含", BonesRelicAnyTextBox, "bones_relic_any");
                    AddTermValues(tags, "骨骰遗物：禁止", BonesRelicBlacklistTextBox, "bones_relic_blacklist");
                }
                string newLeafSelector = SelectedNewLeafSourceSelector();
                if (!newLeafSelector.Equals("starter_basic", StringComparison.OrdinalIgnoreCase))
                    tags.Add(new SearchRequirementTagView("新叶原始牌：" + NewLeafSelectorDisplay(newLeafSelector), "newleaf_source", newLeafSelector));
                AddTermValues(tags, "新叶变化：必须得到", NewLeafCardAllTextBox, "newleaf_card_all");
                AddTermValues(tags, "万花筒给牌：必须包含", KaleidoscopeGroup1CardAllTextBox, "kaleidoscope_card_all");
                AddTermValues(tags, "万花筒给牌：必须包含", KaleidoscopeGroup2CardAllTextBox, "kaleidoscope_card_all");
                AddTermValues(tags, "失物盒卡牌：必须包含", LostCofferCardAllTextBox, "lostcoffer_card_all");
                AddTermValues(tags, "失物盒药水：必须包含", LostCofferPotionAllTextBox, "lostcoffer_potion_all");
                AddDynamicTemplateTags(tags);
                AddTermValues(tags, "骨骰诅咒：必须", BonesCurseAnyTextBox, "bones_curse_any");
                AddTermValues(tags, "骨骰诅咒：禁止", BonesCurseBlacklistTextBox, "bones_curse_blacklist");
            }
            else if (bonesReq == "no")
            {
                tags.Add(new SearchRequirementTagView("骨骰路线：只看直接 Neow", "bones_route", "no"));
                AddTermValues(tags, "Neow 遗物/效果：必须包含", NeowAllTextBox, "direct_relic_all");
                AddDynamicTemplateTags(tags);
            }
        }

        if (HasAnyEventFilterText())
        {
            AddTagHeader(tags, "事件队列");
            tags.Add(new SearchRequirementTagView("事件：按条件表筛选默认可出现队列", "event_limit", SelectedSearchEventFilterLimit().ToString(), false));
            AddEventFilterTermTags(tags, "事件：必须包含", EventAllTextBox, "event_all");
            AddEventFilterTermTags(tags, "事件：排除", EventBlacklistTextBox, "event_blacklist");
        }

        if (ShopFilterEnabledCheckBox.IsChecked == true || HasAnyTerms(ShopRequireTextBox, ShopBlacklistTextBox, ShopExactTextBox))
        {
            AddTagHeader(tags, "商店专属遗物");
            AddTermValues(tags, "商店：第 N 个必须是", ShopExactTextBox, "shop_exact");
            AddTermValues(tags, "商店：前 N 个包含", ShopRequireTextBox, "shop_require");
            AddTermValues(tags, "商店：禁止", ShopBlacklistTextBox, "shop_blacklist");
        }
        if (AncientSearchEnabledCheckBox.IsChecked == true || HasAnyTerms(AncientRequireTextBox, AncientBlacklistTextBox, AncientOptionRequireTextBox, AncientOptionBlacklistTextBox) || SelectedDarvActFilter().Length > 0)
        {
            AddTagHeader(tags, "先古之民");
            AddTermValues(tags, "先古：需要", AncientRequireTextBox, "ancient_require");
            string darvAct = SelectedDarvActFilter();
            if (darvAct.Length > 0) tags.Add(new SearchRequirementTagView("先古：Darv 出现在 " + darvAct.ToUpperInvariant(), "ancient_darv_act", darvAct, false));
            AddTermValues(tags, "先古：不要", AncientBlacklistTextBox, "ancient_blacklist");
            AddTermValues(tags, "先古选项：需要", AncientOptionRequireTextBox, "ancient_option_require");
            AddTermValues(tags, "先古选项：不想要", AncientOptionBlacklistTextBox, "ancient_option_blacklist");
        }
        return tags;
    }

    private static string NewLeafSelectorDisplay(string selector) => selector switch
    {
        "source:LeadPaperweight:any" => "铅制镇纸给的无色卡",
        "source:Kaleidoscope:any" => "万花筒给的其它职业卡",
        "source:LostCoffer:any" => "失物盒选中的卡",
        "source:HeftyTablet:Injury" => "沉重石板给的 Injury",
        "source:NeowsTorment:NeowsFury" => "涅奥的苦痛给的 NeowsFury",
        "first_kaleidoscope_card" => "第一张万花筒卡",
        _ => "默认本职业基础牌",
    };

    private static void AddTagHeader(List<SearchRequirementTagView> tags, string title)
    {
        tags.Add(new SearchRequirementTagView("【" + title + "】", "header", title, false));
    }

    private void AddDynamicTemplateTags(List<SearchRequirementTagView> tags)
    {
        foreach (var kv in _dynamicTemplateTerms.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!_neowTemplatesById.TryGetValue(kv.Key, out var template)) continue;
            if (!_neowRelicEffects.TryGetValue(template.RelicId, out var effect)) continue;
            string label = TemplateDisplayLabel(effect, template) + "：必须包含";
            foreach (var group in kv.Value.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int count = group.Count();
                string suffix = count > 1 ? " ×" + count : "";
                tags.Add(new SearchRequirementTagView(label + " " + DisplayTerm(group.Key) + suffix, "dynamic_template:" + template.TemplateId, group.Key));
            }
        }
    }

    private void AddTermValues(List<SearchRequirementTagView> tags, string label, TextBox box, string kind)
    {
        var terms = SplitTermsAllowDuplicates(box.Text);
        foreach (var group in terms.GroupBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            int count = group.Count();
            string suffix = count > 1 ? " ×" + count : "";
            tags.Add(new SearchRequirementTagView(label + " " + DisplayTerm(group.Key) + suffix, kind, group.Key));
        }
    }

    private void AddEventFilterTermTags(List<SearchRequirementTagView> tags, string label, TextBox box, string kind)
    {
        foreach (var term in SplitTerms(box.Text))
        {
            var parsed = ParseEventQueueTermForDisplay(term);
            string act = parsed.ActNumber is int n ? $"Act{n}" : "任意 Act";
            int limit = parsed.Limit ?? SelectedSearchEventFilterLimit();
            tags.Add(new SearchRequirementTagView($"{label} [{act} 前 {limit}] {D(parsed.EventTerm)}", kind, term));
        }
    }

    private void AddTermTag(List<SearchRequirementTagView> tags, string label, TextBox anyBox, TextBox allBox, TextBox blacklistBox, string kindPrefix)
    {
        AddTermValues(tags, label + "：任意包含", anyBox, kindPrefix + "_any");
        AddTermValues(tags, label + "：必须包含", allBox, kindPrefix + "_all");
        AddTermValues(tags, label + "：禁止", blacklistBox, kindPrefix + "_blacklist");
    }

    private static void AddTermSummary(List<string> parts, string label, TextBox anyBox, TextBox allBox, TextBox blacklistBox)
    {
        if (HasAnyTerms(anyBox, allBox, blacklistBox)) parts.Add(label);
    }


    private bool HasNeowAdvancedTerms()
    {
        string mode = SelectedComboText(NeowFilterModeComboBox, "none").ToLowerInvariant();
        if (mode == "process")
        {
            return true;
        }
        if (mode == "final")
        {
            return HasAnyTerms(FinalRelicAnyTextBox, FinalRelicAllTextBox, FinalRelicBlacklistTextBox,
                FinalCardAnyTextBox, FinalCardAllTextBox, FinalCardBlacklistTextBox,
                FinalCurseAnyTextBox, FinalCurseBlacklistTextBox,
                FinalPotionAnyTextBox, FinalPotionAllTextBox, FinalPotionBlacklistTextBox,
                FinalNeowRelicAnyTextBox, FinalNeowRelicBlacklistTextBox);
        }
        return false;
    }

    private void WriteAncientConditionPresets(JsonObject ancient)
    {
        var conditions = EnsureObject(ancient, "conditions");
        conditions["pael_goopy_enchantable_defends_gte_3"] = AncientPaelGoopyCheckBox.IsChecked == true;
        conditions["pael_removable_cards_gte_5"] = AncientPaelRemovableCheckBox.IsChecked == true;
        conditions["pael_has_event_pet"] = AncientPaelHasEventPetCheckBox.IsChecked == true;
        conditions["orobas_touch_of_orobas_allowed"] = AncientOrobasTouchCheckBox.IsChecked == true;
        conditions["orobas_archaic_tooth_allowed"] = AncientOrobasToothCheckBox.IsChecked == true;
        conditions["tezcatara_has_basic_strike"] = AncientTezcataraBasicStrikeCheckBox.IsChecked == true;
        conditions["nonupeipe_swift_enchantable_cards_gte_4"] = AncientNonupeipeSwiftCheckBox.IsChecked == true;
        conditions["tanx_instinct_enchantable_cards_gte_3"] = AncientTanxInstinctCheckBox.IsChecked == true;
        conditions["darv_pandoras_box_allowed"] = AncientDarvPandoraCheckBox.IsChecked == true;
        string darvAct = SelectedDarvActFilter();
        conditions["darv_act_filter"] = string.IsNullOrWhiteSpace(darvAct) ? "any" : darvAct;
    }

    private void WriteAnalyzeAncientConditionPresets(JsonObject ancient)
    {
        var conditions = EnsureObject(ancient, "conditions");
        conditions["pael_goopy_enchantable_defends_gte_3"] = AnalyzeAncientPaelGoopyCheckBox.IsChecked == true;
        conditions["pael_removable_cards_gte_5"] = AnalyzeAncientPaelRemovableCheckBox.IsChecked == true;
        conditions["pael_has_event_pet"] = AnalyzeAncientPaelHasEventPetCheckBox.IsChecked == true;
        conditions["orobas_touch_of_orobas_allowed"] = AnalyzeAncientOrobasTouchCheckBox.IsChecked == true;
        conditions["orobas_archaic_tooth_allowed"] = AnalyzeAncientOrobasToothCheckBox.IsChecked == true;
        conditions["tezcatara_has_basic_strike"] = AnalyzeAncientTezcataraBasicStrikeCheckBox.IsChecked == true;
        conditions["nonupeipe_swift_enchantable_cards_gte_4"] = AnalyzeAncientNonupeipeSwiftCheckBox.IsChecked == true;
        conditions["tanx_instinct_enchantable_cards_gte_3"] = AnalyzeAncientTanxInstinctCheckBox.IsChecked == true;
        conditions["darv_pandoras_box_allowed"] = AnalyzeAncientDarvPandoraCheckBox.IsChecked == true;
        conditions["darv_act_filter"] = "any";
    }

    private void ApplyEventFilterText(JsonObject eventQueue)
    {
        var events = EnsureObject(eventQueue, "events");
        // v1.1.0-preview2: 事件筛选由条件表维护。每条 term 可保存自己的 Act 与前 N。
        events["whitelist_any"] = new JsonArray();
        events["whitelist_all"] = ToJsonArray(NormalizeEventFilterTermsForConfig(SplitTerms(EventAllTextBox.Text)));
        events["blacklist"] = ToJsonArray(NormalizeEventFilterTermsForConfig(SplitTerms(EventBlacklistTextBox.Text)));
    }

    private IEnumerable<string> NormalizeEventFilterTermsForConfig(IEnumerable<string> terms)
    {
        foreach (var raw in terms)
        {
            var parsed = ParseEventQueueTermForDisplay(raw);
            int limit = Math.Min(15, Math.Max(1, parsed.Limit ?? SelectedSearchEventFilterLimit()));
            string ev = Term.Normalize(parsed.EventTerm ?? "").Trim();
            if (ev.Length == 0) continue;
            if (parsed.ActNumber is int act && act >= 1 && act <= 3) yield return $"act{act}<={limit}:{ev}";
            else yield return $"n{limit}:{ev}";
        }
    }

    private IEnumerable<string> ApplySelectedEventActScope(IEnumerable<string> terms)
    {
        // Legacy helper kept for old call sites / old presets. New UI writes complete condition-table terms.
        string scope = SelectedComboText(EventActScopeComboBox, "any").Trim().ToLowerInvariant();
        if (scope == "any" || scope.Length == 0) return terms;
        if (!int.TryParse(scope, out int act) || act < 1 || act > 3) return terms;
        return terms.Select(t => ApplyEventActPrefix(t, act));
    }

    private static string ApplyEventActPrefix(string term, int act)
    {
        string trimmed = (term ?? "").Trim();
        var parsed = ParseEventQueueTermForDisplayStatic(trimmed);
        int limit = Math.Min(15, Math.Max(1, parsed.Limit ?? 10));
        return $"act{act}<={limit}:{parsed.EventTerm}";
    }

    private string SelectedEventActScopeDisplay()
    {
        string scope = SelectedComboText(EventActScopeComboBox, "any").Trim().ToLowerInvariant();
        return scope switch
        {
            "1" => "Act1",
            "2" => "Act2",
            "3" => "Act3",
            _ => "Act1/2/3",
        };
    }

    private static void ClearEventFilterText(JsonObject eventQueue)
    {
        var events = EnsureObject(eventQueue, "events");
        events["whitelist_any"] = new JsonArray();
        events["whitelist_all"] = new JsonArray();
        events["blacklist"] = new JsonArray();
    }

    private bool HasAnyEventFilterText() => SplitTerms(EventAllTextBox.Text).Count > 0 || SplitTerms(EventBlacklistTextBox.Text).Count > 0;

    private static List<string> SplitTerms(string? text)
    {
        return SplitTermsAllowDuplicates(text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> SplitTermsAllowDuplicates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return Regex.Split(text, @"[\r\n,，;；]")
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }

    private string SelectedCharacter() => SelectedComboText(CharacterComboBox, "IRONCLAD");

    private string SelectedGameRngVersion()
        => SelectedComboText(GameRngVersionComboBox, "sts2_0_107_xoshiro");

    private void SetGameRngVersionIfAvailable(string rngVersion)
    {
        if (string.IsNullOrWhiteSpace(rngVersion)) return;
        SetCombo(GameRngVersionComboBox, rngVersion.Trim());
    }


    private static string SelectedComboText(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string tag && !string.IsNullOrWhiteSpace(tag)) return tag.Trim();
            if (item.Content is string s && !string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        if (combo.SelectedItem is string ss && !string.IsNullOrWhiteSpace(ss)) return ss.Trim();
        return fallback;
    }

    private string FormatResult(OpeningResult result, SearchPlan plan, bool matched, bool includeDebug)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Seed: " + result.Seed);
        sb.AppendLine("角色: " + D(plan.Character) + " | net_id=" + plan.NetId + " | players_count=" + plan.PlayersCount + " | rng=" + GameRngVersions.ToConfig(plan.RngVersion));
        sb.AppendLine("筛选结果: " + (matched ? "命中" : "未命中"));
        sb.AppendLine();
        sb.AppendLine("【运行信息 / 当前视角】");
        foreach (var line in BuildRuntimeInfoSection(result, plan)) sb.AppendLine("- " + line);
        sb.AppendLine();
        sb.AppendLine("【Neow 开局三选项】");
        for (int i = 0; i < result.NeowOptions.Count; i++) sb.AppendLine($"{i + 1}. {result.NeowOptions[i]}");

        if (result.BonesRelics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【骨骰遗物】");
            foreach (var r in result.BonesRelics) sb.AppendLine("- " + r);
        }
        if (result.BonesCurses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【骨骰诅咒】");
            foreach (var c in result.BonesCurses.Distinct(StringComparer.OrdinalIgnoreCase)) sb.AppendLine("- " + c);
        }
        if (result.OpeningRoutes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【开局路线概览】");
            foreach (var route in result.OpeningRoutes.Take(16))
            {
                if (route.Kind.Equals("bones", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("- 骨骰路线: " + string.Join(" -> ", route.PickOrder));
                else if (!string.IsNullOrWhiteSpace(route.DirectRelic))
                    sb.AppendLine("- 直接选择: " + route.DirectRelic);
                if (!string.IsNullOrWhiteSpace(route.BonesCurse)) sb.AppendLine("  诅咒: " + D(route.BonesCurse ?? ""));
                if (route.RelicOpportunities.Count > 0)
                {
                    foreach (var ev in route.RelicOpportunities)
                        if (ev.Relics.Count > 0) sb.AppendLine("  随机遗物[" + ev.Source + "]: " + string.Join(" / ", ev.Relics));
                }
                else if (route.PredictedRelics.Count > 0) sb.AppendLine("  随机遗物: " + string.Join(" / ", route.PredictedRelics));
                if (route.Potions.Count > 0) sb.AppendLine("  药水: " + string.Join(" / ", route.Potions));
                foreach (var ev in route.CardOpportunities.Take(3))
                {
                    var cards = ev.Cards.Count > 0 ? string.Join(" / ", ev.Cards) : string.Join(" | ", ev.Options.Select(o => string.Join(" / ", o)));
                    if (!string.IsNullOrWhiteSpace(cards)) sb.AppendLine("  卡牌[" + ev.Source + "]: " + cards);
                }
            }
            if (result.OpeningRoutes.Count > 16) sb.AppendLine($"... 还有 {result.OpeningRoutes.Count - 16} 条路线未显示");
        }
        if (result.OpeningRoutes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【Neow 最终结果汇总】");
            foreach (var line in BuildNeowFinalResultSection(result, plan)) sb.AppendLine(line);
        }
        if (result.RelicQueues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【普通遗物序列（Common / Uncommon / Rare）】");
            foreach (var line in BuildRelicQueueSection(result, plan)) sb.AppendLine(line);
        }
        if (result.ShopRelics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【商店专属遗物序列（前 8 个）】");
            int i = 0;
            foreach (var r in result.ShopRelics.Take(Math.Max(1, plan.ShopLimit)))
            {
                i++;
                sb.AppendLine($"{i}. {D(r)}");
            }
        }
        if (result.Ancients.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【先古身份】");
            foreach (var a in result.Ancients) sb.AppendLine("- " + a);
        }
        if (result.AncientOptions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【先古选项】");
            foreach (var block in result.AncientOptions)
                sb.AppendLine("- " + block.AncientId + ": " + string.Join(" / ", block.Options));
        }
        if (result.EventQueues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【事件普通读取顺序】");
            foreach (var q in result.EventQueues)
            {
                sb.AppendLine($"Act{q.ActNumber} {q.ActId}:");
                if (plan.EventQueueShowFull && q.StartOffset > 0)
                    sb.AppendLine("  " + AncientPredictor.EventQueueOffsetNote(q, id => id));
                var readOrder = FilterEventQueueForDisplay(AncientPredictor.EventQueueReadOrder(q), q, plan)
                    .Take(Math.Max(1, plan.EventQueueLimit))
                    .ToList();
                for (int i = 0; i < readOrder.Count; i++)
                {
                    string hint = AncientPredictor.EventQueueHint(readOrder[i], q.ActId, plan);
                    sb.AppendLine($"  {i + 1}. {readOrder[i]}{hint}");
                }
            }
        }
        if (includeDebug)
        {
            sb.AppendLine();
            sb.AppendLine("【WPF Debug】");
            sb.AppendLine("RootDir=" + _rootDir);
            sb.AppendLine("Data loaded=" + (plan.Data is not null));
            if (plan.Data is not null)
                sb.AppendLine($"Event rules={plan.Data.EventAllowedRulesCount}, manual={plan.Data.EventAllowedRulesManualReviewCount}, hooks={plan.Data.ModifyNextEventHooksCount}");
        }
        return sb.ToString();
    }

    private async void RunSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_searchCts is not null) return;
        _searchCts = new CancellationTokenSource();
        SearchButton.IsEnabled = false;
        StopSearchButton.IsEnabled = true;
        SearchOutputBox.Clear();
        _searchHits.Clear();
        _selectedSearchSeed = "";
        SearchSelectedSeedBox.Clear();
        SearchSummaryText.Text = "筛种中，参数已锁定...";
        BottomStatusText.Text = "批量筛种运行中";
        AppendLog("Search started");

        try
        {
            // 同上：先在 UI 线程读取输入框/下拉框，后台只执行纯搜索。
            string filterSummary = SearchFilterSummary();
            AppendSearch(filterSummary + "\n\n");
            using var doc = BuildRuntimeConfigDocument(includeSearchFields: true);
            var config = doc.RootElement.Clone();

            await Task.Run(() => RunSearch(config, _searchCts.Token));
            if (_searchCts.IsCancellationRequested) AppendSearch("已停止。\n");
        }
        catch (Exception ex)
        {
            AppendSearch(ex + "\n");
            AppendLog("Search failed: " + ex.Message);
        }
        finally
        {
            _searchCts.Dispose();
            _searchCts = null;
            SearchButton.IsEnabled = true;
            StopSearchButton.IsEnabled = false;
            BottomStatusText.Text = "筛种结束";
        }
    }

    private void RunSearch(JsonElement config, CancellationToken token)
    {
        var plan = SearchPlan.FromConfig(config, _rootDir);
        long start = plan.Start;
        int maxResults = Math.Max(1, plan.MaxResults);
        string mode = plan.Mode;
        bool candidatePoolMode = IsCandidatePoolMode(mode);
        bool randomMode = IsRandomMode(mode);
        var candidateSeeds = candidatePoolMode ? LoadSelectedCandidatePoolSeeds(plan) : new List<string>();
        long end = randomMode ? plan.End : Math.Max(start, plan.End);
        long checkedCount = 0;
        int found = 0;

        long attemptsEnd = randomMode ? Math.Max(0, end) : end;
        int workerCount = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 12));
        if (candidatePoolMode)
        {
            AppendSearch($"搜索模式: 粗筛候选库 | seeds={candidateSeeds.Count} | max_results={maxResults} | workers={workerCount} | ascension_scarcity={plan.Root.Prop("player").Prop("ascension_scarcity").Bool(false)}\n");
        }
        else
        {
            AppendSearch(randomMode
                ? $"搜索模式: 随机十位种子 | attempts={attemptsEnd} | max_results={maxResults} | workers={workerCount} | ascension_scarcity={plan.Root.Prop("player").Prop("ascension_scarcity").Bool(false)}\n"
                : $"搜索范围: {start}..{end} | max_results={maxResults} | mode={mode} | workers={workerCount} | ascension_scarcity={plan.Root.Prop("player").Prop("ascension_scarcity").Bool(false)}\n");
        }

        long loopStart = candidatePoolMode ? 0 : (randomMode ? 0 : start);
        long loopEnd = candidatePoolMode ? candidateSeeds.Count : (randomMode ? attemptsEnd : end);
        if (loopEnd <= loopStart)
        {
            AppendSearch("完成：checked=0, found=0\n");
            UpdateSearchProgress(0, 0);
            return;
        }

        // v1.1.0-preview3：WPF 批量筛种改为多线程。
        // 旧版 WPF 搜索虽然外层放在 Task.Run 里，但实际 seed 循环是单线程；
        // RollCore 本身是纯计算路径，按 worker 复用 OpeningPredictor 可以显著减少等待。
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = workerCount,
            CancellationToken = token,
        };
        using var predictors = new ThreadLocal<OpeningPredictor>(() => new OpeningPredictor(plan));

        try
        {
            Parallel.For(loopStart, loopEnd, options, (i, state) =>
            {
                if (token.IsCancellationRequested || Volatile.Read(ref found) >= maxResults)
                {
                    state.Stop();
                    return;
                }

                long checkedNow = Interlocked.Increment(ref checkedCount);
                string seed = candidatePoolMode ? candidateSeeds[(int)i] : (randomMode ? SeedText.RandomSeed(10) : (IsFixedMode(mode) ? SeedText.ToFixedSeedText(i, 10) : SeedText.ToSeedText(i)));

                OpeningResult result;
                try
                {
                    result = predictors.Value!.Check(seed);
                }
                catch (Exception ex)
                {
                    if (checkedNow <= 3) AppendSearch($"seed error {seed}: {ex.Message}\n");
                    if (checkedNow % 5000 == 0) UpdateSearchProgress(checkedNow, Math.Min(Volatile.Read(ref found), maxResults));
                    return;
                }

                if (!plan.Matches(result))
                {
                    if (checkedNow % 5000 == 0) UpdateSearchProgress(checkedNow, Math.Min(Volatile.Read(ref found), maxResults));
                    return;
                }

                int foundNow = Interlocked.Increment(ref found);
                if (foundNow <= maxResults)
                {
                    string detail = FormatSearchHit(foundNow, checkedNow, result, plan);
                    AddSearchHit(foundNow, checkedNow, result, plan, detail);
                    UpdateSearchProgress(checkedNow, foundNow);
                }
                if (foundNow >= maxResults) state.Stop();
            });
        }
        catch (OperationCanceledException) { }

        long checkedFinal = Interlocked.Read(ref checkedCount);
        int foundFinal = Math.Min(Volatile.Read(ref found), maxResults);
        AppendSearch($"完成：checked={checkedFinal}, found={foundFinal}\n");
        if (foundFinal == 0)
            AppendSearch("没有命中。可以放宽筛选条件，或扩大搜索范围。\n");
        UpdateSearchProgress(checkedFinal, foundFinal);
    }

    private static bool IsFixedMode(string mode)
    {
        return mode.Equals("fixed", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("padded", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("fixed_sequential", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRandomMode(string mode)
    {
        return mode.Equals("random", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("random10", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("random_ten", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCandidatePoolMode(string mode)
        => mode.Equals("candidate_pool", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("pool", StringComparison.OrdinalIgnoreCase);

    private string SelectedCandidatePoolId()
    {
        if (CandidatePoolComboBox.SelectedItem is CandidateSeedPoolView view) return view.Id;
        if (CandidatePoolsDataGrid.SelectedItem is CandidateSeedPoolView selected) return selected.Id;
        return "";
    }

    private List<string> LoadSelectedCandidatePoolSeeds(SearchPlan plan)
    {
        string id = plan.Root.Prop("seed_generation").Prop("candidate_pool_id").Str("");
        if (_candidatePoolStore is null || string.IsNullOrWhiteSpace(id)) return new List<string>();
        var pool = _candidatePoolStore.GetById(id);
        return pool?.Seeds.Select(s => s.Seed).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
    }

    private string D(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string key = id.Trim();
        if (_itemsByRuntimeId.TryGetValue(key, out var item))
        {
            string name = item.DisplayName;
            if (!string.IsNullOrWhiteSpace(name) && !name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return name + "（" + key + "）";
        }
        return key switch
        {
            "Overgrowth" => "繁茂之地（Overgrowth）",
            "Underdocks" => "暗港（Underdocks）",
            "Hive" => "蜂巢（Hive）",
            "Glory" => "荣光之地（Glory）",
            "Ironclad" or "IRONCLAD" => "铁甲战士（Ironclad）",
            "Silent" or "SILENT" => "静默猎手（Silent）",
            "Defect" or "DEFECT" => "故障机器人（Defect）",
            "Necrobinder" or "NECROBINDER" => "亡灵契约师（Necrobinder）",
            "Regent" or "REGENT" => "储君（Regent）",
            "NeowsBones" => "涅奥骨骰（NeowsBones）",
            "LargeCapsule" => "巨大扭蛋（LargeCapsule）",
            "SmallCapsule" => "小型扭蛋（SmallCapsule）",
            "LostCoffer" => "失物盒（LostCoffer）",
            "Kaleidoscope" => "万花筒（Kaleidoscope）",
            "ScrollBoxes" => "卷轴箱（ScrollBoxes）",
            "LeafyPoultice" => "树叶药膏（LeafyPoultice）",
            "NewLeaf" => "新叶（NewLeaf）",
            "CursedPearl" => "诅咒珍珠（CursedPearl）",
            "HeftyTablet" => "沉重石板（HeftyTablet）",
            "NeowsTorment" => "涅奥的苦痛（NeowsTorment）",
            "LavaRock" => "熔岩石（LavaRock）",
            _ => key,
        };
    }

    private string DMany(IEnumerable<string> ids, int take = int.MaxValue)
        => string.Join(" / ", ids.Where(x => !string.IsNullOrWhiteSpace(x)).Take(take).Select(D));

    private string DManyCount(IEnumerable<string> ids, int take = int.MaxValue)
    {
        var ordered = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var groups = new List<(string Id, int Count)>();
        foreach (var id in ordered)
        {
            var index = groups.FindIndex(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) groups[index] = (groups[index].Id, groups[index].Count + 1);
            else groups.Add((id, 1));
        }
        var parts = groups.Take(take)
            .Select(g => D(g.Id) + (g.Count > 1 ? $" ×{g.Count}" : ""))
            .ToList();
        if (groups.Count > take) parts.Add($"… 还有 {groups.Count - take} 种");
        return string.Join(" / ", parts);
    }


    private IEnumerable<ResultCardView> BuildResultCards(OpeningResult result, SearchPlan plan, bool matched, bool includeHitExplanation = false)
    {
        if (includeHitExplanation)
        {
            var explanation = BuildSearchHitExplanationLineViews(result, plan).ToList();
            if (explanation.Count > 0)
                yield return new ResultCardView { Title = "命中解释", Badge = "Hit", Lines = new ResultLineList(explanation) };
        }

        yield return new ResultCardView
        {
            Title = "运行信息 / 当前视角",
            Badge = matched ? "命中" : "未命中",
            Lines = BuildRuntimeInfoSection(result, plan).ToList()
        };

        var mapBossLines = ShowMapBossInSingleSeed(plan) ? BuildMapBossSection(result, ShowDetailedMapBossInSingleSeed(plan)).ToList() : new List<string>();
        if (mapBossLines.Count > 0)
            yield return new ResultCardView { Title = ShowDetailedMapBossInSingleSeed(plan) ? "地图 / Boss 预测（详细）" : "地图 / Boss 预测（简易）", Badge = "Map", Lines = mapBossLines.ToList() };

        var finalLines = BuildNeowFinalResultSection(result, plan).ToList();
        if (finalLines.Count > 0)
            yield return new ResultCardView { Title = "Neow 最终结果汇总", Badge = plan.NeowFinalFilter.HasFilter ? "Final 命中" : "Final", Lines = finalLines.ToList() };

        var relicQueueLines = BuildRelicQueueSection(result, plan).ToList();
        if (relicQueueLines.Count > 0)
            yield return new ResultCardView { Title = "普通遗物序列", Badge = "Relic", Lines = relicQueueLines.ToList() };

        var shopLines = BuildShopSearchSection(result, plan).ToList();
        if (shopLines.Count > 0)
            yield return new ResultCardView { Title = "商店专属遗物序列（前 8 个）", Badge = "Shop", Lines = shopLines.ToList() };

        var ancientLines = BuildAncientSearchSection(result, plan).ToList();
        if (ancientLines.Count > 0)
            yield return new ResultCardView { Title = "先古之民", Badge = "Ancient", Lines = ancientLines.ToList() };

        var eventLines = BuildEventSearchLineViews(result, plan);
        if (eventLines.Count > 0)
            yield return new ResultCardView { Title = "事件队列", Badge = "Event", Lines = eventLines };
    }

    private static void ReplaceCards(ObservableCollection<ResultCardView> target, IEnumerable<ResultCardView> cards)
    {
        target.Clear();
        foreach (var c in cards) target.Add(c);
    }

    private string FormatSearchHit(int index, long checkedCount, OpeningResult result, SearchPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#{index} seed={result.Seed} | checked={checkedCount}");
        sb.AppendLine("命中摘要: " + BuildSearchHitSummary(result, plan));
        sb.AppendLine(new string('=', 72));

        AppendSection(sb, "命中解释", BuildSearchHitExplanationLineViews(result, plan).Select(x => x.Text));
        AppendSection(sb, "地图 / Boss 预测", BuildMapBossSection(result));
        AppendSection(sb, "Neow 开局", BuildNeowSearchSection(result));
        AppendSection(sb, "卡牌 / 药水 / 随机奖励", BuildRewardSearchSection(result));
        AppendSection(sb, "Neow 最终结果汇总", BuildNeowFinalResultSection(result, plan));
        AppendSection(sb, "商店专属遗物序列（前 8 个）", BuildShopSearchSection(result, plan));
        AppendSection(sb, "先古之民", BuildAncientSearchSection(result, plan));
        AppendSection(sb, "事件队列", BuildEventSearchSection(result, plan));

        sb.AppendLine(new string('-', 72));
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines)
    {
        var list = lines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine("【" + title + "】");
        foreach (var line in list) sb.AppendLine(line);
    }

    private static bool ShowMapBossInSingleSeed(SearchPlan plan)
        => plan.Root.Prop("single_seed").Prop("show_map_boss").Bool(false);

    private static bool ShowDetailedMapBossInSingleSeed(SearchPlan plan)
        => plan.Root.Prop("single_seed").Prop("map_boss_detail").Bool(false);

    private static bool ShowRelicSequenceInSingleSeed(SearchPlan plan)
        => plan.Root.Prop("single_seed").Prop("show_relic_sequence").Bool(false);

    private IEnumerable<string> BuildMapBossSection(OpeningResult result, bool detailed = true)
    {
        if (result.MapBosses.Count == 0) yield break;
        foreach (var block in result.MapBosses)
        {
            yield return $"Act{block.ActNumber}: {D(block.ActId)}";
            var bosses = block.BossIds.Count > 0 ? block.BossIds : new List<string> { block.BossId };
            yield return "  Boss: " + DMany(bosses.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (detailed && block.NormalEncounters.Count > 0)
            {
                yield return "  怪物队列:";
                int i = 0;
                foreach (var enc in block.NormalEncounters) yield return $"    {++i}. {D(enc)}";
            }
            if (detailed && block.EliteEncounters.Count > 0)
            {
                yield return "  精英队列:";
                int i = 0;
                foreach (var enc in block.EliteEncounters) yield return $"    {++i}. {D(enc)}";
            }
        }
    }

    private IEnumerable<string> BuildRuntimeInfoSection(OpeningResult result, SearchPlan plan)
    {
        yield return "Seed: " + result.Seed;
        yield return "角色: " + D(plan.Character) + " | net_id=" + plan.NetId + " | 玩家数=" + plan.PlayersCount + " | RNG=" + GameRngVersions.ToConfig(plan.RngVersion);
        int ascension = plan.Root.Prop("player").Prop("ascension").Int(0);
        bool scarcity = ascension >= 7;
        yield return $"进阶等级: A{ascension} | 稀有卡概率档: " + (scarcity ? "A7-A10 高进阶档（Rare 概率约 1.49%）" : "A0-A6 普通档（Rare 概率约 3%）");
        string runMode = plan.Root.Prop("run_mode").Str(plan.PlayersCount > 1 ? "multiplayer" : "singleplayer") ?? "singleplayer";
        yield return "运行模式: " + (runMode.Equals("multiplayer", StringComparison.OrdinalIgnoreCase) ? "多人" : "单人");
        string profileMode = plan.Root.Prop("unlock_profile").Prop("mode").Str("all_unlocked") ?? "all_unlocked";
        yield return "解锁档案: " + (profileMode.Equals("profile", StringComparison.OrdinalIgnoreCase) ? "使用 progress.save 导入档案" : "全解锁 / 默认档案");
        if (plan.PlayersOrder.Count > 1)
        {
            var targetIndex = plan.PlayersOrder.FindIndex(p => p.NetId.Equals(plan.TargetNetIdText, StringComparison.OrdinalIgnoreCase));
            if (targetIndex >= 0)
                yield return $"当前目标: slot index={targetIndex} · Lobby 第 {targetIndex + 1} 位 · {plan.PlayersOrder[targetIndex].Name} · {D(plan.PlayersOrder[targetIndex].Character)} · net_id={plan.PlayersOrder[targetIndex].NetId}";
            else
                yield return "当前目标: net_id=" + plan.TargetNetIdText + "（未在玩家列表中找到同名项）";
            yield return "Lobby 顺序: " + string.Join(" | ", plan.PlayersOrder.Select((p, i) => $"{i + 1}.{p.Name}:{D(p.Character)}:{p.NetId}"));
        }
        yield return "Neow: " + (result.NeowOptions.Count > 0 ? DMany(result.NeowOptions) : "无");
    }

    private IEnumerable<string> BuildNeowSearchSection(OpeningResult result)
    {
        if (result.NeowOptions.Count > 0) yield return "初始选项: " + DMany(result.NeowOptions);
        if (result.BonesRelics.Count > 0) yield return "骨骰遗物: " + DMany(result.BonesRelics);
        if (result.BonesCurses.Count > 0) yield return "骨骰诅咒: " + DMany(result.BonesCurses.Distinct(StringComparer.OrdinalIgnoreCase));
        if (result.OpeningRoutes.Count > 0)
        {
            int shown = 0;
            foreach (var route in result.OpeningRoutes.Take(8))
            {
                shown++;
                if (route.Kind.Equals("bones", StringComparison.OrdinalIgnoreCase))
                    yield return $"路线{shown}: 骨骰 " + string.Join(" -> ", route.PickOrder.Select(D));
                else if (!string.IsNullOrWhiteSpace(route.DirectRelic))
                    yield return $"路线{shown}: 直接选择 " + D(route.DirectRelic ?? "");
                if (!string.IsNullOrWhiteSpace(route.BonesCurse)) yield return "  诅咒: " + D(route.BonesCurse ?? "");
                if (route.RelicOpportunities.Count > 0)
                {
                    foreach (var ev in route.RelicOpportunities)
                        if (ev.Relics.Count > 0) yield return "  随机遗物[" + D(ev.Source) + "]: " + DMany(ev.Relics, 6);
                }
                else if (route.PredictedRelics.Count > 0) yield return "  随机遗物: " + DMany(route.PredictedRelics, 6);
                if (route.Potions.Count > 0) yield return "  药水: " + DMany(route.Potions, 6);
            }
            if (result.OpeningRoutes.Count > 8) yield return $"... 还有 {result.OpeningRoutes.Count - 8} 条路线未展开";
        }
    }

    private IEnumerable<string> BuildRouteDetailSection(OpeningResult result)
    {
        if (result.OpeningRoutes.Count == 0) yield break;
        int routeNo = 0;
        foreach (var route in result.OpeningRoutes.Take(6))
        {
            routeNo++;
            yield return RouteDisplayName(route, routeNo);
            foreach (var ev in route.RelicOpportunities)
            {
                if (ev.Relics.Count > 0)
                    yield return $"  遗物来源 {D(ev.Source)}" + (string.IsNullOrWhiteSpace(ev.Method) ? "" : $" / {ev.Method}") + ": " + DMany(ev.Relics);
            }
            foreach (var ev in route.PotionOpportunities)
            {
                if (ev.Potions.Count > 0)
                    yield return $"  药水来源 {D(ev.Source)}: " + DMany(ev.Potions);
            }
            foreach (var ev in route.CardOpportunities)
            {
                string prefix = $"  卡牌来源 {D(ev.Source)}" + (string.IsNullOrWhiteSpace(ev.Method) ? "" : $" / {ev.Method}") + ": ";
                if (ev.Type.Equals("bundle_choice", StringComparison.OrdinalIgnoreCase) && ev.Options.Count > 0)
                {
                    int i = 0;
                    foreach (var bundle in ev.Options)
                    {
                        i++;
                        yield return prefix + $"卡包{i} = " + DMany(bundle);
                    }
                }
                else if (ev.Type.Equals("choice_group", StringComparison.OrdinalIgnoreCase))
                {
                    yield return prefix + "可选组 = " + DMany(ev.Cards);
                }
                else if (ev.Cards.Count > 0)
                {
                    yield return prefix + DMany(ev.Cards);
                }
            }
            var fixedNotes = FixedEffectNotesForRoute(route).ToList();
            foreach (var note in fixedNotes) yield return "  固定效果: " + note;
        }
        if (result.OpeningRoutes.Count > 6) yield return $"... 还有 {result.OpeningRoutes.Count - 6} 条路线未展开。";
    }

    private IEnumerable<string> FixedEffectNotesForRoute(OpeningRoute route)
    {
        var relics = InitialRelicsForDisplay(route, includeNeowsBonesMarker: true);
        if (relics.Any(x => x.Equals("LargeCapsule", StringComparison.OrdinalIgnoreCase))) yield return "巨大扭蛋固定加入 Strike + Defend（不作为结果导向卡牌筛选项）";
        if (relics.Any(x => x.Equals("NeowsTorment", StringComparison.OrdinalIgnoreCase))) yield return "涅奥的苦痛固定加入 NeowsFury";
        if (relics.Any(x => x.Equals("CursedPearl", StringComparison.OrdinalIgnoreCase))) yield return "诅咒珍珠固定加入 Greed";
        if (relics.Any(x => x.Equals("HeftyTablet", StringComparison.OrdinalIgnoreCase))) yield return "沉重石板固定加入 Injury";
        if (relics.Any(x => x.Equals("LavaRock", StringComparison.OrdinalIgnoreCase))) yield return "熔岩石为 Act1 Boss 后奖励修改器，当前不混入开局即时随机遗物";
    }

    private IEnumerable<string> BuildRewardSearchSection(OpeningResult result)
    {
        if (result.PredictedRelicSources.Count > 0) yield return "随机遗物来源: " + DMany(result.PredictedRelicSources, 10);
        if (result.PotionSources.Count > 0) yield return "药水来源: " + DMany(result.PotionSources, 10);
        foreach (var cr in result.CardSourceRoutes.Take(8))
            yield return "卡牌机会[" + string.Join(",", cr.Categories) + "]: " + DMany(cr.Sources, 8);
    }


    private IEnumerable<string> BuildNeowFinalResultSection(OpeningResult result, SearchPlan plan)
    {
        if (result.OpeningRoutes.Count == 0) yield break;
        var selectedRoutes = result.OpeningRoutes
            .Select((route, index) => new { route, index })
            .Where(x => !plan.NeowFinalFilter.HasFilter || RouteMatchesFinalForDisplay(x.route, plan.NeowFinalFilter))
            .Take(6)
            .ToList();

        if (selectedRoutes.Count == 0 && plan.NeowFinalFilter.HasFilter)
        {
            yield return "没有找到符合最终结果筛选的 Neow 路线；下面显示前几条候选路线用于核对。";
            selectedRoutes = result.OpeningRoutes.Select((route, index) => new { route, index }).Take(3).ToList();
        }
        else if (plan.NeowFinalFilter.HasFilter)
        {
            yield return $"符合最终结果筛选的路线数：至少 {selectedRoutes.Count} 条（最多显示 6 条）。";
        }

        foreach (var item in selectedRoutes)
        {
            var route = item.route;
            string label = RouteDisplayName(route, item.index + 1);
            var relics = FinalOrdinaryRelicsForDisplay(route);
            var fixedCards = FinalFixedCardsForDisplay(route);
            var cardChoiceLines = FinalCardChoiceLines(route).ToList();
            var potions = FinalPotionsForDisplay(route);
            var curses = FinalCursesForDisplay(route);
            yield return label;
            if (relics.Count > 0)
            {
                yield return "  最终普通遗物: " + DManyCount(relics, 8);
                foreach (var line in FinalRelicSourceLines(route).Take(4)) yield return "    " + line;
            }
            if (fixedCards.Count > 0)
            {
                yield return "  确定卡牌/变化结果: " + DManyCount(fixedCards, 8);
            }
            if (cardChoiceLines.Count > 0)
            {
                yield return "  可选卡牌奖励:";
                foreach (var line in cardChoiceLines.Take(8)) yield return "    " + line;
            }
            if (potions.Count > 0)
            {
                yield return "  最终药水: " + DManyCount(potions, 8);
                foreach (var line in FinalPotionSourceLines(route).Take(4)) yield return "    " + line;
            }
            if (curses.Count > 0)
            {
                yield return "  最终诅咒: " + DManyCount(curses, 6);
                foreach (var line in FinalCurseSourceLines(route).Take(4)) yield return "    " + line;
            }
            foreach (var note in FixedEffectNotesForRoute(route).Take(4)) yield return "  固定/延后说明: " + note;
            if (relics.Count == 0 && fixedCards.Count == 0 && cardChoiceLines.Count == 0 && potions.Count == 0 && curses.Count == 0) yield return "  无额外最终收益。";
        }
    }

    private string RouteDisplayName(OpeningRoute route, int index)
    {
        if (route.Kind.Equals("bones", StringComparison.OrdinalIgnoreCase))
            return $"路线{index}: 骨骰 " + (route.PickOrder.Count > 0 ? string.Join(" -> ", route.PickOrder.Select(D)) : "未记录遗物");
        return $"路线{index}: 直接 Neow " + (!string.IsNullOrWhiteSpace(route.DirectRelic) ? D(route.DirectRelic) : "未记录遗物");
    }

    private IEnumerable<string> FinalRelicSourceLines(OpeningRoute route)
    {
        foreach (var ev in route.RelicOpportunities)
        {
            foreach (var relic in ev.Relics.Distinct(StringComparer.OrdinalIgnoreCase))
                yield return $"{D(relic)} ← {D(ev.Source)}" + (string.IsNullOrWhiteSpace(ev.Method) ? "" : $" / {ev.Method}");
        }
    }

    private IEnumerable<string> FinalPotionSourceLines(OpeningRoute route)
    {
        foreach (var ev in route.PotionOpportunities)
        {
            foreach (var potion in ev.Potions.Distinct(StringComparer.OrdinalIgnoreCase))
                yield return $"{D(potion)} ← {D(ev.Source)}";
        }
    }

    private IEnumerable<string> FinalCurseSourceLines(OpeningRoute route)
    {
        if (!string.IsNullOrWhiteSpace(route.BonesCurse)) yield return D(route.BonesCurse) + " ← " + D("NeowsBones");
        var relics = InitialRelicsForDisplay(route, includeNeowsBonesMarker: true);
        if (relics.Any(r => r.Equals("CursedPearl", StringComparison.OrdinalIgnoreCase))) yield return D("Greed") + " ← " + D("CursedPearl") + " 固定";
        if (relics.Any(r => r.Equals("HeftyTablet", StringComparison.OrdinalIgnoreCase))) yield return D("Injury") + " ← " + D("HeftyTablet") + " 固定";
    }

    private static List<string> FinalFixedCardsForDisplay(OpeningRoute route)
    {
        return route.CardOpportunities
            .Where(ev => ev.Type.Equals("fixed", StringComparison.OrdinalIgnoreCase))
            .SelectMany(ev => ev.Cards)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private IEnumerable<string> FinalCardChoiceLines(OpeningRoute route)
    {
        var groupCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in route.CardOpportunities)
        {
            if (ev.Type.Equals("fixed", StringComparison.OrdinalIgnoreCase)) continue;
            if (ev.Type.Equals("bundle_choice", StringComparison.OrdinalIgnoreCase) && ev.Options.Count > 0)
            {
                int i = 0;
                foreach (var bundle in ev.Options)
                {
                    i++;
                    var suffix = ev.Source.Equals("ScrollBoxes", StringComparison.OrdinalIgnoreCase)
                        ? "（只能选一个卡包）"
                        : "";
                    yield return $"{D(ev.Source)} 卡包{i}{suffix}: " + DManyCount(bundle, 8);
                }
                continue;
            }

            if (ev.Type.Equals("choice_group", StringComparison.OrdinalIgnoreCase) && ev.Cards.Count > 0)
            {
                groupCounters.TryGetValue(ev.Source, out var index);
                index++;
                groupCounters[ev.Source] = index;

                string label;
                if (ev.Source.Equals("Kaleidoscope", StringComparison.OrdinalIgnoreCase)) label = $"{D(ev.Source)} 第{index}组（每组选1张）";
                else if (ev.Source.Equals("LostCoffer", StringComparison.OrdinalIgnoreCase)) label = $"{D(ev.Source)} 三选一";
                else if (ev.Source.Equals("HeftyTablet", StringComparison.OrdinalIgnoreCase)) label = $"{D(ev.Source)} 稀有卡三选一";
                else if (ev.Source.Equals("LeadPaperweight", StringComparison.OrdinalIgnoreCase)) label = $"{D(ev.Source)} 无色卡三选一";
                else label = $"{D(ev.Source)} 可选组{index}";
                yield return label + ": " + DManyCount(ev.Cards, 8);
            }
        }
    }

    private static bool RouteMatchesFinalForDisplay(OpeningRoute route, NeowFinalFilter filter)
    {
        if (filter.NeowRelicBlacklist.Count > 0)
        {
            var neowRelics = InitialRelicsForDisplay(route, includeNeowsBonesMarker: true);
            if (neowRelics.Any(r => filter.NeowRelicBlacklist.Any(t => Term.ItemMatches(r, t)))) return false;
        }
        if (filter.Relics.HasTerms && !filter.Relics.Match(FinalOrdinaryRelicsForDisplay(route))) return false;
        if (filter.Cards.HasTerms && !filter.Cards.Match(FinalCardsForDisplay(route))) return false;
        if (filter.Potions.HasTerms && !filter.Potions.Match(FinalPotionsForDisplay(route))) return false;
        var curses = FinalCursesForDisplay(route);
        if (filter.CurseAny.Count > 0 && !filter.CurseAny.Any(t => curses.Any(c => Term.ItemMatches(c, t)))) return false;
        if (filter.CurseBlacklist.Count > 0 && curses.Any(c => filter.CurseBlacklist.Any(t => Term.ItemMatches(c, t)))) return false;
        return true;
    }

    private static List<string> InitialRelicsForDisplay(OpeningRoute route, bool includeNeowsBonesMarker)
    {
        var xs = new List<string>();
        if (route.Kind.Equals("bones", StringComparison.OrdinalIgnoreCase))
        {
            if (includeNeowsBonesMarker) xs.Add("NeowsBones");
            xs.AddRange(route.PickOrder);
        }
        else if (!string.IsNullOrWhiteSpace(route.DirectRelic)) xs.Add(route.DirectRelic!);
        return xs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FinalOrdinaryRelicsForDisplay(OpeningRoute route)
    {
        var xs = new List<string>();
        xs.AddRange(route.PredictedRelics);
        xs.AddRange(route.RelicOpportunities.SelectMany(x => x.Relics));
        return xs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FinalPotionsForDisplay(OpeningRoute route)
    {
        var xs = new List<string>();
        xs.AddRange(route.Potions);
        xs.AddRange(route.PotionOpportunities.SelectMany(x => x.Potions));
        return xs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FinalCursesForDisplay(OpeningRoute route)
    {
        var curses = new List<string>();
        if (!string.IsNullOrWhiteSpace(route.BonesCurse)) curses.Add(route.BonesCurse!);
        var neowRelics = InitialRelicsForDisplay(route, includeNeowsBonesMarker: true);
        if (neowRelics.Any(r => r.Equals("CursedPearl", StringComparison.OrdinalIgnoreCase))) curses.Add("Greed");
        if (neowRelics.Any(r => r.Equals("HeftyTablet", StringComparison.OrdinalIgnoreCase))) curses.Add("Injury");
        return curses.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> FinalCardsForDisplay(OpeningRoute route)
    {
        var xs = new List<string>();
        foreach (var ev in route.CardOpportunities)
        {
            xs.AddRange(ev.Cards);
            xs.AddRange(ev.Options.SelectMany(o => o));
        }
        return xs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private IEnumerable<string> BuildRelicQueueSection(OpeningResult result, SearchPlan plan)
    {
        if (!plan.RelicQueueShow || result.RelicQueues.Count == 0) yield break;
        int limit = Math.Max(1, plan.RelicQueueLimit);
        foreach (var pair in new[]
        {
            (Key: "Common", Label: "普通 Common"),
            (Key: "Uncommon", Label: "罕见 Uncommon"),
            (Key: "Rare", Label: "稀有 Rare"),
        })
        {
            if (!result.RelicQueues.TryGetValue(pair.Key, out var queue) || queue.Count == 0) continue;
            yield return pair.Label + ": " + DMany(queue, limit);
        }
    }

    private IEnumerable<string> BuildShopSearchSection(OpeningResult result, SearchPlan plan)
    {
        if (result.ShopRelics.Count <= 0) yield break;
        int limit = Math.Max(1, plan.ShopLimit);
        int i = 0;
        foreach (var r in result.ShopRelics.Take(limit))
        {
            i++;
            yield return $"{i}. {D(r)}";
        }
    }

    private IEnumerable<string> BuildAncientSearchSection(OpeningResult result, SearchPlan plan)
    {
        if (!plan.AncientEnabled && result.Ancients.Count == 0 && result.AncientOptions.Count == 0) yield break;
        if (result.Ancients.Count > 0) yield return "先古: " + DMany(result.Ancients);
        if (result.AncientOptions.Count > 0)
        {
            yield return "可用选项:";
            foreach (var block in result.AncientOptions.Take(12))
                yield return $"  Act{block.ActNumber}: {D(block.AncientId)}: " + DMany(block.Options);
        }
        else if (plan.AncientEnabled)
        {
            yield return "未返回先古选项；可检查当前状态预设或 RollCore 先古配置。";
        }
    }

    private IEnumerable<string> BuildAncientPresetSummary(SearchPlan plan)
    {
        var c = plan.Root.Prop("ancient").Prop("conditions");
        if (c is null || c.Value.ValueKind == JsonValueKind.Undefined || c.Value.ValueKind == JsonValueKind.Null) yield break;
        if (c.Prop("pael_goopy_enchantable_defends_gte_3").Bool(false)) yield return "Pael: Gooopy 可附魔防御牌 ≥ 3";
        if (c.Prop("pael_removable_cards_gte_5").Bool(false)) yield return "Pael: 可移除牌 ≥ 5";
        if (c.Prop("pael_has_event_pet").Bool(false)) yield return "Pael: 已有事件宠物";
        if (c.Prop("orobas_touch_of_orobas_allowed").Bool(false)) yield return "Orobas: TouchOfOrobas 条件满足";
        if (c.Prop("orobas_archaic_tooth_allowed").Bool(false)) yield return "Orobas: ArchaicTooth 条件满足";
        if (c.Prop("tezcatara_has_basic_strike").Bool(false)) yield return "Tezcatara: 还有 Basic Strike";
        if (c.Prop("nonupeipe_swift_enchantable_cards_gte_4").Bool(false)) yield return "Nonupeipe: Swift 可附魔牌 ≥ 4";
        if (c.Prop("tanx_instinct_enchantable_cards_gte_3").Bool(false)) yield return "Tanx: Instinct 可附魔牌 ≥ 3";
        if (c.Prop("darv_pandoras_box_allowed").Bool(false)) yield return "Darv: 允许潘多拉魔盒出现";
        string darvAct = c.Prop("darv_act_filter").Str("any") ?? "any";
        if (!darvAct.Equals("any", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(darvAct)) yield return "Darv: 出现位置限定 " + darvAct;
    }

    private IEnumerable<string> BuildEventSearchSection(OpeningResult result, SearchPlan plan)
    {
        foreach (var q in result.EventQueues.Take(4))
        {
            var order = FilterEventQueueForDisplay(AncientPredictor.EventQueueReadOrder(q), q, plan).Take(Math.Max(1, plan.EventQueueFilterLimit)).ToList();
            if (order.Count == 0) continue;
            yield return $"Act{q.ActNumber} 事件队列";
            if (plan.EventQueueShowFull && !string.IsNullOrWhiteSpace(q.StartOffsetReason))
                yield return "  起始偏移: " + AncientPredictor.EventQueueOffsetNote(q, D);
            int shown = 0;
            foreach (var ev in order)
            {
                shown++;
                yield return $"  {shown}. {FormatEventQueueDisplay(ev, q, plan)}";
            }
        }
    }

    private ResultLineList BuildEventSearchLineViews(OpeningResult result, SearchPlan plan)
    {
        var lines = new ResultLineList();
        var index = BuildEventTooltipIndex(plan);
        foreach (var q in result.EventQueues.Take(4))
        {
            var order = FilterEventQueueForDisplay(AncientPredictor.EventQueueReadOrder(q), q, plan).Take(Math.Max(1, plan.EventQueueFilterLimit)).ToList();
            if (order.Count == 0) continue;
            lines.Add(new ResultLineView { Text = $"Act{q.ActNumber} 事件队列", ToolTip = "此处显示该 Act 进入事件房时的读取顺序；不再标注事件池来源。", IsHeader = true });
            if (plan.EventQueueShowFull && !string.IsNullOrWhiteSpace(q.StartOffsetReason))
            {
                string offset = AncientPredictor.EventQueueOffsetNote(q, D);
                if (!string.IsNullOrWhiteSpace(offset)) lines.Add(new ResultLineView { Text = "  起始偏移: " + offset, ToolTip = offset, IsMuted = true });
            }
            int shown = 0;
            foreach (var ev in order)
            {
                shown++;
                string display = $"  {shown}. {FormatEventQueueDisplay(ev, q, plan)}";
                bool conditional = AncientPredictor.HasEventConditionRule(ev, plan);
                string skip = plan.Data?.EventActSkipNote(ev, q.ActNumber) ?? "";
                lines.Add(new ResultLineView
                {
                    Text = display,
                    ToolTip = EventTooltipText(ev, index),
                    IsWarning = conditional || !string.IsNullOrWhiteSpace(skip) || IsEventSkippedByStartOffset(ev, q),
                    IsSkip = !string.IsNullOrWhiteSpace(skip) || IsEventSkippedByStartOffset(ev, q)
                });
            }
        }
        return lines;
    }

    private IEnumerable<string> FilterEventQueueForDisplay(IEnumerable<string> eventIds, EventQueueBlock q, SearchPlan plan)
    {
        if (plan.EventQueueShowFull) return eventIds;
        return eventIds.Where(ev =>
            string.IsNullOrWhiteSpace(plan.Data?.EventActSkipNote(ev, q.ActNumber))
            && !IsEventSkippedByStartOffset(ev, q));
    }

    private static bool IsEventSkippedByStartOffset(string eventId, EventQueueBlock q)
    {
        if (q.StartOffset <= 0 || q.Events.Count == 0) return false;
        int skippedCount = Math.Min(q.StartOffset, q.Events.Count);
        for (int i = 0; i < skippedCount; i++)
        {
            if (string.Equals(q.Events[i], eventId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private string FormatEventQueueDisplay(string eventId, EventQueueBlock q, SearchPlan plan)
    {
        var marks = new List<string>();
        if (AncientPredictor.HasEventConditionRule(eventId, plan)) marks.Add("条件事件");
        string skip = plan.Data?.EventActSkipNote(eventId, q.ActNumber) ?? "";
        if (!string.IsNullOrWhiteSpace(skip)) marks.Add(skip);
        if (IsEventSkippedByStartOffset(eventId, q)) marks.Add("起始偏移跳过");
        return marks.Count == 0 ? D(eventId) : $"{D(eventId)} [{string.Join("；", marks)}]";
    }

    private Dictionary<string, EventEncyclopediaItem> BuildEventTooltipIndex(SearchPlan plan)
    {
        if (_eventTooltipIndex.Count > 0) return _eventTooltipIndex;
        try
        {
            var rules = new Dictionary<string, EventRuleView>(StringComparer.OrdinalIgnoreCase);
            void AddRules(IEnumerable<EventRuleView> source)
            {
                foreach (var r in source)
                {
                    AddEventIndexAlias(rules, r.Id, r);
                    AddEventIndexAlias(rules, r.SourceId, r);
                    AddEventIndexAlias(rules, r.ClassName, r);
                }
            }

            // 优先直接读取 data/event_rules.json。之前这里只走 GameData.EventRuleBookItems()，
            // 在某些路径下会丢失 event_texts.json 的 source_id 关联，导致 Tooltip 只显示
            // “默认 IsAllowed=true”。直接解析官方规则文件可以稳定挂上条件摘要。
            string? eventRulesPath = RollDataPaths.FindEventRules(_rootDir);
            if (eventRulesPath is not null && File.Exists(eventRulesPath))
                AddRules(ParseEventRules(File.ReadAllText(eventRulesPath, Encoding.UTF8)));

            if (plan.Data is not null)
            {
                string ruleJson = JsonSerializer.Serialize(plan.Data.EventRuleBookItems(), JsonOut.Options);
                AddRules(ParseEventRules(ruleJson));
            }

            string? eventTextsPath = RollDataPaths.FindEventTexts(_rootDir);
            var items = eventTextsPath is not null && File.Exists(eventTextsPath)
                ? ParseEventTexts(File.ReadAllText(eventTextsPath, Encoding.UTF8), rules)
                : rules.Values.Select(EventEncyclopediaItem.FromRuleOnly).ToList();

            var dict = new Dictionary<string, EventEncyclopediaItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                AddEventIndexAlias(dict, item.Id, item);
                AddEventIndexAlias(dict, item.SourceId, item);
                AddEventIndexAlias(dict, item.ClassName, item);
                AddEventIndexAlias(dict, item.Rule?.Id, item);
                AddEventIndexAlias(dict, item.Rule?.SourceId, item);
                AddEventIndexAlias(dict, Term.Normalize(item.Id), item);
                AddEventIndexAlias(dict, Term.Normalize(item.Rule?.Id ?? ""), item);
            }
            _eventTooltipIndex = dict;
        }
        catch
        {
            _eventTooltipIndex = new Dictionary<string, EventEncyclopediaItem>(StringComparer.OrdinalIgnoreCase);
        }
        return _eventTooltipIndex;
    }

    private static void AddEventIndexAlias<T>(Dictionary<string, T> dict, string? key, T value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        dict[key] = value;
        dict[NormalizeLookup(key)] = value;
    }

    private string EventTooltipText(string eventId, Dictionary<string, EventEncyclopediaItem> index)
    {
        if (index.TryGetValue(eventId, out var item) || index.TryGetValue(NormalizeLookup(eventId), out item))
            return item.CompactTooltipText();
        return D(eventId) + "\n\n未找到事件百科详情。";
    }

    private void AddSearchHit(int index, long checkedCount, OpeningResult result, SearchPlan plan, string detail)
    {
        var hit = new SearchHitView
        {
            Rank = index,
            Seed = result.Seed,
            CheckedCount = checkedCount,
            Character = plan.Character,
            Ascension = plan.Root.Prop("player").Prop("ascension").Int(0),
            RunMode = plan.Root.Prop("run_mode").Str(plan.PlayersCount > 1 ? "multiplayer" : "singleplayer") ?? "singleplayer",
            TargetNetId = plan.TargetNetIdText,
            RngVersion = GameRngVersions.ToConfig(plan.RngVersion),
            Summary = BuildSearchHitSummary(result, plan),
            CategoryText = BuildSearchHitCategoryText(result, plan),
            Detail = detail,
            Cards = BuildResultCards(result, plan, true, includeHitExplanation: true).ToList(),
        };
        Dispatcher.Invoke(() =>
        {
            _searchHits.Add(hit);
            BottomStatusText.Text = $"已命中 {_searchHits.Count} 个，打开“筛种结果”查看详情";
            if (_searchHits.Count == 1)
            {
                SearchHitsListBox.SelectedIndex = 0;
            }
        });
    }


    private ResultLineList BuildSearchHitExplanationLineViews(OpeningResult result, SearchPlan plan)
    {
        var lines = new ResultLineList();
        lines.Add(new ResultLineView { Text = $"命中 Seed: {result.Seed} · {D(plan.Character)} · A{plan.Root.Prop("player").Prop("ascension").Int(0)}", ToolTip = "当前筛种结果的基础信息。", IsHeader = true });

        var category = BuildSearchHitCategoryText(result, plan);
        if (!string.IsNullOrWhiteSpace(category))
            lines.Add(ResultLineView.Plain("命中类别: " + category));

        if (plan.RequireBones && result.HasBones)
            lines.Add(ResultLineView.Plain("Neow: 满足“必须包含骨骰”。"));
        AddTermListHitExplanation(lines, "Neow 三选", plan.NeowTerms, result.NeowOptions);
        AddTermListHitExplanation(lines, "骨骰遗物", plan.BonesRelicTerms, result.BonesRelics);
        if (plan.BonesCurseAny.Count > 0 && result.BonesCurses.Count > 0)
            lines.Add(ResultLineView.Plain("骨骰诅咒: " + DManyCount(result.BonesCurses) + " 命中要求 " + DManyCount(plan.BonesCurseAny)));
        if (plan.BonesCurseBlacklist.Count > 0)
            lines.Add(ResultLineView.Plain("骨骰诅咒排除: 前置结果未包含 " + DManyCount(plan.BonesCurseBlacklist)));

        var processRoute = result.OpeningRoutes.FirstOrDefault(r => RouteMatchesProcessForExplanation(r, plan.NeowProcessFilter));
        if (plan.NeowProcessFilter.HasFilter && processRoute is not null)
        {
            lines.Add(new ResultLineView { Text = "过程导向: " + RouteDisplayName(processRoute, result.OpeningRoutes.IndexOf(processRoute) + 1), ToolTip = "显示第一条满足过程导向筛选的开局路线。", IsHeader = true });
            foreach (var line in BuildRouteKeyExplanation(processRoute).Take(6)) lines.Add(ResultLineView.Plain("  " + line));
        }

        var finalRoute = result.OpeningRoutes.FirstOrDefault(r => RouteMatchesFinalForDisplay(r, plan.NeowFinalFilter));
        if (plan.NeowFinalFilter.HasFilter && finalRoute is not null)
        {
            lines.Add(new ResultLineView { Text = "最终结果导向: " + RouteDisplayName(finalRoute, result.OpeningRoutes.IndexOf(finalRoute) + 1), ToolTip = "显示第一条满足最终结果筛选的开局路线。", IsHeader = true });
            AddFinalFilterExplanation(lines, finalRoute, plan.NeowFinalFilter);
        }

        AddShopHitExplanation(lines, result, plan);
        AddEventHitExplanation(lines, result, plan);
        AddAncientHitExplanation(lines, result, plan);

        if (lines.Count <= 2)
            lines.Add(ResultLineView.Plain("该 seed 满足当前筛选条件；当前条件没有更多可结构化解释，详情见下方各卡片与原始详情。"));
        return lines;
    }

    private void AddTermListHitExplanation(ResultLineList lines, string label, TermList terms, IList<string> actual)
    {
        if (!terms.HasTerms) return;
        if (terms.All.Count > 0) lines.Add(ResultLineView.Plain($"{label}: 必须包含 {DManyCount(terms.All)}；实际 {DManyCount(actual)}"));
        if (terms.Any.Count > 0) lines.Add(ResultLineView.Plain($"{label}: 包含任一 {DManyCount(terms.Any)}；实际 {DManyCount(actual)}"));
        if (terms.Blacklist.Count > 0) lines.Add(ResultLineView.Plain($"{label}: 已排除 {DManyCount(terms.Blacklist)}"));
    }

    private bool RouteMatchesProcessForExplanation(OpeningRoute route, NeowRouteFilter filter)
    {
        if (!filter.HasFilter) return false;
        bool isBones = string.Equals(route.Kind, "bones", StringComparison.OrdinalIgnoreCase);
        var mode = filter.RouteMode;
        if ((mode == "bones" || mode == "bone") && !isBones) return false;
        if ((mode == "direct" || mode == "relic") && isBones) return false;
        if (filter.InitialRelics.HasTerms && !filter.InitialRelics.Match(InitialRelicsForDisplay(route, includeNeowsBonesMarker: true))) return false;
        if (filter.BonesRelics.HasTerms && (!isBones || !filter.BonesRelics.Match(route.PickOrder))) return false;
        if (filter.GeneratedRelics.HasTerms && !filter.GeneratedRelics.Match(route.PredictedRelics)) return false;
        if (filter.Potions.HasTerms && !filter.Potions.Match(route.Potions)) return false;
        if (filter.Cards.HasTerms && !filter.Cards.Match(FinalCardsForDisplay(route))) return false;
        foreach (var kv in filter.SourceCardFilters)
            if (!SourceCardsForDisplay(route, kv.Key).Any() || !kv.Value.Match(SourceCardsForDisplay(route, kv.Key))) return false;
        foreach (var kv in filter.SourcePotionFilters)
            if (!kv.Value.Match(route.PotionOpportunities.Where(x => SourceMatchesFilterKey(x.Source, kv.Key)).SelectMany(x => x.Potions).ToList())) return false;
        foreach (var kv in filter.SourceRelicFilters)
            if (!kv.Value.Match(route.RelicOpportunities.Where(x => SourceMatchesFilterKey(x.Source, kv.Key)).SelectMany(x => x.Relics).ToList())) return false;
        return true;
    }

    private IEnumerable<string> BuildRouteKeyExplanation(OpeningRoute route)
    {
        if (route.Kind.Equals("bones", StringComparison.OrdinalIgnoreCase)) yield return "骨骰路线: " + string.Join(" -> ", route.PickOrder.Select(D));
        else if (!string.IsNullOrWhiteSpace(route.DirectRelic)) yield return "直接选择: " + D(route.DirectRelic ?? "");
        if (!string.IsNullOrWhiteSpace(route.BonesCurse)) yield return "诅咒: " + D(route.BonesCurse ?? "");
        foreach (var ev in route.CardOpportunities.Take(3))
        {
            if (ev.Type.Equals("bundle_choice", StringComparison.OrdinalIgnoreCase) && ev.Options.Count > 0)
                yield return D(ev.Source) + ": " + string.Join(" | ", ev.Options.Select(o => DManyCount(o)));
            else if (ev.Cards.Count > 0)
                yield return D(ev.Source) + ": " + DManyCount(ev.Cards);
        }
        if (route.PredictedRelics.Count > 0) yield return "随机遗物: " + DManyCount(route.PredictedRelics);
        if (route.Potions.Count > 0) yield return "药水: " + DManyCount(route.Potions);
    }

    private void AddFinalFilterExplanation(ResultLineList lines, OpeningRoute route, NeowFinalFilter filter)
    {
        if (filter.Relics.HasTerms) AddTermListHitExplanation(lines, "  最终遗物", filter.Relics, FinalOrdinaryRelicsForDisplay(route));
        if (filter.Cards.HasTerms) AddTermListHitExplanation(lines, "  最终卡牌", filter.Cards, FinalCardsForDisplay(route));
        if (filter.Potions.HasTerms) AddTermListHitExplanation(lines, "  最终药水", filter.Potions, FinalPotionsForDisplay(route));
        if (filter.CurseAny.Count > 0) lines.Add(ResultLineView.Plain("  最终诅咒: 命中 " + DManyCount(FinalCursesForDisplay(route)) + "，要求 " + DManyCount(filter.CurseAny)));
        if (filter.CurseBlacklist.Count > 0) lines.Add(ResultLineView.Plain("  最终诅咒排除: " + DManyCount(filter.CurseBlacklist)));
        if (filter.NeowRelicBlacklist.Count > 0) lines.Add(ResultLineView.Plain("  来源排除: 未使用 " + DManyCount(filter.NeowRelicBlacklist)));
    }

    private void AddShopHitExplanation(ResultLineList lines, OpeningResult result, SearchPlan plan)
    {
        if (result.ShopRelics.Count == 0 || (!plan.ShopExact.Any() && !plan.ShopRequire.Any() && !plan.ShopBlacklist.Any())) return;
        lines.Add(new ResultLineView { Text = "商店专属遗物筛选", ToolTip = "显示商店专属遗物筛选实际命中的位置。", IsHeader = true });
        foreach (var exact in plan.ShopExact)
        {
            int pos = exact.ExactPos ?? 0;
            string actual = pos > 0 && pos <= result.ShopRelics.Count ? result.ShopRelics[pos - 1] : "";
            lines.Add(ResultLineView.Plain($"  第 {pos} 个 = {D(actual)}，命中要求 {D(exact.RelicId)}"));
        }
        foreach (var req in plan.ShopRequire)
        {
            int pos = FindShopRelicPosition(result.ShopRelics, req.RelicId, req.MaxPos ?? plan.ShopLimit);
            lines.Add(ResultLineView.Plain($"  前 {Math.Max(1, req.MaxPos ?? plan.ShopLimit)} 个包含 {D(req.RelicId)}" + (pos > 0 ? $"，实际位置 #{pos}" : "")));
        }
        foreach (var ban in plan.ShopBlacklist)
            lines.Add(ResultLineView.Plain($"  前 {Math.Max(1, ban.MaxPos ?? plan.ShopLimit)} 个未出现排除项 {D(ban.RelicId)}"));
    }

    private static int FindShopRelicPosition(IList<string> shopRelics, string relicId, int limit)
    {
        int n = Math.Min(Math.Max(1, limit), shopRelics.Count);
        for (int i = 0; i < n; i++) if (Term.ItemMatches(shopRelics[i], relicId)) return i + 1;
        return -1;
    }

    private void AddEventHitExplanation(ResultLineList lines, OpeningResult result, SearchPlan plan)
    {
        if (!plan.EventQueueTerms.HasTerms) return;
        lines.Add(new ResultLineView { Text = "事件队列筛选（按条件表匹配默认可出现事件）", ToolTip = "显示事件筛选实际命中的 Act、前 N 与有效位置。", IsHeader = true });
        foreach (var term in plan.EventQueueTerms.All)
        {
            var hit = FindEventQueueHit(result, plan, term);
            lines.Add(ResultLineView.Plain(hit.Found
                ? $"  必须包含 {DisplayEventTerm(term)}：Act{hit.ActNumber} #{hit.Position} {D(hit.EventId)}"
                : $"  必须包含 {DisplayEventTerm(term)}：未定位到具体事件（可查看原始详情）"));
        }
        foreach (var term in plan.EventQueueTerms.Blacklist)
            lines.Add(ResultLineView.Plain($"  排除 {DisplayEventTerm(term)}：指定范围内未出现。"));
        foreach (var term in plan.EventQueueTerms.Any)
        {
            var hit = FindEventQueueHit(result, plan, term);
            if (hit.Found) lines.Add(ResultLineView.Plain($"  包含任一 {DisplayEventTerm(term)}：Act{hit.ActNumber} #{hit.Position} {D(hit.EventId)}"));
        }
    }

    private (bool Found, int ActNumber, int Position, string EventId) FindEventQueueHit(OpeningResult result, SearchPlan plan, string rawTerm)
    {
        var parsed = ParseEventQueueTermForDisplay(rawTerm);
        foreach (var q in result.EventQueues)
        {
            if (parsed.ActNumber is int act && act != q.ActNumber) continue;
            var order = FilterEventQueueForDisplay(AncientPredictor.EventQueueReadOrder(q), q, plan).Take(Math.Max(1, parsed.Limit ?? plan.EventQueueFilterLimit)).ToList();
            for (int i = 0; i < order.Count; i++)
            {
                string eventId = order[i];
                if (plan.Data?.EventMatchesTerm(eventId, parsed.EventTerm) == true || Term.ItemMatches(eventId, parsed.EventTerm))
                    return (true, q.ActNumber, i + 1, eventId);
            }
        }
        return (false, 0, 0, "");
    }

    private static (int? ActNumber, int? Limit, string EventTerm) ParseEventQueueTermForDisplay(string raw)
        => ParseEventQueueTermForDisplayStatic(raw);

    private static (int? ActNumber, int? Limit, string EventTerm) ParseEventQueueTermForDisplayStatic(string raw)
    {
        raw = Term.Normalize(raw ?? "").Trim();
        var mActLimit = Regex.Match(raw, @"^\s*(?:act)?\s*([1-3])\s*(?:<=|#|/n|n)\s*(\d{1,2})\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mActLimit.Success && int.TryParse(mActLimit.Groups[1].Value, out int act1) && int.TryParse(mActLimit.Groups[2].Value, out int lim1))
            return (act1, Math.Min(15, Math.Max(1, lim1)), mActLimit.Groups[3].Value.Trim());

        var mAct = Regex.Match(raw, @"^\s*(?:act)?\s*([1-3])\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mAct.Success && int.TryParse(mAct.Groups[1].Value, out int act2))
            return (act2, null, mAct.Groups[2].Value.Trim());

        var mLimit = Regex.Match(raw, @"^\s*n\s*(\d{1,2})\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        if (mLimit.Success && int.TryParse(mLimit.Groups[1].Value, out int lim2))
            return (null, Math.Min(15, Math.Max(1, lim2)), mLimit.Groups[2].Value.Trim());

        return (null, null, raw.Trim());
    }

    private string DisplayEventTerm(string rawTerm)
    {
        var parsed = ParseEventQueueTermForDisplay(rawTerm);
        string prefix = parsed.ActNumber is int act ? $"Act{act} " : "";
        string limit = parsed.Limit is int n ? $"前 {n} " : "";
        return prefix + limit + D(parsed.EventTerm);
    }

    private void AddAncientHitExplanation(ResultLineList lines, OpeningResult result, SearchPlan plan)
    {
        if (!plan.AncientRequire.Any() && !plan.AncientBlacklist.Any() && !plan.AncientOptionRequire.Any() && !plan.AncientOptionBlacklist.Any()) return;
        lines.Add(new ResultLineView { Text = "先古筛选", ToolTip = "显示先古身份 / 选项筛选的实际结果摘要。", IsHeader = true });
        if (result.Ancients.Count > 0) lines.Add(ResultLineView.Plain("  先古队列: " + DMany(result.Ancients)));
        foreach (var block in result.AncientOptions.Take(6))
            lines.Add(ResultLineView.Plain($"  Act{block.ActNumber} {D(block.AncientId)}: {DMany(block.Options)}"));
    }

    private List<string> SourceCardsForDisplay(OpeningRoute route, string sourceKey)
    {
        var xs = new List<string>();
        foreach (var ev in route.CardOpportunities.Where(x => SourceMatchesFilterKey(x.Source, sourceKey)))
        {
            xs.AddRange(ev.Cards);
            xs.AddRange(ev.Options.SelectMany(o => o));
        }
        return xs;
    }

    private static bool SourceMatchesFilterKey(string source, string key)
    {
        if (source.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
        if ((key.Equals("ScrollBoxesBundle1", StringComparison.OrdinalIgnoreCase) || key.Equals("ScrollBoxes1", StringComparison.OrdinalIgnoreCase)) && source.Equals("ScrollBoxes", StringComparison.OrdinalIgnoreCase)) return true;
        if ((key.Equals("ScrollBoxesBundle2", StringComparison.OrdinalIgnoreCase) || key.Equals("ScrollBoxes2", StringComparison.OrdinalIgnoreCase)) && source.Equals("ScrollBoxes", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string BuildSearchHitCategoryText(OpeningResult result, SearchPlan plan)
    {
        var tags = new List<string>();
        if (result.BonesRelics.Count > 0 || result.BonesCurses.Count > 0) tags.Add("Neow/骨骰");
        if (result.EventQueues.Count > 0) tags.Add("事件");
        if (result.ShopRelics.Count > 0) tags.Add("商店");
        if (result.Ancients.Count > 0 || result.AncientOptions.Count > 0) tags.Add("先古");
        if (plan.NeowFinalFilter.HasFilter) tags.Add("最终结果");
        if (result.CardSourceRoutes.Count > 0) tags.Add("卡牌");
        if (result.PotionSources.Count > 0) tags.Add("药水");
        if (tags.Count == 0) tags.Add("命中");
        return string.Join(" · ", tags);
    }

    private string BuildSearchHitSummary(OpeningResult result, SearchPlan plan)
    {
        var parts = new List<string>();
        if (result.BonesRelics.Count > 0) parts.Add("骨骰: " + DMany(result.BonesRelics, 2));
        if (result.BonesCurses.Count > 0) parts.Add("诅咒: " + DMany(result.BonesCurses.Distinct(StringComparer.OrdinalIgnoreCase), 2));
        if (result.ShopRelics.Count > 0) parts.Add("商店: " + DMany(result.ShopRelics, Math.Min(3, Math.Max(1, plan.ShopLimit))));
        if (result.Ancients.Count > 0) parts.Add("先古: " + DMany(result.Ancients, 3));
        if (plan.NeowFinalFilter.HasFilter)
        {
            var line = FirstFinalResultSummary(result, plan);
            if (!string.IsNullOrWhiteSpace(line)) parts.Add("最终结果: " + line);
        }
        if (result.EventQueues.Count > 0)
        {
            var first = result.EventQueues.FirstOrDefault();
            if (first is not null)
            {
                var order = FilterEventQueueForDisplay(AncientPredictor.EventQueueReadOrder(first), first, plan).Take(Math.Max(1, Math.Min(3, plan.EventQueueFilterLimit))).ToList();
                if (order.Count > 0) parts.Add("事件: " + string.Join(" / ", order.Select(D)));
            }
        }
        return parts.Count == 0 ? "命中筛选条件" : string.Join("  |  ", parts);
    }

    private string FirstFinalResultSummary(OpeningResult result, SearchPlan plan)
    {
        var route = result.OpeningRoutes.FirstOrDefault(r => RouteMatchesFinalForDisplay(r, plan.NeowFinalFilter));
        if (route is null) return "未找到匹配路线";
        var parts = new List<string>();
        var cards = FinalCardsForDisplay(route);
        var relics = FinalOrdinaryRelicsForDisplay(route);
        var potions = FinalPotionsForDisplay(route);
        var curses = FinalCursesForDisplay(route);
        if (cards.Count > 0) parts.Add("卡牌 " + string.Join("/", cards.Take(2).Select(D)));
        if (relics.Count > 0) parts.Add("遗物 " + string.Join("/", relics.Take(2).Select(D)));
        if (potions.Count > 0) parts.Add("药水 " + string.Join("/", potions.Take(2).Select(D)));
        if (curses.Count > 0) parts.Add("诅咒 " + string.Join("/", curses.Take(2).Select(D)));
        return parts.Count == 0 ? RouteDisplayName(route, 1) : string.Join("，", parts);
    }

    private string SearchPresetDir => Path.Combine(_rootDir, "presets");
    private string LegacySearchPresetPath => Path.Combine(_rootDir, "profiles", "wpf_search_preset.json");

    private void SaveSearchPreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SearchPresetDir);
            var dlg = new SaveFileDialog
            {
                Title = "保存筛选预设",
                InitialDirectory = SearchPresetDir,
                FileName = "wpf_search_preset_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json",
                Filter = "RollTheSpire2 筛选预设|*.json|All files|*.*",
                AddExtension = true,
                DefaultExt = ".json",
            };
            if (dlg.ShowDialog(this) != true) return;
            var preset = BuildSearchPresetObject();
            File.WriteAllText(dlg.FileName, preset.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            SearchPresetStatusText.Text = "筛选预设：已保存 " + Path.GetFileName(dlg.FileName);
            BottomStatusText.Text = "已保存筛选预设";
            AppendLog("Search preset saved: " + dlg.FileName);
        }
        catch (Exception ex)
        {
            SearchPresetStatusText.Text = "筛选预设：保存失败";
            BottomStatusText.Text = "保存筛选预设失败: " + ex.Message;
            AppendLog("Save search preset failed: " + ex.Message);
        }
    }

    private void LoadSearchPreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SearchPresetDir);
            var dlg = new OpenFileDialog
            {
                Title = "加载筛选预设",
                InitialDirectory = Directory.Exists(SearchPresetDir) ? SearchPresetDir : _rootDir,
                Filter = "RollTheSpire2 筛选预设|*.json|All files|*.*",
                CheckFileExists = true,
            };
            if (!Directory.EnumerateFiles(SearchPresetDir, "*.json").Any() && File.Exists(LegacySearchPresetPath))
                dlg.InitialDirectory = Path.GetDirectoryName(LegacySearchPresetPath) ?? _rootDir;
            if (dlg.ShowDialog(this) != true) return;

            var node = JsonNode.Parse(File.ReadAllText(dlg.FileName, Encoding.UTF8))?.AsObject()
                ?? throw new InvalidOperationException("preset is not a JSON object");
            ApplySearchPresetObject(node);
            SearchPresetStatusText.Text = "筛选预设：已载入 " + Path.GetFileName(dlg.FileName);
            BottomStatusText.Text = "已载入筛选预设";
            AppendLog("Search preset loaded: " + dlg.FileName);
        }
        catch (Exception ex)
        {
            SearchPresetStatusText.Text = "筛选预设：载入失败";
            BottomStatusText.Text = "载入筛选预设失败: " + ex.Message;
            AppendLog("Load search preset failed: " + ex.Message);
        }
    }

    private void ClearSearchFilters_Click(object sender, RoutedEventArgs e)
    {
        ClearSearchFilterInputs();
        SearchPresetStatusText.Text = "筛选预设：已清空筛选条件";
        BottomStatusText.Text = "已清空筛选条件";
    }

    private JsonObject BuildSearchPresetObject()
    {
        var fields = new JsonObject();
        Put(fields, "SearchStart", SearchStartTextBox.Text);
        Put(fields, "SearchEnd", SearchEndTextBox.Text);
        Put(fields, "SearchMaxResults", SearchMaxResultsTextBox.Text);
        Put(fields, "SearchMode", SelectedComboText(SearchModeComboBox, "random"));
        Put(fields, "SearchCharacter", SelectedComboText(SearchCharacterComboBox, "IRONCLAD"));
        Put(fields, "GameRngVersion", SelectedGameRngVersion());
        Put(fields, "RunMode", SelectedComboText(RunModeComboBox, "singleplayer"));
        Put(fields, "MultiplayerPlayersCount", SelectedComboText(MultiplayerPlayersCountComboBox, "2"));
        Put(fields, "TargetNetId", SelectedTargetNetId());
        Put(fields, "MultiplayerPlayers", MultiplayerPlayersTextBox.Text);
        Put(fields, "MultiplayerUseProfile", MultiplayerUseProfileCheckBox.IsChecked == true);
        Put(fields, "SearchAscension", SelectedSearchAscensionLevel().ToString());
        Put(fields, "SearchAscensionScarcity", SearchAscensionScarcityCheckBox.IsChecked == true);
        Put(fields, "SearchEventLimit", SearchEventLimitTextBox.Text);
        Put(fields, "EventActScope", SelectedComboText(EventActScopeComboBox, "any"));
        Put(fields, "NeowFilterMode", SelectedComboText(NeowFilterModeComboBox, "none"));
        Put(fields, "BonesRequirement", SelectedComboText(BonesRequirementComboBox, "yes"));
        Put(fields, "BonesRelicMode", SelectedComboText(BonesRelicModeComboBox, "undirected"));
        Put(fields, "BonesCurseMode", SelectedComboText(BonesCurseModeComboBox, "none"));

        Put(fields, "RequireBones", RequireBonesCheckBox.IsChecked == true);
        Put(fields, "NeowAny", NeowAnyTextBox.Text);
        Put(fields, "NeowAll", NeowAllTextBox.Text);
        Put(fields, "NeowBlacklist", NeowBlacklistTextBox.Text);
        Put(fields, "BonesRelicAny", BonesRelicAnyTextBox.Text);
        Put(fields, "BonesRelicAll", BonesRelicAllTextBox.Text);
        Put(fields, "BonesRelicBlacklist", BonesRelicBlacklistTextBox.Text);
        Put(fields, "BonesCurseAny", BonesCurseAnyTextBox.Text);
        Put(fields, "BonesCurseBlacklist", BonesCurseBlacklistTextBox.Text);
        Put(fields, "PredictedRelicAny", PredictedRelicAnyTextBox.Text);
        Put(fields, "PredictedRelicAll", PredictedRelicAllTextBox.Text);
        Put(fields, "PredictedRelicBlacklist", PredictedRelicBlacklistTextBox.Text);
        Put(fields, "EnableAdvancedNewLeaf", AdvancedNewLeafEnabled());
        Put(fields, "NewLeafSource", SelectedNewLeafSourceSelector());
        Put(fields, "NewLeafCardAll", NewLeafCardAllTextBox.Text);
        Put(fields, "KaleidoscopeGroup1CardAll", KaleidoscopeGroup1CardAllTextBox.Text);
        Put(fields, "KaleidoscopeGroup2CardAll", KaleidoscopeGroup2CardAllTextBox.Text);
        Put(fields, "LostCofferCardAll", LostCofferCardAllTextBox.Text);
        Put(fields, "LostCofferPotionAll", LostCofferPotionAllTextBox.Text);
        var dynamicTemplates = new JsonObject();
        foreach (var kv in _dynamicTemplateTerms) dynamicTemplates[kv.Key] = ToJsonArray(kv.Value);
        fields["DynamicTemplates"] = dynamicTemplates;
        Put(fields, "NeowRouteMode", SelectedComboText(NeowRouteModeComboBox, "any"));
        Put(fields, "ProcessCardAny", ProcessCardAnyTextBox.Text);
        Put(fields, "ProcessCardBlacklist", ProcessCardBlacklistTextBox.Text);
        Put(fields, "FinalRelicAny", FinalRelicAnyTextBox.Text);
        Put(fields, "FinalRelicAll", FinalRelicAllTextBox.Text);
        Put(fields, "FinalRelicBlacklist", FinalRelicBlacklistTextBox.Text);
        Put(fields, "FinalCardAny", FinalCardAnyTextBox.Text);
        Put(fields, "FinalCardAll", FinalCardAllTextBox.Text);
        Put(fields, "FinalCardBlacklist", FinalCardBlacklistTextBox.Text);
        Put(fields, "FinalCurseAny", FinalCurseAnyTextBox.Text);
        Put(fields, "FinalCurseBlacklist", FinalCurseBlacklistTextBox.Text);
        Put(fields, "FinalPotionAny", FinalPotionAnyTextBox.Text);
        Put(fields, "FinalPotionAll", FinalPotionAllTextBox.Text);
        Put(fields, "FinalPotionBlacklist", FinalPotionBlacklistTextBox.Text);
        Put(fields, "FinalNeowRelicAny", FinalNeowRelicAnyTextBox.Text);
        Put(fields, "FinalNeowRelicBlacklist", FinalNeowRelicBlacklistTextBox.Text);

        Put(fields, "OwnCardsEnabled", OwnCardsEnabledCheckBox.IsChecked == true);
        Put(fields, "OwnCardsAny", OwnCardsAnyTextBox.Text);
        Put(fields, "ColorlessCardsEnabled", ColorlessCardsEnabledCheckBox.IsChecked == true);
        Put(fields, "ColorlessCardsAny", ColorlessCardsAnyTextBox.Text);
        Put(fields, "OtherCardsEnabled", OtherCardsEnabledCheckBox.IsChecked == true);
        Put(fields, "OtherCardsAny", OtherCardsAnyTextBox.Text);
        Put(fields, "PotionAny", PotionAnyTextBox.Text);
        Put(fields, "PotionAll", PotionAllTextBox.Text);
        Put(fields, "PotionBlacklist", PotionBlacklistTextBox.Text);

        Put(fields, "EventAll", EventAllTextBox.Text);
        Put(fields, "EventBlacklist", EventBlacklistTextBox.Text);

        Put(fields, "ShopFilterEnabled", ShopFilterEnabledCheckBox.IsChecked == true);
        Put(fields, "SearchShopLimit", SelectedComboText(SearchShopLimitComboBox, "5"));
        Put(fields, "ShopRequire", ShopRequireTextBox.Text);
        Put(fields, "ShopBlacklist", ShopBlacklistTextBox.Text);
        Put(fields, "ShopExact", ShopExactTextBox.Text);

        Put(fields, "AncientSearchEnabled", AncientSearchEnabledCheckBox.IsChecked == true);
        Put(fields, "AncientRequire", AncientRequireTextBox.Text);
        Put(fields, "AncientBlacklist", AncientBlacklistTextBox.Text);
        Put(fields, "AncientOptionRequire", AncientOptionRequireTextBox.Text);
        Put(fields, "AncientOptionBlacklist", AncientOptionBlacklistTextBox.Text);
        Put(fields, "AncientPaelGoopy", AncientPaelGoopyCheckBox.IsChecked == true);
        Put(fields, "AncientPaelRemovable", AncientPaelRemovableCheckBox.IsChecked == true);
        Put(fields, "AncientPaelHasEventPet", AncientPaelHasEventPetCheckBox.IsChecked == true);
        Put(fields, "AncientOrobasTouch", AncientOrobasTouchCheckBox.IsChecked == true);
        Put(fields, "AncientOrobasTooth", AncientOrobasToothCheckBox.IsChecked == true);
        Put(fields, "AncientTezcataraBasicStrike", AncientTezcataraBasicStrikeCheckBox.IsChecked == true);
        Put(fields, "AncientNonupeipeSwift", AncientNonupeipeSwiftCheckBox.IsChecked == true);
        Put(fields, "AncientTanxInstinct", AncientTanxInstinctCheckBox.IsChecked == true);
        Put(fields, "AncientDarvPandora", AncientDarvPandoraCheckBox.IsChecked == true);
        Put(fields, "AncientDarvAct", SelectedComboText(AncientDarvActComboBox, "any"));

        return new JsonObject
        {
            ["schema"] = 1,
            ["generated_for"] = "RollTheSpire2 search preset",
            ["saved_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["fields"] = fields,
        };
    }

    private void MergeDynamicTemplateTerms(string targetKey, params string[] oldKeys)
    {
        var merged = new List<string>();
        if (_dynamicTemplateTerms.TryGetValue(targetKey, out var existing)) merged.AddRange(existing);
        foreach (var oldKey in oldKeys)
        {
            if (_dynamicTemplateTerms.TryGetValue(oldKey, out var values))
            {
                merged.AddRange(values);
                _dynamicTemplateTerms.Remove(oldKey);
            }
        }
        var kept = merged.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (kept.Count > 0) _dynamicTemplateTerms[targetKey] = kept;
    }

    private void ApplySearchPresetObject(JsonObject root)
    {
        var fields = root["fields"] as JsonObject ?? root;
        SearchStartTextBox.Text = FieldString(fields, "SearchStart", SearchStartTextBox.Text);
        SearchEndTextBox.Text = FieldString(fields, "SearchEnd", SearchEndTextBox.Text);
        SearchMaxResultsTextBox.Text = FieldString(fields, "SearchMaxResults", SearchMaxResultsTextBox.Text);
        SetCombo(SearchModeComboBox, FieldString(fields, "SearchMode", SelectedComboText(SearchModeComboBox, "random")));
        SetCombo(SearchCharacterComboBox, FieldString(fields, "SearchCharacter", SelectedComboText(SearchCharacterComboBox, "IRONCLAD")));
        SetCombo(GameRngVersionComboBox, FieldString(fields, "GameRngVersion", SelectedGameRngVersion()));
        SetCombo(RunModeComboBox, FieldString(fields, "RunMode", SelectedComboText(RunModeComboBox, "singleplayer")));
        SetCombo(MultiplayerPlayersCountComboBox, FieldString(fields, "MultiplayerPlayersCount", SelectedComboText(MultiplayerPlayersCountComboBox, "2")));
        TargetNetIdTextBox.Text = FieldString(fields, "TargetNetId", SelectedTargetNetId());
        MultiplayerPlayersTextBox.Text = FieldString(fields, "MultiplayerPlayers", MultiplayerPlayersTextBox.Text);
        MultiplayerUseProfileCheckBox.IsChecked = FieldBool(fields, "MultiplayerUseProfile", MultiplayerUseProfileCheckBox.IsChecked == true);
        RefreshRunModeStatus();
        SetCombo(SearchAscensionComboBox, FieldString(fields, "SearchAscension", SelectedSearchAscensionLevel().ToString()));
        SearchAscensionScarcityCheckBox.IsChecked = FieldBool(fields, "SearchAscensionScarcity", SearchAscensionScarcityCheckBox.IsChecked == true);
        SearchEventLimitTextBox.Text = FieldString(fields, "SearchEventLimit", SearchEventLimitTextBox.Text);
        SetCombo(EventActScopeComboBox, FieldString(fields, "EventActScope", SelectedComboText(EventActScopeComboBox, "any")));
        string loadedNeowMode = FieldString(fields, "NeowFilterMode", SelectedComboText(NeowFilterModeComboBox, "none"));
        string loadedBonesRequirement = FieldString(fields, "BonesRequirement", SelectedComboText(BonesRequirementComboBox, "yes"));
        // preview10f 起“无所谓”已迁移为 Neow 高级筛选模式的“不筛选”。
        if (loadedNeowMode.Equals("process", StringComparison.OrdinalIgnoreCase)
            && loadedBonesRequirement.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            loadedNeowMode = "none";
            loadedBonesRequirement = "yes";
        }
        SetCombo(NeowFilterModeComboBox, loadedNeowMode);
        SetCombo(BonesRequirementComboBox, loadedBonesRequirement.Equals("any", StringComparison.OrdinalIgnoreCase) ? "yes" : loadedBonesRequirement);
        SetCombo(BonesRelicModeComboBox, FieldString(fields, "BonesRelicMode", SelectedComboText(BonesRelicModeComboBox, "undirected")));
        SetCombo(BonesCurseModeComboBox, FieldString(fields, "BonesCurseMode", SelectedComboText(BonesCurseModeComboBox, "none")));

        RequireBonesCheckBox.IsChecked = FieldBool(fields, "RequireBones", RequireBonesCheckBox.IsChecked == true);
        NeowAnyTextBox.Text = FieldString(fields, "NeowAny");
        NeowAllTextBox.Text = FieldString(fields, "NeowAll");
        NeowBlacklistTextBox.Text = FieldString(fields, "NeowBlacklist");
        BonesRelicAnyTextBox.Text = FieldString(fields, "BonesRelicAny");
        BonesRelicAllTextBox.Text = FieldString(fields, "BonesRelicAll");
        BonesRelicBlacklistTextBox.Text = FieldString(fields, "BonesRelicBlacklist");
        BonesCurseAnyTextBox.Text = FieldString(fields, "BonesCurseAny");
        BonesCurseBlacklistTextBox.Text = FieldString(fields, "BonesCurseBlacklist");
        PredictedRelicAnyTextBox.Text = FieldString(fields, "PredictedRelicAny");
        PredictedRelicAllTextBox.Text = FieldString(fields, "PredictedRelicAll");
        PredictedRelicBlacklistTextBox.Text = FieldString(fields, "PredictedRelicBlacklist");
        EnableAdvancedNewLeafCheckBox.IsChecked = FieldBool(fields, "EnableAdvancedNewLeaf", EnableAdvancedNewLeafCheckBox.IsChecked == true);
        SetCombo(NewLeafSourceComboBox, FieldString(fields, "NewLeafSource", "starter_basic"));
        NewLeafCardAllTextBox.Text = FieldString(fields, "NewLeafCardAll");
        KaleidoscopeGroup1CardAllTextBox.Text = FieldString(fields, "KaleidoscopeGroup1CardAll");
        KaleidoscopeGroup2CardAllTextBox.Text = FieldString(fields, "KaleidoscopeGroup2CardAll");
        LostCofferCardAllTextBox.Text = FieldString(fields, "LostCofferCardAll");
        LostCofferPotionAllTextBox.Text = FieldString(fields, "LostCofferPotionAll");
        _dynamicTemplateTerms.Clear();
        if (fields["DynamicTemplates"] is JsonObject dynamicTemplates)
        {
            foreach (var kv in dynamicTemplates)
            {
                if (kv.Value is JsonArray arr)
                    _dynamicTemplateTerms[kv.Key] = arr.Select(x => x?.GetValue<string>() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
            // preview10d 起万花筒不再暴露第 1/第 2 组；旧预设里的分组模板合并为聚合模板。
            MergeDynamicTemplateTerms("kaleidoscope_cards", "kaleidoscope_group_1", "kaleidoscope_group_2");
            // preview10e 起树叶药膏不再暴露打击/防御槽位；旧预设合并为变化结果聚合模板。
            MergeDynamicTemplateTerms("leafy_poultice_transforms", "leafy_poultice_strike_transform", "leafy_poultice_defend_transform");
        }
        SetCombo(NeowRouteModeComboBox, FieldString(fields, "NeowRouteMode", SelectedComboText(NeowRouteModeComboBox, "any")));
        ProcessCardAnyTextBox.Text = FieldString(fields, "ProcessCardAny");
        ProcessCardBlacklistTextBox.Text = FieldString(fields, "ProcessCardBlacklist");
        FinalRelicAnyTextBox.Text = FieldString(fields, "FinalRelicAny");
        FinalRelicAllTextBox.Text = FieldString(fields, "FinalRelicAll");
        FinalRelicBlacklistTextBox.Text = FieldString(fields, "FinalRelicBlacklist");
        FinalCardAnyTextBox.Text = FieldString(fields, "FinalCardAny");
        FinalCardAllTextBox.Text = FieldString(fields, "FinalCardAll");
        FinalCardBlacklistTextBox.Text = FieldString(fields, "FinalCardBlacklist");
        FinalCurseAnyTextBox.Text = FieldString(fields, "FinalCurseAny");
        FinalCurseBlacklistTextBox.Text = FieldString(fields, "FinalCurseBlacklist");
        FinalPotionAnyTextBox.Text = FieldString(fields, "FinalPotionAny");
        FinalPotionAllTextBox.Text = FieldString(fields, "FinalPotionAll");
        FinalPotionBlacklistTextBox.Text = FieldString(fields, "FinalPotionBlacklist");
        FinalNeowRelicAnyTextBox.Text = FieldString(fields, "FinalNeowRelicAny");
        FinalNeowRelicBlacklistTextBox.Text = FieldString(fields, "FinalNeowRelicBlacklist");

        OwnCardsEnabledCheckBox.IsChecked = FieldBool(fields, "OwnCardsEnabled");
        OwnCardsAnyTextBox.Text = FieldString(fields, "OwnCardsAny");
        ColorlessCardsEnabledCheckBox.IsChecked = FieldBool(fields, "ColorlessCardsEnabled");
        ColorlessCardsAnyTextBox.Text = FieldString(fields, "ColorlessCardsAny");
        OtherCardsEnabledCheckBox.IsChecked = FieldBool(fields, "OtherCardsEnabled");
        OtherCardsAnyTextBox.Text = FieldString(fields, "OtherCardsAny");
        PotionAnyTextBox.Text = FieldString(fields, "PotionAny");
        PotionAllTextBox.Text = FieldString(fields, "PotionAll");
        PotionBlacklistTextBox.Text = FieldString(fields, "PotionBlacklist");

        EventAnyTextBox.Clear();
        EventAllTextBox.Text = FieldString(fields, "EventAll");
        EventBlacklistTextBox.Text = FieldString(fields, "EventBlacklist");

        ShopFilterEnabledCheckBox.IsChecked = FieldBool(fields, "ShopFilterEnabled");
        SetCombo(SearchShopLimitComboBox, FieldString(fields, "SearchShopLimit", SelectedComboText(SearchShopLimitComboBox, "5")));
        ShopRequireTextBox.Text = FieldString(fields, "ShopRequire");
        ShopBlacklistTextBox.Text = FieldString(fields, "ShopBlacklist");
        ShopExactTextBox.Text = FieldString(fields, "ShopExact");

        AncientSearchEnabledCheckBox.IsChecked = FieldBool(fields, "AncientSearchEnabled");
        AncientRequireTextBox.Text = FieldString(fields, "AncientRequire");
        AncientBlacklistTextBox.Text = FieldString(fields, "AncientBlacklist");
        AncientOptionRequireTextBox.Text = FieldString(fields, "AncientOptionRequire");
        AncientOptionBlacklistTextBox.Text = FieldString(fields, "AncientOptionBlacklist");
        AncientPaelGoopyCheckBox.IsChecked = FieldBool(fields, "AncientPaelGoopy", true);
        AncientPaelRemovableCheckBox.IsChecked = FieldBool(fields, "AncientPaelRemovable", true);
        AncientPaelHasEventPetCheckBox.IsChecked = FieldBool(fields, "AncientPaelHasEventPet", false);
        AncientOrobasTouchCheckBox.IsChecked = FieldBool(fields, "AncientOrobasTouch", true);
        AncientOrobasToothCheckBox.IsChecked = FieldBool(fields, "AncientOrobasTooth", true);
        AncientTezcataraBasicStrikeCheckBox.IsChecked = FieldBool(fields, "AncientTezcataraBasicStrike", true);
        AncientNonupeipeSwiftCheckBox.IsChecked = FieldBool(fields, "AncientNonupeipeSwift", true);
        AncientTanxInstinctCheckBox.IsChecked = FieldBool(fields, "AncientTanxInstinct", true);
        AncientDarvPandoraCheckBox.IsChecked = FieldBool(fields, "AncientDarvPandora", true);
        SetCombo(AncientDarvActComboBox, FieldString(fields, "AncientDarvAct", "any"));
        UpdateNeowFilterModeUi();
        UpdateTagBuilderUi(syncLegacy: true);
        UpdateSpecialRelicTemplateUi();
        RefreshSearchRequirementTags();
    }

    private void ClearSearchFilterInputs()
    {
        RequireBonesCheckBox.IsChecked = false;
        _dynamicTemplateTerms.Clear();
        foreach (var box in new[]
        {
            NeowAnyTextBox, NeowAllTextBox, NeowBlacklistTextBox,
            BonesRelicAnyTextBox, BonesRelicAllTextBox, BonesRelicBlacklistTextBox,
            BonesCurseAnyTextBox, BonesCurseBlacklistTextBox,
            PredictedRelicAnyTextBox, PredictedRelicAllTextBox, PredictedRelicBlacklistTextBox,
            ProcessCardAnyTextBox, ProcessCardBlacklistTextBox,
            FinalRelicAnyTextBox, FinalRelicAllTextBox, FinalRelicBlacklistTextBox,
            FinalCardAnyTextBox, FinalCardAllTextBox, FinalCardBlacklistTextBox,
            FinalCurseAnyTextBox, FinalCurseBlacklistTextBox,
            FinalPotionAnyTextBox, FinalPotionAllTextBox, FinalPotionBlacklistTextBox, FinalNeowRelicAnyTextBox, FinalNeowRelicBlacklistTextBox,
            OwnCardsAnyTextBox, ColorlessCardsAnyTextBox, OtherCardsAnyTextBox,
            PotionAnyTextBox, PotionAllTextBox, PotionBlacklistTextBox,
            EventAnyTextBox, EventAllTextBox, EventBlacklistTextBox,
            ShopRequireTextBox, ShopBlacklistTextBox, ShopExactTextBox,
            AncientRequireTextBox, AncientBlacklistTextBox, AncientOptionRequireTextBox, AncientOptionBlacklistTextBox,
            NewLeafCardBuilderTextBox, KaleidoscopeGroup1CardTextBox, KaleidoscopeGroup2CardTextBox,
            LostCofferCardTextBox, LostCofferPotionTextBox,
            NewLeafCardAllTextBox, KaleidoscopeGroup1CardAllTextBox, KaleidoscopeGroup2CardAllTextBox,
            LostCofferCardAllTextBox, LostCofferPotionAllTextBox,
        })
        {
            box.Clear();
        }

        SetCombo(NewLeafSourceComboBox, "starter_basic");
        SetCombo(EventActScopeComboBox, "any");
        EventBuilderComboBox.SelectedItem = null;
        EventBuilderComboBox.Text = "";
        BonesCurseBuilderComboBox.SelectedItem = null;
        BonesCurseBuilderComboBox.Text = "";

        SetCombo(NeowFilterModeComboBox, "none");
        SetCombo(BonesRequirementComboBox, "yes");
        SetCombo(BonesRelicModeComboBox, "undirected");
        SetCombo(BonesCurseModeComboBox, "none");
        SetCombo(NeowRouteModeComboBox, "any");
        BonesRelicBuilderComboBox.Text = "";
        DirectRelicBuilderComboBox.Text = "";
        OwnCardsEnabledCheckBox.IsChecked = false;
        ColorlessCardsEnabledCheckBox.IsChecked = false;
        OtherCardsEnabledCheckBox.IsChecked = false;
        ShopFilterEnabledCheckBox.IsChecked = false;
        ShopRelicBuilderComboBox.Text = "";
        AncientSearchEnabledCheckBox.IsChecked = false;
        AncientBuilderComboBox.Text = "";
        AncientOptionBuilderComboBox.Text = "";
        AncientPaelGoopyCheckBox.IsChecked = true;
        AncientPaelRemovableCheckBox.IsChecked = true;
        AncientPaelHasEventPetCheckBox.IsChecked = false;
        AncientOrobasTouchCheckBox.IsChecked = true;
        AncientOrobasToothCheckBox.IsChecked = true;
        AncientTezcataraBasicStrikeCheckBox.IsChecked = true;
        AncientNonupeipeSwiftCheckBox.IsChecked = true;
        AncientTanxInstinctCheckBox.IsChecked = true;
        AncientDarvPandoraCheckBox.IsChecked = true;
        SetCombo(AncientDarvActComboBox, "any");
        UpdateNeowFilterModeUi();
        UpdateTagBuilderUi(syncLegacy: true);
        UpdateSpecialRelicTemplateUi();
        RefreshSearchRequirementTags();
    }


    private List<string> BuildAncientRequireTerms()
    {
        var terms = SplitTerms(AncientRequireTextBox.Text).ToList();
        string darvAct = SelectedDarvActFilter();
        if (darvAct.Length > 0) terms.Add(darvAct + ":Darv");
        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private string SelectedDarvActFilter()
    {
        string v = SelectedComboText(AncientDarvActComboBox, "any").Trim().ToLowerInvariant();
        if (v is "act2" or "2") return "act2";
        if (v is "act3" or "3") return "act3";
        return "";
    }

    private static int ParseNonNegativeInt(string? text, int fallback)
    {
        return int.TryParse((text ?? "").Trim(), out var n) ? Math.Max(0, n) : fallback;
    }

    private int SelectedAnalyzeAscensionLevel() => ParseAscensionLevel(SelectedComboText(AnalyzeAscensionComboBox, "0"));

    private int SelectedAnalyzeRelicSequenceLimit()
    {
        int n = ParsePositiveInt(SelectedComboText(AnalyzeRelicSequenceLimitComboBox, "15"), 15);
        return Math.Clamp(n, 1, 60);
    }

    private int SelectedSearchAscensionLevel() => ParseAscensionLevel(SelectedComboText(SearchAscensionComboBox, "0"));

    private int SelectedSearchEventFilterLimit()
    {
        int n = ParsePositiveInt(SearchEventLimitTextBox.Text, 10);
        n = Math.Clamp(n, 1, 15);
        if (!string.Equals(SearchEventLimitTextBox.Text.Trim(), n.ToString(), StringComparison.Ordinal))
            SearchEventLimitTextBox.Text = n.ToString();
        return n;
    }
    private int MaxSelectedEventFilterLimitUsed()
    {
        int max = SelectedSearchEventFilterLimit();
        foreach (var term in SplitTerms(EventAllTextBox.Text).Concat(SplitTerms(EventBlacklistTextBox.Text)))
        {
            var parsed = ParseEventQueueTermForDisplay(term);
            if (parsed.Limit is int n) max = Math.Max(max, n);
        }
        return Math.Clamp(max, 1, 15);
    }


    private static int ParseAscensionLevel(string? text)
    {
        string normalized = (text ?? "").Trim();
        if (normalized.StartsWith("A", StringComparison.OrdinalIgnoreCase)) normalized = normalized[1..];
        int value = ParseNonNegativeInt(normalized, 0);
        if (value < 0) return 0;
        if (value > 10) return 10;
        return value;
    }

    private static void Put(JsonObject obj, string name, string? value) => obj[name] = value ?? "";
    private static void Put(JsonObject obj, string name, bool value) => obj[name] = value;

    private static string FieldString(JsonObject obj, string name, string fallback = "")
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node is null) return fallback;
        try { return node.GetValue<string>() ?? fallback; }
        catch { return node.ToString(); }
    }

    private static bool FieldBool(JsonObject obj, string name, bool fallback = false)
    {
        if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node is null) return fallback;
        try { return node.GetValue<bool>(); }
        catch
        {
            var text = node.ToString();
            if (bool.TryParse(text, out bool b)) return b;
            return fallback;
        }
    }

    private static void SetCombo(ComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var item in combo.Items)
        {
            if (item is not ComboBoxItem cb) continue;
            if (cb.Tag is string tag && tag.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cb;
                return;
            }
            if (cb.Content is string s && s.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cb;
                return;
            }
        }
    }

    private void StopSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchCts?.Cancel();
        StopSearchButton.IsEnabled = false;
        BottomStatusText.Text = "正在停止筛种...";
    }

    private void AppendSearch(string text)
    {
        Dispatcher.Invoke(() =>
        {
            SearchOutputBox.AppendText(text);
            SearchOutputBox.ScrollToEnd();
        });
    }

    private void UpdateSearchProgress(long checkedCount, int found)
    {
        Dispatcher.Invoke(() => SearchSummaryText.Text = $"已尝试 {checkedCount}，命中 {found}。详情请打开“筛种结果”。");
    }

    private void LoadEvents_Click(object sender, RoutedEventArgs e) => LoadEvents();

    private void LoadEvents()
    {
        try
        {
            using var doc = BuildRuntimeConfigDocument(includeSearchFields: false);
            var plan = SearchPlan.FromConfigForFullDetails(doc.RootElement.Clone(), _rootDir);
            var data = plan.Data;
            if (data is null)
            {
                EventDetailBox.Text = "GameData 未加载。";
                return;
            }

            var ruleDict = new Dictionary<string, EventRuleView>(StringComparer.OrdinalIgnoreCase);
            string ruleJson = JsonSerializer.Serialize(data.EventRuleBookItems(), JsonOut.Options);
            foreach (var r in ParseEventRules(ruleJson))
            {
                // GameData.EventRuleBookItems() 的 id 是运行时 ID（例如 SlipperyBridge），
                // event_texts.json 的 event_id 是源码/本地化 ID（例如 SLIPPERY_BRIDGE）。
                // 两边必须同时建索引，否则“有条件”筛选会只命中极少数本来就同名的事件。
                if (!string.IsNullOrWhiteSpace(r.Id)) ruleDict[r.Id] = r;
                if (!string.IsNullOrWhiteSpace(r.SourceId)) ruleDict[r.SourceId] = r;
            }

            string? eventTextsPath = RollDataPaths.FindEventTexts(_rootDir);
            if (eventTextsPath is not null)
            {
                _allEvents = ParseEventTexts(File.ReadAllText(eventTextsPath, Encoding.UTF8), ruleDict);
                int dynamicEvents = _allEvents.Count(x => x.HasDynamicTransition);
                EventRuleSummaryText.Text = $"events={_allEvents.Count} rules={data.EventAllowedRulesCount} rule_manual={data.EventAllowedRulesManualReviewCount} dynamic={dynamicEvents}";
                AppendLog($"Event encyclopedia loaded: {_allEvents.Count} events with reviewed texts; dynamic={dynamicEvents}");
            }
            else
            {
                _allEvents = ruleDict.Values.Select(EventEncyclopediaItem.FromRuleOnly)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                EventRuleSummaryText.Text = $"data/event_texts.json 未找到；仅显示条件规则 rules={data.EventAllowedRulesCount}";
                AppendLog("data/event_texts.json not found; loaded rules only: " + _allEvents.Count);
            }
            PopulateEventDropdownItems();
            ApplyEventFilter();
        }
        catch (Exception ex)
        {
            EventDetailBox.Text = ex.ToString();
        }
    }

    private void EnsureEventDropdownItems()
    {
        if (_eventItems.Count > 0) return;
        if (_allEvents.Count == 0)
        {
            LoadEvents();
            return;
        }
        PopulateEventDropdownItems();
    }

    private void PopulateEventDropdownItems()
    {
        _eventItems.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in _allEvents
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            string runtimeId = !string.IsNullOrWhiteSpace(ev.Rule?.Id)
                ? ev.Rule!.Id
                : (!string.IsNullOrWhiteSpace(ev.ClassName) ? ev.ClassName : SourceIdToRuntimeIdLocal(ev.Id));
            if (string.IsNullOrWhiteSpace(runtimeId)) runtimeId = ev.Id;
            if (!seen.Add(runtimeId)) continue;

            string sourceId = !string.IsNullOrWhiteSpace(ev.SourceId) ? ev.SourceId : ev.Id;
            var item = new ItemAliasView(sourceId, runtimeId, "event", ev.Name, ev.EngName);
            _eventItems.Add(item);
            AddItemAlias(item, item.RuntimeId);
            AddItemAlias(item, item.SourceId);
            AddItemAlias(item, ev.Id);
            AddItemAlias(item, ev.ClassName);
            AddItemAlias(item, ev.Name);
            AddItemAlias(item, ev.EngName);
        }
        EventBuilderComboBox.ItemsSource = _eventItems;
    }

    private static List<EventRuleView> ParseEventRules(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<EventRuleView>();
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object) list.Add(ParseEventRuleBookItem(item));
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // Official data/event_rules.json shape:
            // { "event_allowed_rules": { "SLIPPERY_BRIDGE": { ... } } }
            if (root.TryGetProperty("event_allowed_rules", out var rules) && rules.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in rules.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        list.Add(ParseOfficialEventRule(prop.Name, prop.Value));
                }
            }
            else
            {
                // Defensive fallback for any object dictionary of rule nodes.
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        list.Add(ParseOfficialEventRule(prop.Name, prop.Value));
                }
            }
        }

        return list
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) || !string.IsNullOrWhiteSpace(x.SourceId))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static EventRuleView ParseEventRuleBookItem(JsonElement item)
    {
        var view = new EventRuleView
        {
            Id = item.Prop("id").Str(),
            SourceId = item.Prop("source_id").Str(),
            Name = item.Prop("name").Str(),
            ClassName = item.Prop("class_name").Str(),
            Summary = item.Prop("summary_zh").Str(),
            Confidence = item.Prop("confidence").Str(),
            ManualReview = item.Prop("manual_review").Bool(false),
            SourceFile = item.Prop("source_file").Str(),
            SourceLine = item.Prop("source_line").Int(0),
        };
        if (string.IsNullOrWhiteSpace(view.SourceId)) view.SourceId = item.Prop("event_id").Str();
        if (string.IsNullOrWhiteSpace(view.Id)) view.Id = SourceIdToRuntimeIdLocal(view.SourceId);
        if (string.IsNullOrWhiteSpace(view.Name)) view.Name = view.Id;
        AddRuleActsAndConditions(view, item);
        return view;
    }

    private static EventRuleView ParseOfficialEventRule(string sourceId, JsonElement item)
    {
        var displayName = item.Prop("display_name");
        string zh = CleanEventRuleText(displayName.Prop("zhs").Str());
        string eng = CleanEventRuleText(displayName.Prop("eng").Str());
        var view = new EventRuleView
        {
            Id = SourceIdToRuntimeIdLocal(sourceId),
            SourceId = item.Prop("event_id").Str(sourceId),
            Name = string.IsNullOrWhiteSpace(zh) ? (string.IsNullOrWhiteSpace(eng) ? sourceId : eng) : zh,
            ClassName = item.Prop("class_name").Str(),
            Summary = CleanEventRuleText(item.Prop("allowed_summary_zh").Str()),
            Confidence = item.Prop("confidence").Str(),
            ManualReview = item.Prop("manual_review").Bool(false),
            SourceFile = item.Prop("source_file").Str(),
            SourceLine = item.Prop("source_line").Int(0),
        };
        if (string.IsNullOrWhiteSpace(view.Summary)) view.Summary = item.Prop("allowed_summary_en").Str();
        if (string.IsNullOrWhiteSpace(view.SourceId)) view.SourceId = sourceId;
        AddRuleActsAndConditions(view, item);
        return view;
    }

    private static void AddRuleActsAndConditions(EventRuleView view, JsonElement item)
    {
        if (item.TryGetProperty("acts", out var acts) && acts.ValueKind == JsonValueKind.Array)
            foreach (var a in acts.EnumerateArray()) view.Acts.Add(a.GetString() ?? "");

        if (item.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in conds.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                string display = c.Prop("display").Str();
                if (string.IsNullOrWhiteSpace(display)) display = c.Prop("description_zh").Str();
                if (string.IsNullOrWhiteSpace(display)) display = c.Prop("description_en").Str();
                if (string.IsNullOrWhiteSpace(display)) display = c.Prop("type").Str() + " " + c.Prop("operator").Str(c.Prop("op").Str()) + " " + c.Prop("value").Str();
                if (!string.IsNullOrWhiteSpace(display)) view.Conditions.Add(CleanEventRuleText(display));
            }
        }
    }


    private static string CleanEventRuleText(string text)
    {
        return (text ?? "").Replace("\\n", "\n").Replace("\r", "").Trim();
    }

    private static List<EventEncyclopediaItem> ParseEventTexts(string json, Dictionary<string, EventRuleView> rules)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<EventEncyclopediaItem>();
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var prop in events.EnumerateObject())
        {
            var e = prop.Value;
            string id = e.Prop("event_id").Str();
            if (string.IsNullOrWhiteSpace(id)) id = prop.Name;
            string className = e.Prop("class_name").Str();
            if (!rules.TryGetValue(id, out var rule))
                rules.TryGetValue(NormalizeLookup(id), out rule);
            if (rule is null && !string.IsNullOrWhiteSpace(className))
                rules.TryGetValue(className, out rule);
            var item = new EventEncyclopediaItem
            {
                Id = id,
                SourceId = rule?.SourceId ?? id,
                ClassName = className,
                Name = e.Prop("display_name").Prop("zhs").Str(),
                EngName = e.Prop("display_name").Prop("eng").Str(),
                IsSharedEvent = e.Prop("is_shared_event").Bool(false),
                HasCondition = rule is not null,
                Rule = rule,
                SourceFile = e.Prop("source").Prop("source_file").Str(),
            };
            if (string.IsNullOrWhiteSpace(item.Name)) item.Name = rule?.Name ?? item.EngName ?? item.Id;
            if (string.IsNullOrWhiteSpace(item.EngName)) item.EngName = item.Id;

            if (e.TryGetProperty("regions", out var regions) && regions.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in regions.EnumerateArray())
                {
                    var region = r.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(region)) item.Regions.Add(region);
                }
            }
            if (item.IsSharedEvent && !item.Regions.Contains("SharedEvents", StringComparer.OrdinalIgnoreCase)) item.Regions.Add("SharedEvents");

            if (e.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (var w in warnings.EnumerateArray())
                {
                    var t = w.GetString();
                    if (!string.IsNullOrWhiteSpace(t)) item.Warnings.Add(t);
                }
            }

            if (e.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pages.EnumerateArray())
                {
                    var page = new EventPageText
                    {
                        PageId = p.Prop("page_id").Str(),
                        TitleZhs = p.Prop("title").Prop("zhs").Str(),
                        DescriptionZhs = p.Prop("description").Prop("zhs").Str(),
                        DescriptionEng = p.Prop("description").Prop("eng").Str(),
                    };
                    if (p.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in opts.EnumerateArray())
                        {
                            page.Options.Add(new EventOptionText
                            {
                                OptionId = o.Prop("option_id").Str(),
                                LabelZhs = o.Prop("label").Prop("zhs").Str(),
                                DescriptionZhs = o.Prop("description").Prop("zhs").Str(),
                                NextPage = o.Prop("next_page").Str(),
                                Confidence = o.Prop("confidence").Str(),
                                TransitionType = o.Prop("transition_type").Str(),
                                TransitionNoteZhs = o.Prop("transition_note").Prop("zhs").Str(),
                                TransitionNoteEng = o.Prop("transition_note").Prop("eng").Str(),
                            });
                        }
                    }
                    item.Pages.Add(page);
                }
            }
            list.Add(item);
        }
        return list.OrderBy(x => x.RegionSortKey).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void EventSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyEventFilter();
    private void EventFilterChanged(object sender, RoutedEventArgs e) => ApplyEventFilter();

    private void ApplyEventFilter()
    {
        if (EventListBox is null) return;
        string q = (EventSearchTextBox.Text ?? "").Trim();
        IEnumerable<EventEncyclopediaItem> items = _allEvents;

        var enabledRegions = EnabledEventRegions();
        items = items.Where(x => x.Regions.Any(r => enabledRegions.Contains(r)));

        bool showConditional = FilterConditionalCheckBox?.IsChecked == true;
        bool showNoCondition = FilterNoConditionCheckBox?.IsChecked == true;
        items = items.Where(x => (x.HasCondition && showConditional) || (!x.HasCondition && showNoCondition));

        // 动态分支是事件流程本身的属性，不再作为筛选项隐藏。

        if (!string.IsNullOrWhiteSpace(q)) items = items.Where(x => x.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase));
        var shown = items.Take(500).ToList();
        EventListBox.ItemsSource = shown;
        if (shown.Count > 0 && EventListBox.SelectedItem is null) EventListBox.SelectedIndex = 0;
        EventRuleSummaryText.Text = _allEvents.Count > 0
            ? $"显示 {shown.Count} / {_allEvents.Count} | 条件 {_allEvents.Count(x => x.HasCondition)} | 动态分支 {_allEvents.Count(x => x.HasDynamicTransition)}"
            : "尚未加载事件百科";
    }

    private HashSet<string> EnabledEventRegions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (FilterOvergrowthCheckBox?.IsChecked == true) set.Add("Overgrowth");
        if (FilterUnderdocksCheckBox?.IsChecked == true) set.Add("Underdocks");
        if (FilterHiveCheckBox?.IsChecked == true) set.Add("Hive");
        if (FilterGloryCheckBox?.IsChecked == true) set.Add("Glory");
        if (FilterSharedCheckBox?.IsChecked == true) set.Add("SharedEvents");
        return set;
    }

    private void EventListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventListBox.SelectedItem is EventEncyclopediaItem v) EventDetailBox.Text = v.DetailText();
    }

    private void CopyEventDetail_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(EventDetailBox.Text ?? "");

    private void CopyStatus_Click(object sender, RoutedEventArgs e) => SetClipboardSafe(StatusBox.Text ?? "", "已复制数据状态");

    private void RefreshStatus_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("RollTheSpire2 数据状态");
        sb.AppendLine("版本: v2.1.1");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(new string('=', 72));
        sb.AppendLine();

        sb.AppendLine("【路径】");
        sb.AppendLine("RootDir: " + _rootDir);
        sb.AppendLine("Config: " + FileState(_configPath));
        sb.AppendLine("RollCore: " + typeof(SearchPlan).Assembly.Location);
        sb.AppendLine();

        string? sourceData = RollDataPaths.FindSourceData(_rootDir);
        string? eventRules = RollDataPaths.FindEventRules(_rootDir);
        string? eventTexts = RollDataPaths.FindEventTexts(_rootDir);
        string? entityIndex = RollDataPaths.FindEntityIndex(_rootDir);
        string effectPath = Path.Combine(_rootDir, "data", "neow_relic_effects.json");
        string unlockPath = Path.Combine(_rootDir, "profiles", "unlock_profile.json");

        sb.AppendLine("【核心数据文件】");
        sb.AppendLine("sts2_data: " + FileState(sourceData ?? Path.Combine(_rootDir, RollDataPaths.OfficialSourceData)));
        sb.AppendLine("event_rules: " + FileState(eventRules ?? Path.Combine(_rootDir, RollDataPaths.OfficialEventRules)));
        sb.AppendLine("event_texts: " + FileState(eventTexts ?? Path.Combine(_rootDir, RollDataPaths.OfficialEventTexts)));
        sb.AppendLine("entity_index: " + FileState(entityIndex ?? Path.Combine(_rootDir, RollDataPaths.OfficialEntityIndex)) + " | runtime_loaded=" + _entityIndex.Count);
        sb.AppendLine("neow_relic_effects: " + FileState(effectPath) + $" | relics={_neowRelicEffects.Count} templates={_neowTemplatesById.Count}");
        sb.AppendLine();

        var extracted = ExtractedSourceDataStatus.Load(_rootDir);
        sb.AppendLine("【源码抽取数据概览】");
        sb.AppendLine("loaded=" + extracted.Loaded + " | file=" + extracted.FilePath + " | schema=" + extracted.Version);
        sb.AppendLine($"acts={extracted.ActsCount}, cards={extracted.CardsCount}, relics={extracted.RelicsCount}, potions={extracted.PotionsCount}, curses={extracted.CursesCount}");
        sb.AppendLine($"shared_events={extracted.SharedEventsCount}, shared_ancients={extracted.SharedAncientsCount}, encounter_tags={extracted.EncounterTagsCount}");
        sb.AppendLine($"neow_pool={extracted.NeowRewardPoolCount}, neow_routes={extracted.NeowSpecialRoutesCount}, ancient_options={extracted.AncientOptionsCount}, ancient_conditionals={extracted.AncientConditionalOptionsCount}");
        sb.AppendLine($"localization_items={extracted.LocalizationItemsCount}, zhs_names={extracted.LocalizationZhsNamesCount}, languages={extracted.LocalizationLanguages}");
        sb.AppendLine($"source_warnings={extracted.WarningsCount}, source_manual_review={extracted.ManualReviewRulesCount}");
        if (extracted.WarningItems.Count > 0)
        {
            sb.AppendLine("warnings_sample:");
            foreach (var w in extracted.WarningItems.Take(8)) sb.AppendLine("  - " + w);
        }
        sb.AppendLine();

        sb.AppendLine("【事件规则 / 事件百科】");
        AppendJsonSummary(sb, eventRules, "event_rules", new[] { "event_allowed_rules", "modify_next_event_hooks", "manual_review", "warnings" });
        AppendJsonSummary(sb, eventTexts, "event_texts", new[] { "events", "missing" });
        sb.AppendLine($"WPF event tooltip index: cached={_eventTooltipIndex.Count}");
        sb.AppendLine();

        sb.AppendLine("【实体索引 / Neow 遗物效果】");
        AppendJsonSummary(sb, entityIndex, "entity_index", new[] { "entities" });
        AppendJsonSummary(sb, effectPath, "neow_relic_effects", new[] { "relics", "manual_review" });
        sb.AppendLine();

        try
        {
            using var doc = BuildRuntimeConfigDocument(includeSearchFields: true);
            var plan = SearchPlan.FromConfigForFullDetails(doc.RootElement.Clone(), _rootDir);
            string profileMode = plan.Root.Prop("unlock_profile").Prop("mode").Str("all_unlocked") ?? "all_unlocked";
            string mpUnlock = plan.Root.Prop("multiplayer_unlock_mode").Str("all_unlocked") ?? "all_unlocked";

            sb.AppendLine("【解锁档案】");
            sb.AppendLine("mode=" + (profileMode.Equals("profile", StringComparison.OrdinalIgnoreCase) ? "progress.save 限制解锁池" : "全解锁模式"));
            sb.AppendLine("multiplayer_unlock_mode=" + mpUnlock);
            sb.AppendLine("unlock_profile: " + FileState(unlockPath));
            AppendUnlockProfileSummary(sb, unlockPath, profileMode);
            sb.AppendLine();

            sb.AppendLine("【当前运行配置快照】");
            sb.AppendLine("character=" + plan.Character + " | ascension=A" + plan.Root.Prop("player").Prop("ascension").Int(0));
            sb.AppendLine("run=" + (plan.PlayersCount > 1 ? "multiplayer" : "singleplayer") + " | players=" + plan.PlayersCount + " | target_net_id=" + plan.TargetNetIdText + " | rng=" + GameRngVersions.ToConfig(plan.RngVersion));
            if (plan.PlayersOrder.Count > 1) sb.AppendLine("players_order=" + string.Join(" | ", plan.PlayersOrder.Select((p, i) => $"{i + 1}.{p.Name}:{p.Character}:{p.NetId}")));
            sb.AppendLine("neow_filter_mode=" + plan.NeowFilterMode + " | process_filter=" + plan.NeowProcessFilter.HasFilter + " | final_filter=" + plan.NeowFinalFilter.HasFilter);
            sb.AppendLine("shop_limit=" + plan.ShopLimit + " | shop_require=" + plan.ShopRequire.Count + " | shop_exact=" + plan.ShopExact.Count + " | shop_blacklist=" + plan.ShopBlacklist.Count);
            sb.AppendLine("event_queue: enabled=" + plan.EventQueueEnabled + " show=" + plan.EventQueueShow + " full=" + plan.EventQueueShowFull + " display_limit=" + plan.EventQueueLimit + " filter_limit=" + plan.EventQueueFilterLimit + " filter_terms=" + plan.EventQueueTerms.HasTerms);
            sb.AppendLine("ancient: enabled=" + plan.AncientEnabled + " require=" + plan.AncientRequire.Count + " blacklist=" + plan.AncientBlacklist.Count + " option_require=" + plan.AncientOptionRequire.Count + " option_blacklist=" + plan.AncientOptionBlacklist.Count);
            sb.AppendLine("relic_queue: show=" + plan.RelicQueueShow + " limit=" + plan.RelicQueueLimit);
            if (plan.Data is not null)
            {
                sb.AppendLine();
                sb.AppendLine("【RollCore Runtime Data】");
                sb.AppendLine($"SourceData: shared_events={plan.Data.SourceSharedEvents().Count}, shared_ancients={plan.Data.SourceSharedAncients().Count}, warnings={plan.Data.SourceExtractorWarningsCount}");
                sb.AppendLine($"EventRules: loaded={plan.Data.EventAllowedRulesLoaded}, rules={plan.Data.EventAllowedRulesCount}, manual={plan.Data.EventAllowedRulesManualReviewCount}, hooks={plan.Data.ModifyNextEventHooksCount}");
                SidebarStatusText.Text = $"rules={plan.Data.EventAllowedRulesCount}, manual={plan.Data.EventAllowedRulesManualReviewCount}";
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("【当前运行配置快照】");
            sb.AppendLine("Error: " + ex.Message);
            SidebarStatusText.Text = "数据状态异常";
        }

        sb.AppendLine();
        sb.AppendLine("【搜索历史 / 收藏库】");
        var history = _historyStore;
        if (history is null)
        {
            sb.AppendLine("search_history: 未初始化");
        }
        else
        {
            sb.AppendLine("search_history: " + FileState(history.Path));
            try
            {
                var records = history.Load();
                int favoriteCount = records.Count(r => r.Favorite);
                int ratedCount = records.Count(r => r.Rating > 0);
                string rngSet = string.Join(", ", records.Select(r => r.RngVersion).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
                sb.AppendLine($"schema={SeedHistoryStore.CurrentSchemaVersion}, records={records.Count}, starred={favoriteCount}, rated={ratedCount}, favorites/search history enabled=True");
                if (!string.IsNullOrWhiteSpace(rngSet)) sb.AppendLine("rng_versions=" + rngSet);
                sb.AppendLine("path=" + RollDataPaths.SafeRel(_rootDir, history.Path));
                if (_candidatePoolStore is not null)
                {
                    var pools = _candidatePoolStore.Load();
                    int seedCount = pools.Sum(p => p.Seeds.Count);
                    sb.AppendLine($"candidate_pool: enabled=True, schema={CandidatePoolStore.CurrentSchemaVersion}, pools={pools.Count}, seeds={seedCount}");
                    sb.AppendLine("candidate_path=" + RollDataPaths.SafeRel(_rootDir, _candidatePoolStore.Path));
                }
                sb.AppendLine("analysis_database: 暂缓；未来默认保存统计结果与少量样例，不保存每个 seed 完整详情。");
            }
            catch (Exception ex)
            {
                sb.AppendLine("records=parse_error=" + ex.Message);
            }
        }
        StatusBox.Text = sb.ToString();
    }

    private string FileState(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<none> | exists=False";
        bool exists = File.Exists(path);
        if (!exists) return RollDataPaths.SafeRel(_rootDir, path) + " | exists=False";
        var info = new FileInfo(path);
        return RollDataPaths.SafeRel(_rootDir, path) + $" | exists=True | size={FormatBytes(info.Length)} | modified={info.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1024.0;
        return mb.ToString("0.00", CultureInfo.InvariantCulture) + " MB";
    }

    private static void AppendJsonSummary(StringBuilder sb, string? path, string label, string[] keys)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            sb.AppendLine(label + ": missing");
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            var parts = new List<string>();
            string schema = root.Prop("schema").Str(root.Prop("version").Str(""));
            if (!string.IsNullOrWhiteSpace(schema)) parts.Add("schema=" + schema);
            foreach (var key in keys)
            {
                var node = root.Prop(key);
                if (node is null) continue;
                int count = node.Value.ValueKind switch
                {
                    JsonValueKind.Object => node.Value.EnumerateObject().Count(),
                    JsonValueKind.Array => node.Value.GetArrayLength(),
                    _ => -1,
                };
                if (count >= 0) parts.Add(key + "=" + count);
            }
            sb.AppendLine(label + ": " + string.Join(", ", parts));
        }
        catch (Exception ex)
        {
            sb.AppendLine(label + ": parse_error=" + ex.Message);
        }
    }

    private void AppendUnlockProfileSummary(StringBuilder sb, string path, string mode)
    {
        if (!File.Exists(path))
        {
            sb.AppendLine("unlock_profile_summary: 未找到；当前只能使用全解锁模式。");
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            int schema = root.Prop("schema_version").Int(root.Prop("schema").Int(0));
            int total = root.Prop("total_unlocks").Int(0);
            var epochs = root.Prop("epochs");
            int revealedEpochs = 0;
            if (epochs is not null && epochs.Value.ValueKind == JsonValueKind.Array)
                revealedEpochs = epochs.Value.EnumerateArray().Count(e => string.Equals(e.Prop("state").Str(""), "revealed", StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"unlock_profile_summary: schema={schema}, total_unlocks={total}, revealed_epochs={revealedEpochs}");
            int cards = CountJsonArray(root, "discovered_cards");
            int relics = CountJsonArray(root, "discovered_relics");
            int potions = CountJsonArray(root, "discovered_potions");
            int events = CountJsonArray(root, "discovered_events");
            int acts = CountJsonArray(root, "discovered_acts");
            sb.AppendLine($"discovered: cards={cards}, relics={relics}, potions={potions}, events={events}, acts={acts}");
            sb.AppendLine(mode.Equals("profile", StringComparison.OrdinalIgnoreCase)
                ? "effective: 当前已启用 progress.save 解锁限制。"
                : "effective: 已找到档案，但当前仍使用全解锁模式。勾选配置页“按 progress.save 限制解锁池”后才会启用。");
        }
        catch (Exception ex)
        {
            sb.AppendLine("unlock_profile_summary: parse_error=" + ex.Message);
        }
    }

    private static int CountJsonArray(JsonElement root, string name)
    {
        var node = root.Prop(name);
        return node is not null && node.Value.ValueKind == JsonValueKind.Array ? node.Value.GetArrayLength() : 0;
    }

    private void AppendLog(string text)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
        LogBox.ScrollToEnd();
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private void HistoryQueryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        _historyRecords.Clear();
        if (_historyStore is null)
        {
            HistoryStatusText.Text = "收藏库未初始化";
            return;
        }
        string query = HistoryQueryTextBox.Text ?? "";
        var records = _historyStore.Query(query);
        foreach (var r in records)
            _historyRecords.Add(new SeedHistoryRecordView(r));
        HistoryStatusText.Text = $"{records.Count} 条记录 · {RollDataPaths.SafeRel(_rootDir, _historyStore.Path)}";
        if (_historyRecords.Count > 0 && HistoryDataGrid.SelectedItem is null)
            HistoryDataGrid.SelectedIndex = 0;
    }

    private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            ClearHistorySelectionFields();
            return;
        }
        var r = view.Record;
        HistorySeedBox.Text = r.Seed;
        HistoryMetaBox.Text = $"{r.Source} · {SeedHistoryStore.DisplayCharacter(r.Character)} · A{r.Ascension} · RNG={r.RngVersion} · {r.CreatedAt:yyyy-MM-dd HH:mm:ss}";
        HistoryFavoriteCheckBox.IsChecked = r.Favorite;
        SetHistoryRatingCombo(r.Rating);
        HistoryTagsBox.Text = string.Join(", ", r.Tags);
        HistoryNoteBox.Text = r.Note ?? "";
        HistoryDetailBox.Text = BuildHistoryDetailText(r);
    }

    private void ClearHistorySelectionFields()
    {
        HistorySeedBox.Clear();
        HistoryMetaBox.Clear();
        HistoryFavoriteCheckBox.IsChecked = false;
        SetHistoryRatingCombo(0);
        HistoryTagsBox.Clear();
        HistoryNoteBox.Clear();
        HistoryDetailBox.Clear();
    }

    private static string BuildHistoryDetailText(SeedHistoryRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Seed: " + r.Seed);
        sb.AppendLine("角色: " + SeedHistoryStore.DisplayCharacter(r.Character) + " | A" + r.Ascension + " | " + r.RunMode + " | net_id=" + r.TargetNetId);
        sb.AppendLine("来源: " + r.Source);
        sb.AppendLine("RNG: " + r.RngVersion + " | 游戏版本: " + (string.IsNullOrWhiteSpace(r.GameVersion) ? "<未记录>" : r.GameVersion) + " | Roll: " + r.AppVersion);
        sb.AppendLine("收藏: " + (r.Favorite ? "是" : "否"));
        if (r.LastAnalyzedAt is not null) sb.AppendLine("最近分析: " + r.LastAnalyzedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        if (r.VerifiedAt is not null) sb.AppendLine("实测确认: " + r.VerifiedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("标签: " + (r.Tags.Count == 0 ? "<无>" : string.Join(", ", r.Tags)));
        if (!string.IsNullOrWhiteSpace(r.Note)) sb.AppendLine("备注: " + r.Note);
        sb.AppendLine("创建: " + r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("更新: " + r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(r.Summary))
        {
            sb.AppendLine();
            sb.AppendLine("【摘要】");
            sb.AppendLine(r.Summary);
        }
        if (!string.IsNullOrWhiteSpace(r.HitExplanation))
        {
            sb.AppendLine();
            sb.AppendLine("【命中解释】");
            sb.AppendLine(r.HitExplanation);
        }
        if (!string.IsNullOrWhiteSpace(r.Detail))
        {
            sb.AppendLine();
            sb.AppendLine("【详情】");
            sb.AppendLine(r.Detail);
        }
        return sb.ToString();
    }

    private void SaveAnalyzeToHistory_Click(object sender, RoutedEventArgs e)
    {
        string seed = _lastAnalyzeSeed;
        if (string.IsNullOrWhiteSpace(seed)) seed = SeedTextBox.Text.Trim();
        string detail = AnalyzeOutputBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(seed) || string.IsNullOrWhiteSpace(detail))
        {
            BottomStatusText.Text = "请先完成一次单种分析，再收藏。";
            return;
        }
        var record = new SeedHistoryRecord
        {
            Seed = seed.Trim(),
            NormalizedSeed = seed.Trim().ToUpperInvariant(),
            Character = string.IsNullOrWhiteSpace(_lastAnalyzeCharacter) ? SelectedCharacter() : _lastAnalyzeCharacter,
            Ascension = _lastAnalyzeAscension,
            RunMode = IsMultiplayerUiSelected() ? "multiplayer" : "singleplayer",
            TargetNetId = SelectedTargetNetId(),
            RngVersion = SelectedGameRngVersion(),
            Source = "single_analysis",
            Favorite = true,
            Rating = 0,
            GameVersion = "v0.107.1+",
            LastAnalyzedAt = DateTime.Now,
            Tags = SeedHistoryStore.NormalizeTags(new[] { "单种分析", "v2" }),
            Summary = string.IsNullOrWhiteSpace(_lastAnalyzeSummary) ? "单种分析收藏" : _lastAnalyzeSummary,
            HitExplanation = "单种分析记录；未必代表筛种命中。",
            Detail = detail,
            AppVersion = AppVersionText,
        };
        AddHistoryRecord(record, "已收藏当前单种分析");
    }

    private void SaveSelectedSearchHitToHistory_Click(object sender, RoutedEventArgs e)
    {
        if (SearchHitsListBox.SelectedItem is not SearchHitView hit)
        {
            BottomStatusText.Text = "请先选择一个筛种命中结果。";
            return;
        }
        var record = new SeedHistoryRecord
        {
            Seed = hit.Seed,
            NormalizedSeed = hit.Seed.Trim().ToUpperInvariant(),
            Character = hit.Character,
            Ascension = hit.Ascension,
            RunMode = hit.RunMode,
            TargetNetId = hit.TargetNetId,
            RngVersion = string.IsNullOrWhiteSpace(hit.RngVersion) ? SelectedGameRngVersion() : hit.RngVersion,
            Source = "search_hit",
            Favorite = true,
            Rating = 0,
            GameVersion = "v0.107.1+",
            Tags = SeedHistoryStore.NormalizeTags(new[] { "筛种命中", hit.CategoryText, "v2" }),
            Summary = hit.Summary,
            HitExplanation = ExtractHitExplanation(hit.Detail),
            Detail = hit.Detail,
            AppVersion = AppVersionText,
        };
        AddHistoryRecord(record, "已收藏选中筛种命中");
    }

    private void AddHistoryRecord(SeedHistoryRecord record, string message)
    {
        if (_historyStore is null)
        {
            BottomStatusText.Text = "收藏库未初始化。";
            return;
        }
        try
        {
            var saved = _historyStore.AddOrUpdate(record);
            BottomStatusText.Text = message + ": " + saved.Seed;
            AppendLog(message + ": " + saved.Seed);
            if (HistoryPage.Visibility == Visibility.Visible) RefreshHistory();
        }
        catch (Exception ex)
        {
            BottomStatusText.Text = "收藏失败：" + ex.Message;
            AppendLog("History save failed: " + ex.Message);
        }
    }

    private static string ExtractHitExplanation(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "";
        const string start = "【命中解释】";
        int i = detail.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return "";
        int j = detail.IndexOf("\n【", i + start.Length, StringComparison.Ordinal);
        string block = j > i ? detail.Substring(i + start.Length, j - i - start.Length) : detail.Substring(i + start.Length);
        return block.Trim();
    }

    private static string BuildAnalyzeHistorySummary(IEnumerable<ResultCardView> cards)
    {
        var titles = cards.Select(c => c.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Take(4).ToList();
        return titles.Count == 0 ? "单种分析结果" : string.Join(" / ", titles);
    }

    private void SaveHistoryNote_Click(object sender, RoutedEventArgs e)
    {
        if (_historyStore is null || HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        try
        {
            _historyStore.UpdateUserFields(view.Id, SeedHistoryStore.NormalizeTags(new[] { HistoryTagsBox.Text }), HistoryNoteBox.Text ?? "", HistoryFavoriteCheckBox.IsChecked == true, SelectedHistoryRating());
            BottomStatusText.Text = "已保存收藏 / 标签 / 备注";
            RefreshHistory();
        }
        catch (Exception ex)
        {
            BottomStatusText.Text = "保存备注失败：" + ex.Message;
        }
    }

    private void DeleteHistoryRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_historyStore is null || HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        if (MessageBox.Show("确定删除收藏记录：" + view.Seed + "？", "RollTheSpire2", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _historyStore.Delete(view.Id);
        BottomStatusText.Text = "已删除收藏记录: " + view.Seed;
        RefreshHistory();
    }

    private void FillHistoryToAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        var r = view.Record;
        SeedTextBox.Text = r.Seed;
        SetCombo(CharacterComboBox, r.Character);
        SetCombo(AnalyzeAscensionComboBox, r.Ascension.ToString(CultureInfo.InvariantCulture));
        SetGameRngVersionIfAvailable(r.RngVersion);
        ShowPage(AnalyzePage, "单种分析", "已从收藏库填入 seed、角色和进阶，可点击“开始分析”重新校验。 ");
        BottomStatusText.Text = "已从收藏库填入单种分析: " + r.Seed;
    }

    private void CopyHistorySeed_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        SetClipboardSafe(view.Seed, "已复制收藏 Seed: " + view.Seed);
    }

    private void CopyHistoryDetail_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        SetClipboardSafe(BuildHistoryDetailText(view.Record), "已复制收藏详情: " + view.Seed);
    }

    private void CopyVisibleHistorySeeds_Click(object sender, RoutedEventArgs e)
    {
        if (_historyRecords.Count == 0)
        {
            BottomStatusText.Text = "当前没有可复制的收藏记录。";
            return;
        }
        var text = string.Join(Environment.NewLine, _historyRecords.Select(v => v.Seed).Where(x => !string.IsNullOrWhiteSpace(x)));
        SetClipboardSafe(text, "已复制当前可见收藏 Seed 列表：" + _historyRecords.Count + " 条");
    }

    private void ExportVisibleHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_historyStore is null)
        {
            BottomStatusText.Text = "收藏库未初始化。";
            return;
        }
        try
        {
            string file = _historyStore.ExportVisibleRecords(_historyRecords);
            BottomStatusText.Text = "已导出当前可见收藏记录：" + RollDataPaths.SafeRel(_rootDir, file);
            AppendLog("History exported: " + file);
        }
        catch (Exception ex)
        {
            BottomStatusText.Text = "导出收藏记录失败：" + ex.Message;
        }
    }

    private void ReanalyzeHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is not SeedHistoryRecordView view)
        {
            BottomStatusText.Text = "请先选择一条收藏记录。";
            return;
        }
        FillHistoryToAnalyze_Click(sender, e);
        Analyze_Click(sender, e);
    }

    private int SelectedHistoryRating()
    {
        if (HistoryRatingComboBox.SelectedItem is ComboBoxItem item)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int fromTag)) return SeedHistoryStore.ClampRating(fromTag);
            if (item.Content is string content && int.TryParse(content.AsSpan(0, Math.Min(1, content.Length)), out int fromContent)) return SeedHistoryStore.ClampRating(fromContent);
        }
        return 0;
    }

    private void SetHistoryRatingCombo(int rating)
    {
        string wanted = SeedHistoryStore.ClampRating(rating).ToString(CultureInfo.InvariantCulture);
        foreach (var item in HistoryRatingComboBox.Items)
        {
            if (item is ComboBoxItem cb && cb.Tag is string tag && tag.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                HistoryRatingComboBox.SelectedItem = cb;
                return;
            }
        }
        if (HistoryRatingComboBox.Items.Count > 0) HistoryRatingComboBox.SelectedIndex = 0;
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_historyStore is null) return;
        Directory.CreateDirectory(_historyStore.DirectoryPath);
        Process.Start(new ProcessStartInfo { FileName = _historyStore.DirectoryPath, UseShellExecute = true });
    }



    private void RefreshCandidatePools_Click(object sender, RoutedEventArgs e) => RefreshCandidatePools();

    private void CandidateQueryTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCandidatePools();

    private void RefreshCandidatePools()
    {
        _candidatePools.Clear();
        _candidateSeeds.Clear();
        if (_candidatePoolStore is null)
        {
            CandidateStatusText.Text = "粗筛库未初始化";
            return;
        }
        string query = CandidateQueryTextBox.Text ?? "";
        var pools = _candidatePoolStore.Query(query);
        foreach (var p in pools) _candidatePools.Add(new CandidateSeedPoolView(p));
        CandidateStatusText.Text = $"{pools.Count} 个候选池 · {RollDataPaths.SafeRel(_rootDir, _candidatePoolStore.Path)}";
        RefreshCandidatePoolComboBox(pools);
        if (_candidatePools.Count > 0 && CandidatePoolsDataGrid.SelectedItem is null) CandidatePoolsDataGrid.SelectedIndex = 0;
    }

    private void RefreshCandidatePoolComboBox(IEnumerable<CandidateSeedPool>? pools = null)
    {
        if (CandidatePoolComboBox is null) return;
        string previous = SelectedCandidatePoolId();
        CandidatePoolComboBox.ItemsSource = (pools ?? _candidatePoolStore?.Load() ?? new List<CandidateSeedPool>()).Select(p => new CandidateSeedPoolView(p)).ToList();
        if (!string.IsNullOrWhiteSpace(previous))
        {
            foreach (var item in CandidatePoolComboBox.Items)
            {
                if (item is CandidateSeedPoolView view && view.Id.Equals(previous, StringComparison.OrdinalIgnoreCase))
                {
                    CandidatePoolComboBox.SelectedItem = view;
                    return;
                }
            }
        }
        if (CandidatePoolComboBox.Items.Count > 0 && CandidatePoolComboBox.SelectedItem is null) CandidatePoolComboBox.SelectedIndex = 0;
    }

    private void CandidatePoolsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _candidateSeeds.Clear();
        if (CandidatePoolsDataGrid.SelectedItem is not CandidateSeedPoolView view)
        {
            CandidatePoolMetaBox.Clear();
            return;
        }
        var p = view.Pool;
        foreach (var s in p.Seeds.Take(5000)) _candidateSeeds.Add(new CandidateSeedEntryView(s));
        CandidatePoolMetaBox.Text = string.Join(Environment.NewLine, new[]
        {
            p.Name,
            $"{SeedHistoryStore.DisplayCharacter(p.Character)} · A{p.Ascension} · RNG={p.RngVersion}",
            $"seed 数={p.Seeds.Count}",
            $"创建={p.CreatedAt:yyyy-MM-dd HH:mm:ss}",
            $"更新={p.UpdatedAt:yyyy-MM-dd HH:mm:ss}",
            string.Empty,
            "来源筛选：",
            p.SourceFilterSummary ?? string.Empty,
            string.Empty,
            "备注：",
            p.Note ?? string.Empty
        });
        // 同步到批量筛种页候选池下拉。
        RefreshCandidatePoolComboBox();
    }

    private void SaveCurrentHitsToCandidatePool_Click(object sender, RoutedEventArgs e)
    {
        if (_candidatePoolStore is null)
        {
            BottomStatusText.Text = "粗筛库未初始化。";
            return;
        }
        if (_searchHits.Count == 0)
        {
            BottomStatusText.Text = "当前没有筛种命中可保存为粗筛候选池。";
            return;
        }
        string name = CandidatePoolNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "粗筛候选池 " + DateTime.Now.ToString("yyyyMMdd HHmm");
        var pool = new CandidateSeedPool
        {
            Name = name,
            Character = _searchHits.FirstOrDefault()?.Character ?? SelectedComboText(SearchCharacterComboBox, "IRONCLAD"),
            Ascension = _searchHits.FirstOrDefault()?.Ascension ?? SelectedSearchAscensionLevel(),
            RngVersion = SelectedGameRngVersion(),
            GameVersion = "v0.107.1+",
            AppVersion = AppVersionText,
            SourceFilterSummary = SearchFilterSummary(),
            Tags = SeedHistoryStore.NormalizeTags(new[] { "粗筛", "v2" }),
            Note = "由筛种结果页保存；只保存 seed 与简短摘要，精筛时会重新计算。",
            Seeds = _searchHits.Select((h, i) => new CandidateSeedEntry
            {
                Index = i + 1,
                Seed = h.Seed,
                NormalizedSeed = h.Seed.Trim().ToUpperInvariant(),
                Summary = h.Summary,
                CreatedAt = DateTime.Now,
            }).ToList(),
        };
        var saved = _candidatePoolStore.AddPool(pool);
        BottomStatusText.Text = $"已保存粗筛候选池：{saved.Name}（{saved.Seeds.Count} 个 seed）";
        AppendLog($"Candidate pool saved: {saved.Name}, seeds={saved.Seeds.Count}");
        RefreshCandidatePools();
        RefreshCandidatePoolComboBox();
    }

    private void UseCandidatePoolForSearch_Click(object sender, RoutedEventArgs e)
    {
        CandidateSeedPoolView? view = CandidatePoolsDataGrid.SelectedItem as CandidateSeedPoolView;
        if (view is null && CandidatePoolComboBox.SelectedItem is CandidateSeedPoolView fromCombo) view = fromCombo;
        if (view is null)
        {
            BottomStatusText.Text = "请先选择一个粗筛候选池。";
            return;
        }
        SetCombo(SearchModeComboBox, "candidate_pool");
        RefreshCandidatePoolComboBox();
        foreach (var item in CandidatePoolComboBox.Items)
        {
            if (item is CandidateSeedPoolView v && v.Id.Equals(view.Id, StringComparison.OrdinalIgnoreCase))
            {
                CandidatePoolComboBox.SelectedItem = v;
                break;
            }
        }
        SetCombo(SearchCharacterComboBox, view.Pool.Character);
        SetCombo(SearchAscensionComboBox, view.Pool.Ascension.ToString(CultureInfo.InvariantCulture));
        SetGameRngVersionIfAvailable(view.Pool.RngVersion);
        ShowPage(SearchPage, "批量筛种", "已选择粗筛候选池作为 seed 来源，可继续添加条件后开始精筛。 ");
        BottomStatusText.Text = "已选择粗筛候选池：" + view.Name;
    }

    private void DeleteCandidatePool_Click(object sender, RoutedEventArgs e)
    {
        if (_candidatePoolStore is null || CandidatePoolsDataGrid.SelectedItem is not CandidateSeedPoolView view)
        {
            BottomStatusText.Text = "请先选择一个粗筛候选池。";
            return;
        }
        if (MessageBox.Show("确定删除粗筛候选池：" + view.Name + "？", "RollTheSpire2", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _candidatePoolStore.Delete(view.Id);
        BottomStatusText.Text = "已删除粗筛候选池：" + view.Name;
        RefreshCandidatePools();
    }

    private void CopyCandidatePoolSeeds_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatePoolsDataGrid.SelectedItem is not CandidateSeedPoolView view)
        {
            BottomStatusText.Text = "请先选择一个粗筛候选池。";
            return;
        }
        string text = string.Join(Environment.NewLine, view.Pool.Seeds.Select(s => s.Seed).Where(s => !string.IsNullOrWhiteSpace(s)));
        SetClipboardSafe(text, "已复制粗筛候选池 seed：" + view.Pool.Seeds.Count + " 个");
    }

    private void ExportCandidatePoolSeeds_Click(object sender, RoutedEventArgs e)
    {
        if (_candidatePoolStore is null || CandidatePoolsDataGrid.SelectedItem is not CandidateSeedPoolView view)
        {
            BottomStatusText.Text = "请先选择一个粗筛候选池。";
            return;
        }
        string file = _candidatePoolStore.ExportPoolSeeds(view.Pool);
        BottomStatusText.Text = "已导出候选池 seed：" + RollDataPaths.SafeRel(_rootDir, file);
        AppendLog("Candidate pool exported: " + file);
    }

    private void CopyAnalyze_Click(object sender, RoutedEventArgs e) => SetClipboardSafe(AnalyzeOutputBox.Text ?? "", "已复制单种分析结果");
    private void ClearAnalyze_Click(object sender, RoutedEventArgs e)
    {
        AnalyzeOutputBox.Clear();
        _analyzeCards.Clear();
    }
    private void CopySearch_Click(object sender, RoutedEventArgs e) => SetClipboardSafe(SearchOutputBox.Text ?? "", "已复制选中命中详情");

    private void CopyAllSearchHits_Click(object sender, RoutedEventArgs e)
    {
        if (_searchHits.Count == 0)
        {
            SearchSummaryText.Text = "暂无命中可复制";
            BottomStatusText.Text = "暂无命中可复制";
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("RollTheSpire2 批量筛种命中结果");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("命中数: " + _searchHits.Count);
        sb.AppendLine(new string('=', 72));
        foreach (var hit in _searchHits)
            sb.Append(hit.Detail);
        SetClipboardSafe(sb.ToString(), "已复制全部命中信息");
    }

    private void SendSelectedSeedToAnalyze_Click(object sender, RoutedEventArgs e)
    {
        SearchHitView? selectedHit = SearchHitsListBox.SelectedItem as SearchHitView;
        string seed = _selectedSearchSeed;
        if (string.IsNullOrWhiteSpace(seed) && selectedHit is not null)
            seed = selectedHit.Seed;
        if (string.IsNullOrWhiteSpace(seed))
            seed = SearchSelectedSeedBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(seed))
        {
            SearchSummaryText.Text = "请先在左侧选择一个 seed";
            BottomStatusText.Text = "未选择 seed";
            return;
        }

        SeedTextBox.Text = seed.Trim();
        if (selectedHit is not null)
        {
            SetCombo(CharacterComboBox, selectedHit.Character);
            SetCombo(AnalyzeAscensionComboBox, selectedHit.Ascension.ToString());
        }
        ShowPage(AnalyzePage, "单种分析", "已从筛种结果填入 seed、角色和进阶，可继续查看完整单种分析。点击“开始分析”运行。 ");
        BottomStatusText.Text = selectedHit is null
            ? "已填入单种分析: " + seed.Trim()
            : $"已填入单种分析: {seed.Trim()} · {selectedHit.Character} · A{selectedHit.Ascension}";
    }

    private void CopySelectedSeed_Click(object sender, RoutedEventArgs e)
    {
        string seed = _selectedSearchSeed;
        if (string.IsNullOrWhiteSpace(seed) && SearchHitsListBox.SelectedItem is SearchHitView hit)
            seed = hit.Seed;
        if (string.IsNullOrWhiteSpace(seed))
            seed = SearchSelectedSeedBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(seed))
        {
            SearchSummaryText.Text = "请先在左侧选择一个 seed";
            BottomStatusText.Text = "未选择 seed";
            return;
        }

        SetClipboardSafe(seed.Trim(), "已复制选中 Seed: " + seed.Trim());
    }

    private void SearchHitsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchHitsListBox.SelectedItem is SearchHitView hit)
        {
            _selectedSearchSeed = hit.Seed ?? "";
            SearchSelectedSeedBox.Text = _selectedSearchSeed;
            SearchOutputBox.Text = hit.Detail ?? "";
            ReplaceCards(_searchDetailCards, hit.Cards);
        }
        else
        {
            _selectedSearchSeed = "";
            SearchSelectedSeedBox.Clear();
            _searchDetailCards.Clear();
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchOutputBox.Clear();
        _searchHits.Clear();
        _selectedSearchSeed = "";
        SearchSelectedSeedBox.Clear();
        _searchDetailCards.Clear();
        SearchSummaryText.Text = "就绪";
    }

    private void SetClipboardSafe(string text, string successMessage)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            SearchSummaryText.Text = successMessage;
            BottomStatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            try
            {
                Clipboard.Clear();
                Clipboard.SetDataObject(text, true);
                SearchSummaryText.Text = successMessage;
                BottomStatusText.Text = successMessage;
            }
            catch
            {
                SearchSummaryText.Text = "复制失败，可手动复制下方 Seed 文本框";
                BottomStatusText.Text = "复制失败: " + ex.Message;
            }
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(LogBox.Text ?? "");
    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenPath(_configPath);
    private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenPath(_rootDir);
    private void OpenWeb_Click(object sender, RoutedEventArgs e) => OpenPath(Path.Combine(_rootDir, "run_csharp_web.bat"));

    private static void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // best effort only
        }
    }


    private sealed class AnalyzePayload
    {
        public string Text { get; set; } = "";
        public List<ResultCardView> Cards { get; set; } = new();
    }

    private sealed class ResultCardView
    {
        public string Title { get; set; } = "";
        public string Badge { get; set; } = "";
        public ResultLineList Lines { get; set; } = new();
    }

    private sealed class ResultLineView
    {
        public string Text { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public bool IsHeader { get; set; }
        public bool IsMuted { get; set; }
        public bool IsWarning { get; set; }
        public bool IsSkip { get; set; }
        public Brush ForegroundBrush => IsSkip ? Brushes.DarkRed : IsWarning ? Brushes.DarkOrange : IsMuted ? Brushes.DimGray : Brushes.DarkSlateGray;
        public FontWeight FontWeight => IsHeader ? FontWeights.SemiBold : IsSkip ? FontWeights.SemiBold : FontWeights.Normal;
        public override string ToString() => Text;
        public static ResultLineView Plain(string text) => new() { Text = text, ToolTip = text };
    }

    private sealed class ResultLineList : List<ResultLineView>
    {
        public ResultLineList() { }
        public ResultLineList(IEnumerable<ResultLineView> items) : base(items) { }
        public static implicit operator ResultLineList(List<string> lines) => new(lines.Select(ResultLineView.Plain));
    }

    private sealed class SearchHitView
    {
        public int Rank { get; set; }
        public string Seed { get; set; } = "";
        public long CheckedCount { get; set; }
        public string Character { get; set; } = "IRONCLAD";
        public int Ascension { get; set; }
        public string RunMode { get; set; } = "singleplayer";
        public string TargetNetId { get; set; } = "1";
        public string RngVersion { get; set; } = "sts2_0_107_xoshiro";
        public string Summary { get; set; } = "";
        public string CategoryText { get; set; } = "";
        public string Detail { get; set; } = "";
        public List<ResultCardView> Cards { get; set; } = new();
        public string Title => $"#{Rank}  {Seed}";
        public string Meta => $"checked={CheckedCount}";
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Summary)
                ? $"#{Rank}  {Seed}  checked={CheckedCount}"
                : $"#{Rank}  {Seed}\n{Summary}";
        }
    }

    private sealed record DynamicTemplateButtonTag(string RouteKind, string RelicId, string TemplateId);

    private sealed class NeowRelicEffectView
    {
        public string CanonicalId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Zh { get; set; } = "";
        public string En { get; set; } = "";
        public string EffectKind { get; set; } = "";
        public string Confidence { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool SupportedInWpf { get; set; }
        public List<NeowRelicTemplateView> Templates { get; } = new();
    }

    private sealed class NeowRelicTemplateView
    {
        public string RelicId { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string LabelZhs { get; set; } = "";
        public string LabelEng { get; set; } = "";
        public string OutputType { get; set; } = "";
        public string SourceKey { get; set; } = "";
        public List<string> MatchModes { get; } = new();
    }

    private sealed class EventRuleView
    {
        public string Id { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Confidence { get; set; } = "";
        public bool ManualReview { get; set; }
        public string SourceFile { get; set; } = "";
        public int SourceLine { get; set; }
        public List<string> Acts { get; } = new();
        public List<string> Conditions { get; } = new();
    }

    private sealed class EventEncyclopediaItem
    {
        public string Id { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string EngName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public bool IsSharedEvent { get; set; }
        public bool HasCondition { get; set; }
        public EventRuleView? Rule { get; set; }
        public string SourceFile { get; set; } = "";
        public List<string> Regions { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<EventPageText> Pages { get; } = new();
        public bool HasDynamicTransition => Pages.SelectMany(p => p.Options).Any(o => o.TransitionType.Equals("dynamic", StringComparison.OrdinalIgnoreCase));
        public bool HasTextManualReview => false;
        public string RegionSortKey => Regions.Count > 0 ? Regions[0] : "ZZZ";
        public string RegionText => string.Join(" / ", Regions.Select(RegionName));
        public string SearchText => string.Join(" ", new[] { Id, Name, EngName, ClassName, RegionText, Rule?.Summary ?? "", Rule?.Confidence ?? "", SourceFile }
            .Concat(Rule?.Conditions ?? Enumerable.Empty<string>())
            .Concat(Pages.Select(p => p.DescriptionZhs))
            .Concat(Pages.SelectMany(p => p.Options.Select(o => o.LabelZhs + " " + o.DescriptionZhs))));
        public override string ToString() => $"{Name}（{Id}）  [{RegionText}{(HasCondition ? " / 条件" : " / 无条件")}{(HasDynamicTransition ? " / 动态" : "")}]";

        public static EventEncyclopediaItem FromRuleOnly(EventRuleView rule)
        {
            var item = new EventEncyclopediaItem
            {
                Id = string.IsNullOrWhiteSpace(rule.SourceId) ? rule.Id : rule.SourceId,
                Name = string.IsNullOrWhiteSpace(rule.Name) ? (string.IsNullOrWhiteSpace(rule.SourceId) ? rule.Id : rule.SourceId) : rule.Name,
                ClassName = rule.ClassName,
                HasCondition = true,
                Rule = rule,
                SourceFile = rule.SourceFile,
            };
            foreach (var a in rule.Acts) item.Regions.Add(a);
            return item;
        }

        public string CompactTooltipText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Name}（{Id}）");
            if (!string.IsNullOrWhiteSpace(EngName)) sb.AppendLine("英文名: " + EngName);
            if (Regions.Count > 0) sb.AppendLine("区域: " + RegionText);
            sb.AppendLine();
            if (Rule is not null)
            {
                if (!string.IsNullOrWhiteSpace(Rule.Summary)) sb.AppendLine("出现条件: " + Rule.Summary);
                else if (Rule.Conditions.Count > 0) sb.AppendLine("出现条件: " + string.Join("；", Rule.Conditions.Take(3)));
                else sb.AppendLine("出现条件: 有规则，但未提取到中文摘要");
            }
            else
            {
                sb.AppendLine("出现条件: 默认 IsAllowed=true / 暂无特殊条件规则");
            }

            var firstPage = Pages.FirstOrDefault();
            if (firstPage is not null)
            {
                if (!string.IsNullOrWhiteSpace(firstPage.TitleZhs)) sb.AppendLine("标题: " + firstPage.TitleZhs);
                if (!string.IsNullOrWhiteSpace(firstPage.DescriptionZhs))
                {
                    var text = CleanText(firstPage.DescriptionZhs).Replace("\n", " ");
                    if (text.Length > 180) text = text[..180] + "...";
                    sb.AppendLine("文本: " + text);
                }
                var opts = firstPage.Options
                    .Select(o => string.IsNullOrWhiteSpace(o.LabelZhs) ? o.OptionId : o.LabelZhs)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(6)
                    .ToList();
                if (opts.Count > 0) sb.AppendLine("选项: " + string.Join(" / ", opts));
            }
            if (Warnings.Count > 0) sb.AppendLine("备注: " + string.Join("；", Warnings.Take(2)));
            return sb.ToString().Trim();
        }

        public string DetailText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Name}（{Id}）");
            if (!string.IsNullOrWhiteSpace(EngName)) sb.AppendLine("英文名: " + EngName);
            if (!string.IsNullOrWhiteSpace(ClassName)) sb.AppendLine("class: " + ClassName);
            if (Regions.Count > 0) sb.AppendLine("区域: " + RegionText);
            sb.AppendLine("条件事件: " + (HasCondition ? "是" : "否"));
            if (HasDynamicTransition) sb.AppendLine("选项跳转: 存在动态分支（信息已足够，不再视为待复核）");

            if (Rule is not null)
            {
                if (!string.IsNullOrWhiteSpace(Rule.Id) || !string.IsNullOrWhiteSpace(Rule.SourceId))
                    sb.AppendLine("规则ID: " + Rule.Id + (string.IsNullOrWhiteSpace(Rule.SourceId) ? "" : " / source_id=" + Rule.SourceId));
                if (!string.IsNullOrWhiteSpace(Rule.Summary)) sb.AppendLine("出现条件摘要: " + Rule.Summary);
                sb.AppendLine("confidence: " + Rule.Confidence + " | manual_review=" + Rule.ManualReview);
                if (Rule.Conditions.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("出现条件:");
                    foreach (var c in Rule.Conditions) sb.AppendLine("- " + c);
                }
            }
            else
            {
                sb.AppendLine("出现条件: 默认 IsAllowed=true（没有覆写条件规则）");
            }

            if (Pages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("游戏内文本 / 页面 / 选项:");
                foreach (var page in Pages)
                {
                    sb.AppendLine();
                    sb.AppendLine("[页面] " + (string.IsNullOrWhiteSpace(page.PageId) ? "UNKNOWN" : page.PageId));
                    if (!string.IsNullOrWhiteSpace(page.TitleZhs)) sb.AppendLine("标题: " + page.TitleZhs);
                    if (!string.IsNullOrWhiteSpace(page.DescriptionZhs)) sb.AppendLine(CleanText(page.DescriptionZhs));
                    if (page.Options.Count > 0)
                    {
                        sb.AppendLine("选项:");
                        foreach (var opt in page.Options)
                        {
                            var label = string.IsNullOrWhiteSpace(opt.LabelZhs) ? opt.OptionId : opt.LabelZhs;
                            sb.Append("  - " + label);
                            if (!string.IsNullOrWhiteSpace(opt.OptionId)) sb.Append(" [" + opt.OptionId + "]");
                            if (!string.IsNullOrWhiteSpace(opt.NextPage)) sb.Append(" -> " + opt.NextPage);
                            if (!string.IsNullOrWhiteSpace(opt.Confidence) && !opt.Confidence.Equals("likely", StringComparison.OrdinalIgnoreCase)) sb.Append(" (" + opt.Confidence + ")");
                            if (!string.IsNullOrWhiteSpace(opt.TransitionType)) sb.Append(" <" + TransitionTypeName(opt.TransitionType) + ">");
                            sb.AppendLine();
                            if (!string.IsNullOrWhiteSpace(opt.DescriptionZhs)) sb.AppendLine("    " + CleanText(opt.DescriptionZhs));
                            var note = BestTransitionNote(opt);
                            if (!string.IsNullOrWhiteSpace(note)) sb.AppendLine("    转换说明: " + CleanText(note));
                        }
                    }
                }
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("文本/选项备注:");
                foreach (var w in Warnings) sb.AppendLine("- " + w);
            }
            if (!string.IsNullOrWhiteSpace(SourceFile) || !string.IsNullOrWhiteSpace(Rule?.SourceFile))
            {
                sb.AppendLine();
                sb.AppendLine("源码: " + (!string.IsNullOrWhiteSpace(SourceFile) ? SourceFile : Rule!.SourceFile) + (Rule?.SourceLine > 0 ? ":" + Rule.SourceLine : ""));
            }
            return sb.ToString();
        }

        private static string BestTransitionNote(EventOptionText opt)
        {
            // Codex 复核报告中少量中文 transition_note 可能仍有编码问号；遇到这种情况优先显示英文说明。
            if (!string.IsNullOrWhiteSpace(opt.TransitionNoteZhs) && !opt.TransitionNoteZhs.Contains("??", StringComparison.Ordinal)) return opt.TransitionNoteZhs;
            return opt.TransitionNoteEng;
        }

        private static string TransitionTypeName(string type) => type switch
        {
            "page" => "固定页面",
            "combat" => "进入战斗",
            "reward" => "获得奖励",
            "leave" => "离开事件",
            "card_select" => "选牌",
            "remove_card" => "移除牌",
            "upgrade_card" => "升级牌",
            "transform_card" => "变化牌",
            "relic_reward" => "获得遗物",
            "potion_reward" => "获得药水",
            "shop" => "商店",
            "dynamic" => "动态分支",
            "unknown" => "未知",
            _ => type,
        };

        private static string CleanText(string text)
        {
            return text.Replace("\\n", "\n").Replace("\r", "").Trim();
        }

        private static string RegionName(string id) => id switch
        {
            "Overgrowth" => "密林 Overgrowth",
            "Underdocks" => "暗港 Underdocks",
            "Hive" => "巢穴 Hive",
            "Glory" => "荣耀 Glory",
            "SharedEvents" => "共享事件 SharedEvents",
            _ => id,
        };
    }

    private sealed class EventPageText
    {
        public string PageId { get; set; } = "";
        public string TitleZhs { get; set; } = "";
        public string DescriptionZhs { get; set; } = "";
        public string DescriptionEng { get; set; } = "";
        public List<EventOptionText> Options { get; } = new();
    }

    private sealed class EventOptionText
    {
        public string OptionId { get; set; } = "";
        public string LabelZhs { get; set; } = "";
        public string DescriptionZhs { get; set; } = "";
        public string NextPage { get; set; } = "";
        public string Confidence { get; set; } = "";
        public string TransitionType { get; set; } = "";
        public string TransitionNoteZhs { get; set; } = "";
        public string TransitionNoteEng { get; set; } = "";
    }

}


public sealed class MultiplayerPlayerView : INotifyPropertyChanged
{
    private int _order;
    private string _name = "";
    private string _netId = "";
    private string _character = "IRONCLAD";
    private bool _enabled = true;
    private bool _isTarget;

    public int Order { get => _order; set { if (_order != value) { _order = value; OnPropertyChanged(nameof(Order)); } } }
    public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } } }
    public string NetId { get => _netId; set { if (_netId != value) { _netId = value; OnPropertyChanged(nameof(NetId)); } } }
    public string Character { get => _character; set { var v = string.IsNullOrWhiteSpace(value) ? "IRONCLAD" : value.ToUpperInvariant(); if (_character != v) { _character = v; OnPropertyChanged(nameof(Character)); } } }
    public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; OnPropertyChanged(nameof(Enabled)); } } }
    public bool IsTarget { get => _isTarget; set { if (_isTarget != value) { _isTarget = value; OnPropertyChanged(nameof(IsTarget)); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record SearchRequirementTagView(string Text, string Kind, string Value, bool CanRemove = true);

public sealed record EventFilterConditionView(string ActText, string LimitText, string ConditionText, string EventText, string Kind, string RawTerm);

public sealed class CardPoolMetaView
{
    public List<string> Owners { get; } = new();
    public List<string> PoolKinds { get; } = new();
    public string Rarity { get; set; } = "";
    public string MultiplayerConstraint { get; set; } = "";
}

public sealed class PotionPoolMetaView
{
    public List<string> Owners { get; } = new();
    public List<string> PoolKinds { get; } = new();
    public string Rarity { get; set; } = "";
}

public sealed class RelicPoolMetaView
{
    public List<string> Owners { get; } = new();
    public List<string> Pools { get; } = new();
    public List<string> PoolKinds { get; } = new();
    public string Rarity { get; set; } = "";
}

public sealed record ItemAliasView(string SourceId, string RuntimeId, string Category, string Zh, string En)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Zh) ? Zh : (!string.IsNullOrWhiteSpace(En) ? En : RuntimeId);
    public string DisplayText => DisplayName + "（" + RuntimeId + "）";
}
