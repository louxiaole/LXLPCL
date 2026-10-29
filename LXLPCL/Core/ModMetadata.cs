using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LXLPCL.Core;

/// <summary>从模组 jar 中读取的元数据（Fabric / Forge / NeoForge / 旧版 Forge）。</summary>
public class ModMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string Authors { get; set; } = "";
    public string ModId { get; set; } = "";
    public string IconEntry { get; set; } = "";
    public string Loader { get; set; } = "";

    public static ModMetadata? Read(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            return ReadFabric(zip) ?? ReadForge(zip) ?? ReadLegacyForge(zip);
        }
        catch (Exception ex)
        {
            Logger.Error($"读取模组信息失败 ({Path.GetFileName(jarPath)}): {ex.Message}");
            return null;
        }
    }

    /// <summary>把模组内嵌图标解压到缓存目录，返回本地图片路径；失败时返回 null。</summary>
    public static string? ExtractIcon(string jarPath, string iconEntry)
    {
        if (string.IsNullOrWhiteSpace(iconEntry)) return null;
        var name = iconEntry.Trim().TrimStart('/');
        if (name.Length == 0) return null;
        try
        {
            var dir = Path.Combine(AppState.CacheDir, "modicons");
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(
                jarPath + "|" + name + "|" + File.GetLastWriteTimeUtc(jarPath).Ticks))).ToLowerInvariant();
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext.Length < 2 || ext.Length > 5) ext = ".png";
            var file = Path.Combine(dir, key + ext);
            if (File.Exists(file)) return file;

            using var zip = ZipFile.OpenRead(jarPath);
            var entry = Find(zip, name);
            if (entry == null || entry.Length > 4 * 1024 * 1024) return null;

            Directory.CreateDirectory(dir);
            var temp = file + ".tmp";
            using (var input = entry.Open())
            using (var output = File.Create(temp))
                input.CopyTo(output);
            File.Move(temp, file, true);
            return file;
        }
        catch (Exception ex)
        {
            Logger.Error($"解压模组图标失败 ({Path.GetFileName(jarPath)}): {ex.Message}");
            return null;
        }
    }

    private static ZipArchiveEntry? Find(ZipArchive zip, string name)
    {
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Text(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(Text).Where(s => s.Length > 0)),
            JsonValueKind.Object => element.EnumerateObject().Select(p => Text(p.Value)).FirstOrDefault(s => s.Length > 0) ?? "",
            _ => ""
        };
    }

    private static ModMetadata? ReadFabric(ZipArchive zip)
    {
        var entry = Find(zip, "fabric.mod.json");
        if (entry == null) return null;
        using var document = JsonDocument.Parse(ReadText(entry));
        var root = document.RootElement;
        var meta = new ModMetadata
        {
            Name = GetString(root, "name"),
            Version = GetString(root, "version"),
            Description = GetText(root, "description"),
            ModId = GetString(root, "id"),
            Loader = Find(zip, "quilt.mod.json") != null ? "Quilt" : "Fabric"
        };
        if (root.TryGetProperty("authors", out var authors))
            meta.Authors = Text(authors);
        if (root.TryGetProperty("icon", out var icon))
        {
            meta.IconEntry = icon.ValueKind == JsonValueKind.Object
                ? icon.EnumerateObject().OrderByDescending(p => p.Name.Length).Select(p => Text(p.Value)).FirstOrDefault() ?? ""
                : Text(icon);
        }
        if (string.IsNullOrEmpty(meta.Name)) meta.Name = meta.ModId;
        return string.IsNullOrEmpty(meta.Name) ? null : meta;
    }

    private static ModMetadata? ReadForge(ZipArchive zip)
    {
        var isNeo = Find(zip, "META-INF/neoforge.mods.toml") != null;
        var entry = Find(zip, "META-INF/mods.toml") ?? Find(zip, "META-INF/neoforge.mods.toml");
        if (entry == null) return null;
        var meta = new ModMetadata { Loader = isNeo ? "NeoForge" : "Forge" };
        var inMods = false;
        foreach (var raw in ReadText(entry).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("[["))
            {
                inMods = line.StartsWith("[[mods]]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inMods || line.StartsWith("#") || !line.Contains('=')) continue;
            var index = line.IndexOf('=');
            var key = line[..index].Trim();
            var value = Unquote(line[(index + 1)..].Trim());
            switch (key.ToLowerInvariant())
            {
                case "modid": if (meta.ModId.Length == 0) meta.ModId = value; break;
                case "displayname": if (meta.Name.Length == 0) meta.Name = value; break;
                case "description": if (meta.Description.Length == 0) meta.Description = value; break;
                case "version": if (meta.Version.Length == 0) meta.Version = CleanPlaceholder(value); break;
                case "logofile":
                    if (meta.IconEntry.Length == 0 && !value.StartsWith("#")) meta.IconEntry = value;
                    break;
                case "authors":
                    if (meta.Authors.Length == 0) meta.Authors = value;
                    break;
            }
        }
        if (string.IsNullOrEmpty(meta.Name)) meta.Name = meta.ModId;
        if (meta.Version.Contains("${", StringComparison.Ordinal)) meta.Version = "";
        return string.IsNullOrEmpty(meta.Name) ? null : meta;
    }

    private static ModMetadata? ReadLegacyForge(ZipArchive zip)
    {
        var entry = Find(zip, "mcmod.info");
        if (entry == null) return null;
        using var document = JsonDocument.Parse(ReadText(entry));
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("modList", out var list)) root = list;
        if (root.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in root.EnumerateArray())
        {
            var meta = new ModMetadata
            {
                Name = GetString(item, "name"),
                ModId = GetString(item, "modid"),
                Version = CleanPlaceholder(GetString(item, "version")),
                Description = GetText(item, "description"),
                Authors = GetText(item, "authorList"),
                Loader = "Forge"
            };
            if (item.TryGetProperty("logoFile", out var logo)) meta.IconEntry = Text(logo);
            if (string.IsNullOrEmpty(meta.Name)) meta.Name = meta.ModId;
            if (!string.IsNullOrEmpty(meta.Name)) return meta;
        }
        return null;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static string GetText(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? Text(value) : "";
    }

    private static string CleanPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var index = value.IndexOf("${", StringComparison.Ordinal);
        if (index < 0) return value.Trim();
        return value[..index].Trim(' ', '-', '_');
    }

    private static string Unquote(string value)
    {
        var text = value;
        var hash = text.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0) text = text[..hash];
        text = text.Trim();
        if (text.StartsWith("'''", StringComparison.Ordinal))
            text = text[3..];
        if (text.EndsWith("'''", StringComparison.Ordinal))
            text = text[..^3];
        text = text.Trim();
        if (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[^1] == text[0])
            text = text[1..^1];
        return text.Trim();
    }
}
