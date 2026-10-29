using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LXLPCL.Core;

/// <summary>
/// 让离线账户在游戏里用上启动器内的皮肤：写入 CustomSkinLoader 的 LocalSkin 文件夹，
/// 并确保 CustomSkinLoader.json 的 loadlist 里启用了 LocalSkin。
/// </summary>
public static class SkinService
{
    private static string GameDir => AppState.Config.GameDir;

    public static string LocalSkinDir =>
        Path.Combine(GameDir, "CustomSkinLoader", "LocalSkin", "skins");

    private static string ConfigFile =>
        Path.Combine(GameDir, "CustomSkinLoader", "CustomSkinLoader.json");

    public static string? ActiveSkinPath =>
        string.IsNullOrWhiteSpace(AppState.Config.ActiveSkinPath) ? null : AppState.Config.ActiveSkinPath;

    public static bool IsActive(string path)
    {
        var active = ActiveSkinPath;
        if (active == null) return false;
        try
        {
            return string.Equals(Path.GetFullPath(active), Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>扫描各 mods 目录，返回 CustomSkinLoader 的版本号（未安装时为 null）。</summary>
    public static string? FindCustomSkinLoader()
    {
        var dirs = new List<string> { Path.Combine(GameDir, "mods") };
        var versions = Path.Combine(GameDir, "versions");
        if (Directory.Exists(versions))
            dirs.AddRange(Directory.EnumerateDirectories(versions).Select(v => Path.Combine(v, "mods")));

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jar"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("customskinloader", StringComparison.OrdinalIgnoreCase))
                    return ParseVersion(name) ?? "";
            }
        }
        return null;
    }

    private static string? ParseVersion(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+\.\d+(\.\d+)?)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>应用皮肤；返回 false 表示没找到 CustomSkinLoader，游戏里暂时不会生效。</summary>
    public static (bool Applied, string? ModVersion) Apply(string skinPath)
    {
        var name = AppState.SelectedAccount?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("请先在账户页创建或选择一个账户，皮肤会按该账户的玩家名生效。");

        Directory.CreateDirectory(LocalSkinDir);
        var target = Path.Combine(LocalSkinDir, Utils.SafeFileName(name) + ".png");
        File.Copy(skinPath, target, true);

        var modVersion = FindCustomSkinLoader();
        EnsureConfig(modVersion);

        AppState.Config.ActiveSkinPath = skinPath;
        AppState.SaveConfig();
        return (modVersion != null, modVersion);
    }

    private static void EnsureConfig(string? modVersion)
    {
        try
        {
            JsonObject root;
            if (File.Exists(ConfigFile))
            {
                root = JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject ?? new JsonObject();
            }
            else
            {
                // 新建配置时跟随已安装版本号，避免 CustomSkinLoader 认为配置不匹配而重置
                root = new JsonObject
                {
                    ["version"] = string.IsNullOrEmpty(modVersion) ? "14.25" : modVersion,
                    ["buildNumber"] = 0
                };
            }

            if (root["loadlist"] is not JsonArray list)
            {
                list = new JsonArray();
                root["loadlist"] = list;
            }

            var enabled = list.Any(node => node is JsonObject entry &&
                (entry["type"]?.ToString() == "LocalSkin" || entry["name"]?.ToString() == "LocalSkin"));
            if (!enabled)
            {
                list.Insert(0, new JsonObject
                {
                    ["name"] = "LocalSkin",
                    ["type"] = "LocalSkin",
                    ["skin"] = "local"
                });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            File.WriteAllText(ConfigFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Error("写入 CustomSkinLoader 配置失败: " + ex.Message);
        }
    }
}
