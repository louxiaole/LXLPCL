using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class SettingsPage : PageBase
{
    private bool _loading;
    private readonly DispatcherTimer _saveTimer;

    public SettingsPage()
    {
        InitializeComponent();
        LoadBackgroundThumbnails();
        FileNameFormatCombo.ItemsSource = ModNaming.Formats.Select(format => format.Text).ToList();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            AppState.SaveConfig();
        };
        AppState.JavasChanged += () =>
        {
            if (!IsLoaded) return;
            LoadJavaList();
            LoadJavaCombo();
        };
        ApplyPanel("basic");
        LoadAll();
    }

    public override void OnShown() => LoadAll();

    private void LoadAll()
    {
        _loading = true;
        try
        {
            var config = AppState.Config;
            GameDirText.Text = config.GameDir;
            GameDirText.ToolTip = config.GameDir;

            DownloadSourceCombo.SelectedIndex = config.DownloadSource.Equals("官方", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ThreadsSlider.Value = Math.Clamp(config.DownloadThreads, 1, 32);
            ThreadsValueText.Text = ((int)ThreadsSlider.Value).ToString();

            AutoAssetsToggle.IsChecked = config.AutoInstallAssets;
            AutoJavaToggle.IsChecked = config.AutoDownloadJava;

            var formatIndex = Array.FindIndex(ModNaming.Formats, format => format.Key == config.ModFileNameFormat);
            FileNameFormatCombo.SelectedIndex = formatIndex >= 0 ? formatIndex : 0;
            ModStyleCombo.SelectedIndex = config.ModManageStyle == ModNaming.StyleFileName ? 1 : 0;

            AutoMemoryRadio.IsChecked = config.AutoMemory;
            CustomMemoryRadio.IsChecked = !config.AutoMemory;
            SyncMemoryControls();
            WindowWidthBox.Text = config.WindowWidth.ToString();
            WindowHeightBox.Text = config.WindowHeight.ToString();
            FullscreenToggle.IsChecked = config.Fullscreen;

            JvmArgsBox.Text = config.CustomJvmArgs;
            GameArgsBox.Text = config.CustomGameArgs;

            AnimationToggle.IsChecked = config.Animations;
            ThemeLightRadio.IsChecked = config.ThemeMode == ThemeManager.ModeLight;
            ThemeDarkRadio.IsChecked = config.ThemeMode == ThemeManager.ModeDark;
            ThemeMidnightRadio.IsChecked = config.ThemeMode == ThemeManager.ModeMidnight;
            ThemeSepiaRadio.IsChecked = config.ThemeMode == ThemeManager.ModeSepia;
            ThemeSystemRadio.IsChecked = config.ThemeMode == ThemeManager.ModeSystem;
            LoadBackgroundSelection(config.ThemeBackground);
            AccentValueText.Text = "当前：" + config.AccentColor.ToUpperInvariant();
            VersionValueText.Text = "LXLPCL " + AppState.VersionText;

            LoadJavaCombo();
            LoadJavaList();
        }
        finally
        {
            _loading = false;
        }
    }

    private void SettingsNav_Checked(object sender, RoutedEventArgs e)
    {
        if (BasicPanel == null) return;
        if (sender is RadioButton { Tag: string tag }) ApplyPanel(tag);
    }

    private void ApplyPanel(string tag)
    {
        BasicPanel.Visibility = tag == "basic" ? Visibility.Visible : Visibility.Collapsed;
        GamePanel.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
        JavaPanel.Visibility = tag == "java" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DetectGameDir_Click(object sender, RoutedEventArgs e)
    {
        var window = new FindGameWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        GameDirText.Text = AppState.Config.GameDir;
        GameDirText.ToolTip = AppState.Config.GameDir;
        Logger.Info($"已通过自动检测切换游戏目录：{AppState.Config.GameDir}");
    }

    private void BrowseGameDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 Minecraft 游戏目录",
            InitialDirectory = AppState.Config.GameDir
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var dir = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            AppState.Config.GameDir = dir;
            AppState.SaveConfig();
            GameDirText.Text = dir;
            GameDirText.ToolTip = dir;
            Logger.Info("游戏目录已切换到 " + dir);
            _ = AppState.RefreshVersionsAsync();
            _ = AppState.RefreshJavasAsync();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "无法使用该目录", ex.Message, warning: true);
        }
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.Config.GameDir);

    private void OpenDataDir_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.DataDir);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(Logger.LogFile is { Length: > 0 }
        ? Path.GetDirectoryName(Logger.LogFile) ?? AppState.LogDir
        : AppState.LogDir);

    private void DownloadSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.DownloadSource = DownloadSourceCombo.SelectedIndex == 1 ? "官方" : "自动";
        AppState.SaveConfig();
        Logger.Info("下载源已切换为 " + AppState.Config.DownloadSource);
    }

    private void FileNameFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var index = FileNameFormatCombo.SelectedIndex;
        if (index < 0 || index >= ModNaming.Formats.Length) return;
        AppState.Config.ModFileNameFormat = ModNaming.Formats[index].Key;
        AppState.SaveConfig();
        Logger.Info("社区资源文件名格式已切换为 " + ModNaming.Formats[index].Text);
    }

    private void ModStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.ModManageStyle = ModStyleCombo.SelectedIndex == 1
            ? ModNaming.StyleFileName
            : ModNaming.StyleTranslated;
        AppState.SaveConfig();
    }

    private void ThreadsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        var value = (int)Math.Round(e.NewValue);
        ThreadsValueText.Text = value.ToString();
        if (_loading) return;
        AppState.Config.DownloadThreads = value;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void AutoAssetsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.AutoInstallAssets = AutoAssetsToggle.IsChecked == true;
        AppState.SaveConfig();
    }

    private void AutoJavaToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.AutoDownloadJava = AutoJavaToggle.IsChecked == true;
        AppState.SaveConfig();
    }

    private void MemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _loading) return;
        AppState.Config.AutoMemory = AutoMemoryRadio.IsChecked == true;
        SyncMemoryControls();
        AppState.SaveConfig();
    }

    private void SyncMemoryControls()
    {
        var auto = AppState.Config.AutoMemory;
        var total = MemoryInfo.TotalMb;
        MemorySlider.Maximum = Math.Max(16384, total - 1024);
        MemorySlider.IsEnabled = !auto;
        var value = auto
            ? MemoryInfo.AutoMb
            : Math.Clamp((int)Math.Round(AppState.Config.MemoryMb / 64.0) * 64,
                (int)MemorySlider.Minimum, (int)MemorySlider.Maximum);
        MemorySlider.Value = value;
        MemoryValueText.Text = auto
            ? $"自动配置：{value} MB（{value / 1024.0:0.#} GB）"
            : $"{value} MB（{value / 1024.0:0.#} GB）";
        MemoryHintText.Text = auto
            ? "根据系统内存自动推荐，通常无需手动调整；大型整合包可切换到自定义。"
            : "分配给 Minecraft 的最大内存，大型整合包建议 6 GB 以上。";

        var used = MemoryInfo.UsedMb;
        UsedMemoryText.Text = $"{MemoryInfo.FormatGb(used)} / {MemoryInfo.FormatGb(total)}";
        UsedMemoryBar.Maximum = Math.Max(1, total);
        UsedMemoryBar.Value = used;
        AllocMemoryText.Text = MemoryInfo.FormatGb(value);
        AllocMemoryBar.Maximum = Math.Max(1, total);
        AllocMemoryBar.Value = Math.Min(value, total);
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || AppState.Config.AutoMemory) return;
        var value = (int)Math.Round(e.NewValue / 64) * 64;
        MemoryValueText.Text = $"{value} MB（{value / 1024.0:0.#} GB）";
        AllocMemoryText.Text = MemoryInfo.FormatGb(value);
        AllocMemoryBar.Value = Math.Min(value, Math.Max(1, MemoryInfo.TotalMb));
        if (_loading) return;
        AppState.Config.MemoryMb = value;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void WindowSizeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var config = AppState.Config;
        if (int.TryParse(WindowWidthBox.Text.Trim(), out var width))
            config.WindowWidth = Math.Clamp(width, 320, 3840);
        if (int.TryParse(WindowHeightBox.Text.Trim(), out var height))
            config.WindowHeight = Math.Clamp(height, 240, 2160);
        WindowWidthBox.Text = config.WindowWidth.ToString();
        WindowHeightBox.Text = config.WindowHeight.ToString();
        AppState.SaveConfig();
    }

    private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.Fullscreen = FullscreenToggle.IsChecked == true;
        AppState.SaveConfig();
    }

    private void JvmArgsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.CustomJvmArgs = JvmArgsBox.Text.Trim();
        AppState.SaveConfig();
    }

    private void GameArgsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.CustomGameArgs = GameArgsBox.Text.Trim();
        AppState.SaveConfig();
    }

    private void LoadJavaCombo()
    {
        var options = new List<JavaOption> { new("自动选择（推荐）", null) };
        options.AddRange(AppState.Javas.Select(j =>
            new JavaOption($"Java {j.Major} · {j.Source} · {(j.Is64Bit ? "64 位" : "32 位")}", j.Path)));
        var previousLoading = _loading;
        _loading = true;
        GlobalJavaCombo.ItemsSource = options;
        GlobalJavaCombo.SelectedItem = options.FirstOrDefault(o =>
            string.Equals(o.Path, AppState.Config.JavaPath, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        _loading = previousLoading;
    }

    private void LoadJavaList()
    {
        JavaList.ItemsSource = AppState.Javas;
        JavaCountText.Text = $"共检测到 {AppState.Javas.Count} 个 Java 运行时";
        JavaEmptyText.Text = AppState.Javas.Count == 0
            ? "没有检测到 Java。启动游戏时会提示自动下载，或点击右上角手动添加。"
            : "";
        JavaEmptyText.Visibility = AppState.Javas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GlobalJavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (GlobalJavaCombo.SelectedItem is not JavaOption option) return;
        AppState.Config.JavaPath = option.Path ?? "";
        AppState.SaveConfig();
        Logger.Info("默认 Java 已设置为 " + (option.Path ?? "自动选择"));
    }

    private void RefreshJava_Click(object sender, RoutedEventArgs e)
    {
        JavaEmptyText.Text = "正在重新检测 Java...";
        JavaEmptyText.Visibility = Visibility.Visible;
        _ = AppState.RefreshJavasAsync();
    }

    private void DownloadJava_Click(object sender, RoutedEventArgs e)
    {
        var window = new JavaDownloadWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        _ = AppState.RefreshJavasAsync();
        LoadJavaCombo();
        LoadJavaList();
    }

    private void AddJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe 或 javaw.exe",
            Filter = "Java 可执行文件|java.exe;javaw.exe|所有文件|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var info = JavaFinder.Inspect(dialog.FileName);
        if (info == null)
        {
            MessageDialog.Show(Window.GetWindow(this), "无效的 Java", "无法识别该文件为可用的 Java。", warning: true);
            return;
        }
        AppState.Config.JavaPath = info.Path;
        AppState.SaveConfig();
        _ = AppState.RefreshJavasAsync();
        LoadJavaCombo();
        Logger.Info("已手动添加 Java: " + info.Path);
    }

    private void AnimationToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppState.Config.Animations = AnimationToggle.IsChecked == true;
        AppState.SaveConfig();
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        AppState.ApplyAccent(hex);
        AccentValueText.Text = "当前：" + hex.ToUpperInvariant();
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode }) return;
        if (AppState.Config.ThemeMode == mode) return;
        AppState.Config.ThemeMode = mode;
        AppState.SaveConfig();
        ThemeManager.ApplyTheme(mode);
        ThemeManager.Apply(AppState.Config.AccentColor);
    }

    private void ThemeBackground_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string key }) return;
        if (AppState.Config.ThemeBackground == key) return;
        AppState.Config.ThemeBackground = key;
        AppState.SaveConfig();
        ThemeManager.ApplyBackground(key);
        UpdateBackgroundHint(key);
    }

    private void CustomBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp"
        };
        if (dialog.ShowDialog() != true) return;

        string target;
        try
        {
            var dir = Path.Combine(AppState.DataDir, "backgrounds");
            Directory.CreateDirectory(dir);
            target = Path.Combine(dir, "custom" + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            File.Copy(dialog.FileName, target, true);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "无法使用这张图片", ex.Message, warning: true);
            return;
        }

        AppState.Config.ThemeBackground = target;
        AppState.SaveConfig();
        ThemeManager.ApplyBackground(target);

        _loading = true;
        BgNoneRadio.IsChecked = false;
        BgSakuraRadio.IsChecked = false;
        BgStarryRadio.IsChecked = false;
        BgNeonRadio.IsChecked = false;
        BgForestRadio.IsChecked = false;
        _loading = false;
        UpdateBackgroundHint(target);
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        AppState.Config.ThemeBackground = ThemeManager.BgNone;
        AppState.SaveConfig();
        ThemeManager.ApplyBackground(ThemeManager.BgNone);
        _loading = true;
        BgNoneRadio.IsChecked = true;
        BgSakuraRadio.IsChecked = false;
        BgStarryRadio.IsChecked = false;
        BgNeonRadio.IsChecked = false;
        BgForestRadio.IsChecked = false;
        _loading = false;
        UpdateBackgroundHint(ThemeManager.BgNone);
    }

    private void LoadBackgroundSelection(string? value)
    {
        value ??= ThemeManager.BgNone;
        BgNoneRadio.IsChecked = value == ThemeManager.BgNone;
        BgSakuraRadio.IsChecked = value == "sakura";
        BgStarryRadio.IsChecked = value == "starry";
        BgNeonRadio.IsChecked = value == "neon";
        BgForestRadio.IsChecked = value == "forest";
        UpdateBackgroundHint(value);
    }

    private void UpdateBackgroundHint(string? value)
    {
        value ??= ThemeManager.BgNone;
        BackgroundHintText.Text = value == ThemeManager.BgNone
            ? "支持 JPG / PNG / BMP，建议使用 16:9 横向图片。"
            : File.Exists(value)
                ? $"当前背景：自定义图片（{Path.GetFileName(value)}）"
                : $"当前背景：{ThemeManager.BackgroundName(value)}";
    }

    private void LoadBackgroundThumbnails()
    {
        SetThumbnail(BgSakuraTile, "sakura");
        SetThumbnail(BgStarryTile, "starry");
        SetThumbnail(BgNeonTile, "neon");
        SetThumbnail(BgForestTile, "forest");
    }

    private static void SetThumbnail(Border tile, string key)
    {
        var path = ThemeManager.ResolveBackgroundImage(key);
        if (path == null)
        {
            Logger.Error("主题背景缩略图缺失：" + key);
            return;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 216;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            tile.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                Viewbox = new Rect(0, 0, 1, 0.9),
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"主题背景缩略图加载失败（{key}）：{ex.Message}");
        }
    }

    private sealed record JavaOption(string Display, string? Path);
}
