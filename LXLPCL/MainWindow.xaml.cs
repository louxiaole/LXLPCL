using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LXLPCL.Core;
using LXLPCL.UI;
using LXLPCL.UI.Pages;
using LXLPCL.UI.Windows;

namespace LXLPCL;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly DispatcherTimerProvider _timer;
    private bool _ready;
    private string _currentKey = "";

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimerProvider(TimeSpan.FromSeconds(1), UpdateStatus);
        Navigation.Requested += Navigate;
        ThemeManager.BackgroundChanged += UpdateBackgroundLayer;
        UpdateBackgroundLayer();
        Loaded += OnLoaded;
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
    }

    private void UpdateBackgroundLayer()
    {
        var path = ThemeManager.ResolveBackgroundImage(ThemeManager.CurrentBackground);
        if (path == null)
        {
            ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
            ThemeBackgroundMask.Visibility = Visibility.Collapsed;
            ThemeTopFade.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ThemeBackgroundLayer.Fill = new System.Windows.Media.ImageBrush(bitmap)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                Viewbox = new Rect(0, 0, 1, 0.9),
                ViewboxUnits = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox
            };
            ThemeBackgroundLayer.Visibility = Visibility.Visible;
            ThemeBackgroundMask.Visibility = Visibility.Visible;
            ThemeTopFade.Fill = CreateTopFade();
            ThemeTopFade.Visibility = Visibility.Visible;
            Logger.Info("已应用主题背景：" + path);
        }
        catch (Exception ex)
        {
            Logger.Error("主题背景加载失败：" + ex.Message);
            ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
            ThemeBackgroundMask.Visibility = Visibility.Collapsed;
            ThemeTopFade.Visibility = Visibility.Collapsed;
        }
    }

    private static System.Windows.Media.Brush CreateTopFade()
    {
        var color = ThemeManager.IsDark
            ? System.Windows.Media.Color.FromArgb(0x99, 4, 6, 10)
            : System.Windows.Media.Color.FromArgb(0xB8, 255, 255, 255);
        return new System.Windows.Media.LinearGradientBrush(color, System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B),
        new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 1));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ready = true;
        NavLaunch.IsChecked = true;
        UpdateAccountDisplay();
        AppState.AccountsChanged += UpdateAccountDisplay;
        AppState.VersionsChanged += () =>
        {
            UpdateStatus();
            if (_pages.TryGetValue("launch", out var page) && page is LaunchPage launch && page.IsLoaded)
                launch.RefreshVersions();
        };
        AppState.JavasChanged += () =>
        {
            if (_pages.TryGetValue("launch", out var page) && page is LaunchPage launch)
                launch.RefreshJavas();
        };
        _timer.Start();
        await Task.WhenAll(AppState.RefreshVersionsAsync(), AppState.RefreshJavasAsync());
        Logger.Info($"扫描完成：{AppState.Versions.Count} 个版本，{AppState.Javas.Count} 个 Java");
        _ = CheckUpdatesOnStartupAsync();
    }

    /// <summary>启动后静默检查更新，有新版本时弹窗；检查失败不影响使用。</summary>
    private async Task CheckUpdatesOnStartupAsync()
    {
        try
        {
            var info = await Updater.CheckAsync();
            if (info == null || !IsLoaded) return;
            if (string.Equals(AppState.Config.SkippedUpdateVersion, info.Version, StringComparison.OrdinalIgnoreCase))
                return;
            UpdateDialog.Show(this, info);
        }
        catch (Exception ex)
        {
            Logger.Info("启动时检查更新失败：" + ex.Message);
        }
    }

    public void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            try
            {
                page = key switch
                {
                    "launch" => new LaunchPage(),
                    "download" => new DownloadPage(),
                    "versions" => new VersionsPage(),
                    "skin" => new SkinPage(),
                    "myskins" => new MySkinsPage(),
                    "multiplayer" => new MultiplayerPage(),
                    "translate" => new TranslatePage(),
                    "tools" => new ToolsPage(),
                    "settings" => new SettingsPage(),
                    "about" => new AboutPage(),
                    _ => new LaunchPage()
                };
            }
            catch (Exception ex)
            {
                Logger.Error("页面创建失败：" + ex);
                return;
            }
            _pages[key] = page;
        }

        if (_currentKey == key && PageHost.Content == page)
        {
            (page as PageBase)?.OnShown();
            return;
        }
        _currentKey = key;

        PageHost.Content = page;
        SetNavChecked(key);
        (page as PageBase)?.OnShown();

        if (AppState.Config.Animations)
        {
            page.Opacity = 0;
            var transform = new System.Windows.Media.TranslateTransform(0, 10);
            page.RenderTransform = transform;
            var duration = TimeSpan.FromMilliseconds(180);
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            page.Opacity = 1;
            page.RenderTransform = System.Windows.Media.Transform.Identity;
        }
    }

    private void SetNavChecked(string key)
    {
        var target = key switch
        {
            "launch" => NavLaunch,
            "download" => NavDownload,
            "versions" => NavVersions,
            "skin" => NavSkin,
            "myskins" => NavMySkins,
            "multiplayer" => NavMultiplayer,
            "translate" => NavTranslate,
            "tools" => NavTools,
            "settings" => NavSettings,
            "about" => NavAbout,
            _ => NavLaunch
        };
        if (target.IsChecked != true) target.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is RadioButton { Tag: string key }) Navigate(key);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == WindowState.Normal)
        {
            try { DragMove(); } catch { }
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AccountsWindow { Owner = this };
        window.ShowDialog();
        UpdateAccountDisplay();
        UpdateStatus();
    }

    private void UpdateAccountDisplay()
    {
        var account = AppState.SelectedAccount;
        var name = account?.Name ?? "未登录";
        TitleAccountName.Text = name;
        TitleAccountInitial.Text = account?.Initial ?? "?";
        SidebarAccountInitial.Text = account?.Initial ?? "?";
        SidebarAccountButton.ToolTip = account == null ? "账户管理" : $"{account.Name}（{account.Type}）";
    }

    private void UpdateStatus()
    {
        var version = AppState.SelectedVersion;
        StatusVersion.Text = version == null ? "未选择版本" : $"{version.Id}  ·  {version.Loader}";
        var download = DownloadCenter.Current;
        var parts = new List<string>();
        if (download.Active)
            parts.Add($"{download.Title} {download.Percent:0}%");
        else if (AppState.Javas.Count > 0)
            parts.Add($"Java {AppState.Javas[0].Major}");
        try
        {
            using var process = Process.GetCurrentProcess();
            parts.Add(Utils.FormatBytes(process.WorkingSet64));
        }
        catch { }
        StatusRight.Text = string.Join("  ·  ", parts);
    }

    protected override void OnClosed(EventArgs e)
    {
        Navigation.Requested -= Navigate;
        _timer.Stop();
        base.OnClosed(e);
    }
}

public class DispatcherTimerProvider
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly Action _action;

    public DispatcherTimerProvider(TimeSpan interval, Action action)
    {
        _action = action;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => _action();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
