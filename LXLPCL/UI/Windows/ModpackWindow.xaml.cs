using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class ModpackWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private string _importPath = "";
    private bool _busy;

    public ModpackWindow(GameVersion? preselect = null)
    {
        InitializeComponent();
        ExportVersionCombo.ItemsSource = AppState.Versions.ToList();
        var index = preselect == null ? -1 : AppState.Versions.IndexOf(preselect);
        ExportVersionCombo.SelectedIndex = index >= 0 ? index : (AppState.Versions.Count > 0 ? 0 : -1);
        Closed += (_, _) => _cts.Cancel();
    }

    private GameVersion? SelectedExport => ExportVersionCombo.SelectedItem as GameVersion;

    private void PickPack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择整合包",
            Filter = "整合包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        _importPath = dialog.FileName;
        ImportPathText.Text = _importPath;
        ImportPathText.ToolTip = _importPath;
        try
        {
            using var zip = ZipFile.OpenRead(_importPath);
            var count = zip.Entries.Count(entry => !string.IsNullOrEmpty(entry.Name));
            var hasVersions = zip.Entries.Any(entry =>
                entry.FullName.Replace('\\', '/').Contains("versions/", StringComparison.OrdinalIgnoreCase));
            var hasPcl = zip.Entries.Any(entry =>
                entry.FullName.Replace('\\', '/').StartsWith("PCL/", StringComparison.OrdinalIgnoreCase));
            var size = new FileInfo(_importPath).Length;
            ImportInfoText.Text = $"{count} 个文件  ·  {Utils.FormatBytes(size)}"
                                  + (hasVersions ? "  ·  检测到 versions 目录" : "")
                                  + (hasPcl ? "  ·  含 PCL2 配置" : "");
            StatusText.Text = "将导入到：" + Path.Combine(AppState.Config.GameDir, "versions");
        }
        catch (Exception ex)
        {
            ImportInfoText.Text = "";
            MessageDialog.Show(this, "无法读取整合包", ex.Message, warning: true);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(_importPath) || !File.Exists(_importPath))
        {
            MessageDialog.Show(this, "还没有选择整合包", "请先选择要导入的 zip 文件。", warning: true);
            return;
        }

        _busy = true;
        ImportButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ImportProgress.Visibility = Visibility.Visible;
        ImportProgress.Value = 0;

        var progress = new Progress<(string Detail, double Percent)>(value =>
        {
            ImportProgress.Value = value.Percent;
            StatusText.Text = value.Detail;
        });

        try
        {
            var result = await Modpack.ImportAsync(_importPath, AppState.Config.GameDir, progress, _cts.Token);
            await AppState.RefreshVersionsAsync();
            AppState.SetSelectedVersion(result.VersionId);
            StatusText.Text = $"导入完成：{result.VersionId}（{result.Files} 个文件）";
            var hasJson = Directory.EnumerateFiles(result.Folder, "*.json", SearchOption.TopDirectoryOnly).Any();
            var message = hasJson
                ? $"{result.VersionId} 已导入。\n\n位置：{result.Folder}\n文件数：{result.Files}\n\n" +
                  "如果启动提示缺少依赖库或资源，请先在版本管理的“下载”里补全对应游戏版本。"
                : $"已解压 {result.Files} 个文件到：\n{result.Folder}\n\n" +
                  "但这个整合包里没有版本 JSON，启动器无法直接识别。请先在版本管理的“下载”里安装对应游戏版本，再补全版本文件。";
            var open = MessageDialog.Show(this, "整合包导入完成", message, "打开版本目录", "完成");
            if (open) Utils.OpenFolder(result.Folder);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("导入整合包失败: " + ex.Message);
            MessageDialog.Show(this, "导入失败", ex.Message, warning: true);
            StatusText.Text = "导入失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            ImportButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            ImportProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var version = SelectedExport;
        if (version == null)
        {
            MessageDialog.Show(this, "还没有选择版本", "请先在版本管理中安装或导入一个版本。", warning: true);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出整合包",
            FileName = Utils.SafeFileName(version.Id) + "-整合包.zip",
            DefaultExt = ".zip",
            Filter = "整合包 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        var options = new Modpack.Options
        {
            Mods = ExportMods.IsChecked == true,
            Config = ExportConfig.IsChecked == true,
            Saves = ExportSaves.IsChecked == true,
            ResourcePacks = ExportResourcePacks.IsChecked == true,
            ShaderPacks = ExportShaderPacks.IsChecked == true
        };

        _busy = true;
        ImportButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ExportProgress.Visibility = Visibility.Visible;
        ExportProgress.Value = 0;

        var progress = new Progress<(string Detail, double Percent)>(value =>
        {
            ExportProgress.Value = value.Percent;
            StatusText.Text = value.Detail;
        });

        try
        {
            var result = await Modpack.ExportAsync(version, options, dialog.FileName, progress, _cts.Token);
            StatusText.Text = $"导出完成：{result.Files} 个文件，{Utils.FormatBytes(result.Bytes)}";
            var open = MessageDialog.Show(this, "整合包导出完成",
                $"{version.Id} 已导出。\n\n文件：{Path.GetFileName(result.Path)}\n" +
                $"大小：{Utils.FormatBytes(result.Bytes)}\n\n包含：{string.Join("、", options.Folders())}",
                "打开所在文件夹", "完成");
            if (open) Utils.OpenFolder(Path.GetDirectoryName(result.Path) ?? "");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("导出整合包失败: " + ex.Message);
            MessageDialog.Show(this, "导出失败", ex.Message, warning: true);
            StatusText.Text = "导出失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            ImportButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            ExportProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
