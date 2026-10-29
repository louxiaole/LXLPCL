using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LXLPCL.Core;

public class DownloadFile
{
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public long Size { get; set; }
}

public class AssetIndexInfo
{
    public string Id { get; set; } = "";
    public string? Url { get; set; }
    public string? Sha1 { get; set; }
    public long TotalSize { get; set; }
    public bool MapToResources { get; set; }
}

public class Rule
{
    public string Action { get; set; } = "allow";
    public string? OsName { get; set; }
    public string? OsArch { get; set; }
    public string? OsVersion { get; set; }
    public Dictionary<string, bool> Features { get; set; } = new();
}

public class Library
{
    public string Name { get; set; } = "";
    public string? Url { get; set; }
    public DownloadFile? Artifact { get; set; }
    public Dictionary<string, DownloadFile> Classifiers { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    public Dictionary<string, string> Natives { get; set; } = new();
    public bool IsNativeOnly => Artifact == null && Classifiers.Count > 0;

    public string MavenPath
    {
        get
        {
            var parts = Name.Split(':');
            if (parts.Length < 3) return Name.Replace(':', '/') + ".jar";
            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? "-" + parts[3] : "";
            return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
        }
    }

    public string? WindowsNativeClassifier
    {
        get
        {
            if (!Natives.TryGetValue("windows", out var value)) return null;
            return value.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
        }
    }
}

public class LaunchArg
{
    public List<string> Values { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
}

public class LoggingConfig
{
    public string Argument { get; set; } = "";
    public DownloadFile? File { get; set; }
}

public class VersionMeta
{
    public string Id { get; set; } = "";
    public string RootId { get; set; } = "";
    public string Type { get; set; } = "release";
    public string MainClass { get; set; } = "";
    public string Assets { get; set; } = "legacy";
    public string Jar { get; set; } = "";
    public string InheritsFrom { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public int JavaMajor { get; set; } = 8;
    public string JavaComponent { get; set; } = "";
    public bool HasJavaInfo { get; set; }
    public DownloadFile? Client { get; set; }
    public AssetIndexInfo? AssetIndex { get; set; }
    public List<Library> Libraries { get; set; } = new();
    public List<LaunchArg> JvmArgs { get; set; } = new();
    public List<LaunchArg> GameArgs { get; set; } = new();
    public string? MinecraftArguments { get; set; }
    public LoggingConfig? Logging { get; set; }
    public string JsonPath { get; set; } = "";
    public string Folder { get; set; } = "";
}

public class GameVersion
{
    public string Id { get; set; } = "";
    public string Folder { get; set; } = "";
    public string JsonPath { get; set; } = "";
    public VersionMeta? Meta { get; set; }
    public string? Error { get; set; }
    public string Loader { get; set; } = "原版";
    public string LoaderVersion { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string Type { get; set; } = "release";
    public int JavaMajor { get; set; } = 8;

    public bool IsModded => Loader != "原版";

    /// <summary>版本是否使用独立游戏目录（PCL2 整合包默认把 mods/config/存档放在版本文件夹内）。</summary>
    public bool UsesIsolatedDir
    {
        get
        {
            if (AppState.Config.VersionSettings.TryGetValue(Id, out var settings) && settings.Isolated.HasValue)
                return settings.Isolated.Value;
            return HasOwnGameContent;
        }
    }

    private bool HasOwnGameContent
    {
        get
        {
            try
            {
                if (!Directory.Exists(Folder)) return false;
                if (File.Exists(Path.Combine(Folder, "options.txt"))) return true;
                var mods = Path.Combine(Folder, "mods");
                if (Directory.Exists(mods) && Directory.EnumerateFiles(mods, "*.jar").Any()) return true;
                return Directory.Exists(Path.Combine(Folder, "saves"));
            }
            catch
            {
                return false;
            }
        }
    }

    public DateTime LastPlayed =>
        AppState.Config.LastPlayed.TryGetValue(Id, out var time) ? time : File.GetLastWriteTime(JsonPath);

    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => string.IsNullOrEmpty(Type) ? "未知" : Type
    };

    public string LoaderText => Loader;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { TypeText };
            if (!string.IsNullOrEmpty(McVersion)) parts.Add("MC " + McVersion);
            if (Loader != "原版") parts.Add(Loader + (string.IsNullOrEmpty(LoaderVersion) ? "" : " " + LoaderVersion));
            parts.Add("Java " + JavaMajor);
            return string.Join("  ·  ", parts);
        }
    }

    public string LastPlayedText => "上次游玩 " + Utils.FormatDate(LastPlayed);

    public string IconText => Loader switch
    {
        "Fabric" => "FB",
        "Forge" => "FG",
        "NeoForge" => "NF",
        "Quilt" => "QT",
        "OptiFine" => "OF",
        _ => "MC"
    };

    public Brush IconBrush
    {
        get
        {
            var hex = Loader switch
            {
                "Fabric" => "#D8A34B",
                "Forge" => "#6C7A91",
                "NeoForge" => "#D8823C",
                "Quilt" => "#9A6BD8",
                "OptiFine" => "#4E9BD8",
                _ => Type switch
                {
                    "snapshot" => "#E4A33C",
                    "old_beta" => "#8B93A3",
                    "old_alpha" => "#8B93A3",
                    _ => "#3AA76D"
                }
            };
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new LinearGradientBrush(ThemeManager.Shade(color, 0.14), ThemeManager.Shade(color, -0.18), 45);
            brush.Freeze();
            return brush;
        }
    }

    public string StatusText => Error != null ? "解析失败" : Loader == "原版" ? "纯净版本" : Loader + " 版本";

    private bool _logoLoaded;
    private BitmapImage? _logo;

    /// <summary>PCL2 等启动器为整合包保存的图标：versions/&lt;id&gt;/PCL/Logo.png。</summary>
    public BitmapImage? Logo
    {
        get
        {
            if (_logoLoaded) return _logo;
            _logoLoaded = true;
            _logo = ImageLoader.LoadVersionLogo(Folder);
            return _logo;
        }
    }

    public bool HasLogo => Logo != null;
}

public static class VersionResolver
{
    public static VersionMeta Resolve(string gameDir, string id)
    {
        return ResolveInternal(gameDir, id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static VersionMeta ResolveInternal(string gameDir, string id, HashSet<string> seen)
    {
        if (!seen.Add(id)) throw new InvalidDataException("版本继承出现循环: " + id);
        var folder = Path.Combine(gameDir, "versions", id);
        var jsonPath = Path.Combine(folder, id + ".json");
        if (!File.Exists(jsonPath))
        {
            var candidate = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.json").FirstOrDefault()
                : null;
            if (candidate == null) throw new FileNotFoundException("找不到版本文件: " + jsonPath);
            jsonPath = candidate;
        }

        var own = Parse(jsonPath, id, folder);
        if (!string.IsNullOrEmpty(own.InheritsFrom))
        {
            var parent = ResolveInternal(gameDir, own.InheritsFrom, seen);
            return Merge(parent, own);
        }
        own.RootId = own.Id;
        return own;
    }

    private static VersionMeta Parse(string jsonPath, string id, string folder)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        var meta = new VersionMeta
        {
            Id = GetString(root, "id") ?? id,
            Type = GetString(root, "type") ?? "",
            MainClass = GetString(root, "mainClass") ?? "",
            Assets = GetString(root, "assets") ?? "",
            Jar = GetString(root, "jar") ?? "",
            InheritsFrom = GetString(root, "inheritsFrom") ?? "",
            ClientVersion = GetString(root, "clientVersion") ?? "",
            JsonPath = jsonPath,
            Folder = folder
        };

        if (root.TryGetProperty("javaVersion", out var javaVersion))
        {
            meta.HasJavaInfo = true;
            meta.JavaMajor = GetInt(javaVersion, "majorVersion") ?? 8;
            meta.JavaComponent = GetString(javaVersion, "component") ?? "";
        }

        if (root.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("client", out var client))
            meta.Client = ParseDownload(client);

        if (root.TryGetProperty("assetIndex", out var assetIndex))
        {
            meta.AssetIndex = new AssetIndexInfo
            {
                Id = GetString(assetIndex, "id") ?? "",
                Url = GetString(assetIndex, "url"),
                Sha1 = GetString(assetIndex, "sha1"),
                TotalSize = GetLong(assetIndex, "totalSize"),
                MapToResources = assetIndex.TryGetProperty("map_to_resources", out var map) && map.ValueKind == JsonValueKind.True
            };
        }

        if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in libraries.EnumerateArray())
            {
                var lib = new Library { Name = GetString(item, "name") ?? "", Url = GetString(item, "url") };
                if (item.TryGetProperty("downloads", out var libDownloads))
                {
                    if (libDownloads.TryGetProperty("artifact", out var artifact))
                        lib.Artifact = ParseDownload(artifact);
                    if (libDownloads.TryGetProperty("classifiers", out var classifiers) && classifiers.ValueKind == JsonValueKind.Object)
                        foreach (var prop in classifiers.EnumerateObject())
                            lib.Classifiers[prop.Name] = ParseDownload(prop.Value);
                }
                if (item.TryGetProperty("rules", out var rules))
                    lib.Rules = ParseRules(rules);
                if (item.TryGetProperty("natives", out var natives) && natives.ValueKind == JsonValueKind.Object)
                    foreach (var prop in natives.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            lib.Natives[prop.Name] = prop.Value.GetString() ?? "";
                if (!string.IsNullOrEmpty(lib.Name)) meta.Libraries.Add(lib);
            }
        }

        if (root.TryGetProperty("arguments", out var arguments))
        {
            if (arguments.TryGetProperty("jvm", out var jvm)) meta.JvmArgs = ParseArguments(jvm);
            if (arguments.TryGetProperty("game", out var game)) meta.GameArgs = ParseArguments(game);
        }

        meta.MinecraftArguments = GetString(root, "minecraftArguments");

        if (root.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var logClient))
        {
            var config = new LoggingConfig { Argument = GetString(logClient, "argument") ?? "" };
            if (logClient.TryGetProperty("file", out var logFile))
            {
                config.File = new DownloadFile
                {
                    Url = GetString(logFile, "url") ?? "",
                    Sha1 = GetString(logFile, "sha1"),
                    Size = GetLong(logFile, "size")
                };
            }
            meta.Logging = config;
        }

        return meta;
    }

    private static VersionMeta Merge(VersionMeta parent, VersionMeta child)
    {
        var meta = new VersionMeta
        {
            Id = child.Id,
            RootId = parent.RootId,
            Type = string.IsNullOrEmpty(child.Type) ? parent.Type : child.Type,
            MainClass = string.IsNullOrEmpty(child.MainClass) ? parent.MainClass : child.MainClass,
            Assets = string.IsNullOrEmpty(child.Assets) ? parent.Assets : child.Assets,
            Jar = string.IsNullOrEmpty(child.Jar) ? parent.Jar : child.Jar,
            InheritsFrom = child.InheritsFrom,
            ClientVersion = string.IsNullOrEmpty(child.ClientVersion) ? parent.ClientVersion : child.ClientVersion,
            JavaMajor = child.HasJavaInfo ? child.JavaMajor : parent.JavaMajor,
            JavaComponent = child.HasJavaInfo && !string.IsNullOrEmpty(child.JavaComponent) ? child.JavaComponent : parent.JavaComponent,
            HasJavaInfo = child.HasJavaInfo || parent.HasJavaInfo,
            Client = child.Client ?? parent.Client,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            Logging = child.Logging ?? parent.Logging,
            JsonPath = child.JsonPath,
            Folder = child.Folder
        };

        var libs = new Dictionary<string, Library>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Library>();
        foreach (var lib in parent.Libraries.Concat(child.Libraries))
        {
            if (libs.TryGetValue(lib.Name, out var old))
            {
                ordered[ordered.IndexOf(old)] = lib;
                libs[lib.Name] = lib;
            }
            else
            {
                libs[lib.Name] = lib;
                ordered.Add(lib);
            }
        }
        meta.Libraries = ordered;
        meta.JvmArgs = parent.JvmArgs.Concat(child.JvmArgs).ToList();
        meta.GameArgs = parent.GameArgs.Concat(child.GameArgs).ToList();
        return meta;
    }

    private static List<LaunchArg> ParseArguments(JsonElement array)
    {
        var result = new List<LaunchArg>();
        if (array.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in array.EnumerateArray())
        {
            var arg = new LaunchArg();
            if (item.ValueKind == JsonValueKind.String)
            {
                arg.Values.Add(item.GetString() ?? "");
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("rules", out var rules)) arg.Rules = ParseRules(rules);
                if (item.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String) arg.Values.Add(value.GetString() ?? "");
                    else if (value.ValueKind == JsonValueKind.Array)
                        foreach (var v in value.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String) arg.Values.Add(v.GetString() ?? "");
                }
            }
            if (arg.Values.Count > 0) result.Add(arg);
        }
        return result;
    }

    private static List<Rule> ParseRules(JsonElement array)
    {
        var result = new List<Rule>();
        if (array.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in array.EnumerateArray())
        {
            var rule = new Rule { Action = GetString(item, "action") ?? "allow" };
            if (item.TryGetProperty("os", out var os))
            {
                rule.OsName = GetString(os, "name");
                rule.OsArch = GetString(os, "arch");
                rule.OsVersion = GetString(os, "version");
            }
            if (item.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object)
                foreach (var prop in features.EnumerateObject())
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        rule.Features[prop.Name] = prop.Value.GetBoolean();
            result.Add(rule);
        }
        return result;
    }

    private static DownloadFile ParseDownload(JsonElement element) => new()
    {
        Url = GetString(element, "url") ?? "",
        Sha1 = GetString(element, "sha1"),
        Size = GetLong(element, "size")
    };

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32() : null;

    private static long GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt64() : 0;
}

public static class RuleEvaluator
{
    public static bool IsAllowed(List<Rule>? rules, bool hasCustomResolution = false)
    {
        if (rules == null || rules.Count == 0) return true;
        var result = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule, hasCustomResolution)) continue;
            result = rule.Action == "allow";
        }
        return result;
    }

    private static bool Matches(Rule rule, bool hasCustomResolution)
    {
        if (!string.IsNullOrEmpty(rule.OsName) &&
            !rule.OsName.Equals("windows", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.OsArch))
        {
            var arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            if (!rule.OsArch.Equals(arch, StringComparison.OrdinalIgnoreCase) &&
                !(arch == "x86_64" && rule.OsArch.Equals("x64", StringComparison.OrdinalIgnoreCase))) return false;
        }
        foreach (var feature in rule.Features)
        {
            var actual = feature.Key switch
            {
                "has_custom_resolution" => hasCustomResolution,
                "is_demo_user" => false,
                "has_quick_plays_support" => false,
                "is_quick_play_singleplayer" => false,
                "is_quick_play_multiplayer" => false,
                "is_quick_play_realms" => false,
                _ => false
            };
            if (actual != feature.Value) return false;
        }
        return true;
    }
}

public static class VersionScanner
{
    public static List<GameVersion> Scan(string gameDir)
    {
        var result = new List<GameVersion>();
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir)) return result;

        foreach (var folder in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(folder);
            var json = Path.Combine(folder, id + ".json");
            if (!File.Exists(json))
            {
                json = Directory.GetFiles(folder, "*.json").FirstOrDefault() ?? "";
                if (json == "") continue;
            }
            try
            {
                var meta = VersionResolver.Resolve(gameDir, id);
                var version = new GameVersion
                {
                    Id = id,
                    Folder = folder,
                    JsonPath = json,
                    Meta = meta,
                    Type = string.IsNullOrEmpty(meta.Type) ? "release" : meta.Type,
                    JavaMajor = meta.JavaMajor,
                    McVersion = string.IsNullOrEmpty(meta.ClientVersion) ? meta.RootId : meta.ClientVersion
                };
                DetectLoader(version, meta);
                result.Add(version);
            }
            catch (Exception ex)
            {
                result.Add(new GameVersion
                {
                    Id = id,
                    Folder = folder,
                    JsonPath = json,
                    Error = ex.Message,
                    Loader = "未知",
                    McVersion = id
                });
            }
        }
        return result.OrderByDescending(v => v.LastPlayed).ThenBy(v => v.Id).ToList();
    }

    private static void DetectLoader(GameVersion version, VersionMeta meta)
    {
        var id = version.Id.ToLowerInvariant();
        var mainClass = meta.MainClass.ToLowerInvariant();
        var libs = string.Join("|", meta.Libraries.Select(l => l.Name.ToLowerInvariant()));

        string? loader = null;
        if (id.Contains("neoforge") || libs.Contains("neoforge")) loader = "NeoForge";
        else if (id.Contains("optifine") || libs.Contains("optifine")) loader = "OptiFine";
        else if (id.Contains("quilt") || libs.Contains("quilt")) loader = "Quilt";
        else if (id.Contains("fabric") || libs.Contains("fabric-loader") || mainClass.Contains("fabric")) loader = "Fabric";
        else if (id.Contains("forge") || libs.Contains("minecraftforge") || libs.Contains("net.minecraftforge"))
            loader = "Forge";

        version.Loader = loader ?? "原版";
        if (loader == null) return;

        foreach (var lib in meta.Libraries)
        {
            var name = lib.Name.ToLowerInvariant();
            if (loader == "Fabric" && name.Contains("fabric-loader"))
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3) version.LoaderVersion = parts[2];
            }
            else if (loader == "Forge" && name.Contains("forge"))
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3) version.LoaderVersion = parts[2].Split('-').Last();
            }
        }

        if (string.IsNullOrEmpty(version.LoaderVersion))
        {
            var match = Regex.Match(id, @"(?:forge|neoforge|fabric|quilt)[-_]?(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
            if (match.Success) version.LoaderVersion = match.Groups[1].Value;
        }
    }
}
