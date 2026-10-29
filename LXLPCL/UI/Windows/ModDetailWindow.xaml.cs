using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class ModDetailWindow : Window
{
    private const string DisabledSuffix = ".disabled";

    private readonly GameVersion _version;
    private readonly CancellationTokenSource _cts = new();
    private string _path;
    private bool _enabled;
    private bool _switching;
    private bool _hasIcon;
    private string _name = "";
    private string _chinese = "";
    private string _fileName;
    private string _slug = "";
    private ModMetadata? _meta;

    public ModDetailWindow(GameVersion version, string filePath, bool enabled)
    {
        InitializeComponent();
        _version = version;
        _path = filePath;
        _enabled = enabled;
        _fileName = Path.GetFileName(filePath);

        DetailSwitch.IsChecked = enabled;
        UpdateStatus();
        Loaded += async (_, _) => await LoadInfoAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task LoadInfoAsync()
    {
        var token = _cts.Token;
        var path = _path;
        var result = await Task.Run(() =>
        {
            var meta = ModMetadata.Read(path);
            var icon = meta == null ? null : ModMetadata.ExtractIcon(path, meta.IconEntry);
            return (meta, icon);
        });
        if (token.IsCancellationRequested) return;

        _meta = result.meta;
        if (_meta != null)
        {
            _name = Utils.CleanText(_meta.Name);
            _chinese = ModNames.ChineseFor(_name, fuzzy: true);
        }
        if (string.IsNullOrEmpty(_name)) _name = Path.GetFileNameWithoutExtension(_path);
        if (_chinese.Length == 0) _chinese = ModNames.ChineseFor(_name, fuzzy: true);

        if (!string.IsNullOrEmpty(result.icon))
        {
            var bitmap = ImageLoader.LoadLocalFile(result.icon);
            if (bitmap != null) ApplyIcon(bitmap);
        }

        ApplyLocalInfo();
        await EnrichAsync(token);
    }

    private void ApplyLocalInfo()
    {
        var style = AppState.Config.ModManageStyle;
        if (style == ModNaming.StyleFileName)
        {
            NameText.Text = _fileName;
            EnglishText.Text = _chinese.Length > 0 ? $"{_chinese}  ·  {_name}" : _name;
        }
        else
        {
            NameText.Text = _chinese.Length > 0 ? _chinese : _name;
            EnglishText.Text = _chinese.Length > 0 ? _name : _fileName;
        }
        InitialText.Text = NameText.Text.Length == 0 ? "?" : NameText.Text[..1];

        var description = Utils.CleanText(_meta?.Description ?? "");
        DescText.Text = description.Length > 0 ? description : "这个模组没有在 jar 内提供描述。";

        long size = 0;
        try { size = new FileInfo(_path).Length; } catch { }
        var meta = new List<string>();
        if (!string.IsNullOrEmpty(_meta?.Loader)) meta.Add(_meta.Loader);
        if (!string.IsNullOrEmpty(_meta?.Version)) meta.Add("v" + _meta.Version);
        if (!string.IsNullOrEmpty(_meta?.Authors)) meta.Add(Utils.CleanText(_meta.Authors));
        meta.Add(Utils.FormatBytes(size));
        MetaText.Text = string.Join("  ·  ", meta);

        VersionValue.Text = string.IsNullOrEmpty(_meta?.Version) ? "-" : _meta.Version;
        AuthorValue.Text = string.IsNullOrEmpty(_meta?.Authors) ? "-" : Utils.CleanText(_meta.Authors);
        ModIdValue.Text = string.IsNullOrEmpty(_meta?.ModId) ? "-" : _meta.ModId;
        LoaderValue.Text = string.IsNullOrEmpty(_meta?.Loader) ? "-" : _meta.Loader;
        TargetValue.Text = _version.Id;
        SizeValue.Text = Utils.FormatBytes(size);
        UpdatePaths();
    }

    private void UpdatePaths()
    {
        FileNameText.Text = _fileName;
        PathText.Text = _path;
    }

    private void UpdateStatus()
    {
        StatusText.Text = _enabled ? "已启用" : "已禁用（文件保留，不加载）";
        DetailSwitch.IsChecked = _enabled;
    }

    private async Task EnrichAsync(CancellationToken token)
    {
        if (string.IsNullOrEmpty(_name)) return;
        OnlineText.Text = "正在 Modrinth 查询同名项目…";
        try
        {
            var list = await Modrinth.SearchAsync("mod", _name, token);
            if (token.IsCancellationRequested) return;

            var best = list.FirstOrDefault(p => p.Title.Equals(_name, StringComparison.OrdinalIgnoreCase))
                       ?? list.FirstOrDefault(p =>
                           Normalize(p.Title).Equals(Normalize(_name), StringComparison.OrdinalIgnoreCase));
            if (best == null)
            {
                OnlineText.Text = "Modrinth 上没有找到同名项目，可点击“转到 Modrinth”按名称搜索。";
                return;
            }

            _slug = best.Slug;
            var parts = new List<string> { $"Modrinth：{best.DownloadsText}" };
            if (!string.IsNullOrEmpty(best.UpdatedText)) parts.Add(best.UpdatedText);
            if (!string.IsNullOrEmpty(best.VersionRange)) parts.Add("游戏版本 " + best.VersionRange);
            OnlineText.Text = string.Join("  ·  ", parts);

            if (!_hasIcon && !string.IsNullOrEmpty(best.IconUrl))
            {
                var bitmap = await ImageLoader.LoadAsync(best.IconUrl);
                if (bitmap != null && !token.IsCancellationRequested) ApplyIcon(bitmap);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("查询 Modrinth 项目失败: " + ex.Message);
            OnlineText.Text = "Modrinth 查询失败，可点击“转到 Modrinth”按名称搜索。";
        }
    }

    private static string Normalize(string text) =>
        new string(text.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();

    private void ApplyIcon(BitmapImage bitmap)
    {
        IconImage.Source = bitmap;
        IconImage.Visibility = Visibility.Visible;
        InitialText.Visibility = Visibility.Collapsed;
        _hasIcon = true;
    }

    private void DetailSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_switching) return;
        var target = DetailSwitch.IsChecked == true;
        if (target == _enabled) return;
        try
        {
            string destination;
            if (target)
            {
                destination = _path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                    ? _path[..^DisabledSuffix.Length]
                    : _path;
            }
            else
            {
                destination = _path + DisabledSuffix;
            }
            if (destination != _path)
            {
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(_path, destination);
                _path = destination;
                _fileName = Path.GetFileName(destination);
            }
            _enabled = target;
            UpdateStatus();
            UpdatePaths();
            Logger.Info($"模组已{(target ? "启用" : "禁用")}：{_fileName}");
        }
        catch (Exception ex)
        {
            Logger.Error("切换模组状态失败: " + ex.Message);
            _switching = true;
            DetailSwitch.IsChecked = _enabled;
            _switching = false;
            MessageDialog.Show(this, "操作失败", ex.Message, warning: true);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(Path.GetDirectoryName(_path) ?? "");

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(NameText.Text);
            OnlineText.Text = "名称已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "复制失败", ex.Message, warning: true);
        }
    }

    private void OpenModrinth_Click(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrEmpty(_slug)
            ? $"https://modrinth.com/mods?q={Uri.EscapeDataString(_name)}"
            : $"https://modrinth.com/mod/{_slug}";
        Startup(url, "无法打开 Modrinth");
    }

    private void OpenMcmod_Click(object sender, RoutedEventArgs e) =>
        Startup($"https://search.mcmod.cn/s?key={Uri.EscapeDataString(_name)}", "无法打开 MC 百科");

    private void Startup(string url, string errorTitle)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, errorTitle, ex.Message, warning: true);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!MessageDialog.Show(this, "删除模组",
                $"确定要删除 {NameText.Text} 吗？\n\n文件：{_fileName}\n删除后无法恢复。", "删除", "取消"))
            return;
        try
        {
            File.Delete(_path);
            Logger.Info("已删除模组：" + _fileName);
            Close();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "删除失败", ex.Message, warning: true);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
