using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LXLPCL.Core;

/// <summary>一条语言条目：原文与对应的中文翻译。</summary>
public sealed class LangEntry
{
    public string Namespace { get; init; } = "";
    public string Key { get; init; } = "";
    public string Source { get; init; } = "";
    public string Translation { get; init; } = "";
    /// <summary>中文语言文件中缺少该键。</summary>
    public bool Missing { get; init; }
    /// <summary>有中文，但与原文完全相同（多半是未翻译的占位）。</summary>
    public bool SameAsSource { get; init; }

    public string StatusText => Missing ? "缺失" : SameAsSource ? "同原文" : "已汉化";
}

/// <summary>额外文本来源中的单个文件（手册某一页、指南某一篇）。</summary>
public sealed class ExternalTextFile
{
    public string Name { get; init; } = "";
    public bool HasChinese { get; init; }
    public string StatusText => HasChinese ? "已汉化" : "缺失";
}

/// <summary>不属于语言文件的额外文本来源（手册、教程、任务等）。</summary>
public sealed class ExternalTextSource
{
    public string Type { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Total { get; init; }
    public int Chinese { get; init; }
    public List<ExternalTextFile> Files { get; init; } = new();

    public string CoverageText => Total == 0 ? "-" : $"{Chinese}/{Total}";
}

/// <summary>单个模组的汉化情况。</summary>
public sealed class ModLangInfo
{
    public string JarPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string ModId { get; init; } = "";
    public string Version { get; init; } = "";
    public int TotalKeys { get; init; }
    public int ChineseKeys { get; init; }
    public int MissingKeys { get; init; }
    public int SameKeys { get; init; }
    public string SourceFile { get; init; } = "";
    public string ChineseFile { get; init; } = "";
    public string? Error { get; init; }
    public List<LangEntry> Entries { get; init; } = new();
    /// <summary>各命名空间已有的中文语言数据，用于生成汉化补丁包。</summary>
    public Dictionary<string, Dictionary<string, string>> ChineseBase { get; init; } = new();
    /// <summary>非语言文件形式的文本来源（Patchouli 手册、AE2 指南等）。</summary>
    public List<ExternalTextSource> ExtraSources { get; init; } = new();

    public bool HasSource => TotalKeys > 0;
    public bool HasChinese => ChineseKeys > 0;
    public bool HasMissing => MissingKeys > 0;

    public string CoverageText => TotalKeys == 0
        ? "-"
        : $"{ChineseKeys}/{TotalKeys}（{ChineseKeys * 100 / TotalKeys}%）";

    public string StatusText => Error != null
        ? "读取失败"
        : !HasSource ? "无语言文件"
        : !HasChinese ? "无汉化"
        : MissingKeys > 0 ? $"缺失 {MissingKeys}"
        : "汉化完整";
}

/// <summary>
/// 读取模组 jar 内的语言文件（assets/&lt;ns&gt;/lang/en_us.json 与 zh_cn.json），
/// 用于对比原文与汉化内容、统计缺失条目。
/// </summary>
public static class ModLocalization
{
    private static readonly Regex LangPattern = new(
        @"^assets/(?<ns>[^/]+)/lang/(?<file>[^/]+)\.json$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string[] FindMods(string modsDir)
    {
        if (!Directory.Exists(modsDir)) return Array.Empty<string>();
        return Directory.GetFiles(modsDir, "*.jar")
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static List<ModLangInfo> Scan(string modsDir,
        IProgress<(int Done, int Total, string Name)>? progress = null, CancellationToken token = default)
        => ScanJars(FindMods(modsDir), progress, token);

    public static List<ModLangInfo> ScanJars(IReadOnlyList<string> jars,
        IProgress<(int Done, int Total, string Name)>? progress = null, CancellationToken token = default)
    {
        var result = new List<ModLangInfo>(jars.Count);
        for (var index = 0; index < jars.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var path = jars[index];
            result.Add(ReadMod(path));
            progress?.Report((index + 1, jars.Count, Path.GetFileName(path)));
        }
        return result;
    }

    private static ModLangInfo ReadMod(string jarPath)
    {
        var fileName = Path.GetFileName(jarPath);
        var meta = ModMetadata.Read(jarPath);
        var displayName = string.IsNullOrWhiteSpace(meta?.Name) ? fileName : meta!.Name;

        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            // namespace -> (原文语言文件, 中文语言文件)
            var languages = new Dictionary<string, (string? Source, string? Chinese)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                var match = LangPattern.Match(path);
                if (!match.Success) continue;
                var ns = match.Groups["ns"].Value;
                var file = match.Groups["file"].Value;
                var isSource = file.Equals("en_us", StringComparison.OrdinalIgnoreCase);
                var isChinese = file.Equals("zh_cn", StringComparison.OrdinalIgnoreCase);
                if (!isSource && !isChinese) continue;

                languages.TryGetValue(ns, out var pair);
                if (isSource) pair.Source ??= path;
                else pair.Chinese ??= path;
                languages[ns] = pair;
            }

            var sources = languages
                .Where(pair => pair.Value.Source != null)
                .OrderByDescending(pair => pair.Value.Chinese != null)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sources.Count == 0)
            {
                return new ModLangInfo
                {
                    JarPath = jarPath,
                    FileName = fileName,
                    DisplayName = displayName,
                    ModId = meta?.ModId ?? "",
                    Version = meta?.Version ?? "",
                    ChineseFile = string.Join(", ", languages.Values
                        .Where(pair => pair.Chinese != null)
                        .Select(pair => pair.Chinese!))
                };
            }

            var entries = new List<LangEntry>();
            var sourceFiles = new List<string>();
            var chineseFiles = new List<string>();
            var chineseBase = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (ns, pair) in sources)
            {
                sourceFiles.Add(pair.Source!);
                var source = ReadLang(zip, pair.Source!);
                Dictionary<string, string>? chinese = null;
                if (pair.Chinese != null)
                {
                    chineseFiles.Add(pair.Chinese);
                    chinese = ReadLang(zip, pair.Chinese);
                    chineseBase[ns] = chinese;
                }

                foreach (var (key, value) in source)
                {
                    string? text = null;
                    var hasTranslation = chinese != null
                                         && chinese.TryGetValue(key, out text)
                                         && !string.IsNullOrWhiteSpace(text);
                    entries.Add(new LangEntry
                    {
                        Namespace = ns,
                        Key = key,
                        Source = value,
                        Translation = hasTranslation ? text! : "",
                        Missing = !hasTranslation,
                        SameAsSource = hasTranslation && string.Equals(text, value, StringComparison.Ordinal)
                    });
                }
            }

            var sorted = entries
                .OrderBy(entry => entry.Missing ? 0 : entry.SameAsSource ? 1 : 2)
                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ModLangInfo
            {
                JarPath = jarPath,
                FileName = fileName,
                DisplayName = displayName,
                ModId = meta?.ModId ?? "",
                Version = meta?.Version ?? "",
                TotalKeys = sorted.Count,
                ChineseKeys = sorted.Count(entry => !entry.Missing),
                MissingKeys = sorted.Count(entry => entry.Missing),
                SameKeys = sorted.Count(entry => entry.SameAsSource),
                SourceFile = string.Join(", ", sourceFiles),
                ChineseFile = string.Join(", ", chineseFiles),
                Entries = sorted,
                ChineseBase = chineseBase,
                ExtraSources = ScanExtraSources(zip)
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"读取模组语言文件失败 ({fileName}): {ex.Message}");
            return new ModLangInfo
            {
                JarPath = jarPath,
                FileName = fileName,
                DisplayName = displayName,
                ModId = meta?.ModId ?? "",
                Version = meta?.Version ?? "",
                Error = ex.Message
            };
        }
    }

    /// <summary>扫描 jar 内不通过 lang 文件提供文本的来源（Patchouli 手册、AE2 指南）。</summary>
    private static List<ExternalTextSource> ScanExtraSources(ZipArchive zip)
    {
        var result = new List<ExternalTextSource>();

        var patchouli = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var patchouliZh = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var guide = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var guideZh = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');

            var bookIndex = path.IndexOf("/patchouli_books/", StringComparison.OrdinalIgnoreCase);
            if (bookIndex >= 0 && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path[(bookIndex + "/patchouli_books/".Length)..].Split('/');
                if (rest.Length >= 3)
                {
                    Dictionary<string, HashSet<string>>? target = null;
                    if (rest[1].Equals("en_us", StringComparison.OrdinalIgnoreCase)) target = patchouli;
                    else if (rest[1].Equals("zh_cn", StringComparison.OrdinalIgnoreCase)) target = patchouliZh;

                    if (target != null)
                    {
                        if (!target.TryGetValue(rest[0], out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            target[rest[0]] = set;
                        }
                        set.Add(string.Join('/', rest.Skip(2)));
                    }
                }
            }

            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var guideIndex = path.IndexOf("/ae2guide/", StringComparison.OrdinalIgnoreCase);
                if (guideIndex > "assets/".Length)
                {
                    var ns = path["assets/".Length..guideIndex];
                    var name = path[(guideIndex + "/ae2guide/".Length)..];
                    var isChinese = name.EndsWith("_zh_cn.md", StringComparison.OrdinalIgnoreCase);
                    if (isChinese) name = name[..^"_zh_cn.md".Length] + ".md";
                    var set = isChinese
                        ? GetOrAdd(guideZh, ns)
                        : GetOrAdd(guide, ns);
                    set.Add(name);
                }
            }
        }

        foreach (var (book, files) in patchouli)
        {
            patchouliZh.TryGetValue(book, out var chinese);
            var details = files
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new ExternalTextFile { Name = name, HasChinese = chinese?.Contains(name) == true })
                .ToList();
            result.Add(new ExternalTextSource
            {
                Type = "Patchouli 手册",
                Detail = book,
                Total = details.Count,
                Chinese = details.Count(file => file.HasChinese),
                Files = details
            });
        }

        foreach (var (ns, files) in guide)
        {
            guideZh.TryGetValue(ns, out var chinese);
            var names = new SortedSet<string>(files, StringComparer.OrdinalIgnoreCase);
            if (chinese != null) foreach (var name in chinese) names.Add(name);
            var details = names
                .Select(name => new ExternalTextFile { Name = name, HasChinese = chinese?.Contains(name) == true })
                .ToList();
            result.Add(new ExternalTextSource
            {
                Type = "AE2 指南",
                Detail = ns,
                Total = details.Count,
                Chinese = details.Count(file => file.HasChinese),
                Files = details
            });
        }

        return result;
    }

    private static HashSet<string> GetOrAdd(Dictionary<string, HashSet<string>> map, string key)
    {
        if (map.TryGetValue(key, out var set)) return set;
        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        map[key] = set;
        return set;
    }

    /// <summary>扫描整合包配置里的 FTB Quests 任务文本（不在 jar 内）。</summary>
    public static ExternalTextSource? ScanFtbQuests(string gameDir)
        => ScanFtbQuestsDir(Path.Combine(gameDir, "config", "ftbquests", "quests", "lang"));

    /// <summary>按版本的游戏目录扫描 FTB Quests 任务文本（独立目录的整合包则读版本文件夹）。</summary>
    public static ExternalTextSource? ScanFtbQuests(GameVersion version)
    {
        var root = Path.Combine(AppState.Config.GameDir, "versions", version.Id);
        var dir = version.UsesIsolatedDir
            ? Path.Combine(root, "config", "ftbquests", "quests", "lang")
            : Path.Combine(AppState.Config.GameDir, "config", "ftbquests", "quests", "lang");
        return ScanFtbQuestsDir(dir);
    }

    private static ExternalTextSource? ScanFtbQuestsDir(string dir)
    {
        if (!Directory.Exists(dir)) return null;

        var total = CountSnbt(Path.Combine(dir, "en_us.snbt"));
        var chinese = CountSnbt(Path.Combine(dir, "zh_cn.snbt"));
        if (total == 0 && chinese == 0) return null;

        return new ExternalTextSource
        {
            Type = "FTB 任务",
            Detail = "config/ftbquests/quests/lang",
            Total = total,
            Chinese = chinese
        };
    }

    private static int CountSnbt(string path)
    {
        if (!File.Exists(path)) return 0;
        try
        {
            return File.ReadAllLines(path).Count(line => line.Contains(':') && line.Contains('"'));
        }
        catch
        {
            return 0;
        }
    }

    private static Dictionary<string, string> ReadLang(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path)
                    ?? zip.Entries.FirstOrDefault(item =>
                        item.FullName.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException("语言文件不存在：" + path);

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.GetRawText();
        }
        return result;
    }
}
