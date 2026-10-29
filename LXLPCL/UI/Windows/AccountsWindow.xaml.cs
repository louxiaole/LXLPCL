using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LXLPCL.Core;

namespace LXLPCL.UI.Windows;

public partial class AccountsWindow : Window
{
    private CancellationTokenSource? _loginCts;
    private DeviceCodeInfo? _deviceCode;

    public AccountsWindow()
    {
        InitializeComponent();
        LoadAccounts();
        Loaded += (_, _) => NameBox.Focus();
        Closed += (_, _) => _loginCts?.Cancel();
    }

    private async void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_loginCts != null) return;
        _loginCts = new CancellationTokenSource();
        MicrosoftLoginButton.IsEnabled = false;
        CopyCodeButton.IsEnabled = false;
        OpenLoginButton.IsEnabled = false;
        CancelLoginButton.IsEnabled = false;
        DeviceCodePanel.Visibility = Visibility.Visible;
        LoginStatusText.Text = "正在获取授权代码…";
        try
        {
            var info = await MicrosoftAuth.StartDeviceCodeAsync(_loginCts.Token);
            _deviceCode = info;
            DeviceCodeText.Text = info.UserCode;
            VerificationUriText.Text = string.IsNullOrWhiteSpace(info.VerificationUri)
                ? "https://www.microsoft.com/link"
                : info.VerificationUri;
            CopyCodeButton.IsEnabled = true;
            OpenLoginButton.IsEnabled = true;
            CancelLoginButton.IsEnabled = true;
            LoginStatusText.Text = "正在等待浏览器授权…";
            Utils.OpenUrl(VerificationUriText.Text);

            var profile = await MicrosoftAuth.WaitForLoginAsync(info, _loginCts.Token);
            var account = AppState.AddMicrosoftAccount(profile);
            LoadAccounts();
            AccountList.SelectedItem = AppState.Config.Accounts.FirstOrDefault(a => a.Uuid == account.Uuid);
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            MessageDialog.Show(this, "登录成功", $"已登录为 {profile.Name}。\n\n该账户已设为当前账户。");
        }
        catch (OperationCanceledException)
        {
            LoginStatusText.Text = "已取消登录";
            DeviceCodePanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LoginStatusText.Text = "登录失败：" + ex.Message;
            MessageDialog.Show(this, "微软登录失败", ex.Message, warning: true);
        }
        finally
        {
            _loginCts?.Dispose();
            _loginCts = null;
            MicrosoftLoginButton.IsEnabled = true;
            CancelLoginButton.IsEnabled = true;
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceCode == null) return;
        Clipboard.SetText(_deviceCode.UserCode);
        LoginStatusText.Text = "代码已复制，请在浏览器中粘贴。";
    }

    private void OpenLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceCode != null) Utils.OpenUrl(_deviceCode.VerificationUri);
    }

    private void CancelLogin_Click(object sender, RoutedEventArgs e) => _loginCts?.Cancel();

    private void LoadAccounts()
    {
        var selectedUuid = (AccountList.SelectedItem as Account)?.Uuid ?? AppState.Config.SelectedAccountUuid;
        AccountList.ItemsSource = null;
        AccountList.ItemsSource = AppState.Config.Accounts;
        var current = AppState.Config.Accounts.FirstOrDefault(a => a.Uuid == selectedUuid);
        AccountList.SelectedItem = current ?? AppState.Config.Accounts.FirstOrDefault();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = AccountList.SelectedItem as Account;
        DeleteButton.IsEnabled = selected != null;
        UseButton.IsEnabled = selected != null;
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void AccountList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AccountList.SelectedItem is Account) UseSelected();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddAccount();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddAccount();
    }

    private void AddAccount()
    {
        var name = NameBox.Text.Trim();
        try
        {
            AppState.AddOfflineAccount(name);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, "无法添加账户", ex.Message, warning: true);
            return;
        }
        NameBox.Clear();
        LoadAccounts();
        var account = AppState.SelectedAccount;
        AccountList.SelectedItem = AppState.Config.Accounts.FirstOrDefault(a => a.Uuid == account?.Uuid);
        Logger.Info("已添加离线账户 " + name);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not Account account) return;
        if (!MessageDialog.Show(this, "删除账户", $"确定要删除账户“{account.Name}”吗？\n这不会影响游戏存档。",
                "删除", "取消", true)) return;
        AppState.RemoveAccount(account);
        LoadAccounts();
    }

    private void UseButton_Click(object sender, RoutedEventArgs e) => UseSelected();

    private void UseSelected()
    {
        if (AccountList.SelectedItem is not Account account) return;
        AppState.Config.SelectedAccountUuid = account.Uuid;
        AppState.SaveConfig();
        DialogResult = true;
        Close();
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
