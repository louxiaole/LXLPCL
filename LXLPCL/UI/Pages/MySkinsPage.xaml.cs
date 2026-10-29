using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class MySkinsPage : PageBase
{
    public MySkinsPage() => InitializeComponent();

    public override void OnShown() => Reload();

    private static string SkinFolder() => Path.Combine(AppState.DataDir, "skins");

    private void Reload()
    {
        var items = new List<MySkinItem>();
        var dir = SkinFolder();
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.png")
                         .OrderByDescending(File.GetLastWriteTime))
            {
                try
                {
                    var info = new FileInfo(file);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(file, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    if (image.PixelWidth != 64 || image.PixelHeight != 64) continue;

                    var active = SkinService.IsActive(file);
                    items.Add(new MySkinItem
                    {
                        Path = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Image = image,
                        DetailText = $"{Utils.RelativeTime(info.LastWriteTime)} · " +
                                     $"{Math.Max(1, (int)Math.Round(info.Length / 1024.0))} KB",
                        FullText = $"{info.LastWriteTime:yyyy-MM-dd HH:mm}\n{file}",
                        UseText = active ? "使用中" : "使用",
                        UseEnabled = !active
                    });
                }
                catch
                {
                    // 损坏的图片跳过，不影响其它皮肤。
                }
            }
        }

        SkinCards.ItemsSource = items;
        CountText.Text = items.Count == 0 ? "共 0 张皮肤" : $"共 {items.Count} 张皮肤";
        SkinCards.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 操作 ----------

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void NewSkin_Click(object sender, RoutedEventArgs e) => Navigation.Go("skin");

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = SkinFolder();
        Directory.CreateDirectory(dir);
        Utils.OpenFolder(dir);
    }

    private void Preview_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        var viewer = new SkinViewerWindow(item.Name, item.Image, item.UseEnabled)
        {
            Owner = Window.GetWindow(this)
        };
        viewer.ShowDialog();

        if (viewer.UseRequested) UseSkin(item);
        else if (viewer.EditRequested) OpenInEditor(item);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        UseSkin(item);
    }

    private void UseSkin(MySkinItem item)
    {
        try
        {
            var (applied, version) = SkinService.Apply(item.Path);
            var account = AppState.SelectedAccount?.Name ?? "";
            var message = $"已把「{item.Name}」应用到账户 {account}。\n\n" +
                          $"文件位置：{SkinService.LocalSkinDir}\n\n启动游戏进入世界后就会显示这张皮肤。";
            message += applied
                ? $"\n\n已检测到 CustomSkinLoader {version}，并已在它的加载列表中启用本地皮肤。"
                : "\n\n注意：没有检测到 CustomSkinLoader 模组。离线账户的皮肤需要它才能在游戏里显示，可以在「模组 / 下载」里安装后重新应用一次。";
            Reload();
            MessageDialog.Show(Window.GetWindow(this), "皮肤已应用", message, "好的");
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "应用失败", ex.Message, warning: true);
        }
    }

    private void OpenInEditor(MySkinItem item)
    {
        SkinPage.PendingSkinPath = item.Path;
        Navigation.Go("skin");
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        var input = InputDialog.Show(Window.GetWindow(this), "重命名皮肤",
            "输入新的皮肤名称：", item.Name);
        if (string.IsNullOrWhiteSpace(input)) return;

        var name = Utils.SafeFileName(input).Trim();
        if (name.Length == 0 || name == item.Name) return;
        try
        {
            var target = Path.Combine(Path.GetDirectoryName(item.Path)!, name + ".png");
            if (File.Exists(target))
            {
                MessageDialog.Show(Window.GetWindow(this), "重命名失败",
                    $"「{name}」已经存在，请换一个名字。", warning: true);
                return;
            }
            File.Move(item.Path, target);
            if (SkinService.IsActive(item.Path)) AppState.Config.ActiveSkinPath = target;
            AppState.SaveConfig();
            Reload();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "重命名失败", ex.Message, warning: true);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        OpenInEditor(item);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出皮肤",
            Filter = "PNG 图片|*.png",
            FileName = item.Name + ".png",
            DefaultExt = ".png"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.Copy(item.Path, dialog.FileName, true);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "导出失败", ex.Message, warning: true);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MySkinItem item }) return;
        var remove = MessageDialog.Show(Window.GetWindow(this), "删除皮肤",
            $"确定要删除「{item.Name}」吗？\n\n删除后无法恢复。", "删除", "取消", warning: true);
        if (!remove) return;
        try
        {
            File.Delete(item.Path);
            Reload();
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Window.GetWindow(this), "删除失败", ex.Message, warning: true);
        }
    }
}

public class MySkinItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string DetailText { get; init; } = "";
    public string FullText { get; init; } = "";
    public string UseText { get; init; } = "使用";
    public bool UseEnabled { get; init; } = true;
    public BitmapImage Image { get; init; } = null!;
}
