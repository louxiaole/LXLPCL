using System.IO;
using System.Windows.Media.Imaging;

namespace LXLPCL.Core;

public class LocalVersionItem
{
    public string Id { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string Loader { get; set; } = "原版";
    public string LoaderVersion { get; set; } = "";
    public string Folder { get; set; } = "";
    public int ModCount { get; set; }

    public string IconText => Loader switch
    {
        "Fabric" => "FB",
        "Forge" => "FG",
        "NeoForge" => "NF",
        "Quilt" => "QT",
        "OptiFine" => "OF",
        _ => "MC"
    };

    private bool _logoLoaded;
    private BitmapImage? _logo;

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

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(McVersion)) parts.Add("MC " + McVersion);
            if (Loader != "原版") parts.Add(Loader + (string.IsNullOrEmpty(LoaderVersion) ? "" : " " + LoaderVersion));
            if (ModCount > 0) parts.Add(ModCount + " 个模组");
            return parts.Count == 0 ? "未知版本" : string.Join("  ·  ", parts);
        }
    }
}

public class LocalGameFolder
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public bool IsCurrent { get; set; }
    public int ModCount { get; set; }
    public int SaveCount { get; set; }
    public List<LocalVersionItem> Versions { get; set; } = new();

    public string VersionCountText => Versions.Count == 0 ? "没有已安装版本" : $"{Versions.Count} 个版本";
    public string DetailText
    {
        get
        {
            var parts = new List<string> { VersionCountText };
            if (ModCount > 0) parts.Add(ModCount + " 个全局模组");
            if (SaveCount > 0) parts.Add(SaveCount + " 个存档");
            return string.Join("  ·  ", parts);
        }
    }
    public string Badge => IsCurrent ? "当前" : "";
}

public static class LocalDiscovery
{
    private static readonly string[] LauncherFolderNames =
        { "pcl", "hmcl", "minecraft", "我的世界", "game", "games", "mc", "启动器" };

    /// <summary>扫描常见位置（官方启动器、PCL2、HMLC、各磁盘下的 .minecraft 等）。</summary>
    public static List<LocalGameFolder> Scan(CancellationToken token = default)
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path);
        }

        Add(AppState.Config.GameDir);

        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        Add(roaming);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Add(Path.Combine(appData, "PCL2", ".minecraft"));
        Add(Path.Combine(appData, "PCL", ".minecraft"));
        Add(Path.Combine(appData, "HMCL", ".minecraft"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCL2", ".minecraft"));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Path.Combine(userProfile, ".minecraft"));
        Add(Path.Combine(userProfile, "Desktop", ".minecraft"));
        Add(Path.Combine(userProfile, "Downloads", ".minecraft"));
        Add(Path.Combine(userProfile, "Documents", ".minecraft"));

        Add(Path.Combine(AppState.BaseDir, ".minecraft"));
        try
        {
            var parent = Directory.GetParent(AppState.BaseDir)?.FullName;
            if (parent != null) Add(Path.Combine(parent, ".minecraft"));
        }
        catch
        {
            // 忽略无法访问的目录
        }

        foreach (var drive in SafeDrives())
        {
            token.ThrowIfCancellationRequested();
            var root = drive.RootDirectory.FullName;
            Add(Path.Combine(root, ".minecraft"));
            var children = SafeDirs(root);
            foreach (var child in children.Take(300))
            {
                token.ThrowIfCancellationRequested();
                Add(Path.Combine(child, ".minecraft"));
                var name = Path.GetFileName(child).ToLowerInvariant();
                if (!LauncherFolderNames.Any(n => name.Contains(n))) continue;
                foreach (var grandChild in SafeDirs(child).Take(120))
                {
                    token.ThrowIfCancellationRequested();
                    Add(Path.Combine(grandChild, ".minecraft"));
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LocalGameFolder>();
        foreach (var path in candidates)
        {
            token.ThrowIfCancellationRequested();
            var full = NormalizePath(path);
            if (full == null || !seen.Add(full)) continue;
            var folder = Inspect(full, token);
            if (folder == null) continue;
            result.Add(folder);
        }
        return result
            .OrderByDescending(f => f.IsCurrent)
            .ThenByDescending(f => f.Versions.Count)
            .ThenBy(f => f.Path)
            .ToList();
    }

    /// <summary>检查一个目录，返回其中的版本与模组信息；不是有效游戏目录时返回 null。</summary>
    public static LocalGameFolder? Inspect(string folder, CancellationToken token = default)
    {
        var full = NormalizePath(folder);
        if (full == null) return null;
        try
        {
            var versions = VersionScanner.Scan(full);
            var items = versions.Select(v => new LocalVersionItem
            {
                Id = v.Id,
                McVersion = v.McVersion,
                Loader = v.Loader,
                LoaderVersion = v.LoaderVersion,
                Folder = v.Folder,
                ModCount = CountJars(Path.Combine(v.Folder, "mods"))
            }).ToList();

            var mods = CountJars(Path.Combine(full, "mods"));
            var saves = CountDirs(Path.Combine(full, "saves"));
            var isCurrent = PathsEqual(full, AppState.Config.GameDir);
            if (items.Count == 0 && !isCurrent && mods == 0 && saves == 0) return null;

            return new LocalGameFolder
            {
                Path = full,
                Name = DisplayName(full),
                SourceLabel = SourceLabel(full),
                IsCurrent = isCurrent,
                ModCount = mods,
                SaveCount = saves,
                Versions = items
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"扫描游戏目录失败 {folder}: {ex.Message}");
            return null;
        }
    }

    /// <summary>用户手动选择的目录可能多包了一层，向下寻找真正的 .minecraft 目录。</summary>
    public static string? ResolveGameDir(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        var direct = HasVersions(folder) ? folder : null;
        if (direct != null) return NormalizePath(direct);

        foreach (var sub in new[] { ".minecraft", "minecraft" })
        {
            var path = Path.Combine(folder, sub);
            if (Directory.Exists(path) && HasVersions(path)) return NormalizePath(path);
        }

        foreach (var child in SafeDirs(folder).Take(80))
        foreach (var sub in new[] { ".minecraft", "minecraft" })
        {
            var path = Path.Combine(child, sub);
            if (Directory.Exists(path) && HasVersions(path)) return NormalizePath(path);
        }
        return null;
    }

    private static bool HasVersions(string folder)
    {
        var versions = Path.Combine(folder, "versions");
        if (!Directory.Exists(versions)) return false;
        try
        {
            return Directory.EnumerateDirectories(versions).Any(dir =>
                Directory.EnumerateFiles(dir, "*.json").Any());
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizePath(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            var full = Path.GetFullPath(folder.TrimEnd('\\', '/'));
            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        if (!name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(name) ? path : name;
        var parent = Path.GetFileName(Path.GetDirectoryName(path.TrimEnd('\\')) ?? "");
        return string.IsNullOrEmpty(parent) || parent is "Roaming" or "Local" or "AppData" ? "Minecraft" : parent;
    }

    private static string SourceLabel(string path)
    {
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (PathsEqual(path, roaming)) return "官方启动器目录";
        if (path.Contains("PCL", StringComparison.OrdinalIgnoreCase)) return "PCL2 游戏目录";
        if (path.Contains("HMCL", StringComparison.OrdinalIgnoreCase)) return "HMCL 游戏目录";
        return "本地游戏目录";
    }

    private static IEnumerable<string> SafeDirs(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(dir => !Path.GetFileName(dir).StartsWith("$", StringComparison.Ordinal))
                .Where(dir =>
                {
                    try
                    {
                        var attrs = File.GetAttributes(dir);
                        return (attrs & FileAttributes.System) == 0;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .ToList();
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }
    }

    private static int CountJars(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.jar", SearchOption.TopDirectoryOnly).Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountDirs(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Count() : 0;
        }
        catch
        {
            return 0;
        }
    }
}
