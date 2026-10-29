using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class ToolsPage : PageBase
{
    private bool _hooked;
    private bool _gameLogMode;

    public ToolsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HookLogger();
        Unloaded += (_, _) => UnhookLogger();
    }

    public override void OnShown()
    {
        LoadCurrentLog();
        _ = RefreshInfoAsync();
    }

    private void HookLogger()
    {
        if (_hooked) return;
        _hooked = true;
        Logger.LineAdded += OnLogLineAdded;
    }

    private void UnhookLogger()
    {
        if (!_hooked) return;
        _hooked = false;
        Logger.LineAdded -= OnLogLineAdded;
    }

    private void OnLogLineAdded(string line)
    {
        if (_gameLogMode) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded) return;
            LogEmptyText.Visibility = Visibility.Collapsed;
            if (LogViewer.Text.Length > 0) LogViewer.AppendText(Environment.NewLine);
            LogViewer.AppendText(line);
            LogViewer.ScrollToEnd();
        });
    }

    // ================= 目录 =================

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.Config.GameDir);

    private void OpenSaves_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(Path.Combine(AppState.Config.GameDir, "saves"));

    private void OpenMods_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(Path.Combine(AppState.Config.GameDir, "mods"));

    private void OpenGameLogs_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenFolder(Path.Combine(AppState.Config.GameDir, "logs"));

    private void OpenLauncherLogs_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.LogDir);

    private void OpenJavaDir_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.RuntimeDir);

    private void OpenPcl_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenUrl("https://github.com/Hex-Dragon/PCL2");

    private void OpenModrinth_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl("https://modrinth.com");

    private void OpenWiki_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl("https://zh.minecraft.wiki");

    private void OpenFabric_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl("https://fabricmc.net");

    private void OpenBmcl_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenUrl("https://bmclapidoc.bangbang93.com");

    // ================= 日志 =================

    private void LogSource_Checked(object sender, RoutedEventArgs e)
    {
        if (LogViewer == null) return;
        if (sender is not RadioButton { Tag: string tag }) return;
        _gameLogMode = tag == "game";
        LoadCurrentLog();
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e) => LoadCurrentLog();

    private void LoadCurrentLog()
    {
        if (_gameLogMode)
        {
            var path = Path.Combine(AppState.Config.GameDir, "logs", "latest.log");
            LogPathText.Text = path;
            LogViewer.Text = ReadTail(path, 500);
            if (LogViewer.Text.Length == 0)
                LogViewer.Text = "尚未找到游戏日志。启动一次游戏后，这里会显示 logs\\latest.log 的内容。";
        }
        else
        {
            LogPathText.Text = string.IsNullOrEmpty(Logger.LogFile)
                ? Path.Combine(AppState.LogDir, "latest.log")
                : Logger.LogFile;
            LogViewer.Text = ReadTail(LogPathText.Text, 400);
        }

        LogEmptyText.Visibility = string.IsNullOrWhiteSpace(LogViewer.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_gameLogMode && _hooked) LogViewer.ScrollToEnd();
    }

    private static string ReadTail(string path, int lines)
    {
        try
        {
            if (!File.Exists(path)) return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)).Where(l => l.Length > 0));
        }
        catch (Exception ex)
        {
            return "无法读取日志：" + ex.Message;
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LogViewer.Text.Length > 0) Clipboard.SetText(LogViewer.Text);
        }
        catch (Exception ex)
        {
            Logger.Error("复制日志失败: " + ex.Message);
        }
    }

    // ================= 系统信息 =================

    private async void RefreshInfo_Click(object sender, RoutedEventArgs e) => await RefreshInfoAsync();

    private async Task RefreshInfoAsync()
    {
        VersionCountText.Text = AppState.Versions.Count + " 个";
        JavaCountText.Text = AppState.Javas.Count == 0 ? "未检测到" : AppState.Javas.Count + " 个";
        try
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            MemoryInfoText.Text = total > 0 ? "总计 " + Utils.FormatBytes(total) : "-";
        }
        catch { MemoryInfoText.Text = "-"; }
        try
        {
            using var process = Process.GetCurrentProcess();
            ProcessInfoText.Text = Utils.FormatBytes(process.WorkingSet64);
        }
        catch { ProcessInfoText.Text = "-"; }

        GameDirSizeText.Text = "计算中...";
        CacheSizeText.Text = "计算中...";
        var gameDir = AppState.Config.GameDir;
        var cacheDir = AppState.CacheDir;
        var (gameSize, cacheSize) = await Task.Run(() => (
            Directory.Exists(gameDir) ? Utils.DirectorySize(new DirectoryInfo(gameDir)) : 0,
            Directory.Exists(cacheDir) ? Utils.DirectorySize(new DirectoryInfo(cacheDir)) : 0));
        if (!IsLoaded) return;
        GameDirSizeText.Text = Utils.FormatBytes(gameSize);
        CacheSizeText.Text = Utils.FormatBytes(cacheSize);
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var cacheDir = AppState.CacheDir;
        if (!Directory.Exists(cacheDir)) return;
        var size = Utils.DirectorySize(new DirectoryInfo(cacheDir));
        if (size == 0)
        {
            MessageDialog.Show(Window.GetWindow(this), "缓存为空", "当前没有可以清理的下载缓存。");
            return;
        }
        var confirm = MessageDialog.Show(Window.GetWindow(this), "清理下载缓存",
            $"将删除 LXLPCLData\\cache 中的 {Utils.FormatBytes(size)} 缓存文件。\n已安装的游戏资源不受影响。",
            "清理", "取消");
        if (!confirm) return;

        var freed = await Task.Run(() =>
        {
            long total = 0;
            foreach (var dir in Directory.EnumerateDirectories(cacheDir))
            {
                try
                {
                    total += Utils.DirectorySize(new DirectoryInfo(dir));
                    Directory.Delete(dir, true);
                }
                catch { }
            }
            foreach (var file in Directory.EnumerateFiles(cacheDir))
            {
                try
                {
                    total += new FileInfo(file).Length;
                    File.Delete(file);
                }
                catch { }
            }
            return total;
        });

        Logger.Info($"已清理下载缓存 {Utils.FormatBytes(freed)}");
        await RefreshInfoAsync();
        MessageDialog.Show(Window.GetWindow(this), "清理完成", $"已释放 {Utils.FormatBytes(freed)} 磁盘空间。");
    }
}
