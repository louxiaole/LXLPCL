using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class DownloadPage : PageBase
{
    private List<RemoteVersion>? _allVersions;
    private Task<List<RemoteVersion>>? _versionTask;
    private string _filter = "all";
    private bool _busy;
    private CancellationTokenSource? _downloadCts;
    private readonly DispatcherTimer _barTimer;
    private string _projectType = "mod";
    private bool _suppressModFilter;
    private CancellationTokenSource? _searchCts;
    private ModrinthProject? _detailProject;
    private int _modTotalHits;
    private bool _loadingMore;
    private bool _userChangedModFilter;
    private string _modGameVersion = "";
    private string _modLoaderFilter = "";
    private string _modCategoryFilter = "";
    private string _modSort = "relevance";
    private bool _suppressFabric;
    private string _bodyClean = "";
    private string _bodyZh = "";
    private bool _bodyShowZh = true;
    private CancellationTokenSource? _translateCts;

    public DownloadPage()
    {
        InitializeComponent();
        _barTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _barTimer.Tick += (_, _) => UpdateDownloadBar();
        ApplyPanel("vanilla");
        UpdateSourceBadge();
    }

    public override void OnShown()
    {
        UpdateSourceBadge();
        if (_allVersions == null) _ = LoadVersionsAsync();
        if (NavFabric.IsChecked == true && FabricMcCombo.ItemsSource == null) _ = LoadFabricAsync();
        if (ModrinthPanel.Visibility == Visibility.Visible && ModList.ItemsSource == null)
            _ = LoadModrinthAsync();
    }

    private void UpdateSourceBadge()
    {
        var mirror = Http.UseMirror && !AppState.Config.DownloadSource.Equals("官方", StringComparison.OrdinalIgnoreCase);
        SourceBadgeText.Text = "下载源：" + (mirror ? "自动（BMCLAPI 镜像）" : "Mojang 官方");
        SourceHintText.Text = mirror ? "版本与资源使用 BMCLAPI 镜像加速" : "使用 Mojang 官方服务器";
    }

    // ================= 原版版本 =================

    private async Task LoadVersionsAsync(bool force = false)
    {
        VersionStatusText.Text = "正在获取版本列表...";
        try
        {
            _versionTask ??= Installer.GetRemoteVersionsAsync(force);
            var list = await _versionTask;
            _allVersions = list;
            UpdateInstalledFlags();
            ApplyFilter();
            RefreshModVersionFilter();
            Logger.Info($"已获取 {list.Count} 个远程版本");
        }
        catch (Exception ex)
        {
            VersionStatusText.Text = "获取版本列表失败：" + ex.Message;
            Logger.Error("获取远程版本失败: " + ex.Message);
        }
        finally
        {
            _versionTask = null;
        }
    }

    private void UpdateInstalledFlags()
    {
        if (_allVersions == null) return;
        foreach (var version in _allVersions)
            version.Installed = File.Exists(Path.Combine(AppState.Config.GameDir, "versions", version.Id, version.Id + ".json"));
    }

    private void ApplyFilter()
    {
        if (_allVersions == null) return;
        IEnumerable<RemoteVersion> query = _allVersions;
        query = _filter switch
        {
            "release" => query.Where(v => v.Type == "release"),
            "snapshot" => query.Where(v => v.Type == "snapshot"),
            "ancient" => query.Where(v => v.Type is "old_beta" or "old_alpha"),
            _ => query
        };
        var search = VersionSearchBox.Text.Trim();
        if (search.Length > 0)
            query = query.Where(v => v.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        var list = query.ToList();
        VersionList.ItemsSource = list;
        VersionStatusText.Text = list.Count == 0
            ? "没有匹配的版本，试试其它筛选条件"
            : $"共 {list.Count} 个版本，点击右侧“安装”开始下载";
    }

    private void VersionFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string filter }) _filter = filter;
        ApplyFilter();
    }

    private void VersionSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void RefreshVersions_Click(object sender, RoutedEventArgs e) => _ = LoadVersionsAsync(true);

    private bool IsInstalled(string id) =>
        File.Exists(Path.Combine(AppState.Config.GameDir, "versions", id, id + ".json"));

    private async void InstallVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            MessageDialog.Show(Window.GetWindow(this), "请稍候", "当前有下载任务正在进行，请等待完成。", warning: true);
            return;
        }
        if (sender is not Button { Tag: string id }) return;

        if (IsInstalled(id))
        {
            if (!MessageDialog.Show(Window.GetWindow(this), "重新安装",
                    $"版本 {id} 已经安装。\n\n是否重新下载并校验所有文件？", "重新安装", "取消", true)) return;
        }

        _busy = true;
        _downloadCts = new CancellationTokenSource();
        ShowDownloadBar("安装 Minecraft " + id);
        var canceled = false;
        try
        {
            await Installer.InstallVanillaAsync(id, AppState.Config.AutoInstallAssets, _downloadCts.Token);
            await AppState.RefreshVersionsAsync();
            UpdateInstalledFlags();
            ApplyFilter();
            EndDownloadBar();
            var go = MessageDialog.Show(Window.GetWindow(this), "安装完成",
                $"Minecraft {id} 已安装完成。\n\n现在前往启动页启动游戏吗？", "前往启动", "继续下载");
            if (go) Navigation.Go("launch");
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            Logger.Error("安装版本失败: " + ex);
            EndDownloadBar();
            MessageDialog.Show(Window.GetWindow(this), "安装失败",
                ex.Message + "\n\n详细日志：" + Logger.LogFile, warning: true);
        }
        finally
        {
            EndDownloadBar();
            _busy = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            if (canceled) MessageDialog.Show(Window.GetWindow(this), "已取消", "安装已取消，未完成的文件不会影响已有版本。");
        }
    }

    // ================= Fabric =================

    private void DownloadNav_Checked(object sender, RoutedEventArgs e)
    {
        if (VersionPanel == null) return;
        if (sender is not RadioButton { Tag: string tag }) return;
        ApplyPanel(tag);
    }

    private void ApplyPanel(string tag)
    {
        VersionPanel.Visibility = tag == "vanilla" ? Visibility.Visible : Visibility.Collapsed;
        FabricPanel.Visibility = tag == "fabric" ? Visibility.Visible : Visibility.Collapsed;
        ModrinthPanel.Visibility = tag is "mod" or "resourcepack" or "shader" ? Visibility.Visible : Visibility.Collapsed;

        switch (tag)
        {
            case "vanilla":
                DownloadSubtitle.Text = "安装原版游戏、Fabric 加载器与社区资源";
                break;
            case "fabric":
                DownloadSubtitle.Text = "为任意游戏版本安装 Fabric 模组加载器";
                if (FabricMcCombo.ItemsSource == null) _ = LoadFabricAsync();
                break;
            default:
                _projectType = tag;
                ConfigureResourceView();
                ModDetailView.Visibility = Visibility.Collapsed;
                ResourceListView.Visibility = Visibility.Visible;
                _detailProject = null;
                ModList.ItemsSource = null;
                _ = LoadModrinthAsync();
                DownloadSubtitle.Text = _projectType switch
                {
                    "mod" => "从 Modrinth 浏览并安装模组",
                    "resourcepack" => "下载社区资源包，自动放入 resourcepacks",
                    "shader" => "下载光影包，自动放入 shaderpacks",
                    _ => "浏览社区资源"
                };
                break;
        }
    }

    private async Task LoadFabricAsync()
    {
        FabricStatusText.Text = "正在获取游戏版本列表...";
        if (_allVersions == null) await LoadVersionsAsync();
        if (_allVersions == null)
        {
            FabricStatusText.Text = "无法获取版本列表，请检查网络后重试。";
            return;
        }

        var releases = _allVersions.Where(v => v.Type == "release").ToList();
        _suppressFabric = true;
        FabricMcCombo.ItemsSource = releases;
        FabricMcCombo.SelectedItem = releases.FirstOrDefault(v => v.IsLatestRelease) ?? releases.FirstOrDefault();
        _suppressFabric = false;
        await LoadFabricLoadersAsync();
    }

    private async void FabricMc_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFabric) return;
        await LoadFabricLoadersAsync();
    }

    private async Task LoadFabricLoadersAsync()
    {
        if (FabricMcCombo.SelectedItem is not RemoteVersion mc)
        {
            FabricLoaderCombo.ItemsSource = null;
            return;
        }
        FabricStatusText.Text = "正在获取 Fabric Loader 版本...";
        try
        {
            var json = await Http.GetStringAsync(
                new[] { $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(mc.Id)}" });
            using var doc = JsonDocument.Parse(json);
            var options = new List<FabricLoaderOption>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("loader", out var loader)) continue;
                    var version = loader.TryGetProperty("version", out var v) ? v.GetString() : null;
                    if (string.IsNullOrEmpty(version)) continue;
                    var stable = loader.TryGetProperty("stable", out var s) && s.ValueKind == JsonValueKind.True;
                    options.Add(new FabricLoaderOption(version, stable));
                }
            }
            FabricLoaderCombo.ItemsSource = options;
            FabricLoaderCombo.SelectedIndex = options.Count > 0 ? 0 : -1;
            FabricStatusText.Text = options.Count == 0
                ? "该版本没有可用的 Fabric Loader。"
                : $"已找到 {options.Count} 个 Loader 版本，推荐使用稳定版。";
        }
        catch (Exception ex)
        {
            FabricStatusText.Text = "获取 Fabric 版本失败：" + ex.Message;
            Logger.Error("获取 Fabric Loader 版本失败: " + ex.Message);
        }
    }

    private async void InstallFabric_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            MessageDialog.Show(Window.GetWindow(this), "请稍候", "当前有下载任务正在进行，请等待完成。", warning: true);
            return;
        }
        if (FabricMcCombo.SelectedItem is not RemoteVersion mc ||
            FabricLoaderCombo.SelectedItem is not FabricLoaderOption loader)
        {
            MessageDialog.Show(Window.GetWindow(this), "尚未选择", "请先选择游戏版本与 Fabric Loader 版本。", warning: true);
            return;
        }

        if (!MessageDialog.Show(Window.GetWindow(this), "安装 Fabric",
                $"将为 Minecraft {mc.Id} 安装 Fabric Loader {loader.Version}。\n\n如果该原版尚未安装，启动器会先自动下载原版游戏。",
                "开始安装", "取消")) return;

        _busy = true;
        _downloadCts = new CancellationTokenSource();
        ShowDownloadBar("安装 Fabric Loader");
        var canceled = false;
        try
        {
            var id = await Installer.InstallFabricAsync(mc.Id, loader.Version, _downloadCts.Token);
            await AppState.RefreshVersionsAsync();
            await LoadVersionsAsync();
            EndDownloadBar();
            var go = MessageDialog.Show(Window.GetWindow(this), "安装完成",
                $"Fabric 版本 {id} 已安装完成。\n\n现在前往启动页启动游戏吗？", "前往启动", "继续下载");
            if (go) Navigation.Go("launch");
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            Logger.Error("安装 Fabric 失败: " + ex);
            MessageDialog.Show(Window.GetWindow(this), "安装失败",
                ex.Message + "\n\n详细日志：" + Logger.LogFile, warning: true);
        }
        finally
        {
            EndDownloadBar();
            _busy = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            if (canceled) MessageDialog.Show(Window.GetWindow(this), "已取消", "Fabric 安装已取消。");
        }
    }

    // ================= Modrinth =================

    private void ConfigureResourceView()
    {
        (ResourceTitle.Text, ResourceCaption.Text, ModSearchHint.Text) = _projectType switch
        {
            "resourcepack" => ("资源包", "来自 Modrinth 社区，安装到 resourcepacks 目录", "搜索资源包名称..."),
            "shader" => ("光影", "来自 Modrinth 社区，安装到 shaderpacks 目录", "搜索光影名称，支持中文..."),
            _ => ("模组", "来自 Modrinth 社区，安装到当前版本的 mods 目录", "搜索模组名称，支持中文...")
        };
        ModLoaderFilter.Visibility = _projectType == "mod" ? Visibility.Visible : Visibility.Collapsed;
        if (!_userChangedModFilter) ApplySelectedVersionDefaults();
        BuildSortFilter();
        BuildLoaderFilter();
        BuildCategoryFilter();
        RefreshModVersionFilter();
    }

    /// <summary>未手动筛选前：游戏版本默认「全部版本」，加载器跟随当前选中版本。</summary>
    private void ApplySelectedVersionDefaults()
    {
        var selected = AppState.SelectedVersion;
        if (selected == null) return;
        _modGameVersion = "";
        if (_projectType == "mod" && selected.Loader is not ("原版" or "未知" or null))
            _modLoaderFilter = Modrinth.NormalizeLoader(selected.Loader);
    }

    private void BuildSortFilter()
    {
        _suppressModFilter = true;
        ModSortFilter.ItemsSource = new List<FilterOption>
        {
            new("综合排序", "relevance"),
            new("下载量", "downloads"),
            new("最近更新", "updated"),
            new("最新发布", "newest")
        };
        var sort = ((List<FilterOption>)ModSortFilter.ItemsSource).FirstOrDefault(o => o.Value == _modSort);
        ModSortFilter.SelectedItem = sort ?? ((List<FilterOption>)ModSortFilter.ItemsSource)[0];
        _modSort = ((FilterOption)ModSortFilter.SelectedItem).Value;
        _suppressModFilter = false;
    }

    private void BuildLoaderFilter()
    {
        _suppressModFilter = true;
        var items = new List<FilterOption>
        {
            new("全部加载器", ""),
            new("Fabric", "fabric"),
            new("Forge", "forge"),
            new("NeoForge", "neoforge"),
            new("Quilt", "quilt")
        };
        ModLoaderFilter.ItemsSource = items;
        var loader = items.FirstOrDefault(o => o.Value == _modLoaderFilter) ?? items[0];
        ModLoaderFilter.SelectedItem = loader;
        _modLoaderFilter = loader.Value;
        _suppressModFilter = false;
    }

    private void BuildCategoryFilter()
    {
        var items = new List<FilterOption> { new("全部分类", "") };
        if (_projectType == "resourcepack")
        {
            items.AddRange(new[]
            {
                new FilterOption("冒险", "adventure"),
                new FilterOption("装饰", "decoration"),
                new FilterOption("字体", "fonts"),
                new FilterOption("主题", "themed"),
                new FilterOption("实用", "utility"),
                new FilterOption("原版风", "vanilla-like"),
                new FilterOption("简化", "simplified")
            });
        }
        else if (_projectType == "shader")
        {
            items.AddRange(new[]
            {
                new FilterOption("写实", "realistic"),
                new FilterOption("半写实", "semi-realistic"),
                new FilterOption("奇幻", "fantasy"),
                new FilterOption("卡通", "cartoon"),
                new FilterOption("简约", "simplistic"),
                new FilterOption("原版风", "vanilla-like"),
                new FilterOption("优化", "tweaks")
            });
        }
        else
        {
            items.AddRange(new[]
            {
                new FilterOption("科技", "technology"),
                new FilterOption("魔法", "magic"),
                new FilterOption("冒险", "adventure"),
                new FilterOption("装饰", "decoration"),
                new FilterOption("优化", "optimization"),
                new FilterOption("世界生成", "worldgen"),
                new FilterOption("存储", "storage"),
                new FilterOption("工具", "utility"),
                new FilterOption("装备", "equipment"),
                new FilterOption("食物", "food"),
                new FilterOption("生物", "mobs"),
                new FilterOption("运输", "transportation"),
                new FilterOption("库", "library")
            });
        }

        _suppressModFilter = true;
        ModCategoryFilter.ItemsSource = items;
        var category = items.FirstOrDefault(o => o.Value == _modCategoryFilter) ?? items[0];
        ModCategoryFilter.SelectedItem = category;
        _modCategoryFilter = category.Value;
        _suppressModFilter = false;
    }

    private void RefreshModVersionFilter()
    {
        if (ModVersionFilter == null) return;
        var values = new List<FilterOption> { new("全部版本", "") };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in AppState.Versions.Select(v => v.McVersion)
                     .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(version)) continue;
            values.Add(new FilterOption(version, version));
        }
        if (!string.IsNullOrWhiteSpace(_modGameVersion) && seen.Add(_modGameVersion))
            values.Add(new FilterOption(_modGameVersion, _modGameVersion));
        if (_allVersions != null)
        {
            foreach (var version in _allVersions.Where(v => v.Type == "release").Take(90))
            {
                if (!seen.Add(version.Id)) continue;
                values.Add(new FilterOption(version.Id, version.Id));
            }
        }

        _suppressModFilter = true;
        ModVersionFilter.ItemsSource = values;
        var selected = values.FirstOrDefault(o => o.Value == _modGameVersion) ?? values[0];
        ModVersionFilter.SelectedItem = selected;
        _modGameVersion = selected.Value;
        _suppressModFilter = false;
    }

    private void ModFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModFilter) return;
        _userChangedModFilter = true;
        if (ModVersionFilter.SelectedItem is FilterOption version) _modGameVersion = version.Value;
        if (ModLoaderFilter.SelectedItem is FilterOption loader) _modLoaderFilter = loader.Value;
        if (ModCategoryFilter.SelectedItem is FilterOption category) _modCategoryFilter = category.Value;
        if (ModSortFilter.SelectedItem is FilterOption sort) _modSort = sort.Value;
        _ = LoadModrinthAsync();
    }

    private void ResetModFilters_Click(object sender, RoutedEventArgs e)
    {
        if (ModVersionFilter.Items.Count == 0) return;
        _suppressModFilter = true;
        ModVersionFilter.SelectedIndex = 0;
        ModLoaderFilter.SelectedIndex = 0;
        ModCategoryFilter.SelectedIndex = 0;
        ModSortFilter.SelectedIndex = 0;
        _suppressModFilter = false;
        _modGameVersion = "";
        _modLoaderFilter = "";
        _modCategoryFilter = "";
        _modSort = "relevance";
        _userChangedModFilter = true;
        _ = LoadModrinthAsync();
    }

    private void ShowResourceList(ModrinthSearchResult result)
    {
        var list = result.Items;
        ModList.ItemsSource = list;
        _modTotalHits = result.TotalHits > 0 ? result.TotalHits : list.Count;
        ModEmptyPanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_modGameVersion)) parts.Add(_modGameVersion);
        if (!string.IsNullOrEmpty(_modLoaderFilter)) parts.Add(LoaderLabel(_modLoaderFilter));
        if (!string.IsNullOrEmpty(_modCategoryFilter) && ModCategoryFilter.SelectedItem is FilterOption category)
            parts.Add(category.Display);
        var suffix = parts.Count == 0 ? "" : "  ·  " + string.Join("  ·  ", parts);
        ResourceStatusText.Text = (list.Count == 0 ? "没有找到项目" : $"找到 {_modTotalHits:N0} 个项目") + suffix;
        ModLoadMoreButton.Visibility = list.Count > 0 && list.Count < _modTotalHits
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ModrinthSearchOptions BuildSearchOptions(int offset = 0) => new()
    {
        ProjectType = _projectType,
        Query = ModNames.TranslateQuery(ModSearchBox.Text),
        GameVersion = _modGameVersion,
        Loader = _projectType == "mod" ? _modLoaderFilter : "",
        Category = _modCategoryFilter,
        Sort = _modSort,
        Limit = 30,
        Offset = offset
    };

    private async Task LoadModrinthAsync()
    {
        var type = _projectType;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        ResourceStatusText.Text = "正在搜索 Modrinth...";
        ModLoadMoreButton.Visibility = Visibility.Collapsed;
        try
        {
            var result = await Modrinth.SearchAsync(BuildSearchOptions(), token);
            if (token.IsCancellationRequested || _projectType != type) return;
            ShowResourceList(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            ResourceStatusText.Text = "搜索失败，请检查网络后重试";
            Logger.Error("Modrinth 搜索失败: " + ex.Message);
        }
    }

    private async void LoadMoreMods_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingMore) return;
        var current = (ModList.ItemsSource as IEnumerable<ModrinthProject>)?.ToList() ?? new List<ModrinthProject>();
        if (current.Count >= _modTotalHits)
        {
            ModLoadMoreButton.Visibility = Visibility.Collapsed;
            return;
        }

        _loadingMore = true;
        ModLoadMoreButton.IsEnabled = false;
        ModLoadMoreButton.Content = "正在加载...";
        var token = _searchCts?.Token ?? CancellationToken.None;
        try
        {
            var result = await Modrinth.SearchAsync(BuildSearchOptions(current.Count), token);
            if (token.IsCancellationRequested) return;
            if (result.TotalHits > 0) _modTotalHits = result.TotalHits;
            var merged = current.Concat(result.Items).ToList();
            ModList.ItemsSource = merged;
            Logger.Info($"Modrinth 加载更多：新增 {result.Items.Count} 项，累计 {merged.Count} 项");
            ModLoadMoreButton.Visibility = merged.Count < _modTotalHits
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (result.Items.Count == 0) _modTotalHits = merged.Count;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) Logger.Error("Modrinth 加载更多失败: " + ex.Message);
        }
        finally
        {
            _loadingMore = false;
            ModLoadMoreButton.IsEnabled = true;
            ModLoadMoreButton.Content = "加载更多";
        }
    }

    private void SearchModrinth_Click(object sender, RoutedEventArgs e) => _ = LoadModrinthAsync();

    private void RefreshModrinth_Click(object sender, RoutedEventArgs e) => _ = LoadModrinthAsync();

    private void ModSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = LoadModrinthAsync();
    }

    // ================= 项目详情 =================

    private void ModCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement { Tag: ModrinthProject project }) _ = OpenDetailAsync(project);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private async Task OpenDetailAsync(ModrinthProject project)
    {
        _detailProject = project;
        ResourceListView.Visibility = Visibility.Collapsed;
        ModDetailView.Visibility = Visibility.Visible;
        ModVersionList.ItemsSource = null;
        DetailVersionStatusText.Text = "正在加载版本...";
        ShowBody(project, project.Body, translate: false);
        FillDetailHeader(project);

        try
        {
            var details = await Modrinth.GetProjectAsync(project.Id);
            if (_detailProject != project || details == null) return;
            project.Body = details.Body;
            project.Categories = details.Categories.Count > 0 ? details.Categories : project.Categories;
            project.Loaders = details.Loaders.Count > 0 ? details.Loaders : project.Loaders;
            project.Downloads = details.Downloads;
            project.Follows = details.Follows;
            project.SourceUrl = details.SourceUrl;
            project.IssuesUrl = details.IssuesUrl;
            project.WikiUrl = details.WikiUrl;
            project.UpdatedAt = details.UpdatedAt;
            FillDetailHeader(project);
            ShowBody(project, project.Body, translate: true);
        }
        catch (Exception ex)
        {
            Logger.Error("获取 Modrinth 项目详情失败: " + ex.Message);
            if (_detailProject == project) DetailBodyText.Text = "无法加载项目介绍：" + ex.Message;
        }

        try
        {
            var versions = await Modrinth.GetVersionsAsync(project.Id);
            if (_detailProject != project) return;
            ShowDetailVersions(versions);
        }
        catch (Exception ex)
        {
            Logger.Error("获取 Modrinth 版本列表失败: " + ex.Message);
            if (_detailProject == project) DetailVersionStatusText.Text = "版本列表加载失败";
        }
    }

    private void FillDetailHeader(ModrinthProject project)
    {
        DetailTitleText.Text = project.DisplayTitle;
        DetailAuthorText.Text = project.AuthorText;
        var meta = new List<string> { project.DownloadsText };
        if (!string.IsNullOrEmpty(project.UpdatedText)) meta.Add(project.UpdatedText);
        if (!string.IsNullOrEmpty(project.LoadersText)) meta.Add(project.LoadersText);
        DetailMetaText.Text = string.Join("  ·  ", meta);
        DetailInitialText.Text = project.Initial;
        DetailIcon.Url = project.IconUrl;
        DetailCategoryList.ItemsSource = project.Categories.Take(8).ToList();
    }

    private void ShowDetailVersions(List<ModrinthVersion> versions)
    {
        var filtered = versions.AsEnumerable();
        var note = "";
        if (!string.IsNullOrEmpty(_modGameVersion))
        {
            filtered = filtered.Where(v =>
                v.GameVersions.Contains(_modGameVersion, StringComparer.OrdinalIgnoreCase));
            note += "  ·  " + _modGameVersion;
        }
        if (_projectType == "mod" && !string.IsNullOrEmpty(_modLoaderFilter))
        {
            filtered = filtered.Where(v =>
                v.Loaders.Contains(_modLoaderFilter, StringComparer.OrdinalIgnoreCase));
            note += "  ·  " + LoaderLabel(_modLoaderFilter);
        }

        var list = filtered.ToList();
        if (list.Count == 0)
        {
            list = versions.Take(60).ToList();
            DetailVersionStatusText.Text = "当前筛选下没有匹配文件，已显示全部版本";
        }
        else
        {
            DetailVersionStatusText.Text = $"共 {list.Count} 个文件" + note;
        }

        var folder = GetResourceFolder(AppState.SelectedVersion);
        foreach (var version in list)
        {
            if (string.IsNullOrEmpty(version.FileName)) continue;
            try
            {
                var gameVersion = string.IsNullOrEmpty(_modGameVersion)
                    ? version.GameVersions.FirstOrDefault()
                    : _modGameVersion;
                var renamed = _detailProject == null
                    ? ""
                    : ModNaming.Build(AppState.Config.ModFileNameFormat, _detailProject, version, gameVersion);
                version.IsInstalled = File.Exists(Path.Combine(folder, Utils.SafeFileName(version.FileName)))
                    || (renamed.Length > 0 && File.Exists(Path.Combine(folder, renamed)));
            }
            catch { }
        }
        ModVersionList.ItemsSource = list;
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        _translateCts?.Cancel();
        _detailProject = null;
        ModDetailView.Visibility = Visibility.Collapsed;
        ResourceListView.Visibility = Visibility.Visible;
        ModVersionList.ItemsSource = null;
        var count = (ModList.ItemsSource as ICollection<ModrinthProject>)?.Count ?? 0;
        ModLoadMoreButton.Visibility = count > 0 && count < _modTotalHits
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>跳转到 Modrinth 项目页，项目没有 slug 时退回站内搜索。</summary>
    private void OpenModrinth_Click(object sender, RoutedEventArgs e)
    {
        if (_detailProject is not { } project) return;
        var type = string.IsNullOrEmpty(project.ProjectType) ? "mods" : project.ProjectType;
        Utils.OpenUrl(string.IsNullOrEmpty(project.Slug)
            ? $"https://modrinth.com/{type}?q={Uri.EscapeDataString(project.Title)}"
            : project.ProjectUrl);
    }

    /// <summary>用模组名在 MC 百科搜索对应条目。</summary>
    private void OpenMcmod_Click(object sender, RoutedEventArgs e)
    {
        if (_detailProject is not { } project) return;
        var keyword = string.IsNullOrEmpty(project.ChineseTitle) ? project.Title : project.ChineseTitle;
        Utils.OpenUrl($"https://search.mcmod.cn/s?key={Uri.EscapeDataString(keyword)}");
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (_detailProject is not { } project) return;
        try
        {
            Clipboard.SetText(project.Title);
            if (sender is Button button)
            {
                var original = button.Content;
                button.Content = "已复制";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    button.Content = original;
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("复制模组名称失败: " + ex.Message);
        }
    }

    /// <summary>显示项目介绍：默认显示中文（机翻），可切换回英文原文。</summary>
    private void ShowBody(ModrinthProject project, string body, bool translate)
    {
        _translateCts?.Cancel();
        _bodyClean = string.IsNullOrWhiteSpace(body) ? "正在加载项目介绍..." : CleanBody(body);
        _bodyZh = "";
        _bodyShowZh = true;
        BodyLangHint.Text = "";
        BodyLangButton.Visibility = Visibility.Collapsed;
        RenderBody();
        if (!translate || project.Id.Length == 0) return;
        if (ModTranslator.HasChinese(_bodyClean))
        {
            BodyLangHint.Text = "原文为中文";
            return;
        }
        _ = TranslateBodyAsync(project);
    }

    private void RenderBody()
    {
        DetailBodyText.Text = _bodyShowZh && _bodyZh.Length > 0 ? _bodyZh : _bodyClean;
        BodyLangButton.Content = _bodyShowZh ? "查看原文" : "查看中文";
    }

    private async Task TranslateBodyAsync(ModrinthProject project)
    {
        _translateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _translateCts = cts;
        var source = _bodyClean;
        BodyLangHint.Text = "正在翻译为中文…";
        BodyLangButton.Visibility = Visibility.Visible;
        try
        {
            var chinese = await ModTranslator.TranslateAsync(project.Id, source, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_detailProject, project)) return;
            _bodyZh = chinese;
            BodyLangHint.Text = "机器翻译";
            RenderBody();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("翻译项目介绍失败: " + ex.Message);
            if (!ReferenceEquals(_detailProject, project)) return;
            BodyLangHint.Text = "翻译失败，可点击“打开项目页面”查看原站";
            BodyLangButton.Visibility = Visibility.Collapsed;
        }
    }

    private void BodyLang_Click(object sender, RoutedEventArgs e)
    {
        _bodyShowZh = !_bodyShowZh;
        RenderBody();
    }

    private static string CleanBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "该项目没有提供详细介绍，可在项目页面查看完整说明。";
        var text = body.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"(?m)^#{1,6}\s*", "");
        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length > 4000 ? text[..4000] + "\n…" : text;
    }

    private static string LoaderLabel(string loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => loader
    };

    private sealed record FilterOption(string Display, string Value)
    {
        public override string ToString() => Display;
    }

    // ================= 安装 =================

    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthProject project }) return;
        await InstallProjectAsync(project);
    }

    private async void InstallDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_detailProject == null) return;
        await InstallProjectAsync(_detailProject);
    }

    private async void InstallModVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthVersion version }) return;
        var project = _detailProject;
        if (project == null) return;
        if (_busy)
        {
            MessageDialog.Show(Window.GetWindow(this), "请稍候", "当前有下载任务正在进行，请等待完成。", warning: true);
            return;
        }

        var target = ResolveInstallTarget(string.IsNullOrEmpty(_modGameVersion) ? null : _modGameVersion);
        var gameVersion = string.IsNullOrEmpty(_modGameVersion)
            ? version.GameVersions.FirstOrDefault()
            : _modGameVersion;
        if (_projectType == "mod" && target == null)
        {
            MessageDialog.Show(Window.GetWindow(this), "没有可用的目标版本",
                "当前筛选的游戏版本还没有安装带模组加载器的版本。\n\n请先在“下载 → Fabric 加载器”中安装，或切换筛选条件。", warning: true);
            return;
        }

        BeginInstall("安装 " + project.Title);
        var canceled = false;
        try
        {
            await DownloadFileAsync(project, version, target, gameVersion);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            Logger.Error("安装资源失败: " + ex);
            MessageDialog.Show(Window.GetWindow(this), "安装失败",
                ex.Message + "\n\n详细日志：" + Logger.LogFile, warning: true);
        }
        finally
        {
            EndInstall(canceled);
        }
    }

    private async Task InstallProjectAsync(ModrinthProject project)
    {
        if (_busy)
        {
            MessageDialog.Show(Window.GetWindow(this), "请稍候", "当前有下载任务正在进行，请等待完成。", warning: true);
            return;
        }

        var installed = AppState.SelectedVersion;
        var mcVersion = !string.IsNullOrEmpty(_modGameVersion) ? _modGameVersion : installed?.McVersion;
        var loader = !string.IsNullOrEmpty(_modLoaderFilter) ? _modLoaderFilter : installed?.Loader;

        if (_projectType == "mod")
        {
            if (string.IsNullOrEmpty(mcVersion))
            {
                MessageDialog.Show(Window.GetWindow(this), "尚未选择版本",
                    "请先在筛选栏选择游戏版本，或安装一个版本后再安装模组。", warning: true);
                return;
            }
            if (string.IsNullOrEmpty(loader) || loader is "原版" or "未知")
            {
                MessageDialog.Show(Window.GetWindow(this), "需要模组加载器",
                    $"当前版本（{mcVersion}）没有模组加载器，无法直接加载模组。\n\n请先在“下载 → Fabric 加载器”中安装模组加载器，或在筛选栏选择 Fabric / Forge。",
                    warning: true);
                return;
            }
        }

        BeginInstall("安装 " + project.Title);
        var canceled = false;
        try
        {
            var target = ResolveInstallTarget(mcVersion);
            if (_projectType == "mod" && target == null)
            {
                EndDownloadBar();
                MessageDialog.Show(Window.GetWindow(this), "没有可用的目标版本",
                    $"筛选的游戏版本（{mcVersion}）还没有安装带模组加载器的版本。\n\n请先安装对应版本，或在筛选栏切换到已安装的版本。",
                    warning: true);
                return;
            }

            var versions = await Modrinth.GetVersionsAsync(project.Id, _downloadCts!.Token);
            var pick = Modrinth.PickVersion(versions, _projectType, mcVersion, loader);
            if (pick == null)
            {
                EndDownloadBar();
                MessageDialog.Show(Window.GetWindow(this), "没有适配的版本",
                    $"没有找到适配 {mcVersion ?? "未知"} · {LoaderLabel(loader ?? "")} 的文件。\n\n可以更换筛选条件，或在详情页手动选择文件。",
                    warning: true);
                return;
            }
            await DownloadFileAsync(project, pick, target, mcVersion);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            Logger.Error("安装资源失败: " + ex);
            MessageDialog.Show(Window.GetWindow(this), "安装失败",
                ex.Message + "\n\n详细日志：" + Logger.LogFile, warning: true);
        }
        finally
        {
            EndInstall(canceled);
        }
    }

    private GameVersion? ResolveInstallTarget(string? mcVersion)
    {
        if (_projectType != "mod") return AppState.SelectedVersion;

        var loader = string.IsNullOrEmpty(_modLoaderFilter) ? null : LoaderLabel(_modLoaderFilter);
        bool Match(GameVersion version)
        {
            if (version.Loader is "原版" or "未知") return false;
            if (!string.IsNullOrEmpty(mcVersion) &&
                !string.Equals(version.McVersion, mcVersion, StringComparison.OrdinalIgnoreCase)) return false;
            if (loader != null && !string.Equals(version.Loader, loader, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        if (AppState.SelectedVersion is { } selected && Match(selected)) return selected;
        return AppState.Versions.FirstOrDefault(Match);
    }

    private async Task DownloadFileAsync(ModrinthProject project, ModrinthVersion pick, GameVersion? target,
        string? mcVersion)
    {
        var folder = GetResourceFolder(target);
        Directory.CreateDirectory(folder);
        var fileName = ModNaming.Build(AppState.Config.ModFileNameFormat, project, pick, mcVersion);
        if (string.IsNullOrEmpty(fileName)) fileName = Utils.SafeFileName(pick.FileName);
        var destination = Path.Combine(folder, fileName);

        if (File.Exists(destination))
        {
            EndDownloadBar();
            if (!MessageDialog.Show(Window.GetWindow(this), "文件已存在",
                    $"目标位置已存在以下文件：\n{destination}\n\n是否覆盖？", "覆盖", "取消", true)) return;
            ShowDownloadBar("安装 " + project.Title);
        }

        var ok = await Http.DownloadAsync(Http.Candidates(pick.FileUrl), destination, pick.Sha1,
            _downloadCts!.Token);
        if (!ok) throw new IOException("文件下载失败，请检查网络后重试");
        DownloadCenter.Complete("安装完成");
        EndDownloadBar();
        var open = MessageDialog.Show(Window.GetWindow(this), "安装完成",
            $"{project.Title} 已安装。\n\n文件：{fileName}\n位置：{folder}",
            "打开文件夹", "完成");
        if (open) Utils.OpenFolder(folder);
    }

    private void BeginInstall(string title)
    {
        _busy = true;
        _downloadCts = new CancellationTokenSource();
        ShowDownloadBar(title);
    }

    private void EndInstall(bool canceled)
    {
        EndDownloadBar();
        _busy = false;
        _downloadCts?.Dispose();
        _downloadCts = null;
        if (canceled) MessageDialog.Show(Window.GetWindow(this), "已取消", "下载已取消。");
    }

    private string GetResourceFolder(GameVersion? version)
    {
        return _projectType switch
        {
            "resourcepack" => Path.Combine(AppState.Config.GameDir, "resourcepacks"),
            "shader" => Path.Combine(AppState.Config.GameDir, "shaderpacks"),
            _ => version != null
                ? AppState.ResolveModsDir(version)
                : Path.Combine(AppState.Config.GameDir, "mods")
        };
    }

    // ================= 状态条 =================

    private void ShowDownloadBar(string title)
    {
        DownloadStatusBar.Visibility = Visibility.Visible;
        DownloadTitleText.Text = title;
        DownloadPercentText.Text = "0%";
        DownloadDetailText.Text = "正在准备...";
        DownloadProgress.IsIndeterminate = true;
        DownloadProgress.Value = 0;
        DownloadCancelButton.IsEnabled = true;
        _barTimer.Start();
    }

    private void EndDownloadBar()
    {
        _barTimer.Stop();
        DownloadStatusBar.Visibility = Visibility.Collapsed;
        DownloadCancelButton.IsEnabled = true;
    }

    private void UpdateDownloadBar()
    {
        var download = DownloadCenter.Current;
        if (download.Active)
        {
            DownloadTitleText.Text = download.Title;
            DownloadProgress.IsIndeterminate = download.Indeterminate;
            if (!download.Indeterminate) DownloadProgress.Value = download.Percent;
            DownloadPercentText.Text = download.Indeterminate ? "准备中" : $"{download.Percent:0}%";
            var detail = download.Detail;
            if (download.SpeedBytes > 0) detail += (detail.Length > 0 ? "  ·  " : "") + Utils.FormatBytes(download.SpeedBytes) + "/s";
            DownloadDetailText.Text = detail;
        }
    }

    private void DownloadCancel_Click(object sender, RoutedEventArgs e)
    {
        DownloadCancelButton.IsEnabled = false;
        DownloadDetailText.Text = "正在取消...";
        try { _downloadCts?.Cancel(); } catch { }
    }

    private sealed record FabricLoaderOption(string Version, bool Stable)
    {
        public string Display => Stable ? Version + "（稳定）" : Version;
    }
}
