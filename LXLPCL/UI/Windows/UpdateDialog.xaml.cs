using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class UpdateDialog : Window
{
    private readonly UpdateInfo _info;
    private CancellationTokenSource? _cancellation;
    private Stopwatch? _watch;
    private bool _busy;

    private UpdateDialog(UpdateInfo info)
    {
        InitializeComponent();
        _info = info;
        TitleText.Text = $"发现新版本 v{info.Version}";
        VersionLineText.Text = string.IsNullOrEmpty(info.ReleaseDate)
            ? $"当前版本 {AppState.VersionText}　→　最新版本 {info.Version}"
            : $"当前版本 {AppState.VersionText}　→　最新版本 {info.Version} · {info.ReleaseDate}";
        ChangelogText.Text = string.IsNullOrWhiteSpace(info.Changelog)
            ? "该版本没有提供更新说明。"
            : info.Changelog;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !_busy) Close(); };
    }

    public static void Show(Window? owner, UpdateInfo info)
    {
        var dialog = new UpdateDialog(info);
        if (owner != null && owner.IsLoaded) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_busy)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }

    private void Manual_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl(Updater.ReleasesPage);

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // 记录跳过的版本，启动时不再重复提示
        AppState.Config.SkippedUpdateVersion = _info.Version;
        AppState.SaveConfig();
        Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ButtonPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        DownloadBar.Value = 0;
        ProgressText.Text = "准备下载…";
        SpeedText.Text = "";

        _cancellation = new CancellationTokenSource();
        _watch = Stopwatch.StartNew();
        var progress = new Progress<DownloadProgress>(OnProgress);

        try
        {
            var zip = await Updater.DownloadAsync(_info, progress, _cancellation.Token);
            _watch.Stop();
            DownloadBar.Value = 100;
            ProgressText.Text = "下载完成，正在重启并安装更新…";
            SpeedText.Text = "";

            Updater.LaunchInstaller(zip);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            RestoreButtons("已取消下载");
        }
        catch (Exception ex)
        {
            Logger.Error("下载更新失败：" + ex.Message);
            RestoreButtons("下载失败");
            MessageDialog.Show(this, "下载更新失败", ex.Message + "\n\n可以点「打开下载页」手动下载。", warning: true);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void RestoreButtons(string status)
    {
        _busy = false;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ButtonPanel.Visibility = Visibility.Visible;
        UpdateButton.Content = "重试下载";
        ProgressText.Text = status;
    }

    private void OnProgress(DownloadProgress progress)
    {
        if (progress.Total > 0)
        {
            var percent = progress.Received * 100.0 / progress.Total;
            DownloadBar.Value = Math.Clamp(percent, 0, 100);
            ProgressText.Text = $"已下载 {progress.Received / 1024.0 / 1024.0:0.0} MB / "
                                + $"{progress.Total / 1024.0 / 1024.0:0.0} MB（{percent:0}%）";
        }
        else
        {
            ProgressText.Text = $"已下载 {progress.Received / 1024.0 / 1024.0:0.0} MB";
        }

        if (_watch is { Elapsed.TotalSeconds: > 0.5 } && progress.Received > 0)
        {
            var speed = progress.Received / _watch.Elapsed.TotalSeconds;
            SpeedText.Text = speed >= 1024 * 1024
                ? $"{speed / 1024 / 1024:0.0} MB/s"
                : $"{speed / 1024:0} KB/s";
            if (progress.Total > progress.Received)
            {
                var remaining = (progress.Total - progress.Received) / speed;
                SpeedText.Text += $" · 剩余 {(remaining > 60 ? $"{remaining / 60:0} 分 {remaining % 60:0} 秒" : $"{remaining:0} 秒")}";
            }
        }
    }
}
