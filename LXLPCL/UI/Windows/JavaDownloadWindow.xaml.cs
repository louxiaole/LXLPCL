using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class JavaDownloadWindow : Window
{
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public JavaInfo? Installed { get; private set; }

    public JavaDownloadWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => UpdateProgress();
        InstallPathText.Text = "安装位置：" + AppState.RuntimeDir;
        StatusText.Text = "选择需要的 Java 版本后点击“开始下载”。";
        Closed += (_, _) =>
        {
            _timer.Stop();
            _cts?.Cancel();
        };
    }

    private int SelectedMajor => Java8Radio.IsChecked == true ? 8 : Java21Radio.IsChecked == true ? 21 : 17;

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        var major = SelectedMajor;
        SetOptionsEnabled(false);
        DownloadButton.IsEnabled = false;
        DownloadButton.Content = "正在下载...";
        CancelButton.Content = "取消下载";
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        StatusText.Text = $"正在准备 Java {major}...";

        _cts = new CancellationTokenSource();
        _timer.Start();
        try
        {
            var info = await JavaRuntimeInstaller.InstallAsync(major, null, _cts.Token);
            Installed = info;
            AppState.Config.JavaPath = info.Path;
            AppState.SaveConfig();
            StatusText.Text = $"Java {info.Major} 安装完成，已设为默认 Java：{info.Path}";
            Logger.Info($"已下载 Java {info.Major}：{info.Path}");
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
            Progress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Error("下载 Java 失败: " + ex);
            StatusText.Text = "下载失败：" + ex.Message;
            Progress.Visibility = Visibility.Collapsed;
            MessageDialog.Show(this, "下载失败",
                ex.Message + "\n\n也可以手动安装 Java 后点击“手动添加”指定路径。", warning: true);
        }
        finally
        {
            _timer.Stop();
            _busy = false;
            SetOptionsEnabled(true);
            DownloadButton.IsEnabled = true;
            DownloadButton.Content = "开始下载";
            CancelButton.Content = "关闭";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgress()
    {
        var download = DownloadCenter.Current;
        if (!download.Active) return;
        if (download.Indeterminate)
        {
            Progress.IsIndeterminate = true;
        }
        else
        {
            Progress.IsIndeterminate = false;
            Progress.Value = download.Percent;
        }
        StatusText.Text = download.Detail.Length > 0 ? download.Detail : download.Title;
    }

    private void SetOptionsEnabled(bool enabled)
    {
        Java8Radio.IsEnabled = enabled;
        Java17Radio.IsEnabled = enabled;
        Java21Radio.IsEnabled = enabled;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }
        Close();
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
