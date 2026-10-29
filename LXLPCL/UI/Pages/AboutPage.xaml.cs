using System.IO;
using System.Windows;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class AboutPage : PageBase
{
    public AboutPage()
    {
        InitializeComponent();
        UpdateInfo();
    }

    public override void OnShown() => UpdateInfo();

    private void UpdateInfo()
    {
        VersionText.Text = AppState.VersionText;
        DataDirText.Text = AppState.DataDir;
        DataDirText.ToolTip = AppState.DataDir;
        LogFileText.Text = string.IsNullOrEmpty(Logger.LogFile)
            ? Path.Combine(AppState.LogDir, "latest.log")
            : Logger.LogFile;
        LogFileText.ToolTip = LogFileText.Text;
        GameDirText.Text = AppState.Config.GameDir;
        GameDirText.ToolTip = AppState.Config.GameDir;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.DataDir);

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "检查中…";
        try
        {
            var info = await Updater.CheckAsync();
            if (info == null)
            {
                MessageDialog.Show(window, "已是最新版本",
                    $"当前版本 {AppState.VersionText} 已经是最新版本。", okText: "好的");
                return;
            }
            UpdateDialog.Show(window, info);
        }
        catch (Exception ex)
        {
            Logger.Error("手动检查更新失败：" + ex.Message);
            MessageDialog.Show(window, "检查更新失败",
                "无法连接到 GitHub：" + ex.Message + "\n\n可以点「GitHub 仓库」手动查看最新版本。", warning: true);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "检查更新";
        }
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => Utils.OpenFolder(AppState.Config.GameDir);

    private void OpenPcl_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenUrl("https://github.com/Hex-Dragon/PCL2");

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl(Updater.RepoPage);

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl(Updater.ReleasesPage);

    private void OpenModrinth_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl("https://modrinth.com");

    private void OpenBmcl_Click(object sender, RoutedEventArgs e) =>
        Utils.OpenUrl("https://bmclapidoc.bangbang93.com");

    private void OpenFabric_Click(object sender, RoutedEventArgs e) => Utils.OpenUrl("https://fabricmc.net");

    private void CopyGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("416980382");
            if (sender is not System.Windows.Controls.Button button) return;
            var original = button.Content;
            button.Content = "已复制";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1400)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                button.Content = original;
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("复制群号失败: " + ex.Message);
        }
    }
}
