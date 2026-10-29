using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class LaunchPage : PageBase
{
    private bool _suppressSelection;
    private bool _busy;
    private bool _gameRunning;
    private bool _exitSubscribed;
    private bool _settingsLoading;
    private CancellationTokenSource? _launchCts;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _memorySaveTimer;
    private string _lastGameLog = "";

    public LaunchPage()
    {
        InitializeComponent();

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => UpdateLaunchProgress();

        _memorySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _memorySaveTimer.Tick += (_, _) =>
        {
            _memorySaveTimer.Stop();
            AppState.Config.MemoryMb = (int)MemorySlider.Value;
            AppState.SaveConfig();
        };

        VersionCombo.ItemsSource = AppState.Versions;
        RefreshVersions();
        LoadSettings();
        UpdateAccountDisplay();
        UpdateGreeting();

        Loaded += (_, _) =>
        {
            if (!_exitSubscribed)
            {
                GameLauncher.GameExited += OnGameExited;
                _exitSubscribed = true;
            }
        };
    }

    public override void OnShown()
    {
        RefreshVersions();
        if (!_busy && !_gameRunning) LoadSettings();
        UpdateAccountDisplay();
        UpdateGreeting();
    }

    public void RefreshVersions()
    {
        _suppressSelection = true;
        VersionCombo.ItemsSource = AppState.Versions;
        VersionCombo.SelectedItem = AppState.SelectedVersion;
        _suppressSelection = false;
        VersionEmptyLabel.Visibility = AppState.Versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateVersionDetails();
        UpdateGreeting();
    }

    public void RefreshJavas()
    {
        LoadJavaChoices();
        UpdateJavaHint();
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            < 6 => "夜深了",
            < 12 => "早上好",
            < 14 => "中午好",
            < 18 => "下午好",
            _ => "晚上好"
        };
        GreetingText.Text = $"{greeting} · 今天是 {DateTime.Now:M 月 d 日} · 已安装 {AppState.Versions.Count} 个版本";
        VersionCountText.Text = $"{AppState.Versions.Count} 个版本";
    }

    private void UpdateVersionDetails()
    {
        var version = AppState.SelectedVersion;
        if (version == null)
        {
            VersionNameText.Text = "尚未安装版本";
            VersionSubtitleText.Text = "前往下载页安装一个 Minecraft 版本";
            VersionIconText.Text = "MC";
            VersionIconTile.Background = (Brush)FindResource("AccentGradientBrush");
            VersionLogoImage.Source = null;
            VersionLogoImage.Visibility = Visibility.Collapsed;
            VersionIconTile.Visibility = Visibility.Visible;
            VersionBadgeText.Text = "无版本";
            LastPlayedText.Text = "尚未游玩";
            VersionPathText.Text = "-";
            VersionLoaderText.Text = "-";
            VersionTypeText.Text = "-";
            VersionModsText.Text = "0 个已启用";
            VersionJavaText.Text = "-";
            LaunchHintText.Text = "先去下载一个版本吧";
            return;
        }

        VersionNameText.Text = version.Id;
        VersionSubtitleText.Text = version.Subtitle;
        VersionIconTile.Background = version.IconBrush;
        VersionIconText.Text = version.IconText;
        VersionLogoImage.Source = version.Logo;
        VersionLogoImage.Visibility = version.HasLogo ? Visibility.Visible : Visibility.Collapsed;
        VersionIconTile.Visibility = version.HasLogo ? Visibility.Collapsed : Visibility.Visible;
        VersionBadgeText.Text = version.LoaderText;
        LastPlayedText.Text = AppState.Config.LastPlayed.TryGetValue(version.Id, out var played)
            ? "上次游玩 " + Utils.FormatDate(played)
            : "尚未游玩";
        VersionPathText.Text = version.Folder;
        VersionPathText.ToolTip = version.Folder;
        VersionLoaderText.Text = version.Loader == "原版"
            ? "原版"
            : version.Loader + (string.IsNullOrEmpty(version.LoaderVersion) ? "" : " " + version.LoaderVersion);
        VersionTypeText.Text = version.TypeText;
        VersionJavaText.Text = "Java " + version.JavaMajor;
        VersionModsText.Text = CountMods(version) + " 个已启用";
        LaunchHintText.Text = version.Error != null ? "版本文件异常，请重新安装" : "点击开始冒险";
    }

    private static int CountMods(GameVersion version)
    {
        try
        {
            var dir = AppState.ResolveModsDir(version);
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetFiles(dir, "*.jar").Length;
        }
        catch { return 0; }
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (VersionCombo.SelectedItem is not GameVersion version) return;
        AppState.SetSelectedVersion(version.Id);
        UpdateVersionDetails();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _settingsLoading = true;
        try
        {
            var auto = AppState.Config.AutoMemory;
            MemorySlider.Maximum = Math.Max(16384, MemoryInfo.TotalMb - 1024);
            MemorySlider.IsEnabled = !auto;
            AutoMemoryRadio.IsChecked = auto;
            CustomMemoryRadio.IsChecked = !auto;
            var memory = MemoryInfo.Resolve();
            MemorySlider.Value = Math.Clamp(memory, (int)MemorySlider.Minimum, (int)MemorySlider.Maximum);
            MemoryValueText.Text = auto
                ? $"自动配置 · {memory} MB（{memory / 1024.0:0.#} GB）"
                : $"{memory} MB（{memory / 1024.0:0.#} GB）";
            LoadJavaChoices();
            UpdateJavaHint();
        }
        finally
        {
            _settingsLoading = false;
        }
    }

    private void MemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _settingsLoading) return;
        var auto = AutoMemoryRadio.IsChecked == true;
        AppState.Config.AutoMemory = auto;
        MemorySlider.IsEnabled = !auto;
        if (!auto)
        {
            // 切到自定义时以滑条当前值为起点，避免显示值与滑条脱节
            AppState.Config.MemoryMb = (int)Math.Round(MemorySlider.Value / 64) * 64;
        }
        var memory = MemoryInfo.Resolve();
        MemoryValueText.Text = auto
            ? $"自动配置 · {memory} MB（{memory / 1024.0:0.#} GB）"
            : $"{memory} MB（{memory / 1024.0:0.#} GB）";
        _memorySaveTimer.Stop();
        _memorySaveTimer.Start();
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || AppState.Config.AutoMemory) return;
        var value = (int)Math.Round(e.NewValue / 64) * 64;
        MemoryValueText.Text = $"{value} MB（{value / 1024.0:0.#} GB）";
        if (_settingsLoading) return;
        AppState.Config.MemoryMb = value;
        _memorySaveTimer.Stop();
        _memorySaveTimer.Start();
    }

    private void LoadJavaChoices()
    {
        var selectedPath = AppState.Config.JavaPath;
        var choices = new List<JavaChoice> { new("自动选择（推荐）", null) };
        choices.AddRange(AppState.Javas.Select(java =>
            new JavaChoice($"Java {java.Major} · {java.Source} · {(java.Is64Bit ? "64 位" : "32 位")}", java.Path)));

        var previous = JavaCombo.SelectedItem as JavaChoice;
        _settingsLoading = true;
        JavaCombo.ItemsSource = choices;
        JavaCombo.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                                   ?? (previous != null
                                       ? choices.FirstOrDefault(c => string.Equals(c.Path, previous.Path, StringComparison.OrdinalIgnoreCase))
                                       : null)
                                   ?? choices[0];
        _settingsLoading = false;
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading) return;
        if (JavaCombo.SelectedItem is not JavaChoice choice) return;
        AppState.Config.JavaPath = choice.Path ?? "";
        AppState.SaveConfig();
        UpdateJavaHint();
    }

    private void UpdateJavaHint()
    {
        var version = AppState.SelectedVersion;
        var required = version?.JavaMajor ?? 0;
        if (!string.IsNullOrWhiteSpace(AppState.Config.JavaPath))
        {
            var info = JavaFinder.Inspect(AppState.Config.JavaPath);
            JavaHintText.Text = info == null
                ? "指定的 Java 不可用，将自动选择"
                : $"当前指定：Java {info.Major}（{(info.Is64Bit ? "64 位" : "32 位")}）";
            return;
        }
        var picked = JavaFinder.PickFor(required);
        JavaHintText.Text = picked == null
            ? "未检测到 Java，启动时将提示自动下载"
            : $"自动选择：Java {picked.Major}（{picked.Source}）";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        GreetingText.Text = "正在刷新版本与 Java...";
        await Task.WhenAll(AppState.RefreshVersionsAsync(), AppState.RefreshJavasAsync());
        RefreshVersions();
        LoadSettings();
        UpdateGreeting();
    }

    private void RefreshJava_Click(object sender, RoutedEventArgs e)
    {
        JavaHintText.Text = "正在检测 Java...";
        _ = AppState.RefreshJavasAsync();
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
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
        LoadJavaChoices();
        UpdateJavaHint();
    }

    private void UpdateAccountDisplay()
    {
        var account = AppState.SelectedAccount;
        AccountNameText.Text = account?.Name ?? "未登录";
        AccountInitialText.Text = account?.Initial ?? "?";
        AccountTypeText.Text = account == null ? "点击右侧按钮添加账户" : account.TypeText;
        SwitchAccountButton.Content = account == null ? "添加账户" : "切换账户";
    }

    private void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        var window = new AccountsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        UpdateAccountDisplay();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.Config.GameDir);

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
    {
        var version = AppState.SelectedVersion;
        Utils.OpenFolder(version?.Folder ?? AppState.Config.GameDir);
    }

    private void VersionsButton_Click(object sender, RoutedEventArgs e) => Navigation.Go("versions");

    private void ModsButton_Click(object sender, RoutedEventArgs e)
    {
        var version = AppState.SelectedVersion;
        if (version == null)
        {
            MessageDialog.Show(Window.GetWindow(this), "还没有版本",
                "请先在“版本管理”或下载页安装一个版本，再管理模组。", warning: true);
            return;
        }
        new ModManageWindow(version) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e) => Navigation.Go("tools");

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => Navigation.Go("settings");

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _gameRunning) return;

        var version = AppState.SelectedVersion;
        if (version == null)
        {
            var go = MessageDialog.Show(Window.GetWindow(this), "没有可用的版本",
                "还没有安装任何 Minecraft 版本。\n\n是否现在前往下载页安装？", "前往下载", "取消");
            if (go) Navigation.Go("download");
            return;
        }

        if (version.Error != null)
        {
            MessageDialog.Show(Window.GetWindow(this), "版本无法启动",
                $"无法解析版本 {version.Id} 的配置文件：\n{version.Error}\n\n建议在版本管理页重新安装该版本。", warning: true);
            return;
        }

        var account = AppState.SelectedAccount;
        if (account == null)
        {
            var window = new AccountsWindow { Owner = Window.GetWindow(this) };
            window.ShowDialog();
            account = AppState.SelectedAccount;
            UpdateAccountDisplay();
            if (account == null)
            {
                MessageDialog.Show(Window.GetWindow(this), "尚未登录", "请先添加一个账户再启动游戏。", warning: true);
                return;
            }
        }

        _busy = true;
        _launchCts = new CancellationTokenSource();
        SetBusy(true, "正在检查游戏文件...");

        try
        {
            var progress = new Progress<string>(message =>
            {
                if (!DownloadCenter.Current.Active) LaunchStatusText.Text = message;
            });

            try
            {
                await GameLauncher.LaunchAsync(version, account, progress, _launchCts.Token);
            }
            catch (JavaNotFoundException ex)
            {
                if (!await PromptInstallJavaAsync(ex)) return;
                SetBusy(true, "正在重新启动游戏...");
                await GameLauncher.LaunchAsync(version, account, progress, _launchCts.Token);
            }

            _busy = false;
            _gameRunning = true;
            _progressTimer.Stop();
            LaunchProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            LaunchButton.IsEnabled = false;
            LaunchHintText.Text = "游戏运行中";
            LaunchStatusText.Text = "游戏已启动，正在加载窗口...";
            Logger.Info("游戏进程已启动");
        }
        catch (OperationCanceledException)
        {
            LaunchStatusText.Text = "已取消启动";
            LaunchHintText.Text = "点击开始冒险";
            LaunchProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _progressTimer.Stop();
        }
        catch (Exception ex)
        {
            Logger.Error("启动失败: " + ex);
            LaunchStatusText.Text = "启动失败：" + ex.Message;
            LaunchHintText.Text = "点击重试";
            LaunchProgress.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _progressTimer.Stop();
            MessageDialog.Show(Window.GetWindow(this), "启动失败",
                ex.Message + "\n\n详细日志：" + Logger.LogFile, warning: true);
        }
        finally
        {
            _busy = false;
            CancelButton.IsEnabled = true;
            LaunchButton.IsEnabled = !_gameRunning;
        }
    }

    private async Task<bool> PromptInstallJavaAsync(JavaNotFoundException ex)
    {
        var message = $"当前版本需要 Java {ex.RequiredMajor}，但本机没有找到可用的 Java。";
        if (!AppState.Config.AutoDownloadJava)
        {
            MessageDialog.Show(Window.GetWindow(this), "缺少 Java",
                message + "\n\n请在设置中指定 Java，或开启“自动下载 Java”。", warning: true);
            LaunchStatusText.Text = "未找到 Java，启动已取消";
            LaunchHintText.Text = "点击重试";
            return false;
        }

        var confirmed = MessageDialog.Show(Window.GetWindow(this), "缺少 Java",
            message + "\n\n是否自动下载由 Mojang 提供的 Java 运行时？\n下载后保存在启动器目录，不会修改系统环境。",
            "自动下载", "取消");
        if (!confirmed)
        {
            LaunchStatusText.Text = "未找到 Java，启动已取消";
            LaunchHintText.Text = "点击重试";
            return false;
        }

        try
        {
            SetBusy(true, $"正在下载 Java {ex.RequiredMajor}...");
            await JavaRuntimeInstaller.InstallAsync(ex.RequiredMajor, ex.Component, _launchCts?.Token ?? default);
            SetBusy(true, "Java 安装完成，正在启动...");
            return true;
        }
        catch (Exception downloadError)
        {
            MessageDialog.Show(Window.GetWindow(this), "Java 下载失败",
                downloadError.Message + "\n\n可以手动安装 Java 后在设置中指定。", warning: true);
            LaunchStatusText.Text = "Java 下载失败";
            LaunchHintText.Text = "点击重试";
            return false;
        }
    }

    private void SetBusy(bool busy, string status)
    {
        LaunchProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LaunchProgress.IsIndeterminate = true;
        LaunchProgress.Value = 0;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LaunchStatusText.Text = status;
        if (busy) _progressTimer.Start();
        else _progressTimer.Stop();
    }

    private void UpdateLaunchProgress()
    {
        if (!_busy) return;
        var download = DownloadCenter.Current;
        if (!download.Active) return;
        LaunchProgress.IsIndeterminate = download.Indeterminate;
        if (!download.Indeterminate) LaunchProgress.Value = download.Percent;
        var text = string.IsNullOrWhiteSpace(download.Detail) ? download.Title : download.Detail;
        if (download.SpeedBytes > 0) text += "  ·  " + Utils.FormatBytes(download.SpeedBytes) + "/s";
        LaunchStatusText.Text = text;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        LaunchStatusText.Text = "正在取消...";
        try { _launchCts?.Cancel(); } catch { }
    }

    private void OnGameExited(string versionId, int exitCode, string logPath)
    {
        _lastGameLog = logPath;
        Dispatcher.BeginInvoke(() =>
        {
            _gameRunning = false;
            _busy = false;
            LaunchButton.IsEnabled = true;
            LaunchHintText.Text = "点击开始冒险";
            if (exitCode == 0)
            {
                LaunchStatusText.Text = "游戏已正常退出";
                return;
            }
            LaunchStatusText.Text = $"游戏异常退出（代码 {exitCode}）";
            var tail = ReadLogTail(logPath);
            MessageDialog.Show(Window.GetWindow(this), "游戏异常退出",
                $"版本 {versionId} 已退出，退出代码 {exitCode}。\n\n{tail}\n\n完整日志：{logPath}",
                warning: true);
        });
    }

    private static string ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path)) return "未找到游戏日志。";
            var lines = File.ReadAllLines(path);
            var tail = lines.Length > 12 ? lines[^12..] : lines;
            return "最近的日志：\n" + string.Join("\n", tail);
        }
        catch
        {
            return "";
        }
    }

    private sealed record JavaChoice(string Display, string? Path);
}
