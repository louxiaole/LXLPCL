using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace LXLPCL.Core;

public static class AppState
{
    public static LauncherConfig Config { get; private set; } = new();
    public static string VersionText => "1.0.0";
    public static string BaseDir => AppContext.BaseDirectory;
    public static string DataDir => Path.Combine(BaseDir, "LXLPCLData");
    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string RuntimeDir => Path.Combine(DataDir, "java");
    public static string LogDir => Path.Combine(DataDir, "logs");

    public static ObservableCollection<GameVersion> Versions { get; } = new();
    public static List<JavaInfo> Javas { get; private set; } = new();

    public static event Action? VersionsChanged;
    public static event Action? JavasChanged;
    public static event Action? AccountsChanged;
    public static event Action? ConfigChanged;

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(RuntimeDir);
        Directory.CreateDirectory(LogDir);

        Config = ConfigStore.Load();
        var originalDir = Config.GameDir;
        if (string.IsNullOrWhiteSpace(Config.GameDir) || IsLegacyGameDir(Config.GameDir))
            Config.GameDir = DefaultGameDir();
        if (Config.MemoryMb < 512) Config.MemoryMb = 4096;
        Directory.CreateDirectory(Config.GameDir);
        if (!string.Equals(originalDir, Config.GameDir, StringComparison.OrdinalIgnoreCase))
        {
            ConfigStore.Save(Config);
            Logger.Info("游戏目录已切换到启动器根目录: " + Config.GameDir);
        }
        ThemeManager.ApplyTheme(Config.ThemeMode);
        ThemeManager.ApplyBackground(Config.ThemeBackground);
        ThemeManager.Apply(Config.AccentColor);
    }

    public static string DefaultGameDir()
    {
        var local = Path.Combine(BaseDir, ".minecraft");
        Directory.CreateDirectory(local);
        return local;
    }

    /// <summary>旧的默认目录 %APPDATA%\.minecraft，且其中没有任何已安装版本时迁移到启动器根目录。</summary>
    private static bool IsLegacyGameDir(string dir)
    {
        try
        {
            var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            if (!string.Equals(Path.GetFullPath(dir), Path.GetFullPath(roaming), StringComparison.OrdinalIgnoreCase))
                return false;
            var versions = Path.Combine(roaming, "versions");
            return !Directory.Exists(versions) || !Directory.EnumerateDirectories(versions).Any();
        }
        catch
        {
            return false;
        }
    }

    public static void SaveConfig()
    {
        ConfigStore.Save(Config);
        ConfigChanged?.Invoke();
    }

    public static void NotifyAccountsChanged()
    {
        SaveConfig();
        AccountsChanged?.Invoke();
    }

    public static GameVersion? SelectedVersion =>
        Versions.FirstOrDefault(v => v.Id == Config.SelectedVersion) ?? Versions.FirstOrDefault();

    public static Account? SelectedAccount =>
        Config.Accounts.FirstOrDefault(a => a.Uuid == Config.SelectedAccountUuid) ?? Config.Accounts.FirstOrDefault();

    public static void SetSelectedVersion(string id)
    {
        Config.SelectedVersion = id;
        SaveConfig();
    }

    /// <summary>切换游戏目录并重新扫描版本。</summary>
    public static async Task SetGameDirAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        Config.GameDir = dir;
        SaveConfig();
        Logger.Info("游戏目录已切换到 " + dir);
        await RefreshVersionsAsync();
        await RefreshJavasAsync();
        ConfigChanged?.Invoke();
    }

    public static async Task RefreshVersionsAsync()
    {
        var dir = Config.GameDir;
        var list = await Task.Run(() => VersionScanner.Scan(dir));
        var dispatcher = Application.Current?.Dispatcher;
        void Apply()
        {
            Versions.Clear();
            foreach (var v in list) Versions.Add(v);
            if (Versions.Count > 0 && Versions.All(v => v.Id != Config.SelectedVersion))
            {
                Config.SelectedVersion = Versions[0].Id;
                ConfigStore.Save(Config);
            }
            VersionsChanged?.Invoke();
        }
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply);
        else Apply();
    }

    public static async Task RefreshJavasAsync()
    {
        var gameDir = Config.GameDir;
        var configured = Config.JavaPath;
        var list = await Task.Run(() => JavaFinder.Scan(gameDir, configured));
        var dispatcher = Application.Current?.Dispatcher;
        void Apply()
        {
            Javas = list;
            JavasChanged?.Invoke();
        }
        if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply);
        else Apply();
    }

    public static void AddOfflineAccount(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("用户名不能为空");
        if (name.Length > 16) throw new ArgumentException("用户名不能超过 16 个字符");
        if (!name.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new ArgumentException("用户名只能包含字母、数字和下划线");
        var existing = Config.Accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Config.SelectedAccountUuid = existing.Uuid;
        }
        else
        {
            var account = new Account
            {
                Name = name,
                Uuid = Utils.OfflineUuid(name),
                AccessToken = Guid.NewGuid().ToString("N"),
                Type = "离线"
            };
            Config.Accounts.Add(account);
            Config.SelectedAccountUuid = account.Uuid;
        }
        NotifyAccountsChanged();
    }

    public static Account AddMicrosoftAccount(MicrosoftProfile profile)
    {
        var account = Config.Accounts.FirstOrDefault(a => a.Uuid == profile.Uuid);
        if (account == null)
        {
            account = new Account { Uuid = profile.Uuid, Type = "微软" };
            Config.Accounts.Add(account);
        }
        account.Name = profile.Name;
        account.Type = "微软";
        account.AccessToken = profile.AccessToken;
        account.RefreshToken = profile.RefreshToken;
        account.Xuid = profile.Xuid;
        account.TokenExpiresAt = profile.ExpiresAt;
        Config.SelectedAccountUuid = account.Uuid;
        NotifyAccountsChanged();
        return account;
    }

    /// <summary>启动前检查微软令牌，临近过期时自动刷新。</summary>
    public static async Task EnsureMicrosoftTokenAsync(Account account, CancellationToken token = default)
    {
        if (account.Type != "微软") return;
        if (account.TokenExpiresAt is { } expires && expires > DateTime.Now.AddMinutes(5)) return;
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
            throw new InvalidOperationException("Microsoft 登录状态已失效，请在账户管理中重新登录。");
        MicrosoftProfile profile;
        try
        {
            profile = await MicrosoftAuth.RefreshAsync(account.RefreshToken, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("刷新微软令牌失败: " + ex.Message);
            throw new InvalidOperationException("Microsoft 登录状态已失效，请在账户管理中重新登录。", ex);
        }
        account.Name = profile.Name;
        account.AccessToken = profile.AccessToken;
        account.RefreshToken = profile.RefreshToken;
        account.Xuid = profile.Xuid;
        account.TokenExpiresAt = profile.ExpiresAt;
        SaveConfig();
        Logger.Info("已刷新微软账户令牌：" + account.Name);
    }

    public static void RemoveAccount(Account account)
    {
        Config.Accounts.Remove(account);
        if (Config.SelectedAccountUuid == account.Uuid)
            Config.SelectedAccountUuid = Config.Accounts.FirstOrDefault()?.Uuid;
        NotifyAccountsChanged();
    }

    public static void ApplyAccent(string hex)
    {
        Config.AccentColor = hex;
        ThemeManager.Apply(hex);
        SaveConfig();
    }

    public static string ModsDirFor(GameVersion version)
    {
        var versionDir = Path.Combine(Config.GameDir, "versions", version.Id);
        if (version.UsesIsolatedDir) return Path.Combine(versionDir, "mods");
        return Path.Combine(Config.GameDir, "mods");
    }

    public static string ResolveModsDir(GameVersion version)
    {
        var global = Path.Combine(Config.GameDir, "mods");
        var isolated = Path.Combine(Config.GameDir, "versions", version.Id, "mods");
        return version.UsesIsolatedDir ? isolated : global;
    }
}
