using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class VersionsPage : PageBase
{
    private GameVersion? _current;
    private bool _loading;
    private bool _suppressSelection;
    private readonly DispatcherTimer _saveTimer;

    public VersionsPage()
    {
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (_current != null) AppState.SaveConfig();
        };
        AppState.JavasChanged += () =>
        {
            if (IsLoaded && _current != null) UpdateDetails(_current);
        };
    }

    public override void OnShown()
    {
        RefreshList();
    }

    private void RefreshList()
    {
        _suppressSelection = true;
        VersionList.ItemsSource = AppState.Versions;
        var target = AppState.SelectedVersion
                     ?? AppState.Versions.FirstOrDefault(v => v.Id == _current?.Id)
                     ?? AppState.Versions.FirstOrDefault();
        VersionList.SelectedItem = target;
        _suppressSelection = false;
        VersionCountText.Text = $"{AppState.Versions.Count} 个版本";
        VersionSummaryText.Text = AppState.Versions.Count == 0
            ? "还没有版本，可点击“扫描已有游戏”识别 PCL2 等启动器里已安装的整合包"
            : $"已安装 {AppState.Versions.Count} 个版本，可分别配置内存、Java 与隔离选项";
        UpdateDetails(target);
    }

    private void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        var window = new FindGameWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true) RefreshList();
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (VersionList.SelectedItem is GameVersion version)
        {
            AppState.SetSelectedVersion(version.Id);
            UpdateDetails(version);
        }
    }

    private void UpdateDetails(GameVersion? version)
    {
        _current = version;
        DetailPanel.Visibility = version == null ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = version == null ? Visibility.Visible : Visibility.Collapsed;
        LaunchVersionButton.IsEnabled = version != null;
        DeleteVersionButton.IsEnabled = version != null;
        if (version == null) return;

        DetailTitleText.Text = version.Id;
        DetailSubtitleText.Text = version.Subtitle;
        DetailIconTile.Background = version.IconBrush;
        DetailIconText.Text = version.IconText;
        DetailIconImage.Source = version.Logo;
        DetailIconImage.Visibility = version.HasLogo ? Visibility.Visible : Visibility.Collapsed;
        DetailIconTile.Visibility = version.HasLogo ? Visibility.Collapsed : Visibility.Visible;
        DetailLoaderText.Text = version.LoaderText;
        DetailPathText.Text = version.Folder;
        DetailPathText.ToolTip = version.Folder;
        DetailLoaderValueText.Text = version.Loader == "原版"
            ? "原版（无模组加载器）"
            : version.Loader + (string.IsNullOrEmpty(version.LoaderVersion) ? "" : " " + version.LoaderVersion);
        DetailTypeText.Text = version.TypeText;
        DetailLastPlayedText.Text = AppState.Config.LastPlayed.TryGetValue(version.Id, out var played)
            ? Utils.FormatDate(played)
            : "尚未游玩";

        var settings = AppState.Config.GetVersionSettings(version.Id);
        _loading = true;
        try
        {
            var follow = settings.MemoryMb == null;
            VMemoryFollow.IsChecked = follow;
            VMemorySlider.IsEnabled = !follow;
            VMemorySlider.Maximum = Math.Max(16384, MemoryInfo.TotalMb - 1024);
            VMemorySlider.Value = Math.Clamp(follow ? MemoryInfo.Resolve() : settings.MemoryMb!.Value,
                (int)VMemorySlider.Minimum, (int)VMemorySlider.Maximum);
            VMemoryValueText.Text = follow
                ? $"{(int)VMemorySlider.Value} MB（跟随全局{(AppState.Config.AutoMemory ? " · 自动配置" : "")}）"
                : $"{(int)VMemorySlider.Value} MB";
            VJvmArgsBox.Text = settings.JvmArgs ?? "";

            var javaOptions = new List<JavaOption> { new("跟随全局 / 自动选择（推荐）", null) };
            javaOptions.AddRange(AppState.Javas.Select(j =>
                new JavaOption($"Java {j.Major} · {j.Source} · {(j.Is64Bit ? "64 位" : "32 位")}", j.Path)));
            VJavaCombo.ItemsSource = javaOptions;
            VJavaCombo.SelectedItem = javaOptions.FirstOrDefault(o =>
                string.Equals(o.Path, settings.JavaPath, StringComparison.OrdinalIgnoreCase)) ?? javaOptions[0];

            VIsolatedToggle.IsChecked = version.UsesIsolatedDir;
        }
        finally
        {
            _loading = false;
        }

        UpdateModsPath(version);
        UpdateJavaHint(version);
    }

    private void UpdateModsPath(GameVersion version)
    {
        var mods = AppState.ResolveModsDir(version);
        DetailModsPathText.Text = mods;
        DetailModsPathText.ToolTip = mods;
    }

    private void UpdateJavaHint(GameVersion version)
    {
        var settings = AppState.Config.GetVersionSettings(version.Id);
        var configured = settings.JavaPath ?? AppState.Config.JavaPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var info = JavaFinder.Inspect(configured);
            VJavaEffectiveText.Text = info == null
                ? "指定 Java 不可用"
                : $"使用 Java {info.Major}";
            return;
        }
        var picked = JavaFinder.PickFor(version.JavaMajor);
        VJavaEffectiveText.Text = picked == null
            ? $"未找到 Java {version.JavaMajor}"
            : $"自动选择 Java {picked.Major}";
    }

    private void VMemoryFollow_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _current == null) return;
        if (VMemoryFollow.IsChecked == true)
        {
            AppState.Config.GetVersionSettings(_current.Id).MemoryMb = null;
            VMemorySlider.IsEnabled = false;
            VMemorySlider.Maximum = Math.Max(16384, MemoryInfo.TotalMb - 1024);
            VMemorySlider.Value = Math.Clamp(MemoryInfo.Resolve(),
                (int)VMemorySlider.Minimum, (int)VMemorySlider.Maximum);
            VMemoryValueText.Text = $"{(int)VMemorySlider.Value} MB（跟随全局{(AppState.Config.AutoMemory ? " · 自动配置" : "")}）";
        }
        else
        {
            var value = (int)VMemorySlider.Value;
            AppState.Config.GetVersionSettings(_current.Id).MemoryMb = value;
            VMemorySlider.IsEnabled = true;
        }
        AppState.SaveConfig();
    }

    private void VMemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        var value = (int)Math.Round(e.NewValue / 64) * 64;
        VMemoryValueText.Text = $"{value} MB";
        if (_loading || _current == null) return;
        if (VMemoryFollow.IsChecked == true) return;
        AppState.Config.GetVersionSettings(_current.Id).MemoryMb = value;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void VJavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        if (VJavaCombo.SelectedItem is not JavaOption option) return;
        AppState.Config.GetVersionSettings(_current.Id).JavaPath = option.Path;
        AppState.SaveConfig();
        UpdateJavaHint(_current);
    }

    private void VIsolatedToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _current == null) return;
        var settings = AppState.Config.GetVersionSettings(_current.Id);
        settings.Isolated = VIsolatedToggle.IsChecked == true;
        AppState.SaveConfig();
        UpdateModsPath(_current);
        Logger.Info($"版本 {_current.Id} 隔离目录：{(settings.Isolated == true ? "开启" : "关闭")}");
    }

    private void VJvmArgsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var settings = AppState.Config.GetVersionSettings(_current.Id);
        var text = VJvmArgsBox.Text.Trim();
        settings.JvmArgs = text.Length == 0 ? null : text;
        AppState.SaveConfig();
    }

    private void LaunchVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (GameLauncher.RunningProcess is { HasExited: false })
        {
            MessageDialog.Show(Window.GetWindow(this), "游戏正在运行", "请先关闭当前正在运行的游戏。", warning: true);
            return;
        }
        AppState.SetSelectedVersion(_current.Id);
        Navigation.Go("launch");
    }

    private void VersionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchVersion_Click(sender, e);

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshList();
        _ = AppState.RefreshVersionsAsync();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.Config.GameDir);

    private void OpenVersionsFolder_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(Path.Combine(AppState.Config.GameDir, "versions"));

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(_current?.Folder ?? AppState.Config.GameDir);

    private void ManageMods_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        new ModManageWindow(_current) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Modpack_Click(object sender, RoutedEventArgs e) =>
        new ModpackWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void ExportPack_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        new ModpackWindow(_current) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void DownloadVersion_Click(object sender, RoutedEventArgs e) => Navigation.Go("download");

    private void DeleteVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (GameLauncher.RunningProcess is { HasExited: false })
        {
            MessageDialog.Show(Window.GetWindow(this), "游戏正在运行", "请先关闭当前正在运行的游戏，再删除版本。", warning: true);
            return;
        }
        var version = _current;
        if (!MessageDialog.Show(Window.GetWindow(this), "删除版本",
                $"确定要删除版本 {version.Id} 吗？\n\n将删除整个文件夹：\n{version.Folder}\n\n该版本的模组与设置会一并移除，且无法恢复。",
                "删除", "取消", true)) return;

        try
        {
            if (Directory.Exists(version.Folder))
            {
                foreach (var file in Directory.GetFiles(version.Folder, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(version.Folder, true);
            }
            AppState.Config.VersionSettings.Remove(version.Id);
            AppState.Config.LastPlayed.Remove(version.Id);
            AppState.Config.SelectedVersion = "";
            AppState.SaveConfig();
            _current = null;
            _ = AppState.RefreshVersionsAsync();
            RefreshList();
            MessageDialog.Show(Window.GetWindow(this), "已删除", $"版本 {version.Id} 已删除。");
            Logger.Info("已删除版本 " + version.Id);
        }
        catch (Exception ex)
        {
            Logger.Error("删除版本失败: " + ex.Message);
            MessageDialog.Show(Window.GetWindow(this), "删除失败",
                "无法删除版本文件夹，可能仍有文件被占用。\n\n" + ex.Message, warning: true);
        }
    }

    private sealed record JavaOption(string Display, string? Path);
}
