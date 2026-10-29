using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class ModManageWindow : Window
{
    private const string DisabledSuffix = ".disabled";
    private readonly GameVersion _version;
    private readonly string _modsDir;
    private List<ModEntry> _all = new();
    private CancellationTokenSource? _loadCts;

    public ModManageWindow(GameVersion version)
    {
        InitializeComponent();
        _version = version;
        _modsDir = AppState.ResolveModsDir(version);
        VersionText.Text = version.Id;
        ModsPathText.Text = _modsDir + (version.UsesIsolatedDir ? "（版本隔离）" : "（全局模组目录）");
        Loaded += (_, _) => ReloadMods();
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        base.OnClosed(e);
    }

    private async void ReloadMods()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();

        var entries = new List<ModEntry>();
        try
        {
            Directory.CreateDirectory(_modsDir);
            foreach (var file in Directory.EnumerateFiles(_modsDir, "*.jar"))
                entries.Add(new ModEntry(file, enabled: true));
            foreach (var file in Directory.EnumerateFiles(_modsDir, "*.jar" + DisabledSuffix))
                entries.Add(new ModEntry(file, enabled: false));
        }
        catch (Exception ex)
        {
            Logger.Error("读取模组目录失败: " + ex.Message);
            StatusText.Text = "读取模组目录失败：" + ex.Message;
            return;
        }

        _all = entries
            .OrderByDescending(e => e.Enabled)
            .ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ApplyFilter();
        UpdateStatus();

        if (_all.Count == 0)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var total = _all.Count;
        var done = 0;
        LoadingText.Text = $"正在读取模组信息… 0/{total}";
        LoadingPanel.Visibility = Visibility.Visible;

        var gate = new SemaphoreSlim(8);
        var tasks = _all.Select(entry => LoadEntryAsync(entry, gate, cts.Token, () =>
        {
            done++;
            if (done % 20 == 0 || done == total)
                LoadingText.Text = $"正在读取模组信息… {done}/{total}";
        })).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logger.Error("读取模组信息失败: " + ex.Message);
        }
        finally
        {
            gate.Dispose();
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_loadCts, cts)) return;

        _all = _all
            .OrderByDescending(e => e.Enabled)
            .ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LoadingPanel.Visibility = Visibility.Collapsed;
        ApplyFilter();
    }

    private async Task LoadEntryAsync(ModEntry entry, SemaphoreSlim gate, CancellationToken token, Action onLoaded)
    {
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var meta = ModMetadata.Read(entry.FilePath);
            var iconPath = meta == null ? null : ModMetadata.ExtractIcon(entry.FilePath, meta.IconEntry);
            if (token.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                entry.Apply(meta, iconPath);
                onLoaded();
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"读取模组信息失败 ({entry.FileName}): {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    private void ApplyFilter()
    {
        var keyword = SearchBox.Text.Trim();
        var list = string.IsNullOrEmpty(keyword)
            ? _all
            : _all.Where(m => m.Matches(keyword)).ToList();
        ModList.ItemsSource = null;
        ModList.ItemsSource = list;

        if (_all.Count == 0)
        {
            EmptyText.Text = "模组目录里还没有模组，可以点击右上角“添加模组…”导入。";
            EmptyPanel.Visibility = Visibility.Visible;
        }
        else if (list.Count == 0)
        {
            EmptyText.Text = "没有匹配的模组。";
            EmptyPanel.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStatus()
    {
        var enabled = _all.Count(m => m.Enabled);
        var disabled = _all.Count - enabled;
        CountText.Text = $"共 {_all.Count} 个模组，已启用 {enabled} 个" + (disabled > 0 ? $"，已禁用 {disabled} 个" : "");
        StatusText.Text = _version.UsesIsolatedDir
            ? "该版本使用版本隔离，模组仅对本版本生效。"
            : "该版本共享全局 mods 目录，修改会影响所有未隔离的版本。";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ModEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: ModEntry entry } toggle) return;
        SetEnabled(entry, toggle.IsChecked == true);
    }

    private void SetEnabled(ModEntry entry, bool target)
    {
        if (target == entry.Enabled) return;
        try
        {
            if (target)
            {
                var destination = entry.FilePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                    ? entry.FilePath[..^DisabledSuffix.Length]
                    : entry.FilePath;
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(entry.FilePath, destination);
            }
            else
            {
                var destination = entry.FilePath + DisabledSuffix;
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(entry.FilePath, destination);
            }
            Logger.Info($"模组已{(target ? "启用" : "禁用")}：{entry.DisplayName}");
            ReloadMods();
        }
        catch (Exception ex)
        {
            Logger.Error("切换模组状态失败: " + ex.Message);
            MessageDialog.Show(this, "操作失败", ex.Message, warning: true);
            ReloadMods();
        }
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _all.Where(m => !m.Enabled).ToList())
        {
            try
            {
                var destination = entry.FilePath[..^DisabledSuffix.Length];
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(entry.FilePath, destination);
            }
            catch (Exception ex)
            {
                Logger.Error("启用模组失败: " + ex.Message);
            }
        }
        ReloadMods();
    }

    private void DisableAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = _all.Where(m => m.Enabled).ToList();
        if (targets.Count == 0) return;
        if (!MessageDialog.Show(this, "禁用全部模组",
                $"将禁用 {targets.Count} 个模组（文件保留，重命名后不再加载）。是否继续？", "禁用", "取消"))
            return;
        foreach (var entry in targets)
        {
            try
            {
                var destination = entry.FilePath + DisabledSuffix;
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(entry.FilePath, destination);
            }
            catch (Exception ex)
            {
                Logger.Error("禁用模组失败: " + ex.Message);
            }
        }
        ReloadMods();
    }

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ModEntry entry }) return;
        DeleteEntry(entry);
    }

    private void DeleteEntry(ModEntry entry)
    {
        if (!MessageDialog.Show(this, "删除模组",
                $"确定要删除 {entry.DisplayName} 吗？\n\n文件：{entry.FileName}\n删除后无法恢复。", "删除", "取消"))
            return;
        try
        {
            File.Delete(entry.FilePath);
            Logger.Info("已删除模组：" + entry.FileName);
            ReloadMods();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "删除失败", ex.Message, warning: true);
        }
    }

    private static ModEntry? EntryFrom(object sender)
    {
        if (sender is not MenuItem { Tag: ModEntry entry }) return null;
        return entry;
    }

    private void ModList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ModList.ContainerFromElement(source) is not ListBoxItem { DataContext: ModEntry entry } item)
        {
            e.Handled = true;
            return;
        }
        item.IsSelected = true;

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("查看模组详情", entry, DetailMod_Click));
        menu.Items.Add(CreateMenuItem(entry.Enabled ? "禁用模组" : "启用模组", entry, ToggleModMenu_Click));
        menu.Items.Add(CreateMenuItem("打开所在文件夹", entry, OpenModFolder_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("删除模组", entry, DeleteModMenu_Click));
        ModList.ContextMenu = menu;
        menu.IsOpen = true;
        Logger.Info("右键菜单已打开：" + entry.FileName);
        e.Handled = true;
    }

    private static MenuItem CreateMenuItem(string header, ModEntry entry, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header, Tag = entry };
        item.Click += handler;
        return item;
    }

    private void DetailMod_Click(object sender, RoutedEventArgs e) => OpenDetail(EntryFrom(sender));

    private void ToggleModMenu_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryFrom(sender);
        if (entry != null) SetEnabled(entry, !entry.Enabled);
    }

    private void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryFrom(sender);
        if (entry != null) Utils.OpenFolder(Path.GetDirectoryName(entry.FilePath) ?? _modsDir);
    }

    private void DeleteModMenu_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryFrom(sender);
        if (entry != null) DeleteEntry(entry);
    }

    private void ModList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ModList.ContainerFromElement(source) is ListBoxItem { DataContext: ModEntry entry })
            OpenDetail(entry);
    }

    private void OpenDetail(ModEntry? entry)
    {
        if (entry == null) return;
        new ModDetailWindow(_version, entry.FilePath, entry.Enabled) { Owner = this }.ShowDialog();
        ReloadMods();
    }

    private void AddMods_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加的模组",
            Filter = "模组文件 (*.jar)|*.jar",
            Multiselect = true,
            InitialDirectory = _modsDir
        };
        if (dialog.ShowDialog(this) != true) return;
        var copied = 0;
        var skipped = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var destination = Path.Combine(_modsDir, Path.GetFileName(file));
                if (File.Exists(destination) || File.Exists(destination + DisabledSuffix))
                {
                    skipped++;
                    continue;
                }
                File.Copy(file, destination);
                copied++;
            }
            catch (Exception ex)
            {
                Logger.Error("添加模组失败: " + ex.Message);
                skipped++;
            }
        }
        ReloadMods();
        StatusText.Text = $"已添加 {copied} 个模组" + (skipped > 0 ? $"，跳过 {skipped} 个（同名或失败）" : "");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(_modsDir);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private static string Clean(string text) => Utils.CleanText(text);

    private sealed class ModEntry : INotifyPropertyChanged
    {
        private string _name;
        private string _chinese = "";
        private string _description = "";
        private string _version = "";
        private string _authors = "";
        private BitmapImage? _icon;
        private bool _hasIcon;
        private readonly long _size;

        public ModEntry(string path, bool enabled)
        {
            FilePath = path;
            Enabled = enabled;
            FileName = Path.GetFileName(path);
            var full = enabled ? path : path[..^DisabledSuffix.Length];
            _name = Path.GetFileNameWithoutExtension(full);
            _chinese = ModNames.ChineseFor(_name, fuzzy: true);
            try { _size = new FileInfo(path).Length; }
            catch { _size = 0; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath { get; }
        public bool Enabled { get; }
        public string FileName { get; }
        public string Description => _description;
        public BitmapImage? Icon => _icon;
        public bool HasIcon => _hasIcon;
        public string SizeText => Utils.FormatBytes(_size);
        public string SortName => _name;

        public string DisplayName
        {
            get
            {
                var name = _name.Trim();
                if (name.Length == 0) return FileName;
                if (AppState.Config.ModManageStyle == ModNaming.StyleFileName) return FileName;
                return _chinese.Length > 0 ? _chinese : name;
            }
        }

        public string Initial
        {
            get
            {
                var name = DisplayName;
                if (name.Length == 0) return "?";
                return char.IsSurrogatePair(name, 0) ? name[..2] : name[..1];
            }
        }

        public string MetaText
        {
            get
            {
                var parts = new List<string>();
                if (AppState.Config.ModManageStyle == ModNaming.StyleFileName)
                    parts.Add(_chinese.Length > 0 ? "译名：" + _chinese : _name);
                else
                    parts.Add(FileName);
                if (_version.Length > 0) parts.Add("v" + _version);
                if (_authors.Length > 0) parts.Add(_authors);
                parts.Add(Enabled ? "已启用" : "已禁用");
                return string.Join("  ·  ", parts);
            }
        }

        public bool Matches(string keyword)
        {
            return DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || _name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || _chinese.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || _description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || _version.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || _authors.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        public void Apply(ModMetadata? meta, string? iconPath)
        {
            if (meta != null)
            {
                if (!string.IsNullOrWhiteSpace(meta.Name))
                {
                    _name = Clean(meta.Name);
                    _chinese = ModNames.ChineseFor(_name, fuzzy: true);
                }
                _description = Clean(meta.Description);
                _version = meta.Version?.Trim() ?? "";
                _authors = Clean(meta.Authors);
            }
            if (!string.IsNullOrEmpty(iconPath))
            {
                var bitmap = ImageLoader.LoadLocalFile(iconPath);
                if (bitmap != null)
                {
                    _icon = bitmap;
                    _hasIcon = true;
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
