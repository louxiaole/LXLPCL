using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LXLPCL.Core;
using LXLPCL.UI.Windows;

namespace LXLPCL.UI.Pages;

public partial class MultiplayerPage : PageBase
{
    private bool _suppress;

    public MultiplayerPage() => InitializeComponent();

    public override void OnShown()
    {
        LoadSettings();
        RefreshAddresses();
        RefreshStatus();
        RefreshHistory();
    }

    // ---------- 联机方式 ----------

    private void LoadSettings()
    {
        _suppress = true;
        var config = AppState.Config;
        SakuraTunnelBox.Text = config.SakuraTunnelId;
        SakuraPortBox.Text = config.SakuraLocalPort.ToString();
        SakuraPathBox.Text = config.SakuraLauncherPath;
        SakuraRemoteBox.Text = config.SakuraRemote;
        var mode = config.MultiplayerMode;
        ModeLanRadio.IsChecked = mode == "lan";
        ModeDirectRadio.IsChecked = mode == "direct";
        ModeSakuraRadio.IsChecked = mode == "sakura";
        _suppress = false;
        ApplyMode(mode);
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppress) return;
        var mode = (sender as RadioButton)?.Tag as string ?? "lan";
        AppState.Config.MultiplayerMode = mode;
        AppState.SaveConfig();
        ApplyMode(mode);
    }

    private void ApplyMode(string mode)
    {
        LanPanel.Visibility = mode == "lan" ? Visibility.Visible : Visibility.Collapsed;
        DirectPanel.Visibility = mode == "direct" ? Visibility.Visible : Visibility.Collapsed;
        SakuraPanel.Visibility = mode == "sakura" ? Visibility.Visible : Visibility.Collapsed;
        Step3Text.Text = mode == "sakura"
            ? "3. 启动樱花隧道，把访问地址发给朋友"
            : "3. 把本页复制的地址发给朋友";
        RefreshStatus();
    }

    private static string ModeName(string mode) => mode switch
    {
        "direct" => "直连服务器",
        "sakura" => "樱花映射",
        _ => "局域网"
    };

    // ---------- 樱花映射 ----------

    private void SakuraFind_Click(object sender, RoutedEventArgs e)
    {
        var found = FindSakuraLauncher();
        if (found == null)
        {
            MessageDialog.Show(Window.GetWindow(this), "没有找到樱花启动器",
                "常见安装位置都没有 SakuraLauncher.exe。请先从 sakurafrp.com 下载安装，或点「选择…」手动指定。",
                warning: true);
            return;
        }
        SakuraPathBox.Text = found;
        AppState.Config.SakuraLauncherPath = found;
        AppState.SaveConfig();
        SakuraStatusText.Text = "已找到樱花启动器：" + found;
    }

    private void SakuraBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 SakuraLauncher.exe",
            Filter = "樱花启动器 (SakuraLauncher.exe)|SakuraLauncher.exe|可执行文件 (*.exe)|*.exe"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        SakuraPathBox.Text = dialog.FileName;
        AppState.Config.SakuraLauncherPath = dialog.FileName;
        AppState.SaveConfig();
        SakuraStatusText.Text = "已设置樱花启动器：" + dialog.FileName;
    }

    /// <summary>在常见的樱花启动器安装位置里查找 SakuraLauncher.exe。</summary>
    private static string? FindSakuraLauncher()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "SakuraFrpLauncher", "SakuraLauncher.exe"),
            Path.Combine(local, "Programs", "SakuraFrpLauncher", "SakuraLauncher.exe"),
            Path.Combine(roaming, "SakuraFrpLauncher", "SakuraLauncher.exe"),
            @"C:\Program Files\SakuraFrpLauncher\SakuraLauncher.exe",
            @"C:\Program Files (x86)\SakuraFrpLauncher\SakuraLauncher.exe"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private async void SakuraStart_Click(object sender, RoutedEventArgs e)
    {
        var tunnel = SakuraTunnelBox.Text.Trim();
        if (tunnel.Length == 0)
        {
            SakuraStatusText.Text = "请先填写隧道 ID（樱花控制台 → 隧道详情）";
            return;
        }
        if (!int.TryParse(SakuraPortBox.Text.Trim(), out var port) || port <= 0 || port > 65535)
        {
            SakuraStatusText.Text = "本地端口无效，应填 1~65535 之间的数字";
            return;
        }

        var path = SakuraPathBox.Text.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            path = FindSakuraLauncher() ?? "";
            if (path.Length == 0)
            {
                SakuraStatusText.Text = "没有找到樱花启动器，请先点「自动查找」或「选择…」指定 SakuraLauncher.exe";
                return;
            }
            SakuraPathBox.Text = path;
        }

        AppState.Config.SakuraTunnelId = tunnel;
        AppState.Config.SakuraLocalPort = port;
        AppState.Config.SakuraLauncherPath = path;
        AppState.SaveConfig();

        var already = Process.GetProcessesByName("SakuraLauncher").Length > 0;
        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = $"-t {tunnel}",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? ""
            });
            SakuraStatusText.Text = already
                ? $"已请求启动隧道 #{tunnel}，请在樱花启动器的控制台窗口确认连接状态"
                : $"已请求启动隧道 #{tunnel}（本地端口 {port}），请在樱花启动器的控制台窗口确认连接状态";

            // 启动器遇到无效隧道或未登录时会立即退出，稍等一下确认进程是否还在
            await Task.Delay(1500);
            if (!already && Process.GetProcessesByName("SakuraLauncher").Length == 0)
            {
                SakuraStatusText.Text = started is { HasExited: true }
                    ? "樱花启动器启动后退出了：请确认隧道 ID 正确，并先在樱花启动器里登录一次"
                    : "没有检测到樱花启动器进程，请手动打开一次确认能正常运行";
            }
        }
        catch (Exception ex)
        {
            SakuraStatusText.Text = "启动失败：" + ex.Message;
        }
    }

    private void SakuraConsole_Click(object sender, RoutedEventArgs e)
    {
        Utils.OpenUrl("https://www.natfrp.com/user/tunnels");
    }

    private void SakuraDownload_Click(object sender, RoutedEventArgs e)
    {
        Utils.OpenUrl("https://www.natfrp.com/download");
        SakuraStatusText.Text = "已在浏览器打开官网下载页，下载安装樱花启动器后回到这里点「自动查找」";
    }

    private void SakuraSite_Click(object sender, RoutedEventArgs e)
    {
        Utils.OpenUrl("https://www.natfrp.com/");
    }

    private void SakuraCopy_Click(object sender, RoutedEventArgs e)
    {
        var remote = SakuraRemoteBox.Text.Trim();
        if (remote.Length == 0)
        {
            SakuraStatusText.Text = "还没有访问地址：隧道建立后把樱花给出的地址填到下面的输入框";
            return;
        }
        Copy(remote);
        SakuraStatusText.Text = $"已复制访问地址：{remote}";
    }

    private void SakuraRemote_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || _suppress) return;
        AppState.Config.SakuraRemote = SakuraRemoteBox.Text.Trim();
        AppState.SaveConfig();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAddresses();
        RefreshStatus();
        RefreshHistory();
    }

    // ---------- 本机地址 ----------

    private void RefreshAddresses()
    {
        var previous = IpCombo.SelectedItem as string;
        var addresses = GetLocalAddresses();
        IpCombo.ItemsSource = addresses;
        IpCombo.SelectedItem = addresses.Contains(previous ?? "")
            ? previous
            : addresses.FirstOrDefault();
        UpdateLocalAddress();
    }

    private void UpdateLocalAddress()
    {
        var ip = IpCombo.SelectedItem as string;
        var port = ParsePort();
        LocalAddressText.Text = string.IsNullOrEmpty(ip) ? "未检测到可用的本机地址" : $"{ip}:{port}";
    }

    /// <summary>本机所有可用的 IPv4 地址（排除回环与虚拟网卡），供朋友连接。</summary>
    private static List<string> GetLocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = info.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                    if (!list.Contains(ip)) list.Add(ip);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取本机地址失败: " + ex.Message);
        }
        return list;
    }

    private int ParsePort()
    {
        return int.TryParse(PortBox.Text.Trim(), out var port) && port > 0 && port <= 65535 ? port : 25565;
    }

    private void CopyLocal_Click(object sender, RoutedEventArgs e)
    {
        var ip = IpCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(ip))
        {
            MessageDialog.Show(Window.GetWindow(this), "没有可复制的地址",
                "没有检测到可用的本机局域网地址。请确认已连接网络，或点击右上角「刷新本机地址」。", warning: true);
            return;
        }
        Copy($"{ip}:{ParsePort()}");
    }

    private void OpenNetwork_Click(object sender, RoutedEventArgs e)
    {
        Utils.OpenUrl("ms-settings:network");
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch { /* 剪贴板被占用时忽略 */ }
    }

    // ---------- 直连服务器 ----------

    private string ServerText => ServerBox.Text.Trim();

    private void CopyServer_Click(object sender, RoutedEventArgs e)
    {
        if (ServerText.Length == 0)
        {
            MessageDialog.Show(Window.GetWindow(this), "地址为空", "请先输入服务器地址，例如 mc.hypixel.net。", warning: true);
            return;
        }
        Copy(ServerText);
        ServerStatusText.Text = $"已复制：{ServerText}";
    }

    private void SaveServer_Click(object sender, RoutedEventArgs e)
    {
        var text = ServerText;
        if (text.Length == 0) return;
        var list = AppState.Config.MultiplayerServers;
        list.RemoveAll(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, text);
        while (list.Count > 8) list.RemoveAt(list.Count - 1);
        AppState.SaveConfig();
        RefreshHistory();
        ServerStatusText.Text = $"已保存：{text}";
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var text = ServerText;
        if (text.Length == 0)
        {
            ServerStatusText.Text = "请先输入服务器地址";
            return;
        }
        if (!TrySplitAddress(text, out var host, out var port))
        {
            ServerStatusText.Text = "地址格式不正确，应为 主机名或IP:端口";
            return;
        }

        TestButton.IsEnabled = false;
        ServerStatusText.Text = $"正在连接 {host}:{port} ...";
        var watch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(host, port, cts.Token);
            watch.Stop();
            ServerStatusText.Text = client.Connected
                ? $"可以连接 · 延迟 {watch.ElapsedMilliseconds} ms"
                : "无法连接";
        }
        catch (OperationCanceledException)
        {
            ServerStatusText.Text = "连接超时（3 秒内无响应），可能端口错误或被防火墙拦截";
        }
        catch (Exception ex)
        {
            ServerStatusText.Text = "无法连接：" + ex.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private static bool TrySplitAddress(string text, out string host, out int port)
    {
        host = text;
        port = 25565;
        if (text.StartsWith('['))
        {
            // [IPv6]:port
            var end = text.IndexOf(']');
            if (end < 0) return false;
            host = text[1..end];
            if (end + 2 < text.Length && text[end + 1] == ':')
                return int.TryParse(text[(end + 2)..], out port);
            return true;
        }
        var colon = text.IndexOf(':');
        if (colon <= 0) return true;
        host = text[..colon];
        return int.TryParse(text[(colon + 1)..], out port);
    }

    private void RefreshHistory()
    {
        var list = AppState.Config.MultiplayerServers;
        HistoryPanel.Children.Clear();
        foreach (var item in list)
        {
            var button = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = item,
                FontSize = 11,
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Tag = item
            };
            button.Click += (_, _) =>
            {
                ServerBox.Text = item;
                Copy(item);
                ServerStatusText.Text = $"已复制：{item}";
            };
            HistoryPanel.Children.Add(button);
        }
        HistoryPanel.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 状态 ----------

    private void RefreshStatus()
    {
        var account = AppState.SelectedAccount;
        AccountText.Text = account == null
            ? "当前账户：未登录（启动游戏前需要添加账户）"
            : $"当前账户：{account.Name}（{account.TypeText}）";

        var version = AppState.SelectedVersion;
        VersionText.Text = version == null
            ? "当前版本：未选择（请先在启动页选择版本）"
            : $"当前版本：{version.Id}";
        var mode = $"联机方式：{ModeName(AppState.Config.MultiplayerMode)}";
        SubtitleText.Text = version == null
            ? $"局域网联机与服务器直连 · {mode}"
            : $"局域网联机与服务器直连 · 当前版本 {version.Id} · {mode}";
    }

    private void Launch_Click(object sender, RoutedEventArgs e) => Navigation.Go("launch");
}
