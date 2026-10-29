using System.IO;
using System.Text.Json;

namespace LXLPCL.Core;

public class Account
{
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    /// <summary>离线 / 微软 / 外置</summary>
    public string Type { get; set; } = "离线";
    public string? ServerUrl { get; set; }
    public string RefreshToken { get; set; } = "";
    public DateTime? TokenExpiresAt { get; set; }
    public string Xuid { get; set; } = "";

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    public string TypeText => Type switch
    {
        "微软" => "微软正版",
        "外置" => "外置登录",
        _ => "离线账户"
    };
}

public class VersionSettings
{
    public int? MemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool? Fullscreen { get; set; }
    public bool? Isolated { get; set; }
    public string? JvmArgs { get; set; }
    public string? GameArgs { get; set; }
}

public class LauncherConfig
{
    public string GameDir { get; set; } = "";
    public string JavaPath { get; set; } = "";
    public int MemoryMb { get; set; } = 4096;
    public bool AutoMemory { get; set; } = true;
    public int ConfigVersion { get; set; }
    public string MicrosoftClientId { get; set; } = "";
    public string DownloadSource { get; set; } = "自动";
    public int DownloadThreads { get; set; } = 8;
    public string ModFileNameFormat { get; set; } = ModNaming.SlugVersionChinese;
    public string ModManageStyle { get; set; } = ModNaming.StyleTranslated;
    public string AccentColor { get; set; } = "#2E7CF6";
    public string ThemeMode { get; set; } = ThemeManager.ModeLight;
    public string ThemeBackground { get; set; } = ThemeManager.BgNone;
    public bool Animations { get; set; } = true;
    public bool AutoInstallAssets { get; set; } = true;
    public bool AutoDownloadJava { get; set; } = true;
    public string CustomJvmArgs { get; set; } = "";
    public string CustomGameArgs { get; set; } = "";
    public bool Fullscreen { get; set; } = false;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public string SelectedVersion { get; set; } = "";
    public string ActiveSkinPath { get; set; } = "";
    public List<Account> Accounts { get; set; } = new();
    public string? SelectedAccountUuid { get; set; }
    public Dictionary<string, VersionSettings> VersionSettings { get; set; } = new();
    public Dictionary<string, DateTime> LastPlayed { get; set; } = new();
    public List<string> MultiplayerServers { get; set; } = new();
    public string MultiplayerMode { get; set; } = "lan";
    public string SakuraTunnelId { get; set; } = "";
    public int SakuraLocalPort { get; set; } = 25565;
    public string SakuraRemote { get; set; } = "";
    public string SakuraLauncherPath { get; set; } = "";
    public bool IsFirstRun { get; set; } = true;
    public string SkippedUpdateVersion { get; set; } = "";

    public VersionSettings GetVersionSettings(string id)
    {
        if (!VersionSettings.TryGetValue(id, out var settings))
        {
            settings = new VersionSettings();
            VersionSettings[id] = settings;
        }
        return settings;
    }
}

public static class ConfigStore
{
    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "LXLPCL.json");

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);
                if (config != null)
                {
                    config.Accounts ??= new List<Account>();
                    config.VersionSettings ??= new Dictionary<string, VersionSettings>();
                    config.LastPlayed ??= new Dictionary<string, DateTime>();
                    config.MultiplayerServers ??= new List<string>();
                    config.MultiplayerMode ??= "lan";
                    config.SakuraTunnelId ??= "";
                    config.SakuraRemote ??= "";
                    config.SakuraLauncherPath ??= "";
                    config.SkippedUpdateVersion ??= "";
                    if (config.SakuraLocalPort <= 0) config.SakuraLocalPort = 25565;
                    if (config.ConfigVersion < 1)
                    {
                        config.ConfigVersion = 1;
                        config.AutoMemory = true;
                    }
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取配置失败: " + ex.Message);
        }
        return new LauncherConfig();
    }

    public static void Save(LauncherConfig config)
    {
        try
        {
            config.ConfigVersion = 1;
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, ConfigPath, true);
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置失败: " + ex.Message);
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
