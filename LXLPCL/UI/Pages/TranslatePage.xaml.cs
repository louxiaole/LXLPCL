using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

/// <summary>模组列表中的一行。</summary>
public sealed class ModRow
{
    public ModLangInfo Info { get; init; } = new();
    public string Name => Info.DisplayName;

    public string SubText => string.IsNullOrWhiteSpace(Info.Version)
        ? Info.FileName
        : $"{Info.Version}  ·  {Info.FileName}";

    public string PathText => Info.JarPath;
    public string CoverageText => Info.CoverageText;
    public string StatusText => Info.StatusText;
    public Brush StatusBrush { get; init; } = Brushes.Gray;
    public Brush StatusBackground { get; init; } = Brushes.Transparent;

    public override string ToString() => Name;
}

/// <summary>整合包「查看全部」中的一条语言条目。</summary>
public sealed class LangEntryRow
{
    public ModLangInfo Mod { get; init; } = new();
    public string ModName => Mod.DisplayName;
    public string Key { get; init; } = "";
    public string Source { get; init; } = "";
    public string TranslationText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public Brush TranslationBrush { get; init; } = Brushes.Black;
    public Brush StatusBrush { get; init; } = Brushes.Gray;
    public Brush StatusBackground { get; init; } = Brushes.Transparent;
    public string ToolTip => Mod.JarPath;
}

public partial class TranslatePage : PageBase
{
    private const int MaxAllRows = 30000;

    private bool _ready;
    private string _mode = "mod";
    private string _modFilter = "all";
    private string _packView = "all";

    private GameVersion? _modVersion;
    private GameVersion? _packVersion;
    private List<ModLangInfo> _modInfos = new();
    private List<ModLangInfo> _packInfos = new();

    private CancellationTokenSource? _cts;
    private bool _scanning;

    public TranslatePage()
    {
        InitializeComponent();
        VersionBox.DisplayMemberPath = "Id";
        PackVersionBox.DisplayMemberPath = "Id";
    }

    public override void OnShown()
    {
        if (!_ready)
        {
            FillVersions();
            _ready = true;
            _modVersion = VersionBox.SelectedItem as GameVersion;
            _packVersion = PackVersionBox.SelectedItem as GameVersion;
            if (_modVersion == null)
            {
                Render();
                return;
            }
            _ = ScanAsync(false);
            return;
        }

        if (_modInfos.Count == 0 && !_scanning) _ = ScanAsync(false);
    }

    private void FillVersions()
    {
        var versions = AppState.Versions.ToList();
        VersionBox.ItemsSource = versions;
        PackVersionBox.ItemsSource = versions;

        var selected = AppState.SelectedVersion;
        VersionBox.SelectedItem = selected == null
            ? versions.FirstOrDefault()
            : versions.FirstOrDefault(version => version.Id == selected.Id) ?? versions.FirstOrDefault();

        PackVersionBox.SelectedItem = versions.FirstOrDefault(HasMods) ?? versions.FirstOrDefault();
    }

    private static bool HasMods(GameVersion version)
    {
        var dir = AppState.ResolveModsDir(version);
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.jar").Any();
    }

    // ================= 扫描 =================

    private async Task ScanAsync(bool pack)
    {
        var version = pack ? _packVersion : _modVersion;
        if (version == null) return;

        var dir = AppState.ResolveModsDir(version);
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _scanning = true;
        BusyText.Text = "正在读取模组语言文件…";

        try
        {
            var progress = new Progress<(int Done, int Total, string Name)>(report =>
                BusyText.Text = $"读取语言文件 {report.Done}/{report.Total}：{report.Name}");
            var infos = await Task.Run(() => ModLocalization.Scan(dir, progress, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;

            // 合并之前保存过的手工汉化
            infos = infos.Select(info => TranslationPack.ApplySavedOverrides(version, info)).ToList();

            if (pack) _packInfos = infos;
            else _modInfos = infos;

            BusyText.Text = "";
            Logger.Info($"汉化扫描完成：{(pack ? "整合包" : "模组")} {version.Id}，共 {infos.Count} 个模组");
            Render();
        }
        catch (OperationCanceledException)
        {
            // 切换版本时取消上一次扫描。
        }
        catch (Exception ex)
        {
            Logger.Error("扫描模组语言文件失败：" + ex.Message);
            BusyText.Text = "读取失败：" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                _scanning = false;
                _cts = null;
            }
        }
    }

    // ================= 渲染 =================

    private void Render()
    {
        ModModule.Visibility = _mode == "mod" ? Visibility.Visible : Visibility.Collapsed;
        PackModule.Visibility = _mode == "pack" ? Visibility.Visible : Visibility.Collapsed;
        if (_mode == "mod") RenderMod();
        else RenderPack();
    }

    private void RenderMod()
    {
        var rows = FilterRows(_modInfos, ModSearchBox.Text, _modFilter);
        ModList.ItemsSource = rows;
        ModSummaryText.Text = BuildSummary(_modInfos);
        ModEmpty.Text = _modInfos.Count == 0
            ? "这个版本还没有安装模组。选择其它版本，或先在「版本管理」里安装模组。"
            : "没有符合筛选条件的模组。";
        ModEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderPack()
    {
        PackAllHost.Visibility = _packView == "all" ? Visibility.Visible : Visibility.Collapsed;
        PackModsHost.Visibility = _packView == "list" ? Visibility.Visible : Visibility.Collapsed;

        var summary = BuildSummary(_packInfos);
        var extra = _packInfos.Sum(info => info.ExtraSources.Count(source => source.Files.Count > 0));
        if (extra > 0) summary += $"  ·  手册 / 指南 {extra} 部";
        var ftb = _packVersion == null ? null : ModLocalization.ScanFtbQuests(_packVersion);
        if (ftb != null) summary += $"  ·  {ftb.Type} {ftb.CoverageText}";
        if (_packView == "all") RenderPackAll(summary);
        else RenderPackMods(summary);
    }

    private void RenderPackAll(string summary)
    {
        var keyword = PackSearchBox.Text.Trim();
        var mods = _packInfos.Where(info => info.HasChinese || info.ExtraSources.Count > 0);
        if (keyword.Length > 0) mods = mods.Where(info => Matches(info, keyword));

        var rows = new List<LangEntryRow>();
        foreach (var mod in mods)
        {
            foreach (var entry in mod.Entries)
                rows.Add(CreateEntryRow(mod, entry));
            // 手册 / 指南等不在 lang 文件里的文本也一并列出，文件级别显示是否汉化。
            foreach (var source in mod.ExtraSources)
            foreach (var file in source.Files)
                rows.Add(CreateExtraRow(mod, source.Type + " / " + source.Detail, file.Name, file.HasChinese));
        }

        var total = rows.Count;
        if (rows.Count > MaxAllRows) rows = rows.Take(MaxAllRows).ToList();

        PackAllList.ItemsSource = rows;
        PackAllEmpty.Text = _packInfos.Count == 0
            ? "这个整合包里没有找到模组。请确认选择的整合包已安装模组。"
            : "没有找到带汉化的模组。切换到「Mod 列表」可以看到所有模组。";
        PackAllEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        PackSummaryText.Text = total > MaxAllRows
            ? $"{summary}  ·  显示前 {MaxAllRows} 条"
            : $"{summary}  ·  显示 {total} 条";
    }

    private void RenderPackMods(string summary)
    {
        var rows = FilterRows(_packInfos, PackSearchBox.Text, "all");
        PackModList.ItemsSource = rows;
        PackModEmpty.Text = _packInfos.Count == 0
            ? "这个整合包里没有找到模组。请确认选择的整合包已安装模组。"
            : "没有符合搜索条件的模组。";
        PackModEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PackSummaryText.Text = summary + "  ·  点击模组查看单个汉化详情";
    }

    private static string BuildSummary(List<ModLangInfo> infos)
    {
        if (infos.Count == 0) return "暂无可读取的模组";
        var hasChinese = infos.Count(info => info.HasChinese);
        var missingMods = infos.Count(info => info.HasMissing);
        var totalKeys = infos.Sum(info => info.TotalKeys);
        var translatedKeys = infos.Sum(info => info.ChineseKeys);
        var missingKeys = infos.Sum(info => info.MissingKeys);
        return $"共 {infos.Count} 个模组  ·  有汉化 {hasChinese}  ·  无汉化 {infos.Count - hasChinese}  ·  " +
               $"有缺失 {missingMods}  ·  条目 {translatedKeys}/{totalKeys}" +
               (missingKeys > 0 ? $"（缺失 {missingKeys}）" : "");
    }

    private static bool Matches(ModLangInfo info, string keyword) =>
        info.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || info.ModId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || info.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static List<ModRow> FilterRows(List<ModLangInfo> infos, string search, string filter)
    {
        IEnumerable<ModLangInfo> query = infos;
        if (filter == "translated") query = query.Where(info => info.HasChinese);
        else if (filter == "missing") query = query.Where(info => info.HasMissing);
        else if (filter == "untranslated") query = query.Where(info => info.HasSource && !info.HasChinese);

        var keyword = search.Trim();
        if (keyword.Length > 0) query = query.Where(info => Matches(info, keyword));

        return query.Select(info => new ModRow
        {
            Info = info,
            StatusBrush = info.Error != null ? BrushOf("DangerBrush")
                : !info.HasSource ? BrushOf("TextTertiaryBrush")
                : !info.HasChinese ? BrushOf("TextSecondaryBrush")
                : info.MissingKeys > 0 ? BrushOf("WarningBrush")
                : BrushOf("SuccessBrush"),
            StatusBackground = info.Error != null ? BrushOf("DangerLightBrush")
                : !info.HasSource || !info.HasChinese ? BrushOf("HoverBrush")
                : info.MissingKeys > 0 ? BrushOf("WarningLightBrush")
                : BrushOf("SuccessLightBrush")
        }).ToList();
    }

    private static LangEntryRow CreateEntryRow(ModLangInfo mod, LangEntry entry) => new()
    {
        Mod = mod,
        Key = entry.Key,
        Source = entry.Source,
        TranslationText = entry.Missing ? "——" : entry.Translation,
        StatusText = entry.StatusText,
        TranslationBrush = entry.Missing ? BrushOf("TextTertiaryBrush")
            : entry.SameAsSource ? BrushOf("WarningBrush")
            : BrushOf("TextPrimaryBrush"),
        StatusBrush = entry.Missing ? BrushOf("DangerBrush")
            : entry.SameAsSource ? BrushOf("WarningBrush")
            : BrushOf("SuccessBrush"),
        StatusBackground = entry.Missing ? BrushOf("DangerLightBrush")
            : entry.SameAsSource ? BrushOf("WarningLightBrush")
            : BrushOf("SuccessLightBrush")
    };

    /// <summary>手册 / 指南等非 lang 文件的行，只有文件级是否已汉化。</summary>
    private static LangEntryRow CreateExtraRow(ModLangInfo mod, string type, string name, bool hasChinese) => new()
    {
        Mod = mod,
        Key = type,
        Source = name,
        TranslationText = hasChinese ? "已汉化" : "——",
        StatusText = hasChinese ? "已汉化" : "缺失",
        TranslationBrush = hasChinese ? BrushOf("SuccessBrush") : BrushOf("TextTertiaryBrush"),
        StatusBrush = hasChinese ? BrushOf("SuccessBrush") : BrushOf("DangerBrush"),
        StatusBackground = hasChinese ? BrushOf("SuccessLightBrush") : BrushOf("DangerLightBrush")
    };

    private static Brush BrushOf(string key)
    {
        try
        {
            if (Application.Current?.Resources[key] is Brush brush) return brush;
        }
        catch
        {
            // 主题资源缺失时使用默认颜色。
        }
        return Brushes.Gray;
    }

    // ================= 交互 =================

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not RadioButton { Tag: string mode }) return;
        _mode = mode;
        if (_mode == "pack" && _packVersion != null && _packInfos.Count == 0 && !_scanning)
            _ = ScanAsync(true);
        Render();
    }

    private void PackView_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not RadioButton { Tag: string view }) return;
        _packView = view;
        RenderPack();
    }

    private void ModFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not RadioButton { Tag: string filter }) return;
        _modFilter = filter;
        RenderMod();
    }

    private void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (VersionBox.SelectedItem is not GameVersion version) return;
        if (ReferenceEquals(version, _modVersion)) return;
        _modVersion = version;
        _modInfos = new List<ModLangInfo>();
        ModList.ItemsSource = null;
        _ = ScanAsync(false);
    }

    private void PackVersion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (PackVersionBox.SelectedItem is not GameVersion version) return;
        if (ReferenceEquals(version, _packVersion)) return;
        _packVersion = version;
        _packInfos = new List<ModLangInfo>();
        PackAllList.ItemsSource = null;
        PackModList.ItemsSource = null;
        _ = ScanAsync(true);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (ReferenceEquals(sender, ModSearchBox)) RenderMod();
        else RenderPack();
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == "pack") _ = ScanAsync(true);
        else _ = ScanAsync(false);
    }

    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModList.SelectedItem is ModRow row)
        {
            ModList.SelectedItem = null;
            OpenDetail(row.Info);
        }
    }

    private void PackModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackModList.SelectedItem is ModRow row)
        {
            PackModList.SelectedItem = null;
            OpenDetail(row.Info);
        }
    }

    private void PackAllList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackAllList.SelectedItem is LangEntryRow row)
        {
            PackAllList.SelectedItem = null;
            OpenDetail(row.Mod);
        }
    }

    private void OpenDetail(ModLangInfo info)
    {
        try
        {
            var version = _mode == "pack" ? _packVersion : _modVersion;
            var window = new ModTranslationWindow(info, version) { Owner = Window.GetWindow(this) };
            window.ShowDialog();
            if (window.Saved && version != null) _ = RefreshModAsync(version, info.JarPath);
        }
        catch (Exception ex)
        {
            Logger.Error("打开汉化详情失败：" + ex);
        }
    }

    /// <summary>保存汉化后只重读这一个 jar，刷新覆盖率。</summary>
    private async Task RefreshModAsync(GameVersion version, string jarPath)
    {
        try
        {
            var scanned = await Task.Run(() => ModLocalization.ScanJars(new[] { jarPath }));
            if (scanned.Count == 0) return;
            var updated = TranslationPack.ApplySavedOverrides(version, scanned[0]);
            foreach (var list in new[] { _modInfos, _packInfos })
            {
                var index = list.FindIndex(item => item.JarPath == jarPath);
                if (index >= 0) list[index] = updated;
            }
            Render();
        }
        catch (Exception ex)
        {
            Logger.Error("刷新汉化列表失败：" + ex.Message);
        }
    }
}
