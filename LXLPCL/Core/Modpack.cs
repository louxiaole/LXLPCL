using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LXLPCL.Core;

/// <summary>整合包导入导出。导出为 zip，包含版本文件与可选的模组、配置、存档等。</summary>
public static class Modpack
{
    public const string ManifestName = "LXLPCLModpack.json";

    public class Options
    {
        public bool Mods { get; set; } = true;
        public bool Config { get; set; } = true;
        public bool Saves { get; set; }
        public bool ResourcePacks { get; set; }
        public bool ShaderPacks { get; set; }

        public IEnumerable<string> Folders()
        {
            if (Mods) yield return "mods";
            if (Config) yield return "config";
            if (Saves) yield return "saves";
            if (ResourcePacks) yield return "resourcepacks";
            if (ShaderPacks) yield return "shaderpacks";
        }
    }

    public class Manifest
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("mcVersion")] public string McVersion { get; set; } = "";
        [JsonPropertyName("loader")] public string Loader { get; set; } = "";
        [JsonPropertyName("exportedAt")] public string ExportedAt { get; set; } = "";
        [JsonPropertyName("folders")] public List<string> Folders { get; set; } = new();
    }

    public class ExportResult
    {
        public string Path { get; set; } = "";
        public int Files { get; set; }
        public long Bytes { get; set; }
    }

    public class ImportResult
    {
        public string VersionId { get; set; } = "";
        public string Folder { get; set; } = "";
        public int Files { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<ExportResult> ExportAsync(GameVersion version, Options options, string zipPath,
        IProgress<(string Detail, double Percent)>? progress, CancellationToken token)
    {
        var gameDir = AppState.Config.GameDir;
        var versionDir = version.Folder;
        if (!Directory.Exists(versionDir)) throw new DirectoryNotFoundException("版本文件夹不存在：" + versionDir);

        var skipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "mods", "config", "saves", "resourcepacks", "shaderpacks", "logs", "crash-reports" };
        var selected = new HashSet<string>(options.Folders(), StringComparer.OrdinalIgnoreCase);
        var id = Utils.SafeFileName(version.Id);
        var prefix = "versions/" + id + "/";

        var files = new List<(string Source, string Entry)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(versionDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(versionDir, file).Replace('\\', '/');
            var root = relative.Split('/')[0];
            if (!selected.Contains(root) && skipFolders.Contains(root)) continue;
            if (root.Equals("PCL", StringComparison.OrdinalIgnoreCase) && relative.Contains("Setup.ini.tmp")) continue;
            var entry = prefix + relative;
            if (seen.Add(entry)) files.Add((file, entry));
        }

        foreach (var folder in options.Folders())
        {
            var versionSource = Path.Combine(versionDir, folder);
            var globalSource = Path.Combine(gameDir, folder);
            var sources = new[] { versionSource, globalSource }.Where(Directory.Exists).ToList();
            if (sources.Count == 0) continue;
            if (Directory.Exists(versionSource)) sources = new List<string> { versionSource };

            foreach (var source in sources)
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    var entry = prefix + folder + "/" + relative;
                    if (seen.Add(entry)) files.Add((file, entry));
                }
            }
        }

        var manifest = new Manifest
        {
            Name = version.Id,
            Id = id,
            McVersion = version.McVersion,
            Loader = version.LoaderText,
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Folders = options.Folders().ToList()
        };

        var temp = zipPath + ".tmp";
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
            await Task.Run(() =>
            {
                using var stream = File.Create(temp);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                var manifestEntry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

                long bytes = 0;
                var done = 0;
                foreach (var (source, entry) in files)
                {
                    token.ThrowIfCancellationRequested();
                    var item = zip.CreateEntry(entry, CompressionLevel.Optimal);
                    using (var input = File.OpenRead(source))
                    using (var output = item.Open())
                        input.CopyTo(output);
                    bytes += new FileInfo(source).Length;
                    done++;
                    if (done % 5 == 0 || done == files.Count)
                    {
                        var percent = files.Count == 0 ? 100 : done * 100.0 / files.Count;
                        progress?.Report(($"正在打包 {done}/{files.Count}：{Path.GetFileName(source)}", percent));
                    }
                }
                return bytes;
            }, token);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(temp, zipPath);
            progress?.Report(("打包完成", 100));
            return new ExportResult
            {
                Path = zipPath,
                Files = files.Count,
                Bytes = new FileInfo(zipPath).Length
            };
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static async Task<ImportResult> ImportAsync(string zipPath, string gameDir,
        IProgress<(string Detail, double Percent)>? progress, CancellationToken token)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("整合包不存在", zipPath);
        Directory.CreateDirectory(Path.Combine(gameDir, "versions"));

        return await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries
                .Select(entry => new PackEntry(entry, Normalize(entry.FullName)))
                .Where(item => item.Path.Length > 0 && !item.Path.EndsWith("/", StringComparison.Ordinal))
                .Where(item => !item.Path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var manifest = ReadManifest(zip);
            var (sourceId, mapped) = MapEntries(entries, zipPath, manifest);
            if (mapped.Count == 0) throw new InvalidDataException("压缩包里没有找到可导入的版本内容。");

            var targetId = ResolveTargetId(gameDir, manifest?.Id ?? sourceId ?? Path.GetFileNameWithoutExtension(zipPath));
            var targetDir = Path.Combine(gameDir, "versions", targetId);
            Directory.CreateDirectory(targetDir);

            var done = 0;
            var total = mapped.Count;
            foreach (var (entry, target) in mapped)
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(targetDir, target.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetDir);
                entry.ExtractToFile(destination, true);
                done++;
                if (done % 10 == 0 || done == total)
                    progress?.Report(($"正在解压 {done}/{total}：{Path.GetFileName(destination)}", done * 100.0 / total));
            }

            RenameVersionFiles(targetDir, sourceId, targetId);
            progress?.Report(("导入完成", 100));
            return new ImportResult { VersionId = targetId, Folder = targetDir, Files = mapped.Count };
        }, token);
    }

    private static Manifest? ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestName)
                    ?? zip.Entries.FirstOrDefault(e =>
                        Normalize(e.FullName).EndsWith("/" + ManifestName, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<Manifest>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private sealed record PackEntry(ZipArchiveEntry Entry, string Path);

    /// <summary>把压缩包条目映射为版本目录内的相对路径。</summary>
    private static (string? SourceId, List<(ZipArchiveEntry Entry, string Target)> Mapped) MapEntries(
        List<PackEntry> entries, string zipPath, Manifest? manifest)
    {
        var mapped = new List<(ZipArchiveEntry, string)>();
        string? sourceId = string.IsNullOrWhiteSpace(manifest?.Id) ? null : Utils.SafeFileName(manifest.Id);
        const string versionsPrefix = "versions/";

        foreach (var item in entries)
        {
            var path = item.Path;
            var index = path.IndexOf(versionsPrefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (index == 0 || path[index - 1] == '/'))
            {
                var rest = path[(index + versionsPrefix.Length)..];
                var slash = rest.IndexOf('/');
                if (slash <= 0) continue;
                var id = rest[..slash];
                sourceId ??= id;
                mapped.Add((item.Entry, rest[(slash + 1)..]));
                continue;
            }

            if (path.StartsWith(".minecraft/", StringComparison.OrdinalIgnoreCase))
            {
                mapped.Add((item.Entry, path[".minecraft/".Length..]));
                continue;
            }

            if (path.StartsWith("PCL/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("PCL2/", StringComparison.OrdinalIgnoreCase))
            {
                mapped.Add((item.Entry, path));
                continue;
            }

            var head = path.Split('/')[0];
            if (head.Equals("mods", StringComparison.OrdinalIgnoreCase)
                || head.Equals("config", StringComparison.OrdinalIgnoreCase)
                || head.Equals("saves", StringComparison.OrdinalIgnoreCase)
                || head.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase)
                || head.Equals("shaderpacks", StringComparison.OrdinalIgnoreCase)
                || head.Equals("defaultconfigs", StringComparison.OrdinalIgnoreCase))
            {
                mapped.Add((item.Entry, path));
                continue;
            }

            if (path.Equals("options.txt", StringComparison.OrdinalIgnoreCase))
            {
                mapped.Add((item.Entry, path));
                continue;
            }

            // PCL2 / HMCL 直接打包版本内容的格式：根目录下就是 <id>.json、<id>.jar
            if (!path.Contains('/') && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                sourceId ??= Path.GetFileNameWithoutExtension(path);
                mapped.Add((item.Entry, path));
                continue;
            }

            if (!path.Contains('/') && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                mapped.Add((item.Entry, path));
        }

        sourceId ??= Path.GetFileNameWithoutExtension(zipPath);
        return (sourceId, mapped);
    }

    private static string Normalize(string path)
    {
        var result = path.Replace('\\', '/');
        while (result.StartsWith("./", StringComparison.Ordinal)) result = result[2..];
        return result.TrimStart('/');
    }

    private static string ResolveTargetId(string gameDir, string preferred)
    {
        var id = Utils.SafeFileName(string.IsNullOrWhiteSpace(preferred) ? "导入的整合包" : preferred);
        var candidate = id;
        var index = 2;
        while (Directory.Exists(Path.Combine(gameDir, "versions", candidate)))
        {
            candidate = $"{id}-导入{index}";
            index++;
        }
        return candidate;
    }

    private static void RenameVersionFiles(string versionDir, string? sourceId, string targetId)
    {
        if (string.IsNullOrEmpty(sourceId) || sourceId.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var extension in new[] { ".json", ".jar" })
        {
            var oldFile = Path.Combine(versionDir, sourceId + extension);
            var newFile = Path.Combine(versionDir, targetId + extension);
            if (!File.Exists(oldFile)) continue;
            if (File.Exists(newFile)) File.Delete(newFile);
            File.Move(oldFile, newFile);
        }

        var jsonFile = Path.Combine(versionDir, targetId + ".json");
        if (!File.Exists(jsonFile)) return;
        try
        {
            var text = File.ReadAllText(jsonFile);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idNode) || idNode.GetString() != sourceId) return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(text);
            if (node is System.Text.Json.Nodes.JsonObject obj) obj["id"] = targetId;
            File.WriteAllText(jsonFile, node?.ToJsonString(JsonOptions) ?? text);
        }
        catch (Exception ex)
        {
            Logger.Error("改写版本 id 失败: " + ex.Message);
        }
    }
}
