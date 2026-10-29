using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class FindGameWindow : Window
{
    private CancellationTokenSource? _scanCts;
    private List<LocalGameFolder> _folders = new();
    private bool _busy;

    public FindGameWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ScanAsync();
        Closed += (_, _) => _scanCts?.Cancel();
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        StatusText.Text = "正在扫描常用位置（官方启动器、PCL2、各磁盘 .minecraft）…";
        UseButton.IsEnabled = false;
        try
        {
            var list = await Task.Run(() => LocalDiscovery.Scan(token), token);
            if (token.IsCancellationRequested) return;
            _folders = list;
            GameList.ItemsSource = null;
            GameList.ItemsSource = _folders;
            ScanCountText.Text = list.Count == 0 ? "" : $"{list.Count} 个";
            StatusText.Text = list.Count == 0
                ? "没有自动找到已有游戏目录，可点击“选择文件夹…”手动指定"
                : $"找到 {list.Count} 个游戏目录，共 {list.Sum(f => f.Versions.Count)} 个版本";
            var target = _folders.FirstOrDefault(f => f.IsCurrent)
                         ?? _folders.FirstOrDefault(f => f.Versions.Count > 0);
            GameList.SelectedItem = target;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("扫描已有游戏目录失败: " + ex.Message);
            StatusText.Text = "扫描失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is not LocalGameFolder folder)
        {
            PreviewTitleText.Text = "请选择一个游戏目录";
            PreviewPathText.Text = "";
            VersionPreviewList.ItemsSource = null;
            PreviewEmptyText.Visibility = Visibility.Visible;
            UseButton.IsEnabled = false;
            return;
        }

        PreviewTitleText.Text = folder.Name;
        PreviewPathText.Text = folder.Path + "  ·  " + folder.SourceLabel;
        VersionPreviewList.ItemsSource = folder.Versions
            .OrderByDescending(v => v.ModCount)
            .ThenBy(v => v.Id)
            .ToList();
        PreviewEmptyText.Visibility = folder.Versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UseButton.IsEnabled = !folder.IsCurrent && folder.Versions.Count > 0;
        UseButton.Content = folder.IsCurrent ? "正在使用" : "使用此目录";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择已有的游戏目录（含 versions 文件夹）",
            InitialDirectory = AppState.Config.GameDir
        };
        if (dialog.ShowDialog(this) != true) return;
        var folder = LocalDiscovery.ResolveGameDir(dialog.FolderName);
        if (folder == null)
        {
            MessageDialog.Show(this, "无法识别该目录",
                "所选文件夹中没有找到 versions 版本目录。\n\n请选择 PCL2 / HMCL / 官方启动器的 .minecraft 目录，或整合包根目录。",
                warning: true);
            return;
        }

        var existing = _folders.FirstOrDefault(f =>
            string.Equals(f.Path, folder, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            var inspected = LocalDiscovery.Inspect(folder);
            if (inspected == null)
            {
                MessageDialog.Show(this, "无法识别该目录", "该目录中还没有安装任何版本。", warning: true);
                return;
            }
            _folders.Insert(0, inspected);
            GameList.ItemsSource = null;
            GameList.ItemsSource = _folders;
            existing = inspected;
            ScanCountText.Text = $"{_folders.Count} 个";
        }
        GameList.SelectedItem = existing;
        GameList.ScrollIntoView(existing);
        StatusText.Text = $"已选择：{folder}（{existing.Versions.Count} 个版本）";
    }

    private async void Use_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not LocalGameFolder folder || folder.IsCurrent) return;
        UseButton.IsEnabled = false;
        StatusText.Text = "正在切换游戏目录并扫描版本…";
        try
        {
            await AppState.SetGameDirAsync(folder.Path);
            Logger.Info($"已使用已有游戏目录：{folder.Path}（{AppState.Versions.Count} 个版本）");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "切换失败：" + ex.Message;
            MessageDialog.Show(this, "切换失败", ex.Message, warning: true);
            UseButton.IsEnabled = true;
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
