using System.Text.Json;

namespace LXLPCL.Core;

public class ModrinthProject
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public long Downloads { get; set; }
    public long Follows { get; set; }
    public string IconUrl { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public List<string> GameVersions { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string IssuesUrl { get; set; } = "";
    public string WikiUrl { get; set; } = "";
    public string DiscordUrl { get; set; } = "";

    public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title[..1].ToUpperInvariant();

    public string ChineseTitle => ModNames.ChineseFor(Title);

    /// <summary>列表显示用标题：收录了中文译名时显示为「中文名 英文名」。</summary>
    public string DisplayTitle => string.IsNullOrEmpty(ChineseTitle) ? Title : $"{ChineseTitle}  {Title}";
    public string DownloadsText => Utils.FormatNumber(Downloads) + "次下载";
    public string AuthorText => string.IsNullOrEmpty(Author) ? "未知作者" : "作者：" + Author;
    public string UpdatedText => UpdatedAt == default ? "" : "更新于 " + Utils.RelativeTime(UpdatedAt);
    public string CategoriesText => string.Join(" · ", Categories.Take(4));
    public string LoadersText => string.Join(" · ", Loaders.Take(3).Select(FormatLoader));
    public string VersionRange
    {
        get
        {
            if (GameVersions.Count == 0) return "";
            var ordered = GameVersions.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            return ordered.Count > 3 ? $"{ordered[0]} ~ {ordered[^1]}" : string.Join(", ", ordered);
        }
    }

    public string MetaText
    {
        get
        {
            var parts = new List<string> { DownloadsText };
            if (!string.IsNullOrEmpty(VersionRange)) parts.Add(VersionRange);
            if (!string.IsNullOrEmpty(LoadersText)) parts.Add(LoadersText);
            return string.Join("  ·  ", parts);
        }
    }

    public string ProjectUrl => string.IsNullOrEmpty(Slug)
        ? "https://modrinth.com"
        : $"https://modrinth.com/{ProjectType}/{Slug}";

    private static string FormatLoader(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => loader
    };
}

public class ModrinthVersion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string VersionType { get; set; } = "release";
    public DateTime Published { get; set; }
    public List<string> GameVersions { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public string FileName { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public string? Sha1 { get; set; }
    public long FileSize { get; set; }
    public bool IsInstalled { get; set; }

    public string InstallText => IsInstalled ? "重新下载" : "安装";

    public string VersionTypeText => VersionType switch
    {
        "beta" => "测试版",
        "alpha" => "抢先版",
        _ => "正式版"
    };
    public string GameVersionsText
    {
        get
        {
            if (GameVersions.Count == 0) return "全版本";
            return GameVersions.Count > 6
                ? string.Join(", ", GameVersions.Take(6)) + $" 等 {GameVersions.Count} 个版本"
                : string.Join(", ", GameVersions);
        }
    }
    public string LoadersText => Loaders.Count == 0 ? "" : string.Join(" · ", Loaders.Select(FormatLoader));
    public string PublishedText => Published == default ? "" : "更新于 " + Utils.RelativeTime(Published);
    public string SizeText => FileSize > 0 ? Utils.FormatBytes(FileSize) : "";

    private static string FormatLoader(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => loader
    };
}

public class ModrinthSearchOptions
{
    public string ProjectType { get; set; } = "mod";
    public string Query { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public string Category { get; set; } = "";
    public string Sort { get; set; } = "relevance";
    public int Limit { get; set; } = 30;
    public int Offset { get; set; }

    public string Index => Sort switch
    {
        "downloads" => "downloads",
        "updated" => "updated",
        "newest" => "newest",
        "follows" => "follows",
        _ => "relevance"
    };
}

public class ModrinthSearchResult
{
    public List<ModrinthProject> Items { get; } = new();
    public int TotalHits { get; set; }
}

public static class Modrinth
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    public static async Task<List<ModrinthProject>> SearchAsync(string projectType, string query,
        CancellationToken token = default) =>
        (await SearchAsync(new ModrinthSearchOptions { ProjectType = projectType, Query = query }, token)).Items;

    public static async Task<ModrinthSearchResult> SearchAsync(ModrinthSearchOptions options,
        CancellationToken token = default)
    {
        var facets = new List<List<string>> { new() { "project_type:" + options.ProjectType } };
        if (!string.IsNullOrWhiteSpace(options.GameVersion))
            facets.Add(new() { "versions:" + options.GameVersion.Trim() });
        if (!string.IsNullOrWhiteSpace(options.Loader))
            facets.Add(new() { "categories:" + options.Loader.Trim().ToLowerInvariant() });
        if (!string.IsNullOrWhiteSpace(options.Category))
            facets.Add(new() { "categories:" + options.Category.Trim() });

        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(options.Query ?? "")}" +
                  $"&limit={Math.Clamp(options.Limit, 1, 50)}&offset={Math.Max(0, options.Offset)}" +
                  $"&index={options.Index}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(new[] { url }, token));
        var result = new ModrinthSearchResult();
        var root = doc.RootElement;
        if (root.TryGetProperty("total_hits", out var total) && total.ValueKind == JsonValueKind.Number)
            result.TotalHits = total.GetInt32();
        if (!root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var hit in hits.EnumerateArray())
        {
            var categories = GetStringList(hit, "categories");
            var loaders = GetStringList(hit, "loaders");
            if (loaders.Count == 0)
                loaders = categories.Where(IsLoader).ToList();
            result.Items.Add(new ModrinthProject
            {
                Id = GetString(hit, "project_id"),
                Slug = GetString(hit, "slug"),
                Title = GetString(hit, "title"),
                Description = GetString(hit, "description"),
                Author = GetString(hit, "author"),
                Downloads = GetLong(hit, "downloads"),
                Follows = GetLong(hit, "follows"),
                IconUrl = GetString(hit, "icon_url"),
                ProjectType = GetString(hit, "project_type"),
                GameVersions = GetStringList(hit, "versions"),
                Loaders = loaders,
                Categories = categories.Where(c => !IsLoader(c)).ToList(),
                UpdatedAt = GetDate(hit, "date_modified")
            });
        }
        return result;
    }

    private static bool IsLoader(string tag) =>
        tag is "fabric" or "forge" or "neoforge" or "quilt" or "rift" or "liteloader";

    /// <summary>获取项目详情（简介正文、链接、更新时间等）。</summary>
    public static async Task<ModrinthProject?> GetProjectAsync(string id, CancellationToken token = default)
    {
        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(id)}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(new[] { url }, token));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        return new ModrinthProject
        {
            Id = GetString(root, "id"),
            Slug = GetString(root, "slug"),
            Title = GetString(root, "title"),
            Description = GetString(root, "description"),
            Body = GetString(root, "body"),
            IconUrl = GetString(root, "icon_url"),
            ProjectType = GetString(root, "project_type"),
            Downloads = GetLong(root, "downloads"),
            Follows = GetLong(root, "followers"),
            Categories = GetStringList(root, "categories"),
            Loaders = GetStringList(root, "loaders"),
            GameVersions = GetStringList(root, "game_versions"),
            SourceUrl = GetString(root, "source_url"),
            IssuesUrl = GetString(root, "issues_url"),
            WikiUrl = GetString(root, "wiki_url"),
            DiscordUrl = GetString(root, "discord_url"),
            PublishedAt = GetDate(root, "published"),
            UpdatedAt = GetDate(root, "updated")
        };
    }

    public static async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId,
        CancellationToken token = default)
    {
        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}/version";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(new[] { url }, token));
        var result = new List<ModrinthVersion>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var version = new ModrinthVersion
            {
                Id = GetString(item, "id"),
                Name = GetString(item, "name"),
                VersionNumber = GetString(item, "version_number"),
                VersionType = GetString(item, "version_type"),
                GameVersions = GetStringList(item, "game_versions"),
                Loaders = GetStringList(item, "loaders")
            };
            version.Published = GetDate(item, "date_published");
            if (item.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                JsonElement? primary = null;
                foreach (var file in files.EnumerateArray())
                {
                    if (file.TryGetProperty("primary", out var isPrimary) && isPrimary.ValueKind == JsonValueKind.True)
                    {
                        primary = file;
                        break;
                    }
                    primary ??= file;
                }
                if (primary != null)
                {
                    version.FileUrl = GetString(primary.Value, "url");
                    version.FileName = GetString(primary.Value, "filename");
                    version.FileSize = GetLong(primary.Value, "size");
                    if (primary.Value.TryGetProperty("hashes", out var hashes))
                        version.Sha1 = GetString(hashes, "sha1");
                }
            }
            result.Add(version);
        }
        return result.OrderByDescending(v => v.Published).ToList();
    }

    public static ModrinthVersion? PickVersion(List<ModrinthVersion> versions, string projectType,
        string? mcVersion, string? loader)
    {
        var candidates = versions.Where(v => !string.IsNullOrEmpty(v.FileUrl)).ToList();
        if (candidates.Count == 0) return null;

        if (projectType == "mod")
        {
            var loaderKey = NormalizeLoader(loader);
            if (!string.IsNullOrEmpty(loaderKey))
                candidates = candidates.Where(v => v.Loaders.Contains(loaderKey, StringComparer.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(mcVersion))
            {
                var exact = candidates.Where(v =>
                    v.GameVersions.Contains(mcVersion, StringComparer.OrdinalIgnoreCase)).ToList();
                if (exact.Count > 0) candidates = exact;
                else if (candidates.All(v => v.GameVersions.Count > 0))
                    return null;
            }
        }
        else if (!string.IsNullOrEmpty(mcVersion))
        {
            var exact = candidates.Where(v =>
                v.GameVersions.Contains(mcVersion, StringComparer.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) candidates = exact;
        }

        return candidates.FirstOrDefault(v => v.VersionType == "release") ?? candidates[0];
    }

    public static string NormalizeLoader(string? loader) => loader switch
    {
        "Fabric" or "fabric" => "fabric",
        "Quilt" or "quilt" => "quilt",
        "Forge" or "forge" => "forge",
        "NeoForge" or "neoforge" => "neoforge",
        _ => ""
    };

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) &&
        p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static long GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) &&
        p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;

    private static DateTime GetDate(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return DateTime.TryParse(text, out var time) ? time.ToLocalTime() : default;
    }

    private static List<string> GetStringList(JsonElement element, string name)
    {
        var list = new List<string>();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in p.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
        }
        return list;
    }
}
