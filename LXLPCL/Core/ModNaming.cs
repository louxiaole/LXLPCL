using System.IO;

namespace LXLPCL.Core;

/// <summary>社区资源下载后的文件命名规则，与设置中的“文件名格式”对应。</summary>
public static class ModNaming
{
    public const string SlugVersionChinese = "slug-mc-ver-cn";
    public const string SlugVersion = "slug-mc-ver";
    public const string ChineseVersion = "cn-mc-ver";
    public const string Original = "original";

    public const string StyleTranslated = "translated";
    public const string StyleFileName = "filename";

    /// <summary>设置页可选的文件名格式，Text 同时作为示例显示。</summary>
    public static readonly (string Key, string Text)[] Formats =
    {
        (SlugVersionChinese, "create-1.21.1-6.0.4-机械动力"),
        (SlugVersion, "create-1.21.1-6.0.4"),
        (ChineseVersion, "机械动力-1.21.1-6.0.4"),
        (Original, "原始文件名（create-1.21.1-6.0.4.jar）")
    };

    public static string Build(string? format, ModrinthProject project, ModrinthVersion version, string? mcVersion)
    {
        var original = Utils.SafeFileName(version.FileName);
        if (string.IsNullOrWhiteSpace(original)) return "";
        var key = string.IsNullOrWhiteSpace(format) ? SlugVersionChinese : format;
        if (key == Original) return original;

        var extension = Path.GetExtension(original);
        if (string.IsNullOrEmpty(extension)) extension = ".jar";

        var gameVersion = string.IsNullOrWhiteSpace(mcVersion)
            ? version.GameVersions.FirstOrDefault() ?? ""
            : mcVersion;
        var modVersion = version.VersionNumber;
        var slug = Utils.SafeFileName(string.IsNullOrWhiteSpace(project.Slug) ? project.Title : project.Slug);
        var chinese = Utils.SafeFileName(ModNames.ChineseFor(project.Title, fuzzy: true));

        var parts = new List<string>();
        switch (key)
        {
            case SlugVersion:
                parts.Add(slug);
                parts.Add(gameVersion);
                parts.Add(modVersion);
                break;
            case ChineseVersion:
                parts.Add(chinese.Length > 0 ? chinese : slug);
                parts.Add(gameVersion);
                parts.Add(modVersion);
                break;
            default:
                parts.Add(slug);
                parts.Add(gameVersion);
                parts.Add(modVersion);
                if (chinese.Length > 0) parts.Add(chinese);
                break;
        }

        var name = Utils.SafeFileName(string.Join("-", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
        if (name.Length == 0) return original;
        if (name.Length > 150) name = name[..150].TrimEnd('-', '_', ' ');
        return name + extension;
    }
}
